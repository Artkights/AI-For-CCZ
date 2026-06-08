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
    private void SelectPatchFile()
    {
        var initial = ResolvePatchConfigInitialDirectory();

        using var dialog = new OpenFileDialog
        {
            Title = "选择普罗补丁文本",
            Filter = "文本补丁 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            InitialDirectory = Directory.Exists(initial) ? initial : Directory.GetCurrentDirectory()
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _patchPathBox.Text = dialog.FileName;
        PreviewPatch();
    }

    private string ResolvePatchConfigInitialDirectory()
    {
        if (_project == null)
        {
            return Directory.GetCurrentDirectory();
        }

        if (!string.IsNullOrWhiteSpace(_project.PatchConfigRoot) &&
            Directory.Exists(_project.PatchConfigRoot))
        {
            return _project.PatchConfigRoot;
        }

        return ProjectDetector.FindPortableDirectory(_project, "普罗-搬运 注入", "普罗-搬运 注入")
            ?? Path.Combine(_project.WorkspaceRoot, "普罗-搬运 注入");
    }

    private void PreviewPatch()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var patchPath = _patchPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(patchPath) || !File.Exists(patchPath))
        {
            _applyPatchButton.Enabled = false;
            MessageBox.Show(this, "请先选择有效补丁文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var target = Convert.ToString(_patchTargetCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "Ekd5.exe";
        try
        {
            Cursor = Cursors.WaitCursor;
            _currentPatchDocument = _patchParser.Parse(patchPath);
            _currentPatchPreview = _patchService.Preview(_project, _currentPatchDocument, target);
            _patchGrid.DataSource = new BindingList<PatchPreviewRow>(_currentPatchPreview.Rows.ToList());
            ConfigurePatchGrid();
            _applyPatchButton.Enabled = _currentPatchPreview.CanApply;
            _patchInfoBox.Text =
                $"补丁：{_currentPatchDocument.SourcePath}\r\n" +
                $"版本：{_currentPatchDocument.Version}    地址类型：{_currentPatchDocument.AddressKind}    项数：{_currentPatchDocument.Entries.Count}\r\n" +
                $"目标：{_currentPatchPreview.TargetFilePath}\r\n" +
                $"可应用：{_currentPatchPreview.CanApply}    不可应用项：{_currentPatchPreview.WarningCount}    总字节：{_currentPatchPreview.TotalBytes}    将改变字节：{_currentPatchPreview.ChangedBytes}\r\n" +
                "当前项目允许应用补丁；写入前会自动备份目标文件，写入后生成报告。";

            Log($"已预览补丁：{patchPath}");
            Log($"补丁项：{_currentPatchDocument.Entries.Count}，目标：{target}，可应用：{_currentPatchPreview.CanApply}");
            SetStatus(_currentPatchPreview.CanApply ? "补丁预览完成" : "补丁存在不可应用项，请查看补丁页");
        }
        catch (Exception ex)
        {
            _applyPatchButton.Enabled = false;
            _patchInfoBox.Text = ex.ToString();
            Log("补丁预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "补丁预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ConfigurePatchGrid()
    {
        foreach (DataGridViewColumn column in _patchGrid.Columns)
        {
            if (column.DataPropertyName is "OldBytesHex" or "NewBytesHex")
            {
                column.Width = 260;
            }
            if (column.DataPropertyName == nameof(PatchPreviewRow.Comment))
            {
                column.Width = 240;
            }
        }

        foreach (DataGridViewRow row in _patchGrid.Rows)
        {
            if (row.DataBoundItem is PatchPreviewRow previewRow && !previewRow.CanApply)
            {
                row.DefaultCellStyle.BackColor = Color.MistyRose;
            }
            else if (row.DataBoundItem is PatchPreviewRow { Changed: false })
            {
                row.DefaultCellStyle.BackColor = Color.Honeydew;
            }
        }
    }

    private void ApplyPatchToTestCopy()
    {
        if (_project == null || _currentPatchDocument == null || _currentPatchPreview == null)
        {
            return;
        }

        if (!_currentPatchPreview.CanApply)
        {
            MessageBox.Show(this, "补丁存在不可应用项，请先修正或更换目标文件。", "禁止应用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var target = Convert.ToString(_patchTargetCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "Ekd5.exe";
        if (MessageBox.Show(this,
                $"即将把补丁应用到当前项目：\r\n{_currentPatchPreview.TargetFilePath}\r\n\r\n" +
                $"补丁项：{_currentPatchPreview.Rows.Count}\r\n写入字节：{_currentPatchPreview.TotalBytes}\r\n将改变字节：{_currentPatchPreview.ChangedBytes}\r\n\r\n" +
                "写入前会自动备份目标文件并生成报告。是否继续？",
                "确认应用补丁",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _patchService.Apply(_project, _currentPatchDocument, target);
            Log($"已应用补丁：{_currentPatchDocument.SourcePath}");
            Log($"目标文件：{result.TargetFilePath}");
            Log($"备份：{result.BackupPath}");
            Log($"报告：{result.ReportPath}");
            Log($"结构化报告：{result.ReportJsonPath}");
            SetStatus($"补丁应用完成：{result.EntriesApplied} 项，变化 {result.ChangedBytes} 字节");
            MessageBox.Show(this,
                $"补丁应用完成。\r\n项数：{result.EntriesApplied}\r\n写入字节：{result.BytesWritten}\r\n变化字节：{result.ChangedBytes}\r\n备份：{result.BackupPath}\r\n报告：{result.ReportPath}\r\n结构化报告：{result.ReportJsonPath}",
                "补丁应用完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            PreviewPatch();
        }
        catch (Exception ex)
        {
            Log("补丁应用失败：" + ex);
            MessageBox.Show(this, ex.Message, "补丁应用失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void SelectBatchMoveFile()
    {
        var initial = ResolvePatchConfigInitialDirectory();

        using var dialog = new OpenFileDialog
        {
            Title = "选择普罗批量搬运配置",
            Filter = "文本配置 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            InitialDirectory = Directory.Exists(initial) ? initial : Directory.GetCurrentDirectory()
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _movePathBox.Text = dialog.FileName;
        PreviewBatchMove();
    }

    private void PreviewBatchMove()
    {
        var path = _movePathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(this, "请先选择有效搬运配置文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _currentBatchMoveDocument = _batchMoveParser.Parse(path);
            _moveGrid.DataSource = new BindingList<BatchMoveEntry>(_currentBatchMoveDocument.Entries.ToList());
            var totalLength = _currentBatchMoveDocument.Entries.Sum(e => e.Length);
            _moveInfoBox.Text =
                $"配置：{_currentBatchMoveDocument.SourcePath}\r\n" +
                $"条目：{_currentBatchMoveDocument.Entries.Count}    总长度（按十进制长度解释）：{totalLength} 字节\r\n" +
                "当前阶段只做配置解析和报告预览，不执行跨版本搬运写入；写入功能必须在确认源/目标版本和长度单位后再开放。";
            Log($"已预览搬运配置：{path}，条目 {_currentBatchMoveDocument.Entries.Count}");
            SetStatus("搬运配置预览完成");
        }
        catch (Exception ex)
        {
            _moveInfoBox.Text = ex.ToString();
            Log("搬运配置预览失败：" + ex);
            MessageBox.Show(this, ex.Message, "搬运配置预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
