namespace CCZModStudio.Models;

public sealed class BattlefieldEditorDocument
{
    public required ScenarioFileInfo Scenario { get; init; }
    public IReadOnlyList<ScenarioTextEntry> TextEntries { get; init; } = Array.Empty<ScenarioTextEntry>();
    public ScenarioTextEntry? TitleEntry { get; init; }
    public int CampaignId { get; init; } = -1;
    public string CampaignTitle { get; init; } = string.Empty;
    public string OriginalCampaignTitle { get; init; } = string.Empty;
    public int CampaignTitleCapacityBytes { get; init; }
    public string CampaignTitleSource { get; init; } = string.Empty;
    public bool CanWriteCampaignTitle => TitleEntry != null || (CampaignId >= 0 && CampaignTitleCapacityBytes > 0);
    public ScenarioTextEntry? ConditionEntry { get; init; }
    public IReadOnlyList<BattlefieldCommandCandidate> CommandCandidates { get; init; } = Array.Empty<BattlefieldCommandCandidate>();
    public IReadOnlyList<BattlefieldUnitCandidate> UnitCandidates { get; init; } = Array.Empty<BattlefieldUnitCandidate>();
    public string Summary { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
}
