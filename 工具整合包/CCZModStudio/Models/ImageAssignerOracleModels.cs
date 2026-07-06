namespace CCZModStudio.Models;

public sealed class ImageAssignerOracleProfile
{
    public bool Found { get; init; }
    public string VersionKind { get; init; } = "unknown";
    public string CompatibilityStatus { get; init; } = "Missing";
    public string DirectoryPath { get; init; } = string.Empty;
    public string SystemIniPath { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public long? ExecutableLength { get; init; }
    public string ExecutableSha256 { get; init; } = string.Empty;
    public ImageAssignerOracleConfig Config { get; init; } = new();
    public IReadOnlyList<ImageAssignerOracleDependencyStatus> Dependencies { get; init; } = Array.Empty<ImageAssignerOracleDependencyStatus>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = "Official image assigner oracle is read-only unless an explicit test-copy validation mode is requested.";
}

public sealed class ImageAssignerOracleConfig
{
    public bool Found { get; init; }
    public string Path { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> RawValues { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, long> NumericValues { get; init; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> NumericHexValues { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, long> StrategyExtensionAddresses { get; init; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> ParseWarnings { get; init; } = Array.Empty<string>();
}

public sealed class ImageAssignerOracleDependencyStatus
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool Exists { get; init; }
    public long? Length { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed class ImageAssignerOracleComparison
{
    public bool HasOracle { get; init; }
    public string OracleStatus { get; init; } = "ConfigMissing";
    public string ProjectVersion { get; init; } = "unknown";
    public string OracleVersion { get; init; } = "unknown";
    public IReadOnlyList<ImageAssignerOracleCheck> Checks { get; init; } = Array.Empty<ImageAssignerOracleCheck>();
    public IReadOnlyDictionary<string, long> StrategyExtensionReadOnlyCandidates { get; init; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = "Oracle comparison is evidence for validation; it does not enable writes by itself.";
}

public sealed class ImageAssignerOracleCheck
{
    public required string Key { get; init; }
    public required string Status { get; init; }
    public string Expected { get; init; } = string.Empty;
    public string Actual { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class ImageAssignerValidationPlan
{
    public required string ChangeKind { get; init; }
    public int? RowId { get; init; }
    public string OracleVersion { get; init; } = "unknown";
    public string OfficialToolPath { get; init; } = string.Empty;
    public IReadOnlyList<string> RequiredCopies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OfficialObservationSteps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CczModStudioSteps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CompareTargets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExpectedByteRanges { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PromotionCriteria { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = "Use only test copies. Do not save from the official tool into the original project root.";
}

public sealed class ImageAssignerOutputDiffReport
{
    public required string BeforeRoot { get; init; }
    public required string OfficialAfterRoot { get; init; }
    public required string CczAfterRoot { get; init; }
    public bool Matches { get; init; }
    public IReadOnlyList<ImageAssignerFileDiffComparison> Files { get; init; } = Array.Empty<ImageAssignerFileDiffComparison>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = "Diff report compares existing directories only; it does not launch or write through the official tool.";
}

public sealed class ImageAssignerOracleExperimentResult
{
    public required string ExperimentId { get; init; }
    public required string ChangeKind { get; init; }
    public int RowId { get; init; }
    public int OriginalValue { get; init; }
    public int NewValue { get; init; }
    public required string BeforeRoot { get; init; }
    public required string OfficialCaseRoot { get; init; }
    public required string CczCaseRoot { get; init; }
    public required string TargetFile { get; init; }
    public long OfficialOffset { get; init; }
    public long CczOffset { get; init; }
    public string OfficialOffsetHex { get; init; } = string.Empty;
    public string CczOffsetHex { get; init; } = string.Empty;
    public bool SameOffset { get; init; }
    public bool SameBytes { get; init; }
    public bool RereadMatches { get; init; }
    public string OracleStatus { get; init; } = string.Empty;
    public string TableName { get; init; } = string.Empty;
    public string ColumnName { get; init; } = string.Empty;
    public ImageAssignerOutputDiffReport DiffReport { get; init; } = new()
    {
        BeforeRoot = string.Empty,
        OfficialAfterRoot = string.Empty,
        CczAfterRoot = string.Empty
    };
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = "Experiment writes only generated test copies under CCZModStudio_TestCopies.";
}

public sealed class ImageAssignerFileDiffComparison
{
    public required string RelativePath { get; init; }
    public string OfficialStatus { get; init; } = "Missing";
    public string CczStatus { get; init; } = "Missing";
    public bool Matches { get; init; }
    public string BeforeSha256 { get; init; } = string.Empty;
    public string OfficialSha256 { get; init; } = string.Empty;
    public string CczSha256 { get; init; } = string.Empty;
    public IReadOnlyList<ImageAssignerChangedRange> OfficialChangedRanges { get; init; } = Array.Empty<ImageAssignerChangedRange>();
    public IReadOnlyList<ImageAssignerChangedRange> CczChangedRanges { get; init; } = Array.Empty<ImageAssignerChangedRange>();
}

public sealed class ImageAssignerChangedRange
{
    public long Offset { get; init; }
    public int Length { get; init; }
    public string OffsetHex { get; init; } = string.Empty;
    public string EndOffsetHex { get; init; } = string.Empty;
}
