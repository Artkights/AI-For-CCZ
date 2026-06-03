namespace CCZModStudio.Models;

public sealed class MapImageSaveResult
{
    public required string TargetPath { get; init; }
    public required string ReplacementPath { get; init; }
    public required string BackupPath { get; init; }
    public required string ReportJsonPath { get; init; }
    public long OldSizeBytes { get; init; }
    public long NewSizeBytes { get; init; }
    public int OldWidth { get; init; }
    public int OldHeight { get; init; }
    public int NewWidth { get; init; }
    public int NewHeight { get; init; }
    public int ChangedBytesEstimate { get; init; }
    public required string OldSha256 { get; init; }
    public required string NewSha256 { get; init; }
    public string FormatCheckSummary { get; init; } = string.Empty;
    public string Warning { get; init; } = string.Empty;
}
