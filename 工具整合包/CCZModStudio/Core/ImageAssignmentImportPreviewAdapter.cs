using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ImageAssignmentImportPreviewAdapter
{
    public ImageAssignmentImportPreviewDialogModel FromRImage(
        RImageReplacePreviewResult preview,
        string summaryText,
        string title = "一键导入 R 形象预览")
    {
        var items = preview.Files.Select(file => new ImageAssignmentImportPreviewItem
        {
            Kind = "R",
            DisplayName = $"R{preview.Request.RImageId} {file.Role}",
            ResourceId = preview.Request.RImageId,
            ActionName = file.Role,
            TargetFileName = file.TargetFileName,
            TargetPath = file.TargetPath,
            TargetImageNumber = file.ImageNumber,
            SourcePath = file.SourcePath,
            SourceWidth = file.Encode.SourceWidth,
            SourceHeight = file.Encode.SourceHeight,
            OutputWidth = file.Encode.NormalizedWidth,
            OutputHeight = file.Encode.NormalizedHeight,
            OutputBytes = file.Encode.ImageBytes,
            FrameWidth = 48,
            FrameHeight = 64,
            Detail = $"{preview.Mapping.Detail}；{file.Role} -> {file.TargetFileName} #{file.ImageNumber.ToString(CultureInfo.InvariantCulture)}"
        }).ToArray();

        return BuildModel(title, summaryText, preview.TotalOperationCount > 0 && items.Length > 0, items, Array.Empty<BatchImageImportSkippedItem>(), preview.Warnings);
    }

    public ImageAssignmentImportPreviewDialogModel FromBatchRImage(
        BatchRImageReplacePreviewResult preview,
        string summaryText,
        string title = "批量导入 R 形象预览")
    {
        var targetPath = preview.BatchPreview?.TargetPath ?? string.Empty;
        var targetFileName = string.IsNullOrWhiteSpace(preview.BatchPreview?.TargetRelativePath)
            ? "Pmapobj.e5"
            : Path.GetFileName(preview.BatchPreview!.TargetRelativePath);
        var items = preview.Items.SelectMany(item => new[]
        {
            new ImageAssignmentImportPreviewItem
            {
                Kind = "R",
                DisplayName = $"R{item.RImageId} 正面",
                ResourceId = item.RImageId,
                ActionName = "正面",
                TargetFileName = targetFileName,
                TargetPath = targetPath,
                TargetImageNumber = item.FrontImageNumber,
                SourcePath = item.FrontSourcePath,
                SourceWidth = item.FrontEncode.SourceWidth,
                SourceHeight = item.FrontEncode.SourceHeight,
                OutputWidth = item.FrontEncode.NormalizedWidth,
                OutputHeight = item.FrontEncode.NormalizedHeight,
                OutputBytes = item.FrontEncode.ImageBytes,
                FrameWidth = 48,
                FrameHeight = 64,
                Detail = $"R{item.RImageId} -> Pmapobj.e5 #{item.FrontImageNumber.ToString(CultureInfo.InvariantCulture)} 正面"
            },
            new ImageAssignmentImportPreviewItem
            {
                Kind = "R",
                DisplayName = $"R{item.RImageId} 反面",
                ResourceId = item.RImageId,
                ActionName = "反面",
                TargetFileName = targetFileName,
                TargetPath = targetPath,
                TargetImageNumber = item.BackImageNumber,
                SourcePath = item.BackSourcePath,
                SourceWidth = item.BackEncode.SourceWidth,
                SourceHeight = item.BackEncode.SourceHeight,
                OutputWidth = item.BackEncode.NormalizedWidth,
                OutputHeight = item.BackEncode.NormalizedHeight,
                OutputBytes = item.BackEncode.ImageBytes,
                FrameWidth = 48,
                FrameHeight = 64,
                Detail = $"R{item.RImageId} -> Pmapobj.e5 #{item.BackImageNumber.ToString(CultureInfo.InvariantCulture)} 反面"
            }
        }).ToArray();

        return BuildModel(title, summaryText, preview.CanWrite, items, preview.SkippedItems, preview.Warnings);
    }

    public ImageAssignmentImportPreviewDialogModel FromBatchSImage(
        BatchSImageReplacePreviewResult preview,
        string summaryText,
        string title = "批量导入 S 形象预览")
    {
        var items = new List<ImageAssignmentImportPreviewItem>();
        foreach (var item in preview.Items)
        {
            items.Add(BuildSItem(item, "mov", "移动", "Unit_mov.e5", item.MovSourcePath, item.MovEncode, 48, 48));
            items.Add(BuildSItem(item, "atk", "攻击", "Unit_atk.e5", item.AtkSourcePath, item.AtkEncode, 64, 64));
            items.Add(BuildSItem(item, "spc", "特技", "Unit_spc.e5", item.SpcSourcePath, item.SpcEncode, 48, 48));
        }

        return BuildModel(title, summaryText, preview.CanWrite, items, preview.SkippedItems, preview.Warnings);
    }

    public ImageAssignmentImportPreviewDialogModel FromRoleFace(
        BatchRoleFaceImportPreviewResult preview,
        string summaryText,
        string title)
    {
        var items = preview.Items.SelectMany(item => item.TargetImageNumbers.Select(imageNumber => new ImageAssignmentImportPreviewItem
        {
            Kind = "Face",
            DisplayName = $"{item.DisplayName} 头像#{item.FaceId}",
            ResourceId = item.FaceId,
            ActionName = "头像",
            TargetFileName = Path.GetFileName(preview.TargetPath),
            TargetPath = preview.TargetPath,
            TargetImageNumber = imageNumber,
            SourcePath = item.SourcePath,
            SourceWidth = item.SourceWidth,
            SourceHeight = item.SourceHeight,
            OutputWidth = item.OutputWidth,
            OutputHeight = item.OutputHeight,
            OutputBytes = item.OutputBytes,
            FrameWidth = item.OutputWidth,
            FrameHeight = item.OutputHeight,
            Detail = $"头像#{item.FaceId} -> Face.e5 #{imageNumber.ToString(CultureInfo.InvariantCulture)}；{Path.GetFileName(item.SourcePath)}"
        })).ToArray();

        return BuildModel(title, summaryText, preview.CanWrite, items, preview.SkippedItems, preview.Warnings);
    }

    private static ImageAssignmentImportPreviewItem BuildSItem(
        BatchSImageReplaceItemPreview item,
        string actionName,
        string actionText,
        string targetFileName,
        string sourcePath,
        E5TrueColorEncodeResult encode,
        int frameWidth,
        int frameHeight)
        => new()
        {
            Kind = "S",
            DisplayName = $"S{item.SImageId} {item.StageName} {actionText}",
            ResourceId = item.SImageId,
            StageName = item.StageName,
            ActionName = actionText,
            TargetFileName = targetFileName,
            TargetPath = string.Empty,
            TargetImageNumber = item.ImageNumber,
            SourcePath = sourcePath,
            SourceWidth = encode.SourceWidth,
            SourceHeight = encode.SourceHeight,
            OutputWidth = encode.NormalizedWidth,
            OutputHeight = encode.NormalizedHeight,
            OutputBytes = encode.ImageBytes,
            FrameWidth = frameWidth,
            FrameHeight = frameHeight,
            Detail = $"S{item.SImageId} {item.StageName} {actionName} -> {targetFileName} #{item.ImageNumber.ToString(CultureInfo.InvariantCulture)}"
        };

    private static ImageAssignmentImportPreviewDialogModel BuildModel(
        string title,
        string summaryText,
        bool canWrite,
        IReadOnlyList<ImageAssignmentImportPreviewItem> items,
        IReadOnlyList<BatchImageImportSkippedItem> skipped,
        IReadOnlyList<string> warnings)
        => new()
        {
            Title = title,
            SummaryText = summaryText,
            CanWrite = canWrite && items.Count > 0,
            Items = items,
            SkippedItems = skipped,
            Warnings = warnings
        };
}
