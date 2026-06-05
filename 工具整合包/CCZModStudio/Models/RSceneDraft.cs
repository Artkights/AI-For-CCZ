namespace CCZModStudio.Models;

public sealed class RSceneDraft
{
    public string ScenarioFileName { get; set; } = string.Empty;
    public int BackgroundImageNumber { get; set; }
    public int GridSize { get; set; } = 16;
    public string UpdatedAtText { get; set; } = string.Empty;
    public string SafetyNote { get; set; } = string.Empty;
    public List<RScenePlacedActor> Actors { get; set; } = [];
}
