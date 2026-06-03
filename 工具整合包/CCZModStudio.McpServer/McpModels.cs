using System.Text.Json;
using System.Text.Json.Serialization;

namespace CCZModStudio.McpServer;

public sealed class TableRowUpdate
{
    [JsonPropertyName("row_id")]
    public int RowId { get; init; }

    [JsonPropertyName("values")]
    public Dictionary<string, JsonElement> Values { get; init; } = new(StringComparer.Ordinal);
}

public sealed class ScenarioTextUpdate
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed class HexzmapCellUpdate
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("terrain_id")]
    public int TerrainId { get; init; }
}
