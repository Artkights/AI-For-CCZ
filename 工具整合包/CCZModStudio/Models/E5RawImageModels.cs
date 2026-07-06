using System.Text.Json.Serialization;

namespace CCZModStudio.Models;

public sealed record E5RawImageSpec(
    string FileName,
    int Width,
    int FrameHeight,
    int? StrictStripHeight);

public sealed class E5RawEncodeResult
{
    public string SourcePath { get; init; } = string.Empty;
    public string TargetFileName { get; init; } = string.Empty;
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public int RawLength => RawBytes.Length;
    public int TransparentPixels { get; init; }
    public int ExactPalettePixels { get; init; }
    public int NearestPalettePixels { get; init; }
    public string PalettePath { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    [JsonIgnore]
    public byte[] RawBytes { get; init; } = Array.Empty<byte>();
}

public sealed class E5TrueColorEncodeResult
{
    public string SourcePath { get; init; } = string.Empty;
    public string TargetFileName { get; init; } = string.Empty;
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public int NormalizedWidth { get; init; }
    public int NormalizedHeight { get; init; }
    public string StorageFormat { get; init; } = string.Empty;
    public int ColorDepth { get; init; }
    public int ImageLength => ImageBytes.Length;
    public int TransparentPixels { get; init; }
    public int MagentaKeyPixels { get; init; }
    public string Quantization { get; init; } = "未使用 tsb 调色板量化";
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    [JsonIgnore]
    public byte[] ImageBytes { get; init; } = Array.Empty<byte>();
}

public sealed class SImageReplaceRequest
{
    public int SImageId { get; init; }
    public string MaterialFolder { get; init; } = string.Empty;
    public int? CharacterId { get; init; }
    public int? JobId { get; init; }
    public int FactionSlot { get; init; } = 1;
    public IReadOnlyList<int> StageSlots { get; init; } = Array.Empty<int>();
    public string WriteMode { get; init; } = "direct";
}

public sealed record SImageStageTarget(
    int StageSlot,
    int ImageNumber,
    string DisplayName);

public sealed class SImageMappingSnapshot
{
    public int SImageId { get; init; }
    public int? JobId { get; init; }
    public int FactionSlot { get; init; } = 1;
    public IReadOnlyList<int> ImageNumbers { get; init; } = Array.Empty<int>();
    public IReadOnlyList<SImageStageTarget> StageTargets { get; init; } = Array.Empty<SImageStageTarget>();
    public string ShortText { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class SImageReplaceFilePreview
{
    public string ActionName { get; init; } = string.Empty;
    public int StageSlot { get; init; }
    public string StageName { get; init; } = string.Empty;
    public int ImageNumber { get; init; }
    public string TargetFileName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public E5TrueColorEncodeResult Encode { get; init; } = new();
    public E5ImageBatchReplacePreviewResult BatchPreview { get; init; } = new();
}

public sealed class SImageReplacePreviewResult
{
    public SImageReplaceRequest Request { get; init; } = new();
    public SImageMappingSnapshot Mapping { get; init; } = new();
    public IReadOnlyList<SImageReplaceFilePreview> Files { get; init; } = Array.Empty<SImageReplaceFilePreview>();
    public int TotalOperationCount => Files.Sum(file => file.BatchPreview.OperationCount);
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class SImageReplaceFileResult
{
    public string ActionName { get; init; } = string.Empty;
    public int StageSlot { get; init; }
    public string StageName { get; init; } = string.Empty;
    public int ImageNumber { get; init; }
    public string TargetFileName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public E5TrueColorEncodeResult Encode { get; init; } = new();
    public E5ImageBatchReplaceResult WriteResult { get; init; } = new();
}

public sealed class SImageReplaceResult
{
    public SImageReplaceRequest Request { get; init; } = new();
    public SImageMappingSnapshot Mapping { get; init; } = new();
    public IReadOnlyList<SImageReplaceFileResult> Files { get; init; } = Array.Empty<SImageReplaceFileResult>();
    public int TotalOperationCount => Files.Sum(file => file.WriteResult.OperationCount);
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string AggregateReportPath { get; init; } = string.Empty;
}

public sealed class RImageReplaceRequest
{
    public int RImageId { get; init; }
    public string MaterialFolder { get; init; } = string.Empty;
    public int? CharacterId { get; init; }
    public string WriteMode { get; init; } = "direct";
}

public sealed class RImageMappingSnapshot
{
    public int RImageId { get; init; }
    public int FrontImageNumber { get; init; }
    public int BackImageNumber { get; init; }
    public IReadOnlyList<int> ImageNumbers { get; init; } = Array.Empty<int>();
    public string Detail { get; init; } = string.Empty;
}

public sealed class RImageReplaceFilePreview
{
    public string Role { get; init; } = string.Empty;
    public string TargetFileName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public int ImageNumber { get; init; }
    public E5TrueColorEncodeResult Encode { get; init; } = new();
}

public sealed class RImageReplacePreviewResult
{
    public RImageReplaceRequest Request { get; init; } = new();
    public RImageMappingSnapshot Mapping { get; init; } = new();
    public IReadOnlyList<RImageReplaceFilePreview> Files { get; init; } = Array.Empty<RImageReplaceFilePreview>();
    public E5ImageBatchReplacePreviewResult BatchPreview { get; init; } = new();
    public int TotalOperationCount => BatchPreview.OperationCount;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class RImageReplaceFileResult
{
    public string Role { get; init; } = string.Empty;
    public string TargetFileName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public int ImageNumber { get; init; }
    public E5TrueColorEncodeResult Encode { get; init; } = new();
}

public sealed class RImageReplaceResult
{
    public RImageReplaceRequest Request { get; init; } = new();
    public RImageMappingSnapshot Mapping { get; init; } = new();
    public IReadOnlyList<RImageReplaceFileResult> Files { get; init; } = Array.Empty<RImageReplaceFileResult>();
    public E5ImageBatchReplaceResult WriteResult { get; init; } = new();
    public int TotalOperationCount => WriteResult.OperationCount;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string AggregateReportPath { get; init; } = string.Empty;
}

public sealed class E5RoleRawNormalizeRequest
{
    public string WriteMode { get; init; } = "direct";
}

public sealed class E5RoleRawNormalizeEntryPreview
{
    public string TargetFileName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public int ImageNumber { get; init; }
    public string OldKind { get; init; } = string.Empty;
    public int OldSizeBytes { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public E5RawEncodeResult? Encode { get; init; }
}

public sealed class E5RoleRawNormalizeFilePreview
{
    public string TargetFileName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public IReadOnlyList<E5RoleRawNormalizeEntryPreview> Entries { get; init; } = Array.Empty<E5RoleRawNormalizeEntryPreview>();
    public E5ImageBatchReplacePreviewResult? BatchPreview { get; init; }
    public int ConvertCount => Entries.Count(entry => entry.Status.Equals("convert", StringComparison.OrdinalIgnoreCase));
    public int SkipCount => Entries.Count(entry => entry.Status.Equals("skip", StringComparison.OrdinalIgnoreCase));
}

public sealed class E5RoleRawNormalizePreviewResult
{
    public IReadOnlyList<E5RoleRawNormalizeFilePreview> Files { get; init; } = Array.Empty<E5RoleRawNormalizeFilePreview>();
    public int ConvertCount => Files.Sum(file => file.ConvertCount);
    public int SkipCount => Files.Sum(file => file.SkipCount);
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class E5RoleRawNormalizeFileResult
{
    public string TargetFileName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public IReadOnlyList<E5RoleRawNormalizeEntryPreview> Entries { get; init; } = Array.Empty<E5RoleRawNormalizeEntryPreview>();
    public E5ImageBatchReplaceResult? WriteResult { get; init; }
    public int ConvertCount => Entries.Count(entry => entry.Status.Equals("convert", StringComparison.OrdinalIgnoreCase));
    public int SkipCount => Entries.Count(entry => entry.Status.Equals("skip", StringComparison.OrdinalIgnoreCase));
}

public sealed class E5RoleRawNormalizeResult
{
    public IReadOnlyList<E5RoleRawNormalizeFileResult> Files { get; init; } = Array.Empty<E5RoleRawNormalizeFileResult>();
    public int ConvertCount => Files.Sum(file => file.ConvertCount);
    public int SkipCount => Files.Sum(file => file.SkipCount);
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string AggregateReportPath { get; init; } = string.Empty;
}
