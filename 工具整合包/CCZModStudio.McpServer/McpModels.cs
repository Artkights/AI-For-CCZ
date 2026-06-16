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

public sealed class E5ImageBatchUpdate
{
    [JsonPropertyName("image_number")]
    public int ImageNumber { get; init; }

    [JsonPropertyName("replacement_path")]
    public string ReplacementPath { get; init; } = string.Empty;

    [JsonPropertyName("source_image_number")]
    public int? SourceImageNumber { get; init; }

    [JsonPropertyName("operation_kind")]
    public string OperationKind { get; init; } = "replace";
}

public sealed class BattlefieldUnitStatusUpdate
{
    [JsonPropertyName("target_key")]
    public string TargetKey { get; init; } = string.Empty;

    [JsonPropertyName("level_bonus")]
    public int? LevelBonus { get; init; }

    [JsonPropertyName("job_level")]
    public int? JobLevel { get; init; }

    [JsonPropertyName("ai_policy")]
    public int? AiPolicy { get; init; }

    [JsonPropertyName("weapon")]
    public int? Weapon { get; init; }

    [JsonPropertyName("weapon_level")]
    public int? WeaponLevel { get; init; }

    [JsonPropertyName("armor")]
    public int? Armor { get; init; }

    [JsonPropertyName("armor_level")]
    public int? ArmorLevel { get; init; }

    [JsonPropertyName("assist")]
    public int? Assist { get; init; }

    [JsonPropertyName("job_id")]
    public int? JobId { get; init; }

    [JsonPropertyName("abilities")]
    public List<BattlefieldUnitAbilityUpdate>? Abilities { get; init; }
}

public sealed class BattlefieldUnitAbilityUpdate
{
    [JsonPropertyName("ability_id")]
    public int AbilityId { get; init; }

    [JsonPropertyName("operation")]
    public int? Operation { get; init; }

    [JsonPropertyName("value")]
    public int? Value { get; init; }
}
