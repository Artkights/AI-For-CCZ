namespace CCZModStudio.Models;

public sealed class Qinger66Diagnostics
{
    public bool Applies { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string GameRoot { get; init; } = string.Empty;
    public string EngineVersionHint { get; init; } = string.Empty;
    public string TableVersionPrefix { get; init; } = string.Empty;
    public int? VersionResourceLowWord { get; init; }
    public long? ExeSize { get; init; }
    public string ExeSha256 { get; init; } = string.Empty;
    public string PathHint { get; init; } = string.Empty;
    public bool PathHintConflictsWithEngine { get; init; }
    public bool IsCrossVersionFallback { get; init; }
    public Qinger66TableStatusSummary TableStatusSummary { get; init; } = new();
    public Qinger66ItemAuditSummary ItemAuditSummary { get; init; } = new();
    public IReadOnlyList<Qinger66TableDiagnostic> Tables { get; init; } = Array.Empty<Qinger66TableDiagnostic>();
    public IReadOnlyList<Qinger66ResourceDiagnostic> RequiredResources { get; init; } = Array.Empty<Qinger66ResourceDiagnostic>();
    public IReadOnlyList<Qinger66ResourceDiagnostic> ObsoleteRuntimeFiles { get; init; } = Array.Empty<Qinger66ResourceDiagnostic>();
    public IReadOnlyList<string> ObsoleteResourceWarnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class Qinger66TableStatusSummary
{
    public int Total { get; init; }
    public int Native66 { get; init; }
    public int CrossVersionFallback { get; init; }
    public int ReadOnlyEvidenceOnly { get; init; }
    public int Unusable { get; init; }
    public int Writable { get; init; }
}

public sealed class Qinger66TableDiagnostic
{
    public string RequestedName { get; init; } = string.Empty;
    public string ResolvedName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public bool FileExists { get; init; }
    public long FileLength { get; init; }
    public long DataPos { get; init; }
    public long EndOffsetExclusive { get; init; }
    public int RowCount { get; init; }
    public int RowSize { get; init; }
    public string TableStatus { get; init; } = string.Empty;
    public string WriteRisk { get; init; } = string.Empty;
    public bool IsUsable { get; init; }
    public bool CanWrite { get; init; }
    public bool IsNative66 { get; init; }
    public bool IsCrossVersionFallback { get; init; }
    public bool IsReadOnlyEvidenceOnly { get; init; }
    public string SemanticValidationStatus { get; init; } = string.Empty;
    public string HiddenTailPolicy { get; init; } = string.Empty;
    public string EffectResolutionSource { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class Qinger66ResourceDiagnostic
{
    public string RelativePath { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public long SizeBytes { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Usage { get; init; } = string.Empty;
}
