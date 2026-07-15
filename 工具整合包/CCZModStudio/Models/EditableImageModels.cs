using System.Drawing;

namespace CCZModStudio.Models;

public enum EditableImageStorageKind
{
    Raw,
    Bmp24,
    Png,
    Jpeg,
    Ls12,
    Unknown,
    DllBitmap
}

public sealed class EditableImageStorageInfo
{
    public EditableImageStorageKind Kind { get; init; }
    public int OriginalStoredLength { get; init; }
    public int OriginalDecodedLength { get; init; }
    public int DataOffset { get; init; }
    public string EntrySha256 { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int BmpBitsPerPixel { get; init; }
    public int BmpCompression { get; init; }
    public bool BmpTopDown { get; init; }
    public Color BackgroundKeyColor { get; init; } = Color.FromArgb(247, 0, 255);
    public bool CanEdit { get; init; }
    public string ReadOnlyReason { get; init; } = string.Empty;
    public string HeaderHex { get; init; } = string.Empty;

    public string DisplayKind => Kind switch
    {
        EditableImageStorageKind.Raw => "RAW",
        EditableImageStorageKind.Bmp24 => "BMP 24-bit BI_RGB",
        EditableImageStorageKind.Png => "PNG",
        EditableImageStorageKind.Jpeg => "JPG",
        EditableImageStorageKind.Ls12 => "LS12",
        EditableImageStorageKind.DllBitmap => "DLL RT_BITMAP",
        _ => "未知"
    };

    public string ExpectedE5Kind => Kind switch
    {
        EditableImageStorageKind.Raw => "RAW",
        EditableImageStorageKind.Bmp24 => "BMP",
        EditableImageStorageKind.Png => "PNG",
        EditableImageStorageKind.Jpeg => "JPG",
        EditableImageStorageKind.Ls12 => "LS12",
        _ => string.Empty
    };
}

public sealed class EditableImageSourceSnapshot
{
    public byte[] StoredBytes { get; init; } = Array.Empty<byte>();
    public byte[] DecodedBytes { get; init; } = Array.Empty<byte>();
    public int[] OriginalArgbPixels { get; init; } = Array.Empty<int>();
    public int Width { get; init; }
    public int Height { get; init; }
    public int TrailingByteCount { get; init; }
    public string ArchiveSha256 { get; init; } = string.Empty;
    public string IndexSha256 { get; init; } = string.Empty;
    public string PaletteSha256 { get; init; } = string.Empty;
    public IReadOnlyList<Color> Palette { get; init; } = Array.Empty<Color>();
}

public sealed class EditableImageEntrySource
{
    public int ImageNumber { get; init; }
    public EditableImageStorageInfo StorageInfo { get; init; } = new();
    public EditableImageSourceSnapshot Snapshot { get; init; } = new();
}

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
    public CharacterImageTargetDescriptor? CharacterTarget { get; init; }
    public EditableImageStorageInfo? StorageInfo { get; init; }
    public EditableImageSourceSnapshot? SourceSnapshot { get; init; }
    public IReadOnlyDictionary<int, EditableImageEntrySource> RelatedEntrySources { get; init; }
        = new Dictionary<int, EditableImageEntrySource>();
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
    public EditableImageStorageInfo StorageInfo { get; init; } = new()
    {
        Kind = EditableImageStorageKind.Unknown,
        ReadOnlyReason = "尚未检测存储格式。"
    };
    public EditableImageSourceSnapshot SourceSnapshot { get; init; } = new();

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
