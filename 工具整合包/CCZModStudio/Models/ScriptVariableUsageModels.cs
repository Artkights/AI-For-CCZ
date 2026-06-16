namespace CCZModStudio.Models;

public sealed class ScriptVariableOccurrence
{
    public string ScenarioFileName { get; init; } = string.Empty;
    public string ScenarioPath { get; init; } = string.Empty;
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int CommandIndex { get; init; }
    public int CommandOrdinal { get; init; }
    public int CommandId { get; init; }
    public string CommandIdHex => CCZModStudio.Core.HexDisplayFormatter.Format(CommandId, 2);
    public string CommandName { get; init; } = string.Empty;
    public string VariableType { get; init; } = string.Empty;
    public int VariableAddress { get; init; }
    public string VariableAddressText => FormatAddress(VariableAddress);
    public string Scope { get; init; } = string.Empty;
    public int ParameterIndex { get; init; }
    public string ParameterSlot { get; init; } = string.Empty;
    public string AccessKind { get; init; } = string.Empty;
    public string RelatedValue { get; init; } = string.Empty;
    public int CommandOffset { get; init; }
    public string CommandOffsetHex => FormatOffset(CommandOffset);
    public int ParameterOffset { get; init; }
    public string ParameterOffsetHex => ParameterOffset >= 0 ? FormatOffset(ParameterOffset) : string.Empty;
    public bool CanEdit { get; init; }
    public string EditHint { get; init; } = string.Empty;
    public string Location => $"Scene {SceneIndex} / Section {SectionIndex} / Command {CommandIndex:000} / ord {CommandOrdinal}";

    public string SummaryKey => ScriptVariableSummary.BuildKey(VariableType, VariableAddress, Scope);

    private static string FormatAddress(int value)
        => value >= 0 ? $"{value} / {CCZModStudio.Core.HexDisplayFormatter.Format(value)}" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatOffset(int value)
        => CCZModStudio.Core.HexDisplayFormatter.FormatOffset(value);
}

public sealed class ScriptVariableSummary
{
    public string VariableType { get; init; } = string.Empty;
    public int VariableAddress { get; init; }
    public string VariableAddressText => VariableAddress >= 0
        ? $"{VariableAddress} / {CCZModStudio.Core.HexDisplayFormatter.Format(VariableAddress)}"
        : VariableAddress.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string Scope { get; init; } = string.Empty;
    public int OccurrenceCount { get; init; }
    public int ScenarioCount { get; init; }
    public string AccessKinds { get; init; } = string.Empty;
    public string CommandIds { get; init; } = string.Empty;
    public string FirstScenario { get; init; } = string.Empty;
    public bool HasEditableOccurrence { get; init; }
    public string Key => BuildKey(VariableType, VariableAddress, Scope);

    public static string BuildKey(string variableType, int variableAddress, string scope)
        => variableType + "|"
           + variableAddress.ToString(System.Globalization.CultureInfo.InvariantCulture)
           + "|"
           + scope;
}

public class ScriptVariableUsageSnapshot
{
    public IReadOnlyList<ScriptVariableSummary> Summaries { get; init; } = Array.Empty<ScriptVariableSummary>();
    public IReadOnlyList<ScriptVariableOccurrence> Occurrences { get; init; } = Array.Empty<ScriptVariableOccurrence>();
    public string SourceLabel { get; init; } = string.Empty;
    public string VersionKey { get; init; } = string.Empty;
    public DateTime BuiltAt { get; init; } = DateTime.Now;
}

public sealed class ScriptVariableProjectScanResult : ScriptVariableUsageSnapshot
{
    public int ScannedScenarioCount { get; init; }
    public IReadOnlyList<string> FailedScenarios { get; init; } = Array.Empty<string>();
}

public sealed class ScriptVariableProjectScanProgress
{
    public int Completed { get; init; }
    public int Total { get; init; }
    public string CurrentScenario { get; init; } = string.Empty;
}
