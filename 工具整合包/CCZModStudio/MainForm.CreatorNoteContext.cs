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
    private void CaptureCreatorNoteContextFromSelection()
    {
        var context = _lastCreatorNoteContext ?? BuildCreatorNoteContextFromSelection();
        SetCreatorNoteScope(context.Scope);
        _creatorNoteTargetBox.Text = context.TargetKey;
        if (string.IsNullOrWhiteSpace(_creatorNoteTitleBox.Text)) _creatorNoteTitleBox.Text = context.Title;
        _creatorNoteSourceHintBox.Text = context.SourceHint;
        if (string.IsNullOrWhiteSpace(_creatorNoteContentBox.Text)) _creatorNoteContentBox.Text = context.ContentSeed;
        _creatorNoteInfoBox.Text =
            "已抓取当前选择作为备注目标。\r\n" +
            $"范围：{context.Scope}\r\n目标：{context.TargetKey}\r\n来源：{context.SourceHint}\r\n\r\n" +
            "请补充备注内容后点击“保存备注”。";
    }

    private void SetLastCreatorNoteContext(string scope, string targetKey, string title, string sourceHint, string contentSeed)
        => _lastCreatorNoteContext = (scope, targetKey, title, sourceHint, contentSeed);

    private (string Scope, string TargetKey, string Title, string SourceHint, string ContentSeed) BuildCreatorNoteContextFromSelection()
    {
        var selectedTabText = _mainTabs.SelectedTab?.Text ?? string.Empty;
        bool IsCurrentTab(string text) => selectedTabText.Contains(text, StringComparison.Ordinal);

        if (IsCurrentTab("数据表编辑") && _currentTableResult != null && _dataGrid.CurrentCell != null && _dataGrid.CurrentCell.RowIndex >= 0)
        {
            var row = _dataGrid.Rows[_dataGrid.CurrentCell.RowIndex];
            var columnName = _dataGrid.Columns[_dataGrid.CurrentCell.ColumnIndex].DataPropertyName;
            var rowId = row.Cells.Count > 0 ? Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture) ?? row.Index.ToString(CultureInfo.InvariantCulture) : row.Index.ToString(CultureInfo.InvariantCulture);
            var value = Convert.ToString(row.Cells[_dataGrid.CurrentCell.ColumnIndex].Value, CultureInfo.InvariantCulture) ?? string.Empty;
            return (
                "数据表单元格",
                $"{_currentTableResult.Table.TableName}#ID={rowId}#字段={columnName}",
                $"{_currentTableResult.Table.TableName} / ID {rowId} / {columnName}",
                $"从数据表编辑页抓取；当前值={value}",
                $"用途/修改理由：\r\n风险：\r\n实机验证：\r\n当前值：{value}");
        }

        if (IsCurrentTab("R/S eex高级探针") && _scenarioStructureGrid.CurrentRow?.DataBoundItem is ScenarioStructureRow command && _currentScenarioStructureResult != null)
        {
            return (
                "R/S命令",
                $"{_currentScenarioStructureResult.FileName}#Scene={command.SceneIndex}#Section={command.SectionIndex}#Command={command.CommandIndex}#Offset={command.OffsetHex}",
                $"{_currentScenarioStructureResult.FileName} {command.CommandIdHex}/{command.CommandName}",
                "从 R/S eex 结构草图/事件树抓取；命令参数仍为只读候选。",
                $"命令：{command.CommandIdHex} {command.CommandName}\r\n参数：{command.ParameterPreview}\r\n模板：{command.CommandTemplateHint}\r\n用途：\r\n风险/待核对：");
        }

        if (IsCurrentTab("R/S eex高级探针") && _scenarioTextGrid.CurrentRow?.DataBoundItem is ScenarioTextEntry textEntry)
        {
            var scenarioName = GetSelectedScenarioFileItem()?.FileName ?? _currentScenarioStructureResult?.FileName ?? "未知RS";
            return (
                "R/S文本",
                $"{scenarioName}#TextIndex={textEntry.Index}#Offset={textEntry.OffsetHex}",
                $"{scenarioName} 文本 #{textEntry.Index}",
                "从 R/S eex 文本线索页抓取；写回仍受原容量 GBK 字节限制。",
                $"原文：{textEntry.Text}\r\n容量：{textEntry.ByteLength}B；当前GBK：{textEntry.GbkByteCount}B；状态：{textEntry.WriteStatus}\r\n改写意图：\r\n实机验证：");
        }

        if (IsCurrentTab("游戏资源索引") && _gameResourceGrid.CurrentRow?.DataBoundItem is ResourceIndexItem resource)
        {
            return (
                "游戏资源",
                $"{resource.Category}/{resource.Name}",
                $"{resource.Category}：{resource.Name}",
                "从游戏资源索引页抓取。",
                $"路径：{resource.Path}\r\n格式：{resource.FormatHint}\r\n用途：\r\n替换/验证记录：");
        }

        if (IsCurrentTab("图片资源") && _imageResourceEntryGrid.CurrentRow?.DataBoundItem is ImageResourceEntryInfo imageEntry)
        {
            return (
                "图片资源",
                $"{imageEntry.FileName}#Image={imageEntry.ImageNumber}",
                $"{imageEntry.ResourceName} 图 #{imageEntry.ImageNumber}",
                "从图片处理页抓取；E5 图片条目可按 0x110 索引单条替换，DLL 图标当前只读。",
                $"文件：{imageEntry.Path}\r\n图号：{imageEntry.ImageNumber}\r\n用途候选：{imageEntry.Usage}\r\n格式：{imageEntry.Kind}\r\n索引：0x{imageEntry.IndexOffset:X} offset=0x{imageEntry.DataOffset:X} stored={imageEntry.StoredLength} decoded={imageEntry.DecodedLength}\r\n替换/实机验证记录：");
        }

        if (IsCurrentTab("图片资源") && _imageResourceFileGrid.CurrentRow?.DataBoundItem is ImageResourceFileInfo imageFile)
        {
            return (
                "图片资源",
                $"{imageFile.FileName}",
                imageFile.DisplayName,
                "从图片处理页抓取；文件级用途和安全边界记录。",
                $"文件：{imageFile.Path}\r\n分类：{imageFile.Category}\r\n用途：{imageFile.Usage}\r\n状态：{imageFile.Status}\r\n安全边界：{imageFile.SafetyNote}\r\n研究记录：");
        }

        if (IsCurrentTab("EEX资源探针") && _eexEntryProbeGrid.CurrentRow?.DataBoundItem is EexEntryProbeRow eexEntry)
        {
            return (
                "EEX区段",
                BuildEexEntryProbeCreatorNoteTargetKey(eexEntry),
                $"{eexEntry.FileName} 区段 #{eexEntry.Index}",
                "从 EEX 区段探针页抓取；当前只读，不解包、不重封包。",
                $"文件：{eexEntry.Category}/{eexEntry.FileName}\r\n节点：{eexEntry.NodeType} #{eexEntry.Index}\r\n偏移：{eexEntry.OffsetHex}\r\n长度：{eexEntry.Length}B\r\n角色候选：{eexEntry.RoleHint}\r\n文本线索：{eexEntry.TextHints}\r\n中文注释：{eexEntry.Annotation}\r\n研究记录：");
        }

        if (IsCurrentTab("EEX资源探针") && _eexCrossFileGrid.CurrentRow?.DataBoundItem is EexCrossFileComparisonRow eexCross)
        {
            return (
                "EEX跨文件对比",
                BuildEexCrossFileCreatorNoteTargetKey(eexCross),
                $"EEX跨文件：{_currentEexCrossFileComparison?.TargetFileName ?? "基准EEX"} -> {eexCross.FileName}",
                "从 EEX 跨文件对比页抓取；当前只读，不解包、不重封包。",
                $"基准：{_currentEexCrossFileComparison?.TargetCategory}/{_currentEexCrossFileComparison?.TargetFileName}\r\n对比对象：{eexCross.Category}/{eexCross.FileName}\r\n关系：{eexCross.PeerKind}\r\n角色候选：{eexCross.RoleHint}\r\n差异：{eexCross.DifferenceHint}\r\n中文注释：{eexCross.Annotation}\r\n研究记录：");
        }

        if (IsCurrentTab("EEX资源探针") && _eexArchiveGrid.CurrentRow?.DataBoundItem is EexArchiveInfo eex)
        {
            return (
                "EEX资源",
                $"{eex.Category}/{eex.FileName}",
                $"{eex.Category}：{eex.FileName}",
                "从 EEX 资源探针页抓取；当前只读，不重封包。",
                $"路径：{eex.Path}\r\nID：{eex.Id}\r\n条目候选：{eex.EntryCount}\r\n研究记录：");
        }

        if (IsCurrentTab("Ls/E5地图资源探针") && _lsResourceGrid.CurrentRow?.DataBoundItem is LsResourceInfo ls)
        {
            return (
                "Ls/E5资源",
                BuildLsResourceCreatorNoteTargetKey(ls),
                $"{ls.Category}：{ls.FileName}",
                "从 Ls/E5 资源探针页抓取；当前只读，不解压、不重封包。",
                $"路径：{ls.Path}\r\n角色候选：{ls.RoleHint}\r\nMagic：{ls.Magic}\r\n中文说明：{ls.Annotation}\r\n研究记录：");
        }

        if (IsCurrentTab("Hexzmap地形探针") && _hexzmapGrid.CurrentRow?.DataBoundItem is HexzmapBlockInfo block)
        {
            return (
                "Hexzmap地形块",
                $"{block.MapId}#Offset={block.OffsetHex}",
                $"{block.MapId} 地形块",
                "从 Hexzmap 地形探针页抓取；当前只读。",
                $"偏移：{block.OffsetHex}\r\n主地形：{block.DominantTerrainName}\r\n高频地形：{block.TopTerrainNames}\r\n设计备注：");
        }

        if (IsCurrentTab("关卡地图联动") && _scenarioMapLinkGrid.CurrentRow?.DataBoundItem is ScenarioMapLinkInfo mapLink)
        {
            return (
                "关卡地图联动",
                BuildScenarioMapLinkCreatorNoteTargetKey(mapLink),
                $"{mapLink.ScenarioFileName} / {mapLink.MapId}",
                "从关卡地图联动页抓取。",
                $"标题：{mapLink.ScenarioTitle}\r\n状态：{mapLink.Status}\r\n地形：{mapLink.TopTerrainNames}\r\n设计备注：");
        }

        if (IsCurrentTab("资源诊断") && _resourceDiagnosticGrid.CurrentRow?.DataBoundItem is ResourceDiagnosticItem diagnostic)
        {
            return (
                "资源诊断",
                BuildResourceDiagnosticCreatorNoteTargetKey(diagnostic),
                $"资源诊断：{diagnostic.Category}/{diagnostic.Rule}/{diagnostic.Name}",
                "从资源诊断页抓取；用于记录处理结论、风险确认和发布前复查。",
                $"诊断级别：{diagnostic.Severity}\r\n状态：{diagnostic.Status}\r\n证据：{diagnostic.Detail}\r\n建议：{diagnostic.Suggestion}\r\n处理结论：\r\n实机验证：");
        }

        if (IsCurrentTab("测试副本差异/发布") && _projectDiffGrid.CurrentRow?.DataBoundItem is ProjectDiffItem diff)
        {
            return (
                "备份/差异",
                $"Diff#{diff.RelativePath}",
                $"差异：{diff.RelativePath}",
                "从测试副本差异页抓取。",
                $"状态：{diff.Status}\r\n说明：{diff.Detail}\r\n确认结论：");
        }

        if (IsCurrentTab("备份历史/回滚") && _backupHistoryGrid.CurrentRow?.DataBoundItem is BackupHistoryItem backup)
        {
            return (
                "备份/差异",
                $"Backup#{backup.CreatedAtText}#{backup.TargetRelativePath}",
                $"备份：{backup.TargetRelativePath}",
                "从备份历史页抓取。",
                $"备份：{backup.BackupPath}\r\n来源：{backup.SourceAction}\r\n用途/回滚结论：");
        }

        if (IsCurrentTab("制作向导") && _workflowEvidenceGrid.CurrentRow?.DataBoundItem is ProjectEvidenceItem evidence)
        {
            return (
                "报告/发布证据",
                BuildProjectEvidenceCreatorNoteTargetKey(evidence),
                $"{evidence.Kind}：{evidence.FileName}",
                "从制作向导最近报告/发布证据表抓取。",
                $"证据类型：{evidence.Kind}\r\n路径：{evidence.FullPath}\r\n说明：{evidence.Annotation}\r\n用途：{evidence.SuggestedUse}\r\n复查结论：\r\n实机验证：");
        }

        return (
            "全局项目",
            _project?.Name ?? "当前项目",
            "项目全局备注",
            "手动创建。",
            "目标：\r\n设计意图：\r\n待办：\r\n实机验证：");
    }

    private void SetCreatorNoteScope(string scope)
    {
        var index = _creatorNoteScopeCombo.Items.IndexOf(scope);
        if (index < 0)
        {
            _creatorNoteScopeCombo.Items.Add(scope);
            index = _creatorNoteScopeCombo.Items.Count - 1;
        }
        _creatorNoteScopeCombo.SelectedIndex = Math.Max(0, index);
    }
}
