namespace CCZModStudio.Models;

public sealed class MapImageReplacePreviewResult
{
    public required string TargetPath { get; init; }
    public required string TargetRelativePath { get; init; }
    public required string ReplacementPath { get; init; }
    public long OldSizeBytes { get; init; }
    public long NewSizeBytes { get; init; }
    public long SizeDeltaBytes => NewSizeBytes - OldSizeBytes;
    public int OldWidth { get; init; }
    public int OldHeight { get; init; }
    public int NewWidth { get; init; }
    public int NewHeight { get; init; }
    public int ChangedBytesEstimate { get; init; }
    public double ChangedPercent
    {
        get
        {
            var baseline = Math.Max(OldSizeBytes, NewSizeBytes);
            return baseline <= 0 ? 0 : ChangedBytesEstimate * 100.0 / baseline;
        }
    }

    public required string OldSha256 { get; init; }
    public required string NewSha256 { get; init; }
    public bool IsContentIdentical => string.Equals(OldSha256, NewSha256, StringComparison.OrdinalIgnoreCase);
    public required string FormatCheckSummary { get; init; }
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
    public required string RiskSummary { get; init; }
}
