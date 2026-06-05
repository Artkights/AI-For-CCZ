namespace CCZModStudio.Models;

public sealed class BattlefieldDeploymentWriteResult
{
    public string FilePath { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public int ChangedBytes { get; init; }
    public int RequestedPlacementCount { get; init; }
    public int WrittenRecordCount { get; init; }
    public int SkippedRecordCount { get; init; }
    public string ValidationSummary { get; init; } = string.Empty;
    public IReadOnlyList<BattlefieldDeploymentWriteChange> Changes { get; init; } = Array.Empty<BattlefieldDeploymentWriteChange>();
    public IReadOnlyList<string> SkippedReasons { get; init; } = Array.Empty<string>();
}

public sealed class BattlefieldDeploymentWriteChange
{
    public string TargetKey { get; init; } = string.Empty;
    public string CommandIdHex { get; init; } = string.Empty;
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int CommandIndex { get; init; }
    public int RecordIndex { get; init; }
    public int PersonId { get; init; }
    public int GridX { get; init; }
    public int GridY { get; init; }
    public int? AiMode { get; init; }
    public string Summary { get; init; } = string.Empty;
}
