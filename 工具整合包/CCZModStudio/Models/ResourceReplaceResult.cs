namespace CCZModStudio.Models;

public sealed class ResourceReplaceResult
{
    public required string TargetPath { get; init; }
    public required string ReplacementPath { get; init; }
    public required string BackupPath { get; init; }
    public required string ReportPath { get; init; }
    public string ReportJsonPath { get; init; } = string.Empty;
    public long OldSizeBytes { get; init; }
    public long NewSizeBytes { get; init; }
    public int ChangedBytesEstimate { get; init; }
    public required string OldSha256 { get; init; }
    public required string NewSha256 { get; init; }
    public required string FormatCheckSummary { get; init; }
    public IReadOnlyList<string> FormatWarnings { get; init; } = Array.Empty<string>();
    public string RiskSummary { get; init; } = string.Empty;
}
