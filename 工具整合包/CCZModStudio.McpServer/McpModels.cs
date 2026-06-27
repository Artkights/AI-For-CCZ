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

public sealed class BatchSImageUsageUpdate
{
    [JsonPropertyName("s_image_id")]
    public int SImageId { get; init; }

    [JsonPropertyName("job_id")]
    public int? JobId { get; init; }

    [JsonPropertyName("faction_slot")]
    public int FactionSlot { get; init; } = 1;
}

public sealed class BatchItemIconTargetRowUpdate
{
    [JsonPropertyName("row_id")]
    public int RowId { get; init; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("icon_index")]
    public int IconIndex { get; init; }
}

public sealed class BatchStrategyIconTargetRowUpdate
{
    [JsonPropertyName("row_id")]
    public int RowId { get; init; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("icon_index")]
    public int IconIndex { get; init; }
}

public sealed class BatchRoleFaceTargetRowUpdate
{
    [JsonPropertyName("row_id")]
    public int RowId { get; init; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("face_id")]
    public int FaceId { get; init; }
}

public sealed class BmpExportTargetUpdate
{
    [JsonPropertyName("row_id")]
    public int RowId { get; init; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("field_value")]
    public int FieldValue { get; init; }

    [JsonPropertyName("job_id")]
    public int? JobId { get; init; }
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

public sealed class MapDraftSaveRequest
{
    [JsonPropertyName("draft")]
    public JsonElement? Draft { get; init; }

    [JsonPropertyName("draft_id")]
    public string DraftId { get; init; } = string.Empty;

    [JsonPropertyName("bound_map_id")]
    public string BoundMapId { get; init; } = string.Empty;

    [JsonPropertyName("grid_width")]
    public int? GridWidth { get; init; }

    [JsonPropertyName("grid_height")]
    public int? GridHeight { get; init; }

    [JsonPropertyName("base_layer_path")]
    public string? BaseLayerPath { get; init; }

    [JsonPropertyName("material_root")]
    public string? MaterialRoot { get; init; }

    [JsonPropertyName("auto_generate_map_from_terrain")]
    public bool? AutoGenerateMapFromTerrain { get; init; }

    [JsonPropertyName("beautify_generated_map")]
    public bool? BeautifyGeneratedMap { get; init; }

    [JsonPropertyName("beautify_strength")]
    public int? BeautifyStrength { get; init; }

    [JsonPropertyName("feather_radius")]
    public int? FeatherRadius { get; init; }
}

public sealed class ShopRowUpdate
{
    [JsonPropertyName("row_id")]
    public int RowId { get; init; }

    [JsonPropertyName("campaign_name")]
    public string? CampaignName { get; init; }

    [JsonPropertyName("shop_values")]
    public Dictionary<string, JsonElement> ShopValues { get; init; } = new(StringComparer.Ordinal);
}

public sealed class GlobalSettingsUpdate
{
    [JsonPropertyName("game_title")]
    public string? GameTitle { get; init; }

    [JsonPropertyName("job_series_names")]
    public Dictionary<int, string> JobSeriesNames { get; init; } = new();

    [JsonPropertyName("detailed_job_names")]
    public Dictionary<int, string> DetailedJobNames { get; init; } = new();
}

public sealed class JobSettingsUpdate
{
    [JsonPropertyName("job_series_names")]
    public Dictionary<int, string> JobSeriesNames { get; init; } = new();

    [JsonPropertyName("detailed_jobs")]
    public List<DetailedJobUpdate> DetailedJobs { get; init; } = [];

    [JsonPropertyName("restraint_matrix")]
    public List<JobMatrixCellUpdate> RestraintMatrix { get; init; } = [];

    [JsonPropertyName("attribute_matrix")]
    public List<JobMatrixCellUpdate> AttributeMatrix { get; init; } = [];
}

public sealed class DetailedJobUpdate
{
    [JsonPropertyName("job_id")]
    public int JobId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("growth_values")]
    public Dictionary<string, JsonElement> GrowthValues { get; init; } = new(StringComparer.Ordinal);

    [JsonPropertyName("pierce")]
    public int? Pierce { get; init; }
}

public sealed class JobMatrixCellUpdate
{
    [JsonPropertyName("row_id")]
    public int RowId { get; init; }

    [JsonPropertyName("column_id")]
    public int ColumnId { get; init; }

    [JsonPropertyName("value")]
    public int Value { get; init; }
}

public sealed class RSceneDraftSaveRequest
{
    [JsonPropertyName("scenario_file_name")]
    public string ScenarioFileName { get; init; } = string.Empty;

    [JsonPropertyName("background_image_number")]
    public int BackgroundImageNumber { get; init; }

    [JsonPropertyName("grid_size")]
    public int GridSize { get; init; } = 16;

    [JsonPropertyName("actors")]
    public List<RSceneActorSaveRequest> Actors { get; init; } = [];
}

public sealed class RSceneActorSaveRequest
{
    [JsonPropertyName("target_key")]
    public string TargetKey { get; init; } = string.Empty;

    [JsonPropertyName("person_id")]
    public int PersonId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("job_id")]
    public int? JobId { get; init; }

    [JsonPropertyName("job_name")]
    public string JobName { get; init; } = string.Empty;

    [JsonPropertyName("r_image_id")]
    public int RImageId { get; init; }

    [JsonPropertyName("s_image_id")]
    public int SImageId { get; init; }

    [JsonPropertyName("facing")]
    public string Facing { get; init; } = string.Empty;

    [JsonPropertyName("frame_index")]
    public int FrameIndex { get; init; }

    [JsonPropertyName("grid_x")]
    public int GridX { get; init; }

    [JsonPropertyName("grid_y")]
    public int GridY { get; init; }

    [JsonPropertyName("pixel_x")]
    public int PixelX { get; init; }

    [JsonPropertyName("pixel_y")]
    public int PixelY { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = "MCP";

    [JsonPropertyName("actor_note")]
    public string ActorNote { get; init; } = string.Empty;

    [JsonPropertyName("last_action_target_key")]
    public string LastActionTargetKey { get; init; } = string.Empty;
}

public sealed class BattlefieldDeploymentUpdate
{
    [JsonPropertyName("target_key")]
    public string TargetKey { get; init; } = string.Empty;

    [JsonPropertyName("person_id")]
    public int PersonId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("grid_x")]
    public int GridX { get; init; }

    [JsonPropertyName("grid_y")]
    public int GridY { get; init; }

    [JsonPropertyName("ai_mode")]
    public string AiMode { get; init; } = string.Empty;

    [JsonPropertyName("direction")]
    public string Direction { get; init; } = string.Empty;

    [JsonPropertyName("hidden")]
    public bool Hidden { get; init; }

    [JsonPropertyName("faction")]
    public string Faction { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = "MCP";

    [JsonPropertyName("placement_note")]
    public string PlacementNote { get; init; } = string.Empty;
}
