namespace CCZModStudio.Models;

public sealed class ScenarioSearchResultRow
{
    public int Index { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Preview { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
    public string ActionHint { get; init; } = string.Empty;
    public ScenarioStructureRow? CommandRow { get; init; }
    public ScenarioTextEntry? TextEntry { get; init; }
}
