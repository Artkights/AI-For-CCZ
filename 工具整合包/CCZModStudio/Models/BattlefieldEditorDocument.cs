namespace CCZModStudio.Models;

public sealed class BattlefieldEditorDocument
{
    public required ScenarioFileInfo Scenario { get; init; }
    public IReadOnlyList<ScenarioTextEntry> TextEntries { get; init; } = Array.Empty<ScenarioTextEntry>();
    public ScenarioTextEntry? TitleEntry { get; init; }
    public ScenarioTextEntry? ConditionEntry { get; init; }
    public IReadOnlyList<BattlefieldCommandCandidate> CommandCandidates { get; init; } = Array.Empty<BattlefieldCommandCandidate>();
    public IReadOnlyList<BattlefieldUnitCandidate> UnitCandidates { get; init; } = Array.Empty<BattlefieldUnitCandidate>();
    public string Summary { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
}
