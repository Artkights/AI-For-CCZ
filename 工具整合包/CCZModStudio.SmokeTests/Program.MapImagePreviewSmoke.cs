using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Globalization;

internal partial class Program
{
    static void RunMapImagePreviewSmoke(CczProject project)
    {
        var mapRoot = Path.Combine(project.GameRoot, "Map");
        var targetPath = FindFirstJpegMap(mapRoot)
            ?? throw new FileNotFoundException("Map image preview smoke could not find Map\\*.jpg.", mapRoot);
        var replacementRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_MapPreviewSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(replacementRoot);
        var replacementPath = Path.Combine(replacementRoot, "Replacement_" + Path.GetFileName(targetPath));

        int width;
        int height;
        using (var sourceImage = Image.FromFile(targetPath))
        {
            width = sourceImage.Width;
            height = sourceImage.Height;
            using var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(sourceImage, 0, 0, width, height);
                using var brush = new SolidBrush(Color.FromArgb(24, 160, 96));
                graphics.FillRectangle(brush, 0, 0, Math.Min(10, width), Math.Min(10, height));
            }

            bitmap.Save(replacementPath, System.Drawing.Imaging.ImageFormat.Jpeg);
        }

        var beforeTargetHash = WriteOperationReportService.ComputeSha256(File.ReadAllBytes(targetPath));
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        var backupCountBefore = Directory.Exists(backupRoot)
            ? Directory.EnumerateFiles(backupRoot, "*", SearchOption.TopDirectoryOnly).Count()
            : 0;

        var preview = new MapImageReplaceService().PreviewMapImage(project, targetPath, replacementPath);
        var afterTargetHash = WriteOperationReportService.ComputeSha256(File.ReadAllBytes(targetPath));
        var backupCountAfter = Directory.Exists(backupRoot)
            ? Directory.EnumerateFiles(backupRoot, "*", SearchOption.TopDirectoryOnly).Count()
            : 0;

        if (!beforeTargetHash.Equals(afterTargetHash, StringComparison.OrdinalIgnoreCase) ||
            backupCountAfter != backupCountBefore ||
            preview.TargetPath != Path.GetFullPath(targetPath) ||
            preview.ReplacementPath != Path.GetFullPath(replacementPath) ||
            preview.OldWidth != width ||
            preview.OldHeight != height ||
            preview.NewWidth != width ||
            preview.NewHeight != height ||
            preview.ChangedBytesEstimate <= 0 ||
            string.IsNullOrWhiteSpace(preview.OldSha256) ||
            string.IsNullOrWhiteSpace(preview.NewSha256) ||
            string.IsNullOrWhiteSpace(preview.FormatCheckSummary) ||
            string.IsNullOrWhiteSpace(preview.RiskSummary))
        {
            throw new InvalidOperationException("Map image preview smoke failed read-only, dimension, hash, or payload validation.");
        }

        Directory.Delete(replacementRoot, recursive: true);
        Console.WriteLine($"MAP_IMAGE_PREVIEW_SMOKE_OK map={Path.GetFileName(targetPath)} size={preview.OldWidth}x{preview.OldHeight}->{preview.NewWidth}x{preview.NewHeight} changed={preview.ChangedBytesEstimate} backups={backupCountBefore}->{backupCountAfter}");
    }
}
