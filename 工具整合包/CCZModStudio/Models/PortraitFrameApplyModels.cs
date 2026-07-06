namespace CCZModStudio.Models;

public sealed class PortraitFrameApplyRequest
{
    public string FramePath { get; init; } = string.Empty;
    public IReadOnlyList<PortraitFrameTargetRow> TargetRows { get; init; } = Array.Empty<PortraitFrameTargetRow>();
    public string WriteMode { get; init; } = "direct";
}

public sealed record PortraitFrameTargetRow(int RowId, string DisplayName, int FaceId);

public sealed class PortraitFrameApplyItemPreview
{
    public int RowId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public int FaceId { get; init; }
    public string FramePath { get; init; } = string.Empty;
    public IReadOnlyList<int> TargetImageNumbers { get; init; } = Array.Empty<int>();
    public int OutputWidth { get; init; }
    public int OutputHeight { get; init; }
    public byte[]? OutputBytes { get; init; }
    public IReadOnlyDictionary<int, byte[]> OutputBytesByImageNumber { get; init; } = new Dictionary<int, byte[]>();
}

public class PortraitFrameApplyPreviewResult
{
    public PortraitFrameApplyRequest Request { get; init; } = new();
    public string TargetPath { get; init; } = string.Empty;
    public string TargetRelativePath { get; init; } = string.Empty;
    public IReadOnlyList<PortraitFrameApplyItemPreview> Items { get; init; } = Array.Empty<PortraitFrameApplyItemPreview>();
    public IReadOnlyList<BatchImageImportSkippedItem> SkippedItems { get; init; } = Array.Empty<BatchImageImportSkippedItem>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public E5ImageBatchReplacePreviewResult? E5Preview { get; init; }
    public bool CanWrite => Items.Count > 0;
    public int TotalOperationCount => E5Preview?.OperationCount ?? 0;
}

public sealed class PortraitFrameApplyResult : PortraitFrameApplyPreviewResult
{
    public E5ImageBatchReplaceResult? E5Result { get; init; }
    public string AggregateReportPath { get; init; } = string.Empty;
}
