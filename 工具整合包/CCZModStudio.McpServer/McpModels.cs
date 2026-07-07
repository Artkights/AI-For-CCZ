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

    [JsonPropertyName("numeric_settings")]
    public Dictionary<string, int> NumericSettings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("job_series_names")]
    public Dictionary<int, string> JobSeriesNames { get; init; } = new();

    [JsonPropertyName("detailed_job_names")]
    public Dictionary<int, string> DetailedJobNames { get; init; } = new();
}

public sealed class AbilityTierProfileUpdate
{
    [JsonPropertyName("tier_count")]
    public int TierCount { get; init; }

    [JsonPropertyName("display_mode")]
    public string DisplayMode { get; init; } = "Letter";

    [JsonPropertyName("labels")]
    public List<string> Labels { get; init; } = [];
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

public sealed class RoleUpdate
{
    [JsonPropertyName("row_id")]
    public int RowId { get; init; }

    [JsonPropertyName("values")]
    public Dictionary<string, JsonElement> Values { get; init; } = new(StringComparer.Ordinal);

    [JsonPropertyName("face_id")]
    public int? FaceId { get; init; }

    [JsonPropertyName("r_image_id")]
    public int? RImageId { get; init; }

    [JsonPropertyName("s_image_id")]
    public int? SImageId { get; init; }
}

public sealed class RoleTextUpdate
{
    [JsonPropertyName("role_id")]
    public int RoleId { get; init; }

    [JsonPropertyName("biography")]
    public string? Biography { get; init; }

    [JsonPropertyName("critical_quote_mode")]
    public string? CriticalQuoteMode { get; init; }

    [JsonPropertyName("critical_quote_value")]
    public int? CriticalQuoteValue { get; init; }

    [JsonPropertyName("critical_quotes")]
    public List<string>? CriticalQuotes { get; init; }

    [JsonPropertyName("retreat_quote")]
    public string? RetreatQuote { get; init; }
}

public sealed class ImageAssignmentUpdate
{
    [JsonPropertyName("row_id")]
    public int RowId { get; init; }

    [JsonPropertyName("face_id")]
    public int? FaceId { get; init; }

    [JsonPropertyName("r_image_id")]
    public int? RImageId { get; init; }

    [JsonPropertyName("s_image_id")]
    public int? SImageId { get; init; }
}

public sealed class EditableImageTargetRequest
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "e5_standard";

    [JsonPropertyName("target_relative_path")]
    public string? TargetRelativePath { get; init; }

    [JsonPropertyName("image_number")]
    public int? ImageNumber { get; init; }

    [JsonPropertyName("icon_index")]
    public int? IconIndex { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("resource_format")]
    public string? ResourceFormat { get; init; }

    [JsonPropertyName("frame_width")]
    public int? FrameWidth { get; init; }

    [JsonPropertyName("frame_height")]
    public int? FrameHeight { get; init; }

    [JsonPropertyName("is_item_icon_pair")]
    public bool? IsItemIconPair { get; init; }

    [JsonPropertyName("small_image_number")]
    public int? SmallImageNumber { get; init; }

    [JsonPropertyName("large_image_number")]
    public int? LargeImageNumber { get; init; }

    [JsonPropertyName("semantic")]
    public string? Semantic { get; init; }

    [JsonPropertyName("row_id")]
    public int? RowId { get; init; }
}

public sealed class PixelEditUpdate
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("argb")]
    public string Argb { get; init; } = string.Empty;
}

public sealed class PortraitFrameTargetUpdate
{
    [JsonPropertyName("row_id")]
    public int RowId { get; init; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("face_id")]
    public int FaceId { get; init; }
}

public sealed class MapMaterialExtractionMcpRequest
{
    [JsonPropertyName("draft_id")]
    public string DraftId { get; init; } = string.Empty;

    [JsonPropertyName("material_root")]
    public string? MaterialRoot { get; init; }

    [JsonPropertyName("target_type")]
    public string TargetType { get; init; } = "terrain";

    [JsonPropertyName("terrain_id")]
    public int? TerrainId { get; init; }

    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; } = 1;

    [JsonPropertyName("height")]
    public int Height { get; init; } = 1;

    [JsonPropertyName("source")]
    public string Source { get; init; } = "current_composite";
}
