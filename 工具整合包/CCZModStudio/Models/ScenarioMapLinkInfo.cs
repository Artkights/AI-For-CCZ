namespace CCZModStudio.Models;

public sealed class ScenarioMapLinkInfo
{
    public string ScenarioId { get; init; } = string.Empty;
    public string ScenarioFileName { get; init; } = string.Empty;
    public string ScenarioTitle { get; init; } = string.Empty;
    public string ScenarioKind { get; init; } = string.Empty;
    public string MapId { get; init; } = string.Empty;
    public string MapImageName { get; init; } = string.Empty;
    public bool MapImageExists { get; init; }
    public bool HexzmapBlockExists { get; init; }
    public string HexzmapOffsetHex { get; init; } = string.Empty;
    public string DominantTerrain { get; init; } = string.Empty;
    public string TopTerrainNames { get; init; } = string.Empty;
    public int KnownTerrainCount { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Annotation { get; init; } = string.Empty;
    public string Suggestion { get; init; } = string.Empty;
    public string ScenarioPath { get; init; } = string.Empty;
    public string MapImagePath { get; init; } = string.Empty;
}
