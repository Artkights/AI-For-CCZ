namespace CCZModStudio.Models;

public sealed class BatchRImageReplaceRequest
{
    public string MaterialRoot { get; init; } = string.Empty;
    public IReadOnlySet<int> AllowedRImageIds { get; init; } = new HashSet<int>();
    public bool IncludeOnlySelectedOrFiltered { get; init; } = true;
    public string WriteMode { get; init; } = "direct";
}

public sealed class BatchSImageReplaceRequest
{
    public string MaterialRoot { get; init; } = string.Empty;
    public IReadOnlyList<BatchSImageUsage> AllowedSImageUsages { get; init; } = Array.Empty<BatchSImageUsage>();
    public bool IncludeOnlySelectedOrFiltered { get; init; } = true;
    public int FactionSlot { get; init; } = 1;
    public string WriteMode { get; init; } = "direct";
}

public sealed record BatchSImageUsage(int SImageId, int? JobId, int FactionSlot);

public sealed class BatchItemIconImportRequest
{
    public IReadOnlyList<string> SourceFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<BatchItemIconTargetRow> TargetRows { get; init; } = Array.Empty<BatchItemIconTargetRow>();
    public string MatchMode { get; init; } = "auto";
    public string WriteMode { get; init; } = "direct";
}

public sealed record BatchItemIconTargetRow(int RowId, string DisplayName, int IconIndex);

public sealed class BatchRoleFaceImportRequest
{
    public IReadOnlyList<string> SourceFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<BatchRoleFaceTargetRow> TargetRows { get; init; } = Array.Empty<BatchRoleFaceTargetRow>();
    public string MatchMode { get; init; } = "auto";
    public string WriteMode { get; init; } = "direct";
}

public sealed record BatchRoleFaceTargetRow(int RowId, string DisplayName, int FaceId);

