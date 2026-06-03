namespace CCZModStudio.Models;

public sealed class TableSaveResult
{
    public required HexTableDefinition Table { get; init; }
    public required string FilePath { get; init; }
    public int RowsWritten { get; init; }
    public int ChangedBytes { get; init; }
    public required string BackupPath { get; init; }
    public string ReportJsonPath { get; init; } = string.Empty;
}
