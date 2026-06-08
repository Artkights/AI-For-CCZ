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
    private void LoadCreatorNotes(bool silent = false)
    {
        if (_project == null)
        {
            if (!silent) MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _currentCreatorNotes = _creatorNoteService.Load(_project);
            BindCreatorNoteRows(_currentCreatorNotes);
            _creatorNoteInfoBox.Text = _creatorNoteService.BuildSummary(_project, _currentCreatorNotes);
            if (!silent)
            {
                Log($"已读取创作者备注：{_currentCreatorNotes.Count} 条。");
                SetStatus($"创作者备注读取完成：{_currentCreatorNotes.Count} 条");
            }
            RefreshWorkflowGuide(updateStatus: false);
        }
        catch (Exception ex)
        {
            _creatorNoteInfoBox.Text = ex.ToString();
            Log("读取创作者备注失败：" + ex);
            if (!silent) MessageBox.Show(this, ex.Message, "读取创作者备注失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BindCreatorNoteRows(IEnumerable<CreatorNote> notes)
    {
        _creatorNoteGrid.DataSource = new BindingList<CreatorNote>(notes.ToList());
        ConfigureCreatorNoteGrid();
    }

    private void ConfigureCreatorNoteGrid()
    {
        if (_creatorNoteGrid.Columns.Count == 0) return;
        SetCreatorNoteColumn(nameof(CreatorNote.Id), "ID", 90, visible: false);
        SetCreatorNoteColumn(nameof(CreatorNote.ProjectName), "项目", 140);
        SetCreatorNoteColumn(nameof(CreatorNote.Scope), "范围", 120);
        SetCreatorNoteColumn(nameof(CreatorNote.TargetKey), "目标键", 260);
        SetCreatorNoteColumn(nameof(CreatorNote.Title), "标题", 220);
        SetCreatorNoteColumn(nameof(CreatorNote.Tags), "标签", 160);
        SetCreatorNoteColumn(nameof(CreatorNote.SourceHint), "来源/证据", 260);
        SetCreatorNoteColumn(nameof(CreatorNote.UpdatedAtText), "更新时间", 150);
        SetCreatorNoteColumn(nameof(CreatorNote.CreatedAtText), "创建时间", 150);
        SetCreatorNoteColumn(nameof(CreatorNote.SafetyNote), "安全说明", 260);
        SetCreatorNoteColumn(nameof(CreatorNote.Content), "内容", 420);

        foreach (DataGridViewRow row in _creatorNoteGrid.Rows)
        {
            if (row.DataBoundItem is not CreatorNote note) continue;
            row.DefaultCellStyle.BackColor =
                note.Tags.Contains("待办", StringComparison.Ordinal) || note.Tags.Contains("TODO", StringComparison.OrdinalIgnoreCase)
                    ? Color.LemonChiffon
                    : note.Tags.Contains("风险", StringComparison.Ordinal) || note.Tags.Contains("待实测", StringComparison.Ordinal)
                        ? Color.MistyRose
                        : Color.Honeydew;
        }
    }

    private void SetCreatorNoteColumn(string propertyName, string headerText, int width, bool visible = true)
    {
        if (!_creatorNoteGrid.Columns.Contains(propertyName)) return;
        var column = _creatorNoteGrid.Columns[propertyName];
        column.HeaderText = headerText;
        column.Width = width;
        column.Visible = visible;
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
    }

    private CreatorNote? GetSelectedCreatorNote()
    {
        if (_creatorNoteGrid.SelectedRows.Count > 0 && _creatorNoteGrid.SelectedRows[0].DataBoundItem is CreatorNote selected) return selected;
        if (_creatorNoteGrid.CurrentRow?.DataBoundItem is CreatorNote current) return current;
        return null;
    }

    private void ShowSelectedCreatorNote()
    {
        var note = GetSelectedCreatorNote();
        if (note == null) return;

        _editingCreatorNoteId = note.Id;
        SetCreatorNoteScope(note.Scope);
        _creatorNoteTargetBox.Text = note.TargetKey;
        _creatorNoteTitleBox.Text = note.Title;
        _creatorNoteTagsBox.Text = note.Tags;
        _creatorNoteSourceHintBox.Text = note.SourceHint;
        _creatorNoteContentBox.Text = note.Content;
        _creatorNoteInfoBox.Text =
            $"当前备注：{note.Title}\r\n" +
            $"范围：{note.Scope}    目标：{note.TargetKey}\r\n" +
            $"标签：{note.Tags}\r\n" +
            $"创建：{note.CreatedAtText}    更新：{note.UpdatedAtText}\r\n" +
            $"安全说明：{note.SafetyNote}\r\n\r\n" +
            note.Content;
    }

    private void ClearCreatorNoteEditor()
    {
        _editingCreatorNoteId = string.Empty;
        SetCreatorNoteScope("全局项目");
        _creatorNoteTargetBox.Clear();
        _creatorNoteTitleBox.Clear();
        _creatorNoteTagsBox.Clear();
        _creatorNoteSourceHintBox.Clear();
        _creatorNoteContentBox.Clear();
        if (_project != null)
        {
            _creatorNoteInfoBox.Text = _creatorNoteService.BuildSummary(_project, _currentCreatorNotes);
        }
    }

    private void SaveCreatorNoteFromEditor()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_creatorNoteTitleBox.Text) && string.IsNullOrWhiteSpace(_creatorNoteContentBox.Text))
        {
            MessageBox.Show(this, "请至少填写标题或内容。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var saved = _creatorNoteService.Upsert(_project, new CreatorNote
            {
                Id = _editingCreatorNoteId,
                Scope = Convert.ToString(_creatorNoteScopeCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "全局项目",
                TargetKey = _creatorNoteTargetBox.Text,
                Title = _creatorNoteTitleBox.Text,
                Content = _creatorNoteContentBox.Text,
                Tags = _creatorNoteTagsBox.Text,
                SourceHint = _creatorNoteSourceHintBox.Text
            });
            _editingCreatorNoteId = saved.Id;
            _currentCreatorNotes = _creatorNoteService.Load(_project);
            BindCreatorNoteRows(_currentCreatorNotes);
            SelectCreatorNoteById(saved.Id);
            _creatorNoteInfoBox.Text = "备注已保存。\r\n" + _creatorNoteService.BuildSummary(_project, _currentCreatorNotes);
            Log($"已保存创作者备注：{saved.Scope}/{saved.TargetKey}/{saved.Title}");
            SetStatus("创作者备注已保存");
            RefreshWorkflowGuide(updateStatus: false);
        }
        catch (Exception ex)
        {
            Log("保存创作者备注失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存创作者备注失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SelectCreatorNoteById(string id)
    {
        foreach (DataGridViewRow row in _creatorNoteGrid.Rows)
        {
            if (row.DataBoundItem is not CreatorNote note || !note.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
            row.Selected = true;
            if (row.Cells.Count > 0) _creatorNoteGrid.CurrentCell = row.Cells[0];
            break;
        }
    }

    private void DeleteSelectedCreatorNote()
    {
        if (_project == null) return;
        var note = GetSelectedCreatorNote();
        if (note == null)
        {
            MessageBox.Show(this, "请先选择一条备注。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this, $"确认删除备注：{note.Title}？\r\n该操作只删除项目侧备注，不影响游戏文件。", "删除备注", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            _creatorNoteService.Delete(_project, note.Id);
            _currentCreatorNotes = _creatorNoteService.Load(_project);
            BindCreatorNoteRows(_currentCreatorNotes);
            ClearCreatorNoteEditor();
            _creatorNoteInfoBox.Text = "备注已删除。\r\n" + _creatorNoteService.BuildSummary(_project, _currentCreatorNotes);
            SetStatus("创作者备注已删除");
            RefreshWorkflowGuide(updateStatus: false);
        }
        catch (Exception ex)
        {
            Log("删除创作者备注失败：" + ex);
            MessageBox.Show(this, ex.Message, "删除创作者备注失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportCreatorNotesCsv()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var path = _creatorNoteService.ExportCsv(_project, GetVisibleCreatorNotes());
            _creatorNoteInfoBox.Text = "创作者备注 CSV 已导出：\r\n" + path;
            Log("已导出创作者备注 CSV：" + path);
            SetStatus("创作者备注 CSV 已导出");
        }
        catch (Exception ex)
        {
            Log("导出创作者备注 CSV 失败：" + ex);
            MessageBox.Show(this, ex.Message, "导出创作者备注 CSV 失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private IReadOnlyList<CreatorNote> GetVisibleCreatorNotes()
    {
        return _creatorNoteGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => row.Visible && row.DataBoundItem is CreatorNote)
            .Select(row => (CreatorNote)row.DataBoundItem)
            .ToList();
    }

    private void ApplyCreatorNoteFilter()
    {
        var filtered = _creatorNoteService.Filter(_currentCreatorNotes, _creatorNoteSearchBox.Text);
        BindCreatorNoteRows(filtered);
        _creatorNoteInfoBox.Text =
            $"备注筛选：显示 {filtered.Count}/{_currentCreatorNotes.Count} 条。\r\n" +
            (_project == null ? string.Empty : _creatorNoteService.BuildSummary(_project, _currentCreatorNotes));
    }

    private void ClearCreatorNoteFilter()
    {
        _creatorNoteSearchBox.Clear();
        BindCreatorNoteRows(_currentCreatorNotes);
        if (_project != null) _creatorNoteInfoBox.Text = _creatorNoteService.BuildSummary(_project, _currentCreatorNotes);
    }

    private void LocateSelectedCreatorNoteTarget()
    {
        var note = GetSelectedCreatorNote();
        if (note == null)
        {
            MessageBox.Show(this, "请先选择一条备注。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var target = _creatorNoteNavigationService.Parse(note);
        if (!target.IsRecognized)
        {
            _creatorNoteInfoBox.Text =
                $"无法自动定位：{target.DisplayText}\r\n" +
                "建议：点击“抓取当前选择”生成规范目标键，或手动保留该备注作为全局说明。";
            MessageBox.Show(this, target.DisplayText, "暂不能定位", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var located = target.Kind switch
            {
                "数据表单元格" => NavigateToTableCellTarget(target),
                "R/S命令" or "SV命令" => NavigateToScenarioCommandTarget(target),
                "R/S文本" or "SV文本" => NavigateToScenarioTextTarget(target),
                "游戏资源" => NavigateToGameResourceTarget(target),
                "资源诊断" => NavigateToResourceDiagnosticTarget(target),
                "EEX资源" => NavigateToEexResourceTarget(target),
                "EEX区段" => NavigateToEexEntryTarget(target),
                "EEX跨文件对比" => NavigateToEexCrossFileTarget(target),
                "Ls/E5资源" => NavigateToLsResourceTarget(target),
                "Hexzmap地形块" => NavigateToHexzmapTarget(target),
                "关卡地图联动" => NavigateToScenarioMapLinkTarget(target),
                "差异" => NavigateToProjectDiffTarget(target),
                "备份" => NavigateToBackupTarget(target),
                _ => false
            };

            _creatorNoteInfoBox.Text =
                $"{(located ? "已定位备注目标" : "未找到备注目标")}：{target.DisplayText}\r\n" +
                $"范围：{note.Scope}\r\n目标键：{note.TargetKey}\r\n\r\n" +
                (located
                    ? "已切换到对应页面并选中候选行。若该目标来自旧数据，请确认当前项目和测试副本是否仍一致。"
                    : "当前项目中没有找到对应行。可能是筛选、文件缺失、备注来自旧版本或目标键手动改写导致。");
            SetStatus(located ? "创作者备注目标定位完成" : "创作者备注目标未找到");
        }
        catch (Exception ex)
        {
            _creatorNoteInfoBox.Text = ex.ToString();
            Log("定位创作者备注目标失败：" + ex);
            MessageBox.Show(this, ex.Message, "定位备注目标失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private bool NavigateToTableCellTarget(CreatorNoteNavigationTarget target)
        => SelectDataTableCell(target.TableName, target.RowId, target.FieldName);
}
