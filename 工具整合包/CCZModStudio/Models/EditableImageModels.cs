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
    public bool IsItemIconPair { get; init; }
    public int SmallImageNumber { get; init; }
    public int LargeImageNumber { get; init; }
    public string OperationKind { get; init; } = "像素编辑";
}

public sealed class EditableImageDocument : IDisposable
{
    public EditableImageTarget Target { get; init; } = new();
    public Bitmap Bitmap { get; init; } = new(1, 1);
    public Bitmap OriginalBitmap { get; init; } = new(1, 1);
    public IReadOnlyList<Color> Palette { get; init; } = Array.Empty<Color>();
    public string PalettePath { get; init; } = string.Empty;
    public bool RestrictToPalette { get; init; }
    public string PaletteRole { get; init; } = string.Empty;
    public int? FrameWidth { get; init; }
    public int? FrameHeight { get; init; }
    public string LoadDetail { get; init; } = string.Empty;

    public bool HasFrameGrid => FrameWidth.GetValueOrDefault() > 0 && FrameHeight.GetValueOrDefault() > 0;

    public void Dispose()
    {
        Bitmap.Dispose();
        OriginalBitmap.Dispose();
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
