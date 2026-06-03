namespace CCZModStudio.Models;

public sealed class HexzmapSaveResult
{
    public required string FilePath { get; init; }
    public required string BackupPath { get; init; }
    public string ReportJsonPath { get; init; } = string.Empty;
    public int BlockIndex { get; init; }
    public string MapId { get; init; } = string.Empty;
    public string OffsetHex { get; init; } = string.Empty;
    public int ChangedCells { get; init; }
    public int ChangedBytes { get; init; }
}
