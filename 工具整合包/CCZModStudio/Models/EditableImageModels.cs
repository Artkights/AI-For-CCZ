using System.Drawing;

namespace CCZModStudio.Models;

public enum EditableImageTargetKind
{
    E5Standard,
    E5RawStrip,
    DllBitmapIcon
}

public sealed class EditableImageTarget
{
    public EditableImageTargetKind Kind { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public int ImageNumber { get; init; }
    public int IconIndex { get; init; }
    public string ResourceFormat { get; init; } = string.Empty;
    public int? FrameWidth { get; init; }
    public int? FrameHeight { get; init; }
    public string OperationKind { get; init; } = "像素编辑";
}

public sealed class EditableImageDocument : IDisposable
{
    public EditableImageTarget Target { get; init; } = new();
    public Bitmap Bitmap { get; init; } = new(1, 1);
    public Bitmap OriginalBitmap { get; init; } = new(1, 1);
    public IReadOnlyList<Color> Palette { get; init; } = Array.Empty<Color>();
    public string PalettePath { get; init; } = string.Empty;
    public int? FrameWidth { get; init; }
    public int? FrameHeight { get; init; }
    public string LoadDetail { get; init; } = string.Empty;
    public EditableImageIconSlotInfo? IconSlotInfo { get; init; }

    public bool HasFrameGrid => FrameWidth.GetValueOrDefault() > 0 && FrameHeight.GetValueOrDefault() > 0;

    public void Dispose()
    {
        Bitmap.Dispose();
        OriginalBitmap.Dispose();
    }
}

public sealed class EditableImageIconSlotInfo
{
    public int IconIndex { get; init; }
    public string ResourceFileName { get; init; } = string.Empty;
    public IReadOnlyList<IconResourceVariantInfo> Variants { get; init; } = Array.Empty<IconResourceVariantInfo>();
    public IconResourceVariantInfo? SmallVariant { get; init; }
    public IconResourceVariantInfo? LargeVariant { get; init; }
    public string SelectionMode { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectionWarnings { get; init; } = Array.Empty<string>();

    public string DisplayText
    {
        get
        {
            var small = SmallVariant == null ? "小图=无" : $"小图 ID={SmallVariant.ResourceId} Lang={SmallVariant.LanguageId} {SmallVariant.Width}x{SmallVariant.Height} {SmallVariant.BitCount}bpp";
            var large = LargeVariant == null ? "大图=无" : $"大图 ID={LargeVariant.ResourceId} Lang={LargeVariant.LanguageId} {LargeVariant.Width}x{LargeVariant.Height} {LargeVariant.BitCount}bpp";
            var mode = string.IsNullOrWhiteSpace(SelectionMode) ? string.Empty : $"    选择={SelectionMode}";
            var warnings = SelectionWarnings.Count == 0 ? string.Empty : $"    警告={string.Join("；", SelectionWarnings.Take(2))}";
            return $"字段={IconIndex}    {Path.GetFileName(ResourceFileName)}    {small}    {large}{mode}{warnings}";
        }
    }
}

public sealed class EditableImageWritePreview
{
    public string TargetRelativePath { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public E5ImageBatchReplacePreviewResult? E5Preview { get; init; }
    public IconResourceBatchReplacePreviewResult? DllPreview { get; init; }
}

public sealed class EditableImageWriteResult
{
    public string TargetRelativePath { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public E5ImageBatchReplaceResult? E5Result { get; init; }
    public IconResourceBatchReplaceResult? DllResult { get; init; }
}
