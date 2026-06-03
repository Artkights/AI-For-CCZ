using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 地图底图整文件替换服务。
/// 曹操传 6.5 的战场底图以 Map\Mxxx.jpg 形式存在；这里不重封包未知格式，只对已确认的 JPG/JPEG 底图做整文件替换。
/// </summary>
public sealed class MapImageReplaceService
{
    private readonly WriteOperationReportService _reportService = new();

    public MapImageSaveResult ReplaceMapImage(CczProject project, string targetPath, string replacementPath)
    {
        targetPath = Path.GetFullPath(targetPath);
        replacementPath = Path.GetFullPath(replacementPath);

        EnsureTargetIsProjectMap(project, targetPath);
        if (!File.Exists(targetPath)) throw new FileNotFoundException("目标地图图片不存在。", targetPath);
        if (!File.Exists(replacementPath)) throw new FileNotFoundException("替换来源图片不存在。", replacementPath);
        EnsureJpegLike(targetPath, "目标地图图片");
        EnsureJpegLike(replacementPath, "替换来源图片");

        int oldWidth;
        int oldHeight;
        int newWidth;
        int newHeight;
        using (var oldImage = Image.FromFile(targetPath))
        using (var newImage = Image.FromFile(replacementPath))
        {
            oldWidth = oldImage.Width;
            oldHeight = oldImage.Height;
            newWidth = newImage.Width;
            newHeight = newImage.Height;
        }

        var oldBytes = File.ReadAllBytes(targetPath);
        var newBytes = File.ReadAllBytes(replacementPath);
        var oldHash = WriteOperationReportService.ComputeSha256(oldBytes);
        var newHash = WriteOperationReportService.ComputeSha256(newBytes);
        if (oldHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("替换来源与当前地图底图内容完全相同，无需保存。");
        }

        var changedBytes = EstimateChangedBytes(oldBytes, newBytes);
        var warning = oldWidth == newWidth && oldHeight == newHeight
            ? string.Empty
            : $"尺寸变化：原图 {oldWidth}x{oldHeight}，新图 {newWidth}x{newHeight}。曹操传地图底图通常建议保持尺寸一致，请实机确认。";
        var formatCheck = string.IsNullOrWhiteSpace(warning)
            ? $"JPEG 图片格式检查通过：尺寸 {oldWidth}x{oldHeight}。"
            : $"JPEG 图片格式检查通过；{warning}";

        var backupPath = CreateBeforeSaveBackup(project, targetPath);
        var tempPath = targetPath + ".CCZModStudio.tmp";
        File.WriteAllBytes(tempPath, newBytes);
        File.Move(tempPath, targetPath, overwrite: true);

        var writtenBytes = File.ReadAllBytes(targetPath);
        var writtenHash = WriteOperationReportService.ComputeSha256(writtenBytes);
        if (!writtenHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("地图底图写入后复读校验失败：目标文件哈希与替换来源不一致。请使用备份恢复。");
        }

        using (var verifyImage = Image.FromFile(targetPath))
        {
            if (verifyImage.Width != newWidth || verifyImage.Height != newHeight)
            {
                throw new InvalidOperationException("地图底图写入后复读校验失败：图片尺寸与替换来源不一致。请使用备份恢复。");
            }
        }

        var reportPath = WriteStructuredReport(
            project,
            targetPath,
            replacementPath,
            backupPath,
            oldBytes,
            newBytes,
            oldWidth,
            oldHeight,
            newWidth,
            newHeight,
            changedBytes,
            oldHash,
            newHash,
            formatCheck,
            warning);

        return new MapImageSaveResult
        {
            TargetPath = targetPath,
            ReplacementPath = replacementPath,
            BackupPath = backupPath,
            ReportJsonPath = reportPath,
            OldSizeBytes = oldBytes.LongLength,
            NewSizeBytes = newBytes.LongLength,
            OldWidth = oldWidth,
            OldHeight = oldHeight,
            NewWidth = newWidth,
            NewHeight = newHeight,
            ChangedBytesEstimate = changedBytes,
            OldSha256 = oldHash,
            NewSha256 = newHash,
            FormatCheckSummary = formatCheck,
            Warning = warning
        };
    }

    private string WriteStructuredReport(
        CczProject project,
        string targetPath,
        string replacementPath,
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
        var targetRelative = WriteOperationReportService.ToProjectRelativePath(project, targetPath);
        var report = new WriteOperationReport
        {
            OperationKind = "地图底图替换",
            SourceAction = "地图制作页替换 Map 底图前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = targetRelative,
            TargetPath = targetPath,
            BackupPath = backupPath,
            BeforeSha256 = oldHash,
            AfterSha256 = newHash,
            ChangedBytes = changedBytes,
            Summary = $"替换地图底图 {targetRelative}，尺寸 {oldWidth}x{oldHeight} -> {newWidth}x{newHeight}，估算改动 {changedBytes:N0} 字节。",
            SafetyNotes = project.IsTestCopy
                ? "该报告由测试副本地图制作流程自动生成。还原时请使用备份历史/回滚页的预览和再备份流程。"
                : "该报告由当前 MOD 项目地图制作流程直接保存生成。保存前已备份原地图底图；如需回退，请使用备份文件或备份历史功能。",
            FormatCheckSummary = formatCheck,
            RiskSummary = string.IsNullOrWhiteSpace(warning)
                ? "地图底图仍为 JPEG，尺寸未变化；仍建议进游戏确认战场显示和坐标对齐。"
                : warning,
            Changes =
            [
                new WriteOperationChange
                {
                    Category = "Map地图底图",
                    TableName = targetRelative,
                    ColumnName = Path.GetFileName(targetPath),
                    OffsetHex = "整文件",
                    ByteLength = newBytes.Length,
                    OldValue = $"旧大小={oldBytes.LongLength:N0} 字节；尺寸={oldWidth}x{oldHeight}；SHA256={oldHash}",
                    NewValue = $"新大小={newBytes.LongLength:N0} 字节；尺寸={newWidth}x{newHeight}；SHA256={newHash}；来源={replacementPath}",
                    Annotation = $"替换 {targetRelative} 地图底图；Hexzmap.e5 地形层不会被本操作修改，若底图内容变动较大，请在地图制作页叠加按地图分辨率/48 划分的地形格复核。"
                }
            ],
            Metadata =
            {
                ["ReplacementPath"] = replacementPath,
                ["OldWidth"] = oldWidth.ToString(CultureInfo.InvariantCulture),
                ["OldHeight"] = oldHeight.ToString(CultureInfo.InvariantCulture),
                ["NewWidth"] = newWidth.ToString(CultureInfo.InvariantCulture),
                ["NewHeight"] = newHeight.ToString(CultureInfo.InvariantCulture),
                ["OldSizeBytes"] = oldBytes.LongLength.ToString(CultureInfo.InvariantCulture),
                ["NewSizeBytes"] = newBytes.LongLength.ToString(CultureInfo.InvariantCulture),
                ["ChangedBytesEstimate"] = changedBytes.ToString(CultureInfo.InvariantCulture),
                ["Warning"] = warning
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static void EnsureTargetIsProjectMap(CczProject project, string targetPath)
    {
        var mapRoot = Path.GetFullPath(project.ResolveGameFile("Map")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!targetPath.StartsWith(mapRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标文件不在当前项目 Map 目录内，拒绝替换：" + targetPath);
        }
    }

    private static void EnsureJpegLike(string path, string displayName)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{displayName} 必须是 JPG/JPEG 文件：{path}");
        }

        using var image = Image.FromFile(path);
        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new InvalidOperationException($"{displayName} 图片尺寸无效：{path}");
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
