namespace CCZModStudio.Models;

public sealed class E5ImageBatchReplaceRequest
{
    public int ImageNumber { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public byte[]? SourceBytes { get; init; }
    public string SourceLabel { get; init; } = string.Empty;
    public string OperationKind { get; init; } = "替换";

    public string DisplaySource => string.IsNullOrWhiteSpace(SourceLabel) ? SourcePath : SourceLabel;
}

public sealed class E5ImageBatchOperationPreviewResult
{
    public int ImageNumber { get; init; }
    public int IndexOffset { get; init; }
    public int OldDataOffset { get; init; }
    public int NewDataOffset { get; init; }
    public int OldSizeBytes { get; init; }
    public int NewSizeBytes { get; init; }
    public string OldKind { get; init; } = string.Empty;
    public string NewKind { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string OperationKind { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public int? SourceWidth { get; init; }
    public int? SourceHeight { get; init; }
    public string Placement { get; init; } = string.Empty;
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
}

public class E5ImageBatchReplacePreviewResult
{
    public string TargetPath { get; init; } = string.Empty;
    public string TargetRelativePath { get; init; } = string.Empty;
    public int OperationCount { get; init; }
    public long OldFileSizeBytes { get; init; }
    public long NewFileSizeBytes { get; init; }
    public long FileSizeDeltaBytes => NewFileSizeBytes - OldFileSizeBytes;
    public int ChangedBytesEstimate { get; init; }
    public string OldFileSha256 { get; init; } = string.Empty;
    public string NewFileSha256 { get; init; } = string.Empty;
    public IReadOnlyList<E5ImageBatchOperationPreviewResult> Operations { get; init; } = Array.Empty<E5ImageBatchOperationPreviewResult>();
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
    public string RiskSummary { get; init; } = string.Empty;
}

public sealed class E5ImageBatchReplaceResult : E5ImageBatchReplacePreviewResult
{
    public string BackupPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
}
