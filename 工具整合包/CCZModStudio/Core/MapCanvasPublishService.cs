using System.Globalization;
using System.Drawing.Imaging;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class MapCanvasPublishService
{
    private readonly WriteOperationReportService _reportService = new();
    private readonly MapCanvasComposeService _composeService = new();

    public MapImageSaveResult PublishToMapImage(CczProject project, MapWorkbenchDraft draft, ResourceIndexItem target)
    {
        if (draft.GridWidth <= 0 || draft.GridHeight <= 0)
        {
            throw new InvalidOperationException("地图草稿格数无效。");
        }

        EnsureTargetIsProjectMap(project, target.Path);
        if (!File.Exists(target.Path)) throw new FileNotFoundException("目标地图图片不存在。", target.Path);
        if (target.GridWidth != draft.GridWidth || target.GridHeight != draft.GridHeight)
        {
            throw new InvalidOperationException($"草稿尺寸 {draft.GridWidth}x{draft.GridHeight} 与绑定槽位 {target.Name} 的尺寸 {target.GridWidth}x{target.GridHeight} 不一致，已拒绝发布到底图。");
        }

        int oldWidth;
        int oldHeight;
        using (var oldImage = Image.FromFile(target.Path))
        {
            oldWidth = oldImage.Width;
            oldHeight = oldImage.Height;
        }

        using var bitmap = _composeService.ComposeFinal(draft);
        var newBytes = EncodeJpeg(bitmap, quality: 92L);
        var oldBytes = File.ReadAllBytes(target.Path);
        var oldHash = WriteOperationReportService.ComputeSha256(oldBytes);
        var newHash = WriteOperationReportService.ComputeSha256(newBytes);
        if (oldHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("合成后的地图底图与当前目标文件内容完全相同，无需发布。");
        }

        var changedBytes = EstimateChangedBytes(oldBytes, newBytes);
        var warning = oldWidth == bitmap.Width && oldHeight == bitmap.Height
            ? string.Empty
            : $"尺寸变化：原图 {oldWidth}x{oldHeight}，合成图 {bitmap.Width}x{bitmap.Height}。";
        var formatCheck = string.IsNullOrWhiteSpace(warning)
            ? $"JPEG 图片格式检查通过，尺寸 {bitmap.Width}x{bitmap.Height}。"
            : $"JPEG 图片格式检查通过；{warning}";

        var backupPath = CreateBeforeSaveBackup(project, target.Path);
        var tempPath = target.Path + ".CCZModStudio.tmp";
        File.WriteAllBytes(tempPath, newBytes);
        File.Move(tempPath, target.Path, overwrite: true);

        var writtenBytes = File.ReadAllBytes(target.Path);
        var writtenHash = WriteOperationReportService.ComputeSha256(writtenBytes);
        if (!writtenHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("地图底图发布后复读校验失败：目标文件哈希与合成输出不一致。请使用备份恢复。");
        }

        using (var verifyImage = Image.FromFile(target.Path))
        {
            if (verifyImage.Width != bitmap.Width || verifyImage.Height != bitmap.Height)
            {
                throw new InvalidOperationException("地图底图发布后复读校验失败：图片尺寸与合成输出不一致。请使用备份恢复。");
            }
        }

        var reportPath = WriteStructuredReport(
            project,
            target,
            draft,
            backupPath,
            oldBytes,
            newBytes,
            oldWidth,
            oldHeight,
            bitmap.Width,
            bitmap.Height,
            changedBytes,
            oldHash,
            newHash,
            formatCheck,
            warning);

        return new MapImageSaveResult
        {
            TargetPath = target.Path,
            ReplacementPath = $"MapWorkbenchDraft:{draft.DraftId}",
            BackupPath = backupPath,
            ReportJsonPath = reportPath,
            OldSizeBytes = oldBytes.LongLength,
            NewSizeBytes = newBytes.LongLength,
            OldWidth = oldWidth,
            OldHeight = oldHeight,
            NewWidth = bitmap.Width,
            NewHeight = bitmap.Height,
            ChangedBytesEstimate = changedBytes,
            OldSha256 = oldHash,
            NewSha256 = newHash,
            FormatCheckSummary = formatCheck,
            Warning = warning
        };
    }

    public void ExportJpeg(MapWorkbenchDraft draft, string outputPath)
    {
        using var bitmap = _composeService.ComposeFinal(draft);
        var bytes = EncodeJpeg(bitmap, quality: 92L);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, bytes);
    }

    private string WriteStructuredReport(
        CczProject project,
        ResourceIndexItem target,
        MapWorkbenchDraft draft,
        string backupPath,
        byte[] oldBytes,
        byte[] newBytes,
        int oldWidth,
        int oldHeight,
        int newWidth,
        int newHeight,
        int changedBytes,
        string oldHash,
        string newHash,
        string formatCheck,
        string warning)
    {
        var targetRelative = WriteOperationReportService.ToProjectRelativePath(project, target.Path);
        var report = new WriteOperationReport
        {
            OperationKind = "地图工作台底图发布",
            SourceAction = "地图制作页由草稿内部合成 JPEG 后写入 Map 底图",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = targetRelative,
            TargetPath = target.Path,
            BackupPath = backupPath,
            BeforeSha256 = oldHash,
            AfterSha256 = newHash,
            ChangedBytes = changedBytes,
            Summary = $"发布地图工作台草稿到 {targetRelative}，尺寸 {oldWidth}x{oldHeight} -> {newWidth}x{newHeight}，估算改动 {changedBytes:N0} 字节。",
            SafetyNotes = project.IsTestCopy
                ? "该报告由测试副本地图工作台发布流程生成。发布前已备份原地图底图。"
                : "该报告由当前 MOD 项目地图工作台发布流程生成。发布前已备份原地图底图；如需回退，请使用备份文件或备份历史功能。",
            FormatCheckSummary = formatCheck,
            RiskSummary = string.IsNullOrWhiteSpace(warning)
                ? "地图底图仍为 JPEG，草稿尺寸与绑定槽位一致；仍建议进游戏确认战场显示和坐标对齐。"
                : warning,
            Changes =
            [
                new WriteOperationChange
                {
                    Category = "Map地图底图",
                    TableName = targetRelative,
                    ColumnName = target.Name,
                    OffsetHex = "整文件",
                    ByteLength = newBytes.Length,
                    OldValue = $"旧大小 {oldBytes.LongLength:N0} 字节；尺寸 {oldWidth}x{oldHeight}；SHA256={oldHash}",
                    NewValue = $"新大小 {newBytes.LongLength:N0} 字节；尺寸 {newWidth}x{newHeight}；SHA256={newHash}；草稿={draft.DraftId}",
                    Annotation = $"地图工作台将底稿与 {draft.MapCellOverrides.Count} 个地图画笔覆盖格合成为 JPEG。Hexzmap.e5 地形层不会被本操作修改。"
                }
            ],
            Metadata =
            {
                ["DraftId"] = draft.DraftId,
                ["BoundMapId"] = draft.BoundMapId,
                ["GridWidth"] = draft.GridWidth.ToString(CultureInfo.InvariantCulture),
                ["GridHeight"] = draft.GridHeight.ToString(CultureInfo.InvariantCulture),
                ["TileSize"] = draft.TileSize.ToString(CultureInfo.InvariantCulture),
                ["MaterialRoot"] = draft.MaterialRoot,
                ["BaseLayerPath"] = draft.BaseLayerPath,
                ["MapCellOverrideCount"] = draft.MapCellOverrides.Count.ToString(CultureInfo.InvariantCulture),
                ["TerrainCellCount"] = draft.TerrainCells.Length.ToString(CultureInfo.InvariantCulture),
                ["Warning"] = warning
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static byte[] EncodeJpeg(Image image, long quality)
    {
        using var stream = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
        if (codec == null)
        {
            image.Save(stream, ImageFormat.Jpeg);
            return stream.ToArray();
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        image.Save(stream, codec, parameters);
        return stream.ToArray();
    }

    private static void EnsureTargetIsProjectMap(CczProject project, string targetPath)
    {
        targetPath = Path.GetFullPath(targetPath);
        var mapRoot = Path.GetFullPath(project.ResolveGameFile("Map")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!targetPath.StartsWith(mapRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标文件不在当前项目 Map 目录内，拒绝发布：" + targetPath);
        }
    }

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{Path.GetFileName(filePath)}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{Path.GetFileName(filePath)}");
        }
        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static int EstimateChangedBytes(byte[] oldBytes, byte[] newBytes)
    {
        var length = Math.Min(oldBytes.Length, newBytes.Length);
        var changed = 0L;
        for (var i = 0; i < length; i++)
        {
            if (oldBytes[i] != newBytes[i]) changed++;
        }

        changed += Math.Abs((long)oldBytes.Length - newBytes.Length);
        return changed > int.MaxValue ? int.MaxValue : (int)changed;
    }
}
