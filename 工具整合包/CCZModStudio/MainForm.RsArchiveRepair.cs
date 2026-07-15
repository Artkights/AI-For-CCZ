using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private void OpenRsArchiveRepair()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "请先加载项目。", "R/S 档案修复", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Cursor = Cursors.WaitCursor;
            var service = new RsArchiveRepairService();
            var preview = service.Scan(_project);
            Cursor = Cursors.Default;
            using var dialog = new RsArchiveRepairDialog(preview);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            Cursor = Cursors.WaitCursor;
            var result = service.Execute(_project, preview, dialog.SelectedMode, dialog.SelectedBackupPath);
            ClearRsPreviewCachesAfterSingleFrameWrite();
            _imageResourceCatalogService.ClearCache();
            Cursor = Cursors.Default;
            MessageBox.Show(this,
                $"R/S 档案修复/整理完成。\r\n修改档案：{result.ChangedArchives.Count}\r\n" +
                $"总大小：{result.OldTotalSize:N0} → {result.NewTotalSize:N0}（{result.NewTotalSize - result.OldTotalSize:+#,0;-#,0;0} 字节）\r\n" +
                $"安全备份：{result.BackupPaths.Count}\r\n报告：{result.ReportPath}",
                "R/S 档案修复完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatus($"R/S 档案修复完成：{result.ChangedArchives.Count} 个档案");
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            System.Diagnostics.Debug.WriteLine("R/S 档案修复失败：" + ex);
            MessageBox.Show(this, ex.Message, "R/S 档案修复失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
