namespace CCZModStudio.Models;

public class IconResourceReplacePreviewResult
{
    public string TargetPath { get; init; } = string.Empty;
    public string TargetRelativePath { get; init; } = string.Empty;
    public int IconIndex { get; init; }
    public IReadOnlyList<int> ResourceIds { get; init; } = Array.Empty<int>();
    public string SourcePath { get; init; } = string.Empty;
    public string OperationKind { get; init; } = string.Empty;
    public long OldFileSizeBytes { get; init; }
    public long SourceSizeBytes { get; init; }
    public string OldFileSha256 { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public int? SourceWidth { get; init; }
    public int? SourceHeight { get; init; }
    public string ResourceFormat { get; init; } = string.Empty;
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
    public string RiskSummary { get; init; } = string.Empty;
}

public sealed class IconResourceReplaceResult : IconResourceReplacePreviewResult
{
    public string BackupPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public long NewFileSizeBytes { get; init; }
    public int ChangedBytesEstimate { get; init; }
    public string NewFileSha256 { get; init; } = string.Empty;
}