public sealed class BatchImageImportSkippedItem
{
    public string Key { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class BatchRImageReplaceItemPreview
{
    public int RImageId { get; init; }
    public string MaterialFolder { get; init; } = string.Empty;
    public int FrontImageNumber { get; init; }
    public int BackImageNumber { get; init; }
    public string FrontSourcePath { get; init; } = string.Empty;
    public string BackSourcePath { get; init; } = string.Empty;
    public E5RawEncodeResult FrontEncode { get; init; } = new();
    public E5RawEncodeResult BackEncode { get; init; } = new();
}

public class BatchRImageReplacePreviewResult
{
    public BatchRImageReplaceRequest Request { get; init; } = new();
    public IReadOnlyList<BatchRImageReplaceItemPreview> Items { get; init; } = Array.Empty<BatchRImageReplaceItemPreview>();
    public IReadOnlyList<BatchImageImportSkippedItem> SkippedItems { get; init; } = Array.Empty<BatchImageImportSkippedItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public E5ImageBatchReplacePreviewResult? BatchPreview { get; init; }
    public bool CanWrite => Items.Count > 0 && SkippedItems.All(item => !BatchImageImportSkipReasons.IsBlocking(item.Reason));
    public int TotalOperationCount => BatchPreview?.OperationCount ?? 0;
}

public sealed class BatchRImageReplaceResult : BatchRImageReplacePreviewResult
{
    public E5ImageBatchReplaceResult? WriteResult { get; init; }
    public string AggregateReportPath { get; init; } = string.Empty;
}

public sealed class BatchSImageReplaceItemPreview
{
    public int SImageId { get; init; }
    public int? JobId { get; init; }
    public int FactionSlot { get; init; }
    public string MaterialFolder { get; init; } = string.Empty;
    public IReadOnlyList<int> ImageNumbers { get; init; } = Array.Empty<int>();
    public string MappingDetail { get; init; } = string.Empty;
    public string MovSourcePath { get; init; } = string.Empty;
    public string AtkSourcePath { get; init; } = string.Empty;
    public string SpcSourcePath { get; init; } = string.Empty;
    public E5RawEncodeResult MovEncode { get; init; } = new();
    public E5RawEncodeResult AtkEncode { get; init; } = new();
    public E5RawEncodeResult SpcEncode { get; init; } = new();
}

public class BatchSImageReplacePreviewResult
{
    public BatchSImageReplaceRequest Request { get; init; } = new();
    public IReadOnlyList<BatchSImageReplaceItemPreview> Items { get; init; } = Array.Empty<BatchSImageReplaceItemPreview>();
    public IReadOnlyList<BatchImageImportSkippedItem> SkippedItems { get; init; } = Array.Empty<BatchImageImportSkippedItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, E5ImageBatchReplacePreviewResult> FilePreviews { get; init; } = new Dictionary<string, E5ImageBatchReplacePreviewResult>();
    public bool CanWrite => Items.Count > 0 && SkippedItems.All(item => !BatchImageImportSkipReasons.IsBlocking(item.Reason));
    public int TotalOperationCount => FilePreviews.Values.Sum(preview => preview.OperationCount);
}

public sealed class BatchSImageReplaceResult : BatchSImageReplacePreviewResult
{
    public IReadOnlyDictionary<string, E5ImageBatchReplaceResult> WriteResults { get; init; } = new Dictionary<string, E5ImageBatchReplaceResult>();
    public string AggregateReportPath { get; init; } = string.Empty;
}

public sealed class BatchItemIconImportItemPreview
{
    public int RowId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public int IconIndex { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public IReadOnlyList<int> TargetImageNumbers { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> ResourceIds { get; init; } = Array.Empty<int>();
}

public class BatchItemIconImportPreviewResult
{
    public BatchItemIconImportRequest Request { get; init; } = new();
    public string TargetPath { get; init; } = string.Empty;
    public string TargetRelativePath { get; init; } = string.Empty;
    public string ResourceKind { get; init; } = string.Empty;
    public IReadOnlyList<BatchItemIconImportItemPreview> Items { get; init; } = Array.Empty<BatchItemIconImportItemPreview>();
    public IReadOnlyList<BatchImageImportSkippedItem> SkippedItems { get; init; } = Array.Empty<BatchImageImportSkippedItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IconResourceBatchReplacePreviewResult? DllPreview { get; init; }
    public E5ImageBatchReplacePreviewResult? E5Preview { get; init; }
    public bool CanWrite => Items.Count > 0 && SkippedItems.All(item =>
        !BatchImageImportSkipReasons.IsBlocking(item.Reason) &&
        !item.Reason.StartsWith(BatchImageImportSkipReasons.InvalidName, StringComparison.Ordinal));
    public int TotalOperationCount => DllPreview?.Items.Count ?? E5Preview?.OperationCount ?? 0;
}

public sealed class BatchItemIconImportResult : BatchItemIconImportPreviewResult
{
    public IconResourceBatchReplaceResult? DllResult { get; init; }
    public E5ImageBatchReplaceResult? E5Result { get; init; }
    public string AggregateReportPath { get; init; } = string.Empty;
}

public sealed class BatchRoleFaceImportItemPreview
{
    public int RowId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public int FaceId { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public string SourceKind { get; init; } = string.Empty;
    public int? SourceWidth { get; init; }
    public int? SourceHeight { get; init; }
    public string OutputKind { get; init; } = string.Empty;
    public int OutputWidth { get; init; }
    public int OutputHeight { get; init; }
    public byte[]? OutputBytes { get; init; }
    public string FormatRequirement { get; init; } = string.Empty;
    public IReadOnlyList<int> TargetImageNumbers { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> TrueColorResourceIds { get; init; } = Array.Empty<int>();
}

public class BatchRoleFaceImportPreviewResult
{
    public BatchRoleFaceImportRequest Request { get; init; } = new();
    public string TargetPath { get; init; } = string.Empty;
    public string TargetRelativePath { get; init; } = string.Empty;
    public IReadOnlyList<BatchRoleFaceImportItemPreview> Items { get; init; } = Array.Empty<BatchRoleFaceImportItemPreview>();
    public IReadOnlyList<BatchImageImportSkippedItem> SkippedItems { get; init; } = Array.Empty<BatchImageImportSkippedItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public E5ImageBatchReplacePreviewResult? E5Preview { get; init; }
    public bool CanWrite => Items.Count > 0 && SkippedItems.All(item =>
        !BatchImageImportSkipReasons.IsBlocking(item.Reason) &&
        !item.Reason.StartsWith(BatchImageImportSkipReasons.InvalidName, StringComparison.Ordinal));
    public int TotalOperationCount => E5Preview?.OperationCount ?? 0;
}

public sealed class BatchRoleFaceImportResult : BatchRoleFaceImportPreviewResult
{
    public E5ImageBatchReplaceResult? E5Result { get; init; }
    public string AggregateReportPath { get; init; } = string.Empty;
}

public static class BatchImageImportSkipReasons
{
    public const string Unused = "unused";
    public const string InvalidName = "invalid-name";
    public const string MissingFile = "missing-file";
    public const string DuplicateId = "duplicate-id";
    public const string DuplicateTarget = "duplicate-target";
    public const string UnmatchedFile = "unmatched-file";
    public const string CountMismatch = "count-mismatch";
    public const string InvalidFormat = "invalid-format";
    public const string InvalidSize = "invalid-size";

    public static bool IsBlocking(string reason)
        => reason.StartsWith(MissingFile, StringComparison.Ordinal) ||
           reason.StartsWith(DuplicateId, StringComparison.Ordinal) ||
           reason.StartsWith(DuplicateTarget, StringComparison.Ordinal) ||
           reason.StartsWith(CountMismatch, StringComparison.Ordinal) ||
           reason.StartsWith(InvalidFormat, StringComparison.Ordinal) ||
           reason.StartsWith(InvalidSize, StringComparison.Ordinal);
}
