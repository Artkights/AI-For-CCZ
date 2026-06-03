namespace CCZModStudio.Models;

public sealed class ScenarioTextSaveResult
{
    public string FilePath { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public int EntriesWritten { get; init; }
    public int ChangedBytes { get; init; }
}
