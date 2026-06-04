namespace CCZModStudio.Models;

public class E5ImageReplacePreviewResult
{
    public required string TargetPath { get; init; }
    public required string TargetRelativePath { get; init; }
    public required string SourcePath { get; init; }
    public int ImageNumber { get; init; }
    public int IndexOffset { get; init; }
    public int OldDataOffset { get; init; }
    public int NewDataOffset { get; init; }
    public int OldSizeBytes { get; init; }
    public int NewSizeBytes { get; init; }
    public long OldFileSizeBytes { get; init; }
    public long NewFileSizeBytes { get; init; }
    public long FileSizeDeltaBytes => NewFileSizeBytes - OldFileSizeBytes;
    public int ChangedBytesEstimate { get; init; }
    public required string OldFileSha256 { get; init; }
    public required string NewFileSha256 { get; init; }
    public required string SourceSha256 { get; init; }
    public required string OldKind { get; init; }
    public required string NewKind { get; init; }
    public int? SourceWidth { get; init; }
    public int? SourceHeight { get; init; }
    public required string Placement { get; init; }
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
    public required string RiskSummary { get; init; }
}
