using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private void CreateTestCopy()
    {
        if (_project == null) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            var progress = new Progress<string>(msg => SetStatus(msg));
            var path = _backupManager.CreateTestCopy(_project, progress);
            Log("已创建测试副本：" + path);
            SetStatus("测试副本创建完成");
            if (MessageBox.Show(this, "测试副本已创建，是否切换到该副本进行编辑？\r\n" + path, "完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                LoadProject(_projectDetector.CreateProjectFromGameRoot(path));
            }
        }
        catch (Exception ex)
        {
            Log("创建测试副本失败：" + ex);
            MessageBox.Show(this, ex.Message, "创建测试副本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            RefreshWorkflowGuide(updateStatus: false);
            Cursor = Cursors.Default;
        }
    }

    private void RunProjectAudit()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentAuditItems = _projectAuditService.Analyze(_project, _tables);
            _auditGrid.DataSource = new BindingList<ProjectAuditItem>(_currentAuditItems.ToList());
            ConfigureProjectAuditGrid();
            foreach (DataGridViewRow row in _auditGrid.Rows)
            {
                if (row.DataBoundItem is not ProjectAuditItem item) continue;
                row.DefaultCellStyle.BackColor = item.Severity switch
                {
                    "Error" => Color.MistyRose,
                    "Warn" => Color.LemonChiffon,
                    _ => row.DefaultCellStyle.BackColor
                };
            }

            var errors = _currentAuditItems.Count(x => x.Severity == "Error");
            var warnings = _currentAuditItems.Count(x => x.Severity == "Warn");
            _auditInfoBox.Text =
                $"项目：{_project.GameRoot}\r\n" +
                $"体检项：{_currentAuditItems.Count}    错误：{errors}    警告：{warnings}\r\n" +
                (errors == 0
                    ? "没有发现阻断性问题。仍建议所有修改先进入测试副本。"
                    : "存在阻断性问题，请先处理红色项。");
            _writeAuditReportButton.Enabled = _currentAuditItems.Count > 0;
            Log($"项目体检完成：错误 {errors}，警告 {warnings}。");
            SetStatus($"项目体检完成：错误 {errors}，警告 {warnings}");
            RefreshWorkflowGuide(updateStatus: false);
        }
        catch (Exception ex)
        {
            _auditInfoBox.Text = ex.ToString();
            Log("项目体检失败：" + ex);
            MessageBox.Show(this, ex.Message, "项目体检失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ConfigureProjectAuditGrid()
    {
        if (_auditGrid.Columns.Count == 0) return;
        HideNonAuthoringColumns(
            _auditGrid,
            nameof(ProjectAuditItem.Detail),
            nameof(ProjectAuditItem.Path));
    }

    private void WriteAuditReport()
    {
        if (_project == null || _currentAuditItems.Count == 0) return;
        try
        {
            var path = _projectAuditService.WriteReport(_project, _currentAuditItems);
            Log("已导出项目体检报告：" + path);
            SetStatus("体检报告已导出");
            RefreshWorkflowGuide(updateStatus: false);
            MessageBox.Show(this, "体检报告已导出：\r\n" + path, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("导出体检报告失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出体检报告失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AnalyzeProjectDiff()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_project.IsTestCopy)
        {
            _projectDiffInfoBox.Text = "当前目录不是测试副本，无法生成“测试副本 vs 原始项目”的差异报告。\r\n当前项目仍可直接编辑、备份和生成发布副本；如需文件级差异，请先创建测试副本作为对照。";
            _projectDiffGrid.DataSource = null;
            _projectDiffStatusFilterCombo.Items.Clear();
            _projectDiffSearchBox.Clear();
            _writeProjectDiffReportButton.Enabled = false;
            _createReleaseCopyButton.Enabled = false;
            _showDiffBackupTimelineButton.Enabled = false;
            RefreshWorkflowGuide(updateStatus: false);
            MessageBox.Show(this, "测试副本差异只适用于带 _CCZModStudio_TestCopy.txt 标记的目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentProjectDiffItems = _testCopyDiffService.Analyze(_project);
            PopulateProjectDiffStatusFilter();
            BindProjectDiffRows(_currentProjectDiffItems);
            var sourceRoot = _testCopyDiffService.ReadSourceRoot(_project);
            var modified = _currentProjectDiffItems.Count(x => x.Status == "已修改");
            var added = _currentProjectDiffItems.Count(x => x.Status == "新增");
            var missing = _currentProjectDiffItems.Count(x => x.Status == "缺失");
            _projectDiffInfoBox.Text =
                $"原始项目：{sourceRoot}\r\n" +
                $"测试副本：{_project.GameRoot}\r\n" +
                $"差异项：{_currentProjectDiffItems.Count}    当前显示：{_currentProjectDiffItems.Count}    已修改：{modified}    新增：{added}    缺失：{missing}\r\n" +
                "用途：发布前确认测试副本相对原始项目究竟改动了哪些文件；可按状态/关键字筛选；不比较 _CCZModStudio_Backups 和测试副本标记文件。";
            _writeProjectDiffReportButton.Enabled = true;
            _createReleaseCopyButton.Enabled = true;
            _showDiffBackupTimelineButton.Enabled = _currentProjectDiffItems.Count > 0;
            Log($"测试副本差异完成：总计 {_currentProjectDiffItems.Count}，修改 {modified}，新增 {added}，缺失 {missing}。");
            SetStatus($"测试副本差异完成：{_currentProjectDiffItems.Count} 项");
            RefreshWorkflowGuide(updateStatus: false);
        }
        catch (Exception ex)
        {
            _projectDiffInfoBox.Text = ex.ToString();
            _projectDiffStatusFilterCombo.Items.Clear();
            _projectDiffSearchBox.Clear();
            _writeProjectDiffReportButton.Enabled = false;
            _createReleaseCopyButton.Enabled = false;
            _showDiffBackupTimelineButton.Enabled = false;
            Log("测试副本差异失败：" + ex);
            MessageBox.Show(this, ex.Message, "测试副本差异失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BindProjectDiffRows(IEnumerable<ProjectDiffItem> rows)
    {
        _projectDiffGrid.DataSource = new BindingList<ProjectDiffItem>(rows.ToList());
        ConfigureProjectDiffGrid();
    }

    private void PopulateProjectDiffStatusFilter()
    {
        var previous = Convert.ToString(_projectDiffStatusFilterCombo.SelectedItem, CultureInfo.InvariantCulture);
        _projectDiffStatusFilterCombo.Items.Clear();
        _projectDiffStatusFilterCombo.Items.Add("全部");
        foreach (var status in _currentProjectDiffItems.Select(x => x.Status).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            _projectDiffStatusFilterCombo.Items.Add(status);
        }
        SelectComboValueOrFirst(_projectDiffStatusFilterCombo, previous);
    }

    private void ApplyProjectDiffFilter()
    {
        if (_currentProjectDiffItems.Count == 0) return;
        var status = Convert.ToString(_projectDiffStatusFilterCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "全部";
        var keyword = _projectDiffSearchBox.Text.Trim();
        var filtered = _currentProjectDiffItems.Where(item =>
            (status == "全部" || string.Equals(item.Status, status, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(keyword) || ProjectDiffMatchesKeyword(item, keyword)))
            .ToList();
        BindProjectDiffRows(filtered);
        UpdateProjectDiffInfoAfterFilter(filtered.Count, status, keyword);
        SetStatus($"测试副本差异筛选：{filtered.Count}/{_currentProjectDiffItems.Count}");
    }

    private void ClearProjectDiffFilter()
    {
        if (_currentProjectDiffItems.Count == 0) return;
        _projectDiffSearchBox.Clear();
        if (_projectDiffStatusFilterCombo.Items.Count > 0) _projectDiffStatusFilterCombo.SelectedIndex = 0;
        BindProjectDiffRows(_currentProjectDiffItems);
        UpdateProjectDiffInfoAfterFilter(_currentProjectDiffItems.Count, "全部", string.Empty);
        SetStatus("已显示全部测试副本差异");
    }

    private void UpdateProjectDiffInfoAfterFilter(int visibleCount, string status, string keyword)
    {
        if (_project == null) return;
        var modified = _currentProjectDiffItems.Count(x => x.Status == "已修改");
        var added = _currentProjectDiffItems.Count(x => x.Status == "新增");
        var missing = _currentProjectDiffItems.Count(x => x.Status == "缺失");
        var filterText = status == "全部" && string.IsNullOrWhiteSpace(keyword)
            ? "未筛选"
            : $"状态={status}，关键字={keyword}";
        _projectDiffInfoBox.Text =
            $"测试副本：{_project.GameRoot}\r\n" +
            $"差异项：{_currentProjectDiffItems.Count}    当前显示：{visibleCount}    已修改：{modified}    新增：{added}    缺失：{missing}\r\n" +
            $"筛选：{filterText}\r\n" +
            "提示：选中某个差异文件可查看同路径备份时间线；点击“筛出相关备份”可联动到备份历史/回滚页。";
    }

    private static bool ProjectDiffMatchesKeyword(ProjectDiffItem item, string keyword)
    {
        return ContainsKeyword(item.Status, keyword) ||
               ContainsKeyword(item.RelativePath, keyword) ||
               ContainsKeyword(item.Detail, keyword) ||
               ContainsKeyword(item.SourceSha256, keyword) ||
               ContainsKeyword(item.TestSha256, keyword) ||
               ContainsKeyword(item.SourcePath, keyword) ||
               ContainsKeyword(item.TestPath, keyword);
    }

    private void ConfigureProjectDiffGrid()
    {
        foreach (DataGridViewColumn column in _projectDiffGrid.Columns)
        {
            if (column.DataPropertyName is nameof(ProjectDiffItem.SourceSha256) or nameof(ProjectDiffItem.TestSha256))
            {
                column.Width = 260;
            }
            else if (column.DataPropertyName is nameof(ProjectDiffItem.SourcePath) or nameof(ProjectDiffItem.TestPath))
            {
                column.Width = 280;
            }
        }
        HideNonAuthoringColumns(
            _projectDiffGrid,
            nameof(ProjectDiffItem.Detail),
            nameof(ProjectDiffItem.SourceSha256),
            nameof(ProjectDiffItem.TestSha256),
            nameof(ProjectDiffItem.SourcePath),
            nameof(ProjectDiffItem.TestPath));

        foreach (DataGridViewRow row in _projectDiffGrid.Rows)
        {
            if (row.DataBoundItem is not ProjectDiffItem item) continue;
            row.DefaultCellStyle.BackColor = item.Status switch
            {
                "已修改" => Color.LemonChiffon,
                "新增" => Color.Honeydew,
                "缺失" => Color.MistyRose,
                _ => row.DefaultCellStyle.BackColor
            };
        }
    }

    private ProjectDiffItem? GetSelectedProjectDiffItem()
    {
        if (_projectDiffGrid.SelectedRows.Count > 0 && _projectDiffGrid.SelectedRows[0].DataBoundItem is ProjectDiffItem selectedItem) return selectedItem;
        if (_projectDiffGrid.CurrentRow?.DataBoundItem is ProjectDiffItem currentItem) return currentItem;
        return null;
    }

    private void ShowSelectedProjectDiffItem()
    {
        var item = GetSelectedProjectDiffItem();
        if (item == null) return;

        SetLastCreatorNoteContext(
            "备份/差异",
            $"Diff#{item.RelativePath}",
            $"差异：{item.RelativePath}",
            "从测试副本差异页抓取。",
            $"状态：{item.Status}\r\n说明：{item.Detail}\r\n确认结论：");
        var related = FindRelatedBackupsForDiff(item, refreshIfEmpty: true);
        _projectDiffInfoBox.Text =
            _backupTimelineLinkService.BuildSummary(item, related) +
            BuildLatestStructuredReportSummaryForDiff(related) +
            $"\r\n原始路径：{(string.IsNullOrWhiteSpace(item.SourcePath) ? "-" : item.SourcePath)}\r\n" +
            $"测试路径：{(string.IsNullOrWhiteSpace(item.TestPath) ? "-" : item.TestPath)}\r\n" +
            $"原始SHA256：{item.SourceSha256}\r\n" +
            $"测试SHA256：{item.TestSha256}" +
            BuildRelatedCreatorNotesText("备份/差异", $"Diff#{item.RelativePath}");
        _showDiffBackupTimelineButton.Enabled = true;
        SetStatus($"差异文件：{item.RelativePath}，相关备份 {related.Count} 条");
    }

    private IReadOnlyList<BackupHistoryItem> FindRelatedBackupsForDiff(ProjectDiffItem item, bool refreshIfEmpty)
    {
        if (_project == null) return Array.Empty<BackupHistoryItem>();
        if (refreshIfEmpty && _currentBackupHistoryItems.Count == 0)
        {
            _currentBackupHistoryItems = _backupHistoryService.Scan(_project);
        }

        return _backupTimelineLinkService.FindRelatedBackups(item, _currentBackupHistoryItems);
    }

    private string BuildLatestStructuredReportSummaryForDiff(IReadOnlyList<BackupHistoryItem> relatedBackups)
    {
        var latestStructuredReport = relatedBackups.FirstOrDefault(item =>
            item.ReportPath.EndsWith("WriteOperationReport.json", StringComparison.OrdinalIgnoreCase));
        if (latestStructuredReport == null)
        {
            return "\r\n最近结构化报告：未找到同路径 WriteOperationReport.json；可能是手动改动、旧版本写入或仅存在 TXT 报告。\r\n";
        }

        return
            $"\r\n最近结构化报告详情（备份时间 {latestStructuredReport.CreatedAtText}，来源 {latestStructuredReport.SourceAction}）：\r\n" +
            _writeOperationReportFormatter.FormatForCreator(latestStructuredReport.ReportPath, maxChanges: 6);
    }

    private void ShowBackupTimelineForSelectedProjectDiff()
    {
        var item = GetSelectedProjectDiffItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先选择一条测试副本差异。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            if (_project != null && _currentBackupHistoryItems.Count == 0)
            {
                _currentBackupHistoryItems = _backupHistoryService.Scan(_project);
            }
            var related = _backupTimelineLinkService.FindRelatedBackups(item, _currentBackupHistoryItems);
            BindBackupHistoryRows(related);
            _backupHistoryInfoBox.Text =
                _backupTimelineLinkService.BuildSummary(item, related) +
                "\r\n已将“备份历史/回滚”页筛选为该差异文件的相关备份；如需恢复，请切换到该页选择备份并点击“还原选中备份”。";
            _projectDiffInfoBox.Text =
                _backupTimelineLinkService.BuildSummary(item, related) +
                BuildLatestStructuredReportSummaryForDiff(related) +
                "\r\n已同步筛选“备份历史/回滚”页。";
            Log($"已按差异文件筛出相关备份：{item.RelativePath}，{related.Count} 条。");
            SetStatus($"相关备份筛选完成：{item.RelativePath} -> {related.Count} 条");
        }
        catch (Exception ex)
        {
            Log("筛出相关备份失败：" + ex);
            MessageBox.Show(this, ex.Message, "筛出相关备份失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void WriteProjectDiffReport()
    {
        if (_project == null || _currentProjectDiffItems.Count == 0) return;
        try
        {
            var path = _testCopyDiffService.WriteReport(_project, _currentProjectDiffItems);
            Log("已导出测试副本差异报告：" + path);
            SetStatus("测试副本差异报告已导出");
            RefreshWorkflowGuide(updateStatus: false);
            MessageBox.Show(this, "测试副本差异报告已导出：\r\n" + path, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("导出测试副本差异报告失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出测试副本差异报告失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void WriteProjectDeliveryReport()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var auditItems = _currentAuditItems.Count > 0 ? _currentAuditItems : null;
            var diffItems = _currentProjectDiffItems.Count > 0 ? _currentProjectDiffItems : null;
            var backupItems = _currentBackupHistoryItems.Count > 0 ? _currentBackupHistoryItems : null;
            var resourceDiagnostics = _currentResourceDiagnostics.Count > 0 ? _currentResourceDiagnostics : null;
            var scenarioMapLinks = _currentScenarioMapLinks.Count > 0 ? _currentScenarioMapLinks : null;
            var creatorNotes = _currentCreatorNotes.Count > 0 ? _currentCreatorNotes : null;
            var path = _projectDeliveryReportService.WriteReport(
                _project,
                _tables,
                auditItems,
                diffItems,
                backupItems,
                resourceDiagnostics,
                scenarioMapLinks,
                creatorNotes);
            _projectDiffInfoBox.Text =
                "发布前综合报告已生成：\r\n" +
                path + "\r\n\r\n" +
                "报告内容包含：项目体检摘要、测试副本差异摘要、备份历史、结构化写入报告摘要、MOD创作风险摘要、最近报告/发布证据摘要、发布前检查清单。\r\n" +
                "提示：若报告提示未提供资源诊断或关卡地图联动，请先运行对应页面后重新生成。\r\n" +
                "该操作只写入 CCZModStudio_Reports，不修改任何游戏文件。";
            Log("已生成发布前综合报告：" + path);
            SetStatus("发布前综合报告已生成");
            RefreshProjectEvidence(updateStatus: false);
            RefreshWorkflowGuide(updateStatus: false);
            MessageBox.Show(this, "发布前综合报告已生成：\r\n" + path, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("生成发布前综合报告失败：" + ex);
            MessageBox.Show(this, ex.Message, "生成发布前综合报告失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void CreateReleaseCopy()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            if (_project.IsTestCopy && _currentProjectDiffItems.Count == 0)
            {
                _currentProjectDiffItems = _testCopyDiffService.Analyze(_project);
            }

            var missing = _currentProjectDiffItems.Count(x => x.Status == "缺失");
            var inputLabel = _project.IsTestCopy ? "当前测试副本" : "当前项目";
            var diffLine = _project.IsTestCopy
                ? $"当前差异项：{_currentProjectDiffItems.Count}，其中缺失项：{missing}。\r\n"
                : "当前项目不是测试副本，将直接生成干净发布目录，不生成测试副本差异摘要。\r\n";
            var prompt =
                $"将把{inputLabel}复制为一个干净发布目录，并自动排除：\r\n" +
                "_CCZModStudio_TestCopy.txt、备份目录、报告目录、导出目录。\r\n\r\n" +
                diffLine +
                "是否继续生成发布副本？";
            if (MessageBox.Show(this, prompt, "生成发布副本", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            var progress = new Progress<string>(msg => SetStatus(msg));
            var result = _releasePackageService.CreateReleaseCopy(_project, _currentProjectDiffItems, progress);
            _projectDiffInfoBox.Text +=
                "\r\n\r\n发布副本已生成：\r\n" +
                $"目录：{result.ReleaseRoot}\r\n" +
                $"清单：{result.ManifestPath}\r\n" +
                $"复制文件：{result.FilesCopied}    字节数：{result.BytesCopied}\r\n" +
                $"差异项：{result.ChangedItems}    修改：{result.ModifiedItems}    新增：{result.AddedItems}    缺失：{result.MissingItems}";
            Log("已生成发布副本：" + result.ReleaseRoot);
            SetStatus("发布副本生成完成");
            MessageBox.Show(this, "发布副本生成完成：\r\n" + result.ReleaseRoot, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("生成发布副本失败：" + ex);
            MessageBox.Show(this, ex.Message, "生成发布副本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void LoadBackupHistory()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            _currentBackupHistoryItems = _backupHistoryService.Scan(_project);
            BindBackupHistoryRows(_currentBackupHistoryItems);
            var restorable = _currentBackupHistoryItems.Count(x => x.Restorable);
            var withReport = _currentBackupHistoryItems.Count(x => !string.IsNullOrWhiteSpace(x.ReportPath));
            var kindSummary = string.Join("，", _currentBackupHistoryItems
                .GroupBy(x => x.Kind)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
                .Take(8)
                .Select(x => $"{x.Key}:{x.Count()}"));
            _backupHistoryInfoBox.Text =
                $"备份目录：{Path.Combine(_project.GameRoot, "_CCZModStudio_Backups")}\r\n" +
                $"备份数量：{_currentBackupHistoryItems.Count}    可还原：{restorable}    关联报告：{withReport}\r\n" +
                $"类型摘要：{kindSummary}\r\n" +
                (_project.IsTestCopy
                    ? "当前为测试副本：可选择“可还原”的备份整文件回滚；回滚前仍会预览、备份当前文件并生成报告。"
                    : "当前为项目目录：可选择“可还原”的备份整文件回滚；回滚前仍会预览、备份当前文件并生成报告。");
            Log($"已读取备份历史：{_currentBackupHistoryItems.Count} 项，可还原 {restorable} 项。");
            SetStatus($"备份历史读取完成：{_currentBackupHistoryItems.Count} 项");
            RefreshWorkflowGuide(updateStatus: false);
        }
        catch (Exception ex)
        {
            _backupHistoryInfoBox.Text = ex.ToString();
            Log("读取备份历史失败：" + ex);
            MessageBox.Show(this, ex.Message, "读取备份历史失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BindBackupHistoryRows(IEnumerable<BackupHistoryItem> rows)
    {
        _backupHistoryGrid.DataSource = new BindingList<BackupHistoryItem>(rows.ToList());
        ConfigureBackupHistoryGrid();
    }

    private void ConfigureBackupHistoryGrid()
    {
        if (_backupHistoryGrid.Columns.Count == 0) return;
        SetBackupHistoryColumn(nameof(BackupHistoryItem.CreatedAt), "时间值", 120, visible: false);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.CreatedAtText), "备份时间", 160);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.Kind), "类型", 160);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.TargetRelativePath), "目标路径", 220);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.BackupFileName), "备份文件", 260);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.BackupSizeBytes), "备份大小", 95);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.SourceAction), "来源动作", 170);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.Restorable), "可还原", 70);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.Status), "状态", 180);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.Annotation), "中文说明", 380);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.TargetPath), "目标完整路径", 300);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.BackupPath), "备份完整路径", 300);
        SetBackupHistoryColumn(nameof(BackupHistoryItem.ReportPath), "报告路径", 300);
        HideNonAuthoringColumns(
            _backupHistoryGrid,
            nameof(BackupHistoryItem.SourceAction),
            nameof(BackupHistoryItem.Annotation),
            nameof(BackupHistoryItem.TargetPath),
            nameof(BackupHistoryItem.BackupPath),
            nameof(BackupHistoryItem.ReportPath));

        foreach (DataGridViewRow row in _backupHistoryGrid.Rows)
        {
            if (row.DataBoundItem is not BackupHistoryItem item) continue;
            row.DefaultCellStyle.BackColor = item.Restorable
                ? Color.Honeydew
                : item.Status.Contains("不可", StringComparison.Ordinal)
                    ? Color.LemonChiffon
                    : Color.AliceBlue;
        }
    }

    private void SetBackupHistoryColumn(string propertyName, string headerText, int width, bool visible = true)
    {
        if (!_backupHistoryGrid.Columns.Contains(propertyName)) return;
        var column = _backupHistoryGrid.Columns[propertyName];
        column.HeaderText = headerText;
        column.Width = width;
        column.Visible = visible;
        if (propertyName is nameof(BackupHistoryItem.Annotation) or nameof(BackupHistoryItem.TargetPath) or nameof(BackupHistoryItem.BackupPath) or nameof(BackupHistoryItem.ReportPath))
        {
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        }
    }

    private BackupHistoryItem? GetSelectedBackupHistoryItem()
    {
        if (_backupHistoryGrid.SelectedRows.Count > 0 && _backupHistoryGrid.SelectedRows[0].DataBoundItem is BackupHistoryItem selectedItem) return selectedItem;
        if (_backupHistoryGrid.CurrentRow?.DataBoundItem is BackupHistoryItem currentItem) return currentItem;
        return null;
    }

    private void ShowSelectedBackupHistoryItem()
    {
        var item = GetSelectedBackupHistoryItem();
        if (item == null) return;
        var backupNoteTargetKey = $"Backup#{item.CreatedAtText}#{item.TargetRelativePath}";
        SetLastCreatorNoteContext(
            "备份/差异",
            backupNoteTargetKey,
            $"备份：{item.TargetRelativePath}",
            "从备份历史页抓取。",
            $"备份：{item.BackupPath}\r\n来源：{item.SourceAction}\r\n用途/回滚结论：");
        var text =
            $"备份：{item.BackupFileName}    时间：{item.CreatedAtText}    类型：{item.Kind}\r\n" +
            $"目标：{item.TargetRelativePath}\r\n" +
            $"备份路径：{item.BackupPath}\r\n" +
            $"报告：{(string.IsNullOrWhiteSpace(item.ReportPath) ? "无" : item.ReportPath)}\r\n" +
            $"大小：{item.BackupSizeBytes:N0} 字节    状态：{item.Status}\r\n" +
            $"来源动作：{item.SourceAction}\r\n" +
            $"中文说明：{item.Annotation}";
        if (item.ReportPath.EndsWith("WriteOperationReport.json", StringComparison.OrdinalIgnoreCase))
        {
            text += "\r\n\r\n" + _writeOperationReportFormatter.FormatForCreator(item.ReportPath);
        }
        text += BuildRelatedCreatorNotesText("备份/差异", backupNoteTargetKey);
        _backupHistoryInfoBox.Text = text;
    }

    private void OpenSelectedBackupHistoryLocation()
    {
        var item = GetSelectedBackupHistoryItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先选择一条备份历史。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!File.Exists(item.BackupPath))
        {
            MessageBox.Show(this, "备份文件不存在：" + item.BackupPath, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        OpenFileLocation(item.BackupPath);
    }

    private void OpenSelectedBackupHistoryReport()
    {
        var item = GetSelectedBackupHistoryItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先选择一条备份历史。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(item.ReportPath) || !File.Exists(item.ReportPath))
        {
            MessageBox.Show(this, "该备份没有可打开的写入报告。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.ReportPath,
                UseShellExecute = true
            });
        }
        catch
        {
            OpenFileLocation(item.ReportPath);
        }
    }

    private void ExportBackupHistoryCsv() =>
        ExportGridItemsCsv<BackupHistoryItem>(_backupHistoryGrid, "导出备份历史CSV", "备份历史.csv", "BackupHistory", "备份历史");

    private void RestoreSelectedBackupHistoryItem()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var item = GetSelectedBackupHistoryItem();
        if (item == null)
        {
            MessageBox.Show(this, "请先选择一条备份历史。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!item.Restorable)
        {
            MessageBox.Show(this, "该备份当前不可还原：\r\n" + item.Status, "不可还原", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            ResourceReplacePreviewResult preview;
            try
            {
                Cursor = Cursors.WaitCursor;
                preview = _resourceReplaceService.PreviewReplacement(_project, item.TargetPath, item.BackupPath);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            var resourceItem = new ResourceIndexItem
            {
                Category = item.Kind,
                Id = string.Empty,
                Name = Path.GetFileName(item.TargetRelativePath),
                Extension = Path.GetExtension(item.TargetPath),
                SizeBytes = File.Exists(item.TargetPath) ? new FileInfo(item.TargetPath).Length : 0,
                FormatHint = item.Kind,
                Annotation = item.Annotation,
                Path = item.TargetPath
            };

            var detail =
                "即将从备份历史回滚目标文件。\r\n\r\n" +
                $"备份时间：{item.CreatedAtText}\r\n" +
                $"类型：{item.Kind}\r\n" +
                $"目标：{item.TargetRelativePath}\r\n" +
                $"备份：{item.BackupPath}\r\n\r\n" +
                BuildResourceReplaceConfirmText(resourceItem, preview) +
                "\r\n\r\n注意：这会整文件覆盖目标；回滚前仍会自动备份当前文件。";
            var confirm = ShowResourceReplacePreviewDialog("确认从备份历史回滚", "确认回滚", resourceItem, preview, detail);
            if (confirm != DialogResult.Yes) return;

            Cursor = Cursors.WaitCursor;
            var result = _resourceReplaceService.Replace(_project, item.TargetPath, item.BackupPath);
            LoadBackupHistory();
            _backupHistoryInfoBox.Text =
                $"已从备份历史回滚：{item.TargetRelativePath}\r\n" +
                $"旧大小：{result.OldSizeBytes:N0} 字节    回滚后大小：{result.NewSizeBytes:N0} 字节    改动估算：{result.ChangedBytesEstimate:N0} 字节\r\n" +
                $"格式检查：{result.FormatCheckSummary}\r\n" +
                $"格式警告：{(result.FormatWarnings.Count == 0 ? "无" : string.Join("；", result.FormatWarnings))}\r\n" +
                $"风险提示：{result.RiskSummary}\r\n" +
                $"回滚前当前文件备份：{result.BackupPath}\r\n报告：{result.ReportPath}\r\n" +
                "建议：回滚后重新查看资源诊断和相关差异，并进入游戏实测。";
            _currentGameResources = _gameResourceIndexer.Index(_project);
            Log($"已从备份历史回滚：{item.TargetRelativePath} <- {item.BackupPath}");
            SetStatus("备份历史回滚完成");
        }
        catch (Exception ex)
        {
            Log("备份历史回滚失败：" + ex);
            MessageBox.Show(this, ex.Message, "回滚失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }
}
