namespace CCZModStudio.Models;

public sealed class ScenarioTextSaveResult
{
    private IReadOnlyList<string>? _backupPaths;
    private IReadOnlyList<string>? _reportJsonPaths;

    public string FilePath { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public int EntriesWritten { get; init; }
    public int ChangedBytes { get; init; }
    public ScenarioTextSaveResult? TitleSave { get; init; }
    public ScenarioTextSaveResult? ConditionSave { get; init; }
    public IReadOnlyList<string> BackupPaths
    {
        get => _backupPaths ?? SinglePathOrEmpty(BackupPath);
        init => _backupPaths = value;
    }

    public IReadOnlyList<string> ReportJsonPaths
    {
        get => _reportJsonPaths ?? SinglePathOrEmpty(ReportJsonPath);
        init => _reportJsonPaths = value;
    }

    private static IReadOnlyList<string> SinglePathOrEmpty(string path)
        => string.IsNullOrWhiteSpace(path) ? Array.Empty<string>() : new[] { path };
}
