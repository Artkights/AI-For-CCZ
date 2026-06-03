namespace CCZModStudio.Models;

public sealed class ScenarioStructureFilterOptions
{
    public string Keyword { get; init; } = string.Empty;
    public bool TemplatesOnly { get; init; }
    public bool TextRelatedOnly { get; init; }
    public bool MapCoordinateOnly { get; init; }
    public bool HighRiskOnly { get; init; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Keyword) &&
        !TemplatesOnly &&
        !TextRelatedOnly &&
        !MapCoordinateOnly &&
        !HighRiskOnly;
}
