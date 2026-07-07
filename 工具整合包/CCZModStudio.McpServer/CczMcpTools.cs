using System.ComponentModel;
using CCZModStudio.Models;
using ModelContextProtocol.Server;

namespace CCZModStudio.McpServer;

[McpServerToolType]
public sealed class CczMcpTools(CczMcpRuntime runtime)
{
    [McpServerTool]
    [Description("Detect the CCZModStudio project paths, core files, and portable legacy-resource paths.")]
    public object detect_project(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection from cwd/base directory.")]
        string? game_root = null)
        => runtime.DetectProject(game_root);

    [McpServerTool]
    [Description("Read the authoritative MCP capability manifest with tool count, groups, aliases, and safety notes.")]
    public object read_mcp_capability_manifest(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.ReadMcpCapabilityManifest(game_root);

    [McpServerTool]
    [Description("List editable HexTable definitions loaded from the detected project.")]
    public object list_tables(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.ListTables(game_root);

    [McpServerTool]
    [Description("Read rows from a CCZ 6.X HexTable-backed data table.")]
    public object read_table(
        [Description("HexTable table name. Exact name is preferred; partial names are accepted when unique enough.")]
        string table_name,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional row IDs to return.")]
        List<int>? row_ids = null,
        [Description("Optional column names to return. ID is always included.")]
        List<string>? columns = null,
        [Description("Optional keyword filter applied to returned columns.")]
        string? keyword = null,
        [Description("Maximum rows to return. Defaults to 50; capped at 500.")]
        int limit = 50)
        => runtime.ReadTable(game_root, table_name, row_ids, columns, keyword, limit);

    [McpServerTool]
    [Description("Write selected rows/columns in a HexTable-backed data table. Creates a backup and structured report.")]
    public object write_table_rows(
        [Description("HexTable table name.")]
        string table_name,
        [Description("Rows to update. Each item uses row_id plus a values object keyed by column name.")]
        List<TableRowUpdate> updates,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes only native/exact tables. For a 6.6 CrossVersionFallback table, pass CrossVersionFallbackWrite to explicitly accept the non-native layout risk.")]
        string? write_mode = null)
        => runtime.WriteTableRows(game_root, table_name, updates, write_mode);

    [McpServerTool]
    [Description("Read a HexTable schema with writable/derived column metadata, offsets, storage types, and write restrictions.")]
    public object read_table_schema(
        [Description("HexTable table name.")]
        string table_name,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ReadTableSchema(game_root, table_name);

    [McpServerTool]
    [Description("Read derived/display columns for selected table rows. Read-only.")]
    public object read_table_derived_display(
        [Description("HexTable table name.")]
        string table_name,
        [Description("Optional row IDs to return.")]
        List<int>? row_ids = null,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Maximum rows to return. Defaults to 50; capped at 500.")]
        int limit = 50)
        => runtime.ReadTableDerivedDisplay(game_root, table_name, row_ids, limit);

    [McpServerTool]
    [Description("Export a HexTable-backed table to CSV under CCZModStudio_Exports/TableCsv or an explicit output path. Read-only for game files.")]
    public object export_table_csv(
        [Description("HexTable table name.")]
        string table_name,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output path. Relative paths resolve under the workspace.")]
        string? output_path = null,
        [Description("Optional column names to export. ID is always included.")]
        List<string>? columns = null,
        [Description("Optional row IDs to export.")]
        List<int>? row_ids = null,
        [Description("Optional keyword filter.")]
        string? keyword = null,
        [Description("Include a second CSV annotation row for AI-readable column notes.")]
        bool include_annotation_row = false,
        [Description("Maximum rows to export. Defaults to 10000; capped at 50000.")]
        int limit = 10000)
        => runtime.ExportTableCsv(game_root, table_name, output_path, columns, row_ids, keyword, include_annotation_row, limit);

    [McpServerTool]
    [Description("Preview CSV import into a HexTable-backed table without writing game files. Only writable columns are applied.")]
    public object preview_import_table_csv(
        [Description("HexTable table name.")]
        string table_name,
        [Description("CSV path. Relative paths resolve from workspace, project root, then cwd.")]
        string csv_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Allow CSV to contain a subset of table columns.")]
        bool allow_partial_columns = true,
        [Description("Match rows by ID column when present.")]
        bool match_by_id_when_present = true)
        => runtime.PreviewImportTableCsv(game_root, table_name, csv_path, allow_partial_columns, match_by_id_when_present);

    [McpServerTool]
    [Description("Apply CSV import into a HexTable-backed table. Creates backup, structured report, and reread validation.")]
    public object apply_import_table_csv(
        [Description("HexTable table name.")]
        string table_name,
        [Description("CSV path. Relative paths resolve from workspace, project root, then cwd.")]
        string csv_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null,
        [Description("Allow CSV to contain a subset of table columns.")]
        bool allow_partial_columns = true,
        [Description("Match rows by ID column when present.")]
        bool match_by_id_when_present = true)
        => runtime.ApplyImportTableCsv(game_root, table_name, csv_path, write_mode, allow_partial_columns, match_by_id_when_present);

    [McpServerTool]
    [Description("Read GBK scenario text candidates from an RS/R_*.eex or RS/S_*.eex file.")]
    public object read_scenario_texts(
        [Description("Project-relative scenario path, for example RS/R_00.eex.")]
        string relative_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional keyword filter.")]
        string? keyword = null,
        [Description("Maximum entries to return. Defaults to 100; capped at 2000.")]
        int limit = 100)
        => runtime.ReadScenarioTexts(game_root, relative_path, keyword, limit);

    [McpServerTool]
    [Description("List R/S eex scenario files from the project RS directory.")]
    public object list_scenario_files(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional kind filter: R, S, R剧本, or S剧本.")]
        string? kind = null,
        [Description("Optional keyword across file name, id, kind, and annotation.")]
        string? keyword = null,
        [Description("Maximum files to return. Defaults to 200; capped at 1000.")]
        int limit = 200)
        => runtime.ListScenarioFiles(game_root, kind, keyword, limit);

    [McpServerTool]
    [Description("Read legacy Scene/Section/Command summaries and parameters from one R/S eex file. Read-only.")]
    public object read_scenario_commands(
        [Description("Project-relative scenario path, for example RS/S_00.eex.")]
        string relative_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional Scene index filter, 1-based.")]
        int? scene_index = null,
        [Description("Optional Section index filter, 1-based.")]
        int? section_index = null,
        [Description("Optional command id/name filter, for example 0x72 or 对话.")]
        string? command_filter = null,
        [Description("Optional keyword across command name, id, offset, and parameter previews.")]
        string? keyword = null,
        [Description("Maximum commands to return. Defaults to 200; capped at 2000.")]
        int limit = 200)
        => runtime.ReadScenarioCommands(game_root, relative_path, scene_index, section_index, command_filter, keyword, limit);

    [McpServerTool]
    [Description("List writable battlefield unit status targets found in S scenario deployment commands.")]
    public object list_battlefield_unit_status_targets(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional project-relative S scenario path, for example RS/S_00.eex. If omitted, scans all S scenarios.")]
        string? relative_path = null,
        [Description("Optional keyword across target key, person, command, location, and annotation.")]
        string? keyword = null,
        [Description("Maximum targets to return. Defaults to 200; capped at 1000.")]
        int limit = 200)
        => runtime.ListBattlefieldUnitStatusTargets(game_root, relative_path, keyword, limit);

    [McpServerTool]
    [Description("Read a writable battlefield unit status draft by target key. Read-only.")]
    public object read_battlefield_unit_status(
        [Description("Project-relative S scenario path, for example RS/S_00.eex.")]
        string relative_path,
        [Description("Target key returned by list_battlefield_unit_status_targets.")]
        string target_key,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ReadBattlefieldUnitStatus(game_root, relative_path, target_key);

    [McpServerTool]
    [Description("Write selected battlefield unit status fields for a target key. Creates backup, report, and reread validation.")]
    public object write_battlefield_unit_status(
        [Description("Project-relative S scenario path, for example RS/S_00.eex.")]
        string relative_path,
        [Description("Status update. target_key must come from list_battlefield_unit_status_targets.")]
        BattlefieldUnitStatusUpdate update,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.WriteBattlefieldUnitStatus(game_root, relative_path, update, write_mode);

    [McpServerTool]
    [Description("Search command and text content in one or more R/S eex scenario files. Read-only.")]
    public object search_scenario_scripts(
        [Description("Required keyword across command names/ids/parameters and GBK text entries.")]
        string keyword,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional project-relative scenario path. If omitted, scans R/S files by kind up to max_files.")]
        string? relative_path = null,
        [Description("Optional kind filter when relative_path is omitted: R, S, R剧本, or S剧本.")]
        string? file_kind = null,
        [Description("Maximum matches to return. Defaults to 100; capped at 1000.")]
        int limit = 100,
        [Description("Maximum files to scan when relative_path is omitted. Defaults to 20; capped at 200.")]
        int max_files = 20)
        => runtime.SearchScenarioScripts(game_root, keyword, relative_path, file_kind, limit, max_files);

    [McpServerTool]
    [Description("List read-only R/S eex scenario command parameter templates from the local 6.5 command dictionary.")]
    public object list_scenario_command_templates(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional keyword across command id, dictionary name, template name, purpose, risks, and slots.")]
        string? keyword = null,
        [Description("Optional category filter, for example 剧情/文本, 流程/变量, 人物/战场单位.")]
        string? category = null,
        [Description("Optional template status filter, for example 已覆盖 or 待补充.")]
        string? status = null,
        [Description("Maximum rows to return. Defaults to 100; capped at 1000.")]
        int limit = 100)
        => runtime.ListScenarioCommandTemplates(game_root, keyword, category, status, limit);

    [McpServerTool]
    [Description("Read one read-only R/S eex scenario command parameter template by id or name.")]
    public object read_scenario_command_template(
        [Description("Command id such as 0x78, or a dictionary/template name.")]
        string command,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.ReadScenarioCommandTemplate(command, game_root);

    [McpServerTool]
    [Description("List Hexzmap terrain blocks resolved from Map/Mxxx.jpg dimensions. Read-only.")]
    public object list_hexzmap_blocks(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional keyword across map id, map image name, offsets, terrain names, and annotation.")]
        string? keyword = null,
        [Description("When true, return only blocks whose segment length matches the map grid and can be written by the dedicated Hexzmap writer.")]
        bool editable_only = false,
        [Description("Maximum blocks to return. Defaults to 200; capped at 1000.")]
        int limit = 200)
        => runtime.ListHexzmapBlocks(game_root, keyword, editable_only, limit);

    [McpServerTool]
    [Description("Read one Hexzmap terrain block and optional cell matrix by map id, for example M000. Read-only.")]
    public object read_hexzmap_block(
        [Description("Map ID, for example M000.")]
        string map_id,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Whether to include terrain cell rows. Defaults to true.")]
        bool include_cells = true,
        [Description("Maximum cell rows to return when include_cells is true. Defaults to 120; capped at 500.")]
        int max_rows = 120)
        => runtime.ReadHexzmapBlock(game_root, map_id, include_cells, max_rows);

    [McpServerTool]
    [Description("Write scenario text entries in place. Text must fit the original GBK byte capacity.")]
    public object write_scenario_texts(
        [Description("Project-relative scenario path, for example RS/R_00.eex.")]
        string relative_path,
        [Description("Text updates keyed by reader index.")]
        List<ScenarioTextUpdate> updates,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.WriteScenarioTexts(game_root, relative_path, updates, write_mode);

    [McpServerTool]
    [Description("Restore one R/S scenario file from a verified backup, backing up the current target first and validating by legacy reread.")]
    public object restore_scenario_backup(
        [Description("Project-relative scenario path, for example RS/S_02.eex.")]
        string relative_path,
        [Description("Backup file path. Relative paths resolve from the selected game root.")]
        string backup_path,
        [Description("Expected SHA256 of backup_path.")]
        string expected_backup_sha256,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.RestoreScenarioBackup(game_root, relative_path, backup_path, expected_backup_sha256, write_mode);

    [McpServerTool]
    [Description("Write Hexzmap terrain cells for a map block. Creates a backup and structured report.")]
    public object write_hexzmap_block(
        [Description("Map ID, for example M000.")]
        string map_id,
        [Description("Cell changes using x/y grid coordinates and terrain_id 0..255.")]
        List<HexzmapCellUpdate> changes,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.WriteHexzmapBlock(game_root, map_id, changes, write_mode);

    [McpServerTool]
    [Description("Preview replacing a project Map/*.jpg image without writing. Returns dimensions, sizes, hashes, estimated changed bytes, format checks, and risk warnings.")]
    public object preview_map_image(
        [Description("Project-relative target path, for example Map/M000.jpg.")]
        string target_relative_path,
        [Description("Replacement image path. Relative paths resolve from workspace root first.")]
        string replacement_path,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewMapImage(game_root, target_relative_path, replacement_path);

    [McpServerTool]
    [Description("Replace a project Map/*.jpg image with another JPEG. Creates a backup and structured report.")]
    public object replace_map_image(
        [Description("Project-relative target path, for example Map/M000.jpg.")]
        string target_relative_path,
        [Description("Replacement image path. Relative paths resolve from workspace root first.")]
        string replacement_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceMapImage(game_root, target_relative_path, replacement_path, write_mode);

    [McpServerTool]
    [Description("Preview replacement of a non-core project resource file without writing. Returns size, hash, format checks, and risk notes.")]
    public object preview_resource_replace(
        [Description("Project-relative target resource path.")]
        string target_relative_path,
        [Description("Replacement file path. Relative paths resolve from workspace root first.")]
        string replacement_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Require target and replacement extensions to match.")]
        bool require_same_extension = true)
        => runtime.PreviewResourceReplace(game_root, target_relative_path, replacement_path, require_same_extension);

    [McpServerTool]
    [Description("List known image resource files, including E5 indexed resources and DLL bitmap icon resources.")]
    public object list_image_resources(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional category/key/name/path keyword filter.")]
        string? keyword = null,
        [Description("Only return resources with at least one previewable entry.")]
        bool previewable_only = false,
        [Description("Only return resources that support replacement.")]
        bool replaceable_only = false,
        [Description("Maximum resources to return. Defaults to 100; capped at 1000.")]
        int limit = 100)
        => runtime.ListImageResources(game_root, keyword, previewable_only, replaceable_only, limit);

    [McpServerTool]
    [Description("List entries inside one image resource resolved by key, display name, or file name. Supports E5 indexed images and DLL icon resources.")]
    public object list_image_resource_entries(
        [Description("Image resource key, file name, display name, or project-relative path such as Face, Unit_mov.e5, or Itemicon.dll.")]
        string resource,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional keyword across usage/kind/category/resource names.")]
        string? keyword = null,
        [Description("Maximum entries to return. Defaults to 200; capped at 10000.")]
        int limit = 200)
        => runtime.ListImageResourceEntries(game_root, resource, keyword, limit);

    [McpServerTool]
    [Description("Render one image resource entry to a PNG file under CCZModStudio_Exports/ImagePreviews. Read-only.")]
    public object export_image_resource_preview(
        [Description("Image resource key, file name, display name, or project-relative path.")]
        string resource,
        [Description("E5 image number is 1-based; DLL icon index is 0-based.")]
        int image_number,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Preview canvas width. Defaults to 360; capped at 2048.")]
        int width = 360,
        [Description("Preview canvas height. Defaults to 260; capped at 2048.")]
        int height = 260)
        => runtime.ExportImageResourcePreview(game_root, resource, image_number, width, height);

    [McpServerTool]
    [Description("Preview an item or strategy icon by the table field value. 6.6 item icons map to E5/Item.e5 small #2N+1 and large #2N+2; strategy icons map to E5/Mtem.e5 #N+1.")]
    public object preview_item_icon_field(
        [Description("Icon field value from the table, for example item 图标=N.")]
        int field_value,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Icon kind: item or strategy. Defaults to item.")]
        string? kind = "item",
        [Description("Preview canvas size. Defaults to 96; capped at 512.")]
        int canvas_size = 96)
        => runtime.PreviewItemIconField(game_root, field_value, kind, canvas_size);

    [McpServerTool]
    [Description("Replace a 6.6 Item.e5 item-icon pair by table field value. Writes small #2N+1 as 16x16 BMP and large #2N+2 as 32x32 BMP with backup/report/reread validation.")]
    public object replace_item_icon_pair(
        [Description("Item icon table field value N.")]
        int field_value,
        [Description("Large/source image path. Relative paths resolve from workspace/game/current directory.")]
        string large_source,
        [Description("Optional explicit small image source. If omitted, small is downscaled from large_source.")]
        string? small_source = null,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes with backups and reports.")]
        string? write_mode = null)
        => runtime.ReplaceItemIconPair(game_root, field_value, large_source, small_source, write_mode);

    [McpServerTool]
    [Description("Export importable BMP material files for job S, R, S, face, item icon, or strategy icon assets. Writes BMP files only; no game resources are modified.")]
    public object export_bmp_assets(
        [Description("Export kind: job_s_image, r_image, s_image, face, item_icon, or strategy_icon.")]
        string kind,
        [Description("Output directory. Relative paths resolve from the workspace root. The directory is created when needed.")]
        string output_root,
        [Description("Targets to export. Each item uses row_id, display_name, field_value, and optional job_id.")]
        List<BmpExportTargetUpdate> targets,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Single-item layout. Defaults to true when one target is supplied; false when multiple targets are supplied.")]
        bool? single_mode = null,
        [Description("Overwrite existing BMP files. Defaults false.")]
        bool overwrite_existing = false,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1)
        => runtime.ExportBmpAssets(game_root, kind, output_root, targets, single_mode, overwrite_existing, faction_slot);

    [McpServerTool]
    [Description("List AI drawable CCZ image asset presets for Image Studio style headless generation.")]
    public object list_ccz_image_asset_presets()
        => runtime.ListCczImageAssetPresets();

    [McpServerTool]
    [Description("Build an AI image prompt and target mapping plan for a CCZ 6.5 image asset. Does not call the network.")]
    public object build_ccz_image_prompt(
        [Description("Preset key: r_background, dll_icon, face, r_actor, or s_unit.")]
        string preset,
        [Description("Natural-language description of the desired image.")]
        string description,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional project-relative target resource path. Defaults from preset.")]
        string? target_relative_path = null,
        [Description("Optional direct target image number. E5 numbers are 1-based; DLL icon indexes are 0-based.")]
        int? image_number = null,
        [Description("Optional R image id. r_actor maps R=n to Pmapobj.e5 images 2n+1/2n+2.")]
        int? r_image_id = null,
        [Description("Optional S image id. s_unit maps compact S ids to Unit image numbers.")]
        int? s_image_id = null,
        [Description("Optional Data face id. face maps Data face id to Face.e5 image number.")]
        int? face_id = null,
        [Description("Optional job id for S=0 default unit mapping.")]
        int? job_id = null,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1,
        [Description("Optional final output format: png, jpg, or bmp.")]
        string? output_format = null,
        [Description("Optional final output width.")]
        int? width = null,
        [Description("Optional final output height.")]
        int? height = null,
        [Description("Optional reference image paths. For R/S RetroDiffusion these are sent as reference_images; roles should usually be design, format_action, style_optional.")]
        List<string>? reference_image_paths = null,
        [Description("Optional roles for reference_image_paths. Must be empty or the same length as reference_image_paths.")]
        List<string>? reference_roles = null)
        => runtime.BuildCczImagePrompt(game_root, preset, description, target_relative_path, image_number, r_image_id, s_image_id, face_id, job_id, faction_slot, output_format, width, height, reference_image_paths, reference_roles);

    [McpServerTool]
    [Description("Prepare an existing generated image for a CCZ 6.5 asset and run replacement preview. Writes only CCZModStudio_Exports.")]
    public object prepare_ccz_generated_image(
        [Description("Preset key: r_background, dll_icon, face, r_actor, or s_unit.")]
        string preset,
        [Description("Natural-language description used for prompt/manifest context.")]
        string description,
        [Description("Source image path. Relative paths resolve from workspace, project root, then cwd.")]
        string source_image_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional project-relative target resource path. Defaults from preset.")]
        string? target_relative_path = null,
        [Description("Optional direct target image number. E5 numbers are 1-based; DLL icon indexes are 0-based.")]
        int? image_number = null,
        [Description("Optional R image id. r_actor maps R=n to Pmapobj.e5 images 2n+1/2n+2.")]
        int? r_image_id = null,
        [Description("Optional S image id. s_unit maps compact S ids to Unit image numbers.")]
        int? s_image_id = null,
        [Description("Optional Data face id. face maps Data face id to Face.e5 image number.")]
        int? face_id = null,
        [Description("Optional job id for S=0 default unit mapping.")]
        int? job_id = null,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1,
        [Description("Optional final output format: png, jpg, or bmp.")]
        string? output_format = null,
        [Description("Optional final output width.")]
        int? width = null,
        [Description("Optional final output height.")]
        int? height = null)
        => runtime.PrepareCczGeneratedImage(game_root, preset, description, source_image_path, target_relative_path, image_number, r_image_id, s_image_id, face_id, job_id, faction_slot, output_format, width, height);

    [McpServerTool]
    [Description("Draw a CCZ 6.5 image asset, then post-process and preview replacement. R/S use RetroDiffusion by default; other presets use Image Studio. Does not write game resources.")]
    public object draw_ccz_image_asset(
        [Description("Preset key: r_background, dll_icon, face, r_actor, or s_unit.")]
        string preset,
        [Description("Natural-language description of the desired image.")]
        string description,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional project-relative target resource path. Defaults from preset.")]
        string? target_relative_path = null,
        [Description("Optional direct target image number. E5 numbers are 1-based; DLL icon indexes are 0-based.")]
        int? image_number = null,
        [Description("Optional R image id. r_actor maps R=n to Pmapobj.e5 images 2n+1/2n+2.")]
        int? r_image_id = null,
        [Description("Optional S image id. s_unit maps compact S ids to Unit image numbers.")]
        int? s_image_id = null,
        [Description("Optional Data face id. face maps Data face id to Face.e5 image number.")]
        int? face_id = null,
        [Description("Optional job id for S=0 default unit mapping.")]
        int? job_id = null,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1,
        [Description("Optional final output format: png, jpg, or bmp.")]
        string? output_format = null,
        [Description("Optional final output width.")]
        int? width = null,
        [Description("Optional final output height.")]
        int? height = null,
        [Description("When true, only returns prompt and target plan without network calls.")]
        bool dry_run = true,
        [Description("Optional reference image paths. For R/S RetroDiffusion these are sent as reference_images; roles should usually be design, format_action, style_optional.")]
        List<string>? reference_image_paths = null,
        [Description("Optional roles for reference_image_paths. Must be empty or the same length as reference_image_paths.")]
        List<string>? reference_roles = null)
        => runtime.DrawCczImageAsset(game_root, preset, description, target_relative_path, image_number, r_image_id, s_image_id, face_id, job_id, faction_slot, output_format, width, height, dry_run, reference_image_paths, reference_roles);

    [McpServerTool]
    [Description("Draw, post-process, and directly replace a CCZ image asset in an E5 or DLL resource. Creates backups and reports.")]
    public object draw_and_replace_ccz_image_asset(
        [Description("Preset key: r_background, dll_icon, face, r_actor, or s_unit.")]
        string preset,
        [Description("Natural-language description of the desired image.")]
        string description,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional project-relative target resource path. Defaults from preset.")]
        string? target_relative_path = null,
        [Description("Optional direct target image number. E5 numbers are 1-based; DLL icon indexes are 0-based.")]
        int? image_number = null,
        [Description("Optional R image id. r_actor maps R=n to Pmapobj.e5 images 2n+1/2n+2.")]
        int? r_image_id = null,
        [Description("Optional S image id. s_unit maps compact S ids to Unit image numbers.")]
        int? s_image_id = null,
        [Description("Optional Data face id. face maps Data face id to Face.e5 image number.")]
        int? face_id = null,
        [Description("Optional job id for S=0 default unit mapping.")]
        int? job_id = null,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1,
        [Description("Optional final output format: png, jpg, or bmp.")]
        string? output_format = null,
        [Description("Optional final output width.")]
        int? width = null,
        [Description("Optional final output height.")]
        int? height = null,
        [Description("Optional reference image paths. For R/S RetroDiffusion these are sent as reference_images; roles should usually be design, format_action, style_optional.")]
        List<string>? reference_image_paths = null,
        [Description("Optional roles for reference_image_paths. Must be empty or the same length as reference_image_paths.")]
        List<string>? reference_roles = null)
        => runtime.DrawAndReplaceCczImageAsset(game_root, preset, description, target_relative_path, image_number, r_image_id, s_image_id, face_id, job_id, faction_slot, output_format, width, height, reference_image_paths, reference_roles);

    [McpServerTool]
    [Description("Build an MCP-first R/S character pixel package from a design image and a format/action reference image or folder. It records reference images, writes package reports and prompt plans, and can optionally call RetroDiffusion generation. It never writes game resources.")]
    public object build_rs_pixel_character_design(
        [Description("Package id under CCZModStudio_Exports/RS_PixelDesign, for example SunCe_MCP_SingleSpearCavalry_v1.")]
        string package_id,
        [Description("Design image path. Relative paths resolve from workspace, project root, then cwd.")]
        string design_image_path,
        [Description("Character display name.")]
        string display_name = "孙策 MCP 单枪枪骑兵 v1",
        [Description("Unit type profile, for example spear_cavalry.")]
        string unit_type = "spear_cavalry",
        [Description("Optional format/action reference image path. If omitted, format_reference_folder or the default local S64 mounted reference is used.")]
        string? format_action_image_path = null,
        [Description("Optional folder containing format/action reference images such as mov.bmp/atk.bmp/spc.bmp or a contact sheet.")]
        string? format_reference_folder = null,
        [Description("Optional game root used to export an S image format reference when no format image/folder is supplied.")]
        string? format_reference_game_root = null,
        [Description("Optional S image id used with format_reference_game_root for format reference export.")]
        int? format_reference_s_image_id = null,
        [Description("Optional row id label for exported format reference.")]
        int? format_reference_row_id = null,
        [Description("Optional display name label for exported format reference.")]
        string? format_reference_display_name = null,
        [Description("Character brief. The design image remains authoritative for appearance; this text records constraints.")]
        string character_brief = "孙策；江东主将；黑金重甲；金冠束发；红黑披风；主将气质；曹操传 6.5 短身骑乘战棋小人。",
        [Description("Weapon brief. For this workflow default is single spear cavalry.")]
        string weapon_brief = "唯一武器是一把长枪/长矛；攻击为蓄力、突刺、挑击、枪尖白金枪芒爆发、收枪。",
        [Description("Forbidden visual readings.")]
        string forbidden_readings = "禁止剑、短刃、腰剑、背剑、第二枪、双武器、宽白剑弧、现代像素贴纸、程序化几何块。",
        [Description("When true, call draw pipeline. When false, only create package, reference records, and prompt plans.")]
        bool generate_now = false,
        [Description("Passed to draw pipeline when generate_now=true. dry_run=true does not call the network.")]
        bool dry_run = true,
        [Description("Optional R image id for mapping preview/plan.")]
        int? r_image_id = null,
        [Description("Optional S image id for mapping preview/plan.")]
        int? s_image_id = null,
        [Description("Optional job id when S=0 default unit mapping is used.")]
        int? job_id = null,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.BuildRsPixelCharacterDesign(game_root, package_id, display_name, unit_type, design_image_path, format_action_image_path, format_reference_folder, format_reference_game_root, format_reference_s_image_id, format_reference_row_id, format_reference_display_name, character_brief, weapon_brief, forbidden_readings, generate_now, dry_run, r_image_id, s_image_id, job_id, faction_slot);

    [McpServerTool]
    [Description("Create a local MCP R/S pixel-edit workspace from local design and format-reference BMPs. No external image generation and no game resource writes.")]
    public object create_rs_pixel_edit_workspace(
        [Description("Package id under CCZModStudio_Exports/RS_PixelDesign, for example SunCe_LocalPixelEditor_SingleSpearCavalry_v1.")]
        string package_id,
        [Description("Display name for reports, for example 孙策.")]
        string display_name,
        [Description("Unit type, for example spear_cavalry.")]
        string unit_type,
        [Description("Local design image path. Used as visual reference record only.")]
        string design_image_path,
        [Description("Folder containing front.bmp/back.bmp/mov.bmp/atk.bmp/spc.bmp, or materials/r_actor and materials/s_unit.")]
        string format_reference_root,
        [Description("When true, rebuild an existing workspace folder.")]
        bool overwrite_existing = false,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.CreateRsPixelEditWorkspace(game_root, package_id, display_name, unit_type, design_image_path, format_reference_root, overwrite_existing);

    [McpServerTool]
    [Description("Build a deterministic local R/S pixel-edit recipe for an existing workspace. Does not call external image providers.")]
    public object build_rs_pixel_edit_plan(
        [Description("Workspace package root, usually CCZModStudio_Exports/RS_PixelDesign/<PackageId>.")]
        string package_root,
        [Description("Unit type, for example spear_cavalry.")]
        string unit_type = "spear_cavalry",
        [Description("Character visual brief. Does not override explicit edit operations.")]
        string character_brief = "",
        [Description("Weapon brief. For spear cavalry this should require exactly one long spear/lance.")]
        string weapon_brief = "")
        => runtime.BuildRsPixelEditPlan(package_root, unit_type, character_brief, weapon_brief);

    [McpServerTool]
    [Description("Apply audited frame-level local pixel edits to R/S BMP materials in a workspace. No external image generation and no game resource writes.")]
    public object apply_rs_pixel_frame_edits(
        [Description("Workspace package root, usually CCZModStudio_Exports/RS_PixelDesign/<PackageId>.")]
        string package_root,
        [Description("Edit operations. Supported operations include recolor_palette, tint_region_by_luminance, clean_face_box, erase_weapon_residue, erase_effect_residue, erase_rect_to_magenta, draw_spear_axis, draw_spear_tip, draw_spear_tip_diamond, draw_spear_effect, draw_polyline, paint_pixel_runs, paint_region_mask, repaint_armor_blocks, repaint_cape_blocks, magenta_key_cleanup, copy_region_from_reference, copy_region_from_frame.")]
        List<RsPixelFrameEditOperation> operations)
        => runtime.ApplyRsPixelFrameEdits(package_root, operations);

    [McpServerTool]
    [Description("Export original-size-aware contact sheets for a local R/S pixel-edit workspace. Writes only under the workspace drafts folder.")]
    public object export_rs_pixel_contact_sheets(
        [Description("Workspace package root, usually CCZModStudio_Exports/RS_PixelDesign/<PackageId>.")]
        string package_root,
        [Description("Nearest-neighbor preview scale. Defaults 4.")]
        int scale = 4,
        [Description("Annotate face boxes and spear guide lines.")]
        bool annotate = true)
        => runtime.ExportRsPixelContactSheets(package_root, scale, annotate);

    [McpServerTool]
    [Description("Validate a local R/S pixel-edit workspace, including existing package validation plus local frame, face, and single-spear checks. Does not write game resources.")]
    public object validate_rs_pixel_edit_workspace(
        [Description("Workspace package root, usually CCZModStudio_Exports/RS_PixelDesign/<PackageId>.")]
        string package_root,
        [Description("Optional R image id for read-only replacement preview.")]
        int? r_image_id = null,
        [Description("Optional S image id for read-only replacement preview.")]
        int? s_image_id = null,
        [Description("Optional job id for S=0 default unit mapping.")]
        int? job_id = null,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ValidateRsPixelEditWorkspace(game_root, package_root, r_image_id, s_image_id, job_id, faction_slot);

    [McpServerTool]
    [Description("Build a read-only R/S sample-learning MVP workspace from local real 6.5 reference exports. Generates normalized candidates, metrics, contact sheets, annotation templates, and reports without writing game resources or local_sample_index.json.")]
    public object build_rs_pixel_sample_learning_mvp(
        [Description("Unit type to learn. Defaults to spear_cavalry.")]
        string unit_type = "spear_cavalry",
        [Description("Output folder id under CCZModStudio_Exports/RS_PixelDesign/_sample_learning. Defaults to spear_cavalry_mvp.")]
        string output_id = "spear_cavalry_mvp",
        [Description("When true, rebuild an existing sample-learning output folder.")]
        bool overwrite_existing = false,
        [Description("Number of complete candidates to place in the top review set.")]
        int top_review_count = 12,
        [Description("Optional local roots to scan for real reference exports. Defaults to known local reference/base roots.")]
        List<string>? reference_roots = null,
        [Description("Optional local roots to scan as negative cases. Defaults to old failed SunCe/smoke/procedural packages when present.")]
        List<string>? negative_roots = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.BuildRsPixelSampleLearningMvp(game_root, unit_type, output_id, overwrite_existing, top_review_count, reference_roots, negative_roots);

    [McpServerTool]
    [Description("List image index entries from an E5 image resource such as Face.e5 or Unit_mov.e5.")]
    public object list_e5_image_entries(
        [Description("Project-relative E5 resource path, for example Unit_mov.e5.")]
        string target_relative_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Maximum entries to return. Defaults to 2000; capped at 10000.")]
        int limit = 2000)
        => runtime.ListE5ImageEntries(game_root, target_relative_path, limit);

    [McpServerTool]
    [Description("Preview replacement of one E5 image index entry without writing. Supports a normal image file or an E5 backup entry as source.")]
    public object preview_e5_image_replace(
        [Description("Project-relative E5 resource path, for example Unit_mov.e5.")]
        string target_relative_path,
        [Description("Target E5 image number, 1-based.")]
        int image_number,
        [Description("Replacement image file path, or an E5 file path when source_image_number is supplied. Relative paths resolve from workspace, project root, then cwd.")]
        string replacement_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional 1-based image number to read from replacement_path when replacement_path is an E5 file.")]
        int? source_image_number = null)
        => runtime.PreviewE5ImageReplace(game_root, target_relative_path, image_number, replacement_path, source_image_number);

    [McpServerTool]
    [Description("Replace one E5 image index entry. Creates a backup, writes a structured report, and verifies by rereading the written entry.")]
    public object replace_e5_image_entry(
        [Description("Project-relative E5 resource path, for example Unit_mov.e5.")]
        string target_relative_path,
        [Description("Target E5 image number, 1-based.")]
        int image_number,
        [Description("Replacement image file path, or an E5 file path when source_image_number is supplied. Relative paths resolve from workspace, project root, then cwd.")]
        string replacement_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null,
        [Description("Optional 1-based image number to read from replacement_path when replacement_path is an E5 file.")]
        int? source_image_number = null)
        => runtime.ReplaceE5ImageEntry(game_root, target_relative_path, image_number, replacement_path, write_mode, source_image_number);

    [McpServerTool]
    [Description("Preview batch replacement of multiple E5 image index entries without writing.")]
    public object preview_e5_image_batch_replace(
        [Description("Project-relative E5 resource path, for example Unit_mov.e5.")]
        string target_relative_path,
        [Description("Batch operations. Each item uses image_number plus replacement_path, and optionally source_image_number when replacement_path is another E5 file.")]
        List<E5ImageBatchUpdate> updates,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewE5ImageBatchReplace(game_root, target_relative_path, updates);

    [McpServerTool]
    [Description("Replace multiple E5 image index entries in one write. Creates one backup, writes reports, and verifies each entry by rereading.")]
    public object replace_e5_image_batch(
        [Description("Project-relative E5 resource path, for example Unit_mov.e5.")]
        string target_relative_path,
        [Description("Batch operations. Each item uses image_number plus replacement_path, and optionally source_image_number when replacement_path is another E5 file.")]
        List<E5ImageBatchUpdate> updates,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceE5ImageBatch(game_root, target_relative_path, updates, write_mode);

    [McpServerTool]
    [Description("Preview true-color R actor image replacement from a material folder without writing. The tool name is kept for legacy clients.")]
    public object preview_r_image_raw_replace(
        [Description("R image id. r_actor maps R=n to Pmapobj.e5 images 2n+1/2n+2.")]
        int r_image_id,
        [Description("Material folder containing source image files. Relative paths resolve from workspace, project root, then cwd.")]
        string material_folder,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewRImageRawReplace(game_root, r_image_id, material_folder);

    [McpServerTool]
    [Description("Validate a CCZ 6.5 R/S pixel material package and run read-only replacement previews. Checks front/back/mov/atk/spc names, strict dimensions, magenta-key risk, R/S mapping, and RAW->PNG test-copy risk. Does not write game resources.")]
    public object validate_rs_pixel_material_package(
        [Description("Optional package root. If supplied, materials/r_actor and materials/s_unit are used when present. Relative paths resolve from workspace, project root, then cwd.")]
        string? material_root = null,
        [Description("Optional R material folder containing front.bmp/back.bmp. Overrides material_root detection.")]
        string? r_material_folder = null,
        [Description("Optional S material folder containing mov.bmp/atk.bmp/spc.bmp. Overrides material_root detection.")]
        string? s_material_folder = null,
        [Description("Optional R image id. r_actor maps R=n to Pmapobj.e5 images 2n+1/2n+2.")]
        int? r_image_id = null,
        [Description("Optional S image id. s_unit maps compact S ids to Unit image numbers.")]
        int? s_image_id = null,
        [Description("Optional job id for S=0 default unit mapping.")]
        int? job_id = null,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ValidateRsPixelMaterialPackage(game_root, material_root, r_material_folder, s_material_folder, r_image_id, s_image_id, job_id, faction_slot);

    [McpServerTool]
    [Description("Replace R actor image assets as true-color PNG entries from a material folder. Creates backup and structured report. The tool name is kept for legacy clients.")]
    public object replace_r_image_raw(
        [Description("R image id. r_actor maps R=n to Pmapobj.e5 images 2n+1/2n+2.")]
        int r_image_id,
        [Description("Material folder containing source image files. Relative paths resolve from workspace, project root, then cwd.")]
        string material_folder,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceRImageRaw(game_root, r_image_id, material_folder, write_mode);

    [McpServerTool]
    [Description("Preview batch true-color R actor image replacement from a material root without writing. Subfolders use R12, R_12, or 12 and contain front.bmp/back.bmp.")]
    public object preview_r_image_raw_batch_replace(
        [Description("Material root containing numbered R image subfolders. Relative paths resolve from workspace, project root, then cwd.")]
        string material_root,
        [Description("Optional R image ids to include. When omitted or empty, all valid numbered subfolders are considered.")]
        List<int>? allowed_r_image_ids = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewRImageRawBatchReplace(game_root, material_root, allowed_r_image_ids);

    [McpServerTool]
    [Description("Replace batch R actor image assets as true-color PNG entries from a material root. Creates backup and structured aggregate report. The tool name is kept for legacy clients.")]
    public object replace_r_image_raw_batch(
        [Description("Material root containing numbered R image subfolders. Relative paths resolve from workspace, project root, then cwd.")]
        string material_root,
        [Description("Optional R image ids to include. When omitted or empty, all valid numbered subfolders are considered.")]
        List<int>? allowed_r_image_ids = null,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceRImageRawBatch(game_root, material_root, allowed_r_image_ids, write_mode);

    [McpServerTool]
    [Description("Preview true-color S unit image replacement from a material folder without writing. The tool name is kept for legacy clients.")]
    public object preview_s_image_raw_replace(
        [Description("S image id. s_unit maps compact S ids to Unit image numbers.")]
        int s_image_id,
        [Description("Material folder containing source image files. Relative paths resolve from workspace, project root, then cwd.")]
        string material_folder,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional job id for S=0 default unit mapping.")]
        int? job_id = null,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1)
        => runtime.PreviewSImageRawReplace(game_root, s_image_id, material_folder, job_id, faction_slot);

    [McpServerTool]
    [Description("Replace S unit image assets as true-color PNG entries from a material folder. Creates backup and structured report. The tool name is kept for legacy clients.")]
    public object replace_s_image_raw(
        [Description("S image id. s_unit maps compact S ids to Unit image numbers.")]
        int s_image_id,
        [Description("Material folder containing source image files. Relative paths resolve from workspace, project root, then cwd.")]
        string material_folder,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional job id for S=0 default unit mapping.")]
        int? job_id = null,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceSImageRaw(game_root, s_image_id, material_folder, job_id, faction_slot, write_mode);

    [McpServerTool]
    [Description("Preview default job S unit image replacement without writing. Uses S=0 plus detailed job id and selected faction slots.")]
    public object preview_job_s_image_raw_replace(
        [Description("Detailed job id from the 6.5-4 detailed job table.")]
        int job_id,
        [Description("Material folder containing mov.bmp, atk.bmp, and/or spc.bmp. Sources are written as true-color PNG entries. Relative paths resolve from workspace, project root, then cwd.")]
        string material_folder,
        [Description("Faction slots to preview: 1=ally, 2=friendly, 3=enemy. Must contain at least one value.")]
        List<int> faction_slots,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewJobSImageRawReplace(game_root, job_id, material_folder, faction_slots);

    [McpServerTool]
    [Description("Replace default job S unit image assets for selected faction slots. Uses S=0 plus detailed job id; creates backups and reports.")]
    public object replace_job_s_image_raw(
        [Description("Detailed job id from the 6.5-4 detailed job table.")]
        int job_id,
        [Description("Material folder containing mov.bmp, atk.bmp, and/or spc.bmp. Sources are written as true-color PNG entries. Relative paths resolve from workspace, project root, then cwd.")]
        string material_folder,
        [Description("Faction slots to write: 1=ally, 2=friendly, 3=enemy. Must contain at least one value.")]
        List<int> faction_slots,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceJobSImageRaw(game_root, job_id, material_folder, faction_slots, write_mode);

    [McpServerTool]
    [Description("Preview batch true-color S unit image replacement from a material root without writing. Subfolders use S12, S_12, or 12 and contain mov.bmp/atk.bmp/spc.bmp.")]
    public object preview_s_image_raw_batch_replace(
        [Description("Material root containing numbered S image subfolders. Relative paths resolve from workspace, project root, then cwd.")]
        string material_root,
        [Description("Optional S usages to include. Use job_id for S=0 default unit mapping.")]
        List<BatchSImageUsageUpdate>? allowed_usages = null,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default faction slot for S=0 when allowed_usages is omitted: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1)
        => runtime.PreviewSImageRawBatchReplace(game_root, material_root, allowed_usages, faction_slot);

    [McpServerTool]
    [Description("Replace batch S unit image assets as true-color PNG entries from a material root. Creates backups and structured aggregate report. The tool name is kept for legacy clients.")]
    public object replace_s_image_raw_batch(
        [Description("Material root containing numbered S image subfolders. Relative paths resolve from workspace, project root, then cwd.")]
        string material_root,
        [Description("Optional S usages to include. Use job_id for S=0 default unit mapping.")]
        List<BatchSImageUsageUpdate>? allowed_usages = null,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default faction slot for S=0 when allowed_usages is omitted: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceSImageRawBatch(game_root, material_root, allowed_usages, faction_slot, write_mode);

    [McpServerTool]
    [Description("Preview batch item icon import without writing. 6.5 writes Itemicon.dll; 6.6 writes E5/Item.e5. Item table icon fields are not changed.")]
    public object preview_item_icon_batch_import(
        [Description("Optional source image files. Relative paths resolve from workspace, project root, then cwd.")]
        List<string>? source_files = null,
        [Description("Optional source root directory. Scans importable item_icon_{N}.bmp files and compatible legacy export subfolders.")]
        string? source_root = null,
        [Description("Optional item rows with icon indexes. If supplied, source_files count must match target_rows count and order is used.")]
        List<BatchItemIconTargetRowUpdate>? target_rows = null,
        [Description("Optional match mode label, defaults to auto.")]
        string? match_mode = "auto",
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewItemIconBatchImport(game_root, source_files, source_root, target_rows, match_mode);

    [McpServerTool]
    [Description("Batch import item icons. Creates backup and structured aggregate report. Item table icon fields are not changed.")]
    public object replace_item_icon_batch_import(
        [Description("Optional source image files. Relative paths resolve from workspace, project root, then cwd.")]
        List<string>? source_files = null,
        [Description("Optional source root directory. Scans importable item_icon_{N}.bmp files and compatible legacy export subfolders.")]
        string? source_root = null,
        [Description("Optional item rows with icon indexes. If supplied, source_files count must match target_rows count and order is used.")]
        List<BatchItemIconTargetRowUpdate>? target_rows = null,
        [Description("Optional match mode label, defaults to auto.")]
        string? match_mode = "auto",
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceItemIconBatchImport(game_root, source_files, source_root, target_rows, match_mode, write_mode);

    [McpServerTool]
    [Description("Preview batch strategy icon import without writing. 6.5 writes Mgcicon.dll; 6.6 writes E5/Mtem.e5.")]
    public object preview_strategy_icon_batch_import(
        [Description("Optional source image files. Relative paths resolve from workspace, project root, then cwd.")]
        List<string>? source_files = null,
        [Description("Optional source root directory. Scans importable strategy_icon_{N}.bmp files and compatible legacy export subfolders.")]
        string? source_root = null,
        [Description("Optional strategy rows with icon indexes.")]
        List<BatchStrategyIconTargetRowUpdate>? target_rows = null,
        [Description("Optional match mode label, defaults to auto.")]
        string? match_mode = "auto",
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewStrategyIconBatchImport(game_root, source_files, source_root, target_rows, match_mode);

    [McpServerTool]
    [Description("Batch import strategy icons. Creates backup and structured aggregate report.")]
    public object replace_strategy_icon_batch_import(
        [Description("Optional source image files. Relative paths resolve from workspace, project root, then cwd.")]
        List<string>? source_files = null,
        [Description("Optional source root directory. Scans importable strategy_icon_{N}.bmp files and compatible legacy export subfolders.")]
        string? source_root = null,
        [Description("Optional strategy rows with icon indexes.")]
        List<BatchStrategyIconTargetRowUpdate>? target_rows = null,
        [Description("Optional match mode label, defaults to auto.")]
        string? match_mode = "auto",
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceStrategyIconBatchImport(game_root, source_files, source_root, target_rows, match_mode, write_mode);

    [McpServerTool]
    [Description("Preview batch role face import without writing. Writes Face.e5 entries through the current face import pipeline.")]
    public object preview_role_face_batch_import(
        [Description("Optional source image files. Relative paths resolve from workspace, project root, then cwd.")]
        List<string>? source_files = null,
        [Description("Optional source root directory. Scans importable face_{N}.bmp files and compatible legacy export subfolders.")]
        string? source_root = null,
        [Description("Optional role rows with face ids.")]
        List<BatchRoleFaceTargetRowUpdate>? target_rows = null,
        [Description("Optional match mode label, defaults to auto.")]
        string? match_mode = "auto",
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewRoleFaceBatchImport(game_root, source_files, source_root, target_rows, match_mode);

    [McpServerTool]
    [Description("Batch import role faces. Creates backup and structured aggregate report.")]
    public object replace_role_face_batch_import(
        [Description("Optional source image files. Relative paths resolve from workspace, project root, then cwd.")]
        List<string>? source_files = null,
        [Description("Optional source root directory. Scans importable face_{N}.bmp files and compatible legacy export subfolders.")]
        string? source_root = null,
        [Description("Optional role rows with face ids.")]
        List<BatchRoleFaceTargetRowUpdate>? target_rows = null,
        [Description("Optional match mode label, defaults to auto.")]
        string? match_mode = "auto",
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceRoleFaceBatchImport(game_root, source_files, source_root, target_rows, match_mode, write_mode);

    [McpServerTool]
    [Description("Read the integrated role editor view: person table, R/S assignments, job names, equipment choices, and resource status. Read-only.")]
    public object read_role_editor(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional keyword across returned role rows.")]
        string? keyword = null,
        [Description("Maximum rows to return. Defaults to 100; capped at 1000.")]
        int limit = 100)
        => runtime.ReadRoleEditor(game_root, keyword, limit);

    [McpServerTool]
    [Description("Preview integrated role editor writes without modifying game files.")]
    public object preview_write_roles(
        [Description("Role row updates. Each update may include person-table values plus face_id/r_image_id/s_image_id.")]
        List<RoleUpdate> updates,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewWriteRoles(game_root, updates);

    [McpServerTool]
    [Description("Write integrated role editor updates to person/R/S tables. Creates backups, reports, and reread validation.")]
    public object write_roles(
        [Description("Role row updates. Each update may include person-table values plus face_id/r_image_id/s_image_id.")]
        List<RoleUpdate> updates,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.WriteRoles(game_root, updates, write_mode);

    [McpServerTool]
    [Description("Read role biography, critical quote mapping, and retreat quote mapping. Read-only.")]
    public object read_role_texts(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional role IDs to return.")]
        List<int>? role_ids = null,
        [Description("Maximum rows to return. Defaults to 100; capped at 1000.")]
        int limit = 100)
        => runtime.ReadRoleTexts(game_root, role_ids, limit);

    [McpServerTool]
    [Description("Preview role biography/critical/retreat text writes and critical quote assignment without modifying game files.")]
    public object preview_write_role_texts(
        [Description("Role text updates.")]
        List<RoleTextUpdate> updates,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewWriteRoleTexts(game_root, updates);

    [McpServerTool]
    [Description("Write role biography/critical/retreat text updates. Creates backups, reports, and reread validation.")]
    public object write_role_texts(
        [Description("Role text updates.")]
        List<RoleTextUpdate> updates,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.WriteRoleTexts(game_root, updates, write_mode);

    [McpServerTool]
    [Description("Find free face/R/S image assignment IDs by comparing assigned rows with existing image resources. Read-only.")]
    public object find_free_image_assignment_ids(
        [Description("Kind: face, r_actor, or s_unit.")]
        string kind,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Minimum ID to return.")]
        int start_id = 0,
        [Description("Maximum IDs to return. Defaults to 50; capped at 500.")]
        int limit = 50)
        => runtime.FindFreeImageAssignmentIds(game_root, kind, start_id, limit);

    [McpServerTool]
    [Description("Preview safe image assignment updates without writing game files.")]
    public object preview_image_assignment_update(
        [Description("Image assignment updates keyed by role row_id.")]
        List<ImageAssignmentUpdate> updates,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewImageAssignmentUpdate(game_root, updates);

    [McpServerTool]
    [Description("Write safe image assignment updates to person/R/S fields. Creates backups, reports, and reread validation.")]
    public object write_image_assignment_update(
        [Description("Image assignment updates keyed by role row_id.")]
        List<ImageAssignmentUpdate> updates,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.WriteImageAssignmentUpdate(game_root, updates, write_mode);

    [McpServerTool]
    [Description("Preview batch default job S unit image replacement from a material root without writing. Subfolders use Job12 or Job_12 and contain mov.bmp/atk.bmp/spc.bmp.")]
    public object preview_job_s_image_raw_batch_replace(
        [Description("Material root containing Job{jobId} subfolders. Relative paths resolve from workspace, project root, then cwd.")]
        string material_root,
        [Description("Faction slots to preview: 1=ally, 2=friendly, 3=enemy. Must contain at least one value.")]
        List<int> faction_slots,
        [Description("Optional detailed job ids to include. When omitted or empty, all valid Job folders are considered.")]
        List<int>? allowed_job_ids = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewJobSImageRawBatchReplace(game_root, material_root, allowed_job_ids, faction_slots);

    [McpServerTool]
    [Description("Replace batch default job S unit image assets from a material root. Creates backups and structured reports.")]
    public object replace_job_s_image_raw_batch_replace(
        [Description("Material root containing Job{jobId} subfolders. Relative paths resolve from workspace, project root, then cwd.")]
        string material_root,
        [Description("Faction slots to write: 1=ally, 2=friendly, 3=enemy. Must contain at least one value.")]
        List<int> faction_slots,
        [Description("Optional detailed job ids to include. When omitted or empty, all valid Job folders are considered.")]
        List<int>? allowed_job_ids = null,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceJobSImageRawBatch(game_root, material_root, allowed_job_ids, faction_slots, write_mode);

    [McpServerTool]
    [Description("Compatibility alias for replace_job_s_image_raw_batch_replace. Replaces batch default job S unit image assets from a material root.")]
    public object replace_job_s_image_raw_batch(
        [Description("Material root containing Job{jobId} subfolders. Relative paths resolve from workspace, project root, then cwd.")]
        string material_root,
        [Description("Faction slots to write: 1=ally, 2=friendly, 3=enemy. Must contain at least one value.")]
        List<int> faction_slots,
        [Description("Optional detailed job ids to include. When omitted or empty, all valid Job folders are considered.")]
        List<int>? allowed_job_ids = null,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceJobSImageRawBatch(game_root, material_root, allowed_job_ids, faction_slots, write_mode);

    [McpServerTool]
    [Description("Preview E5 role raw-image normalization without writing.")]
    public object preview_e5_role_raw_normalize(
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewE5RoleRawNormalize(game_root);

    [McpServerTool]
    [Description("Normalize E5 role raw-image resources. Creates backup and structured report.")]
    public object normalize_e5_role_raw(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.NormalizeE5RoleRaw(game_root, write_mode);

    [McpServerTool]
    [Description("Preview replacement of one DLL RT_BITMAP icon resource without writing.")]
    public object preview_dll_icon_replace(
        [Description("Project-relative DLL path or known file name such as Itemicon.dll, Mgcicon.dll, or Cmdicon.dll.")]
        string target_relative_path,
        [Description("Icon field index. DLL icon resources use 0-based field indexes.")]
        int icon_index,
        [Description("Replacement image path. Supports GDI+ readable BMP/JPG/PNG and scales to resource size.")]
        string replacement_path,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewDllIconReplace(game_root, target_relative_path, icon_index, replacement_path);

    [McpServerTool]
    [Description("Replace one DLL RT_BITMAP icon resource. Creates a backup and structured report.")]
    public object replace_dll_icon(
        [Description("Project-relative DLL path or known file name such as Itemicon.dll, Mgcicon.dll, or Cmdicon.dll.")]
        string target_relative_path,
        [Description("Icon field index. DLL icon resources use 0-based field indexes.")]
        int icon_index,
        [Description("Replacement image path. Supports GDI+ readable BMP/JPG/PNG and scales to resource size.")]
        string replacement_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ReplaceDllIcon(game_root, target_relative_path, icon_index, replacement_path, write_mode);

    [McpServerTool]
    [Description("Preview clearing one DLL RT_BITMAP icon resource without writing.")]
    public object preview_clear_dll_icon(
        [Description("Project-relative DLL path or known file name such as Itemicon.dll, Mgcicon.dll, or Cmdicon.dll.")]
        string target_relative_path,
        [Description("Icon field index. DLL icon resources use 0-based field indexes.")]
        int icon_index,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewClearDllIcon(game_root, target_relative_path, icon_index);

    [McpServerTool]
    [Description("Clear one DLL RT_BITMAP icon resource by writing transparent placeholders at the original resource sizes.")]
    public object clear_dll_icon(
        [Description("Project-relative DLL path or known file name such as Itemicon.dll, Mgcicon.dll, or Cmdicon.dll.")]
        string target_relative_path,
        [Description("Icon field index. DLL icon resources use 0-based field indexes.")]
        int icon_index,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ClearDllIcon(game_root, target_relative_path, icon_index, write_mode);

    [McpServerTool]
    [Description("Read an editable image target and export the editable bitmap under CCZModStudio_Exports/EditableImages. Supports semantic item/strategy icons, Face/R/S assignments, or explicit E5/DLL/raw targets. Read-only.")]
    public object read_editable_image_target(
        [Description("Editable image target request. Supports semantic=item_icon/strategy_icon/face_assignment/r_actor/s_unit with row_id, or direct target_relative_path/image_number/icon_index.")]
        EditableImageTargetRequest request,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ReadEditableImageTarget(game_root, request);

    [McpServerTool]
    [Description("Preview editable image writeback from a replacement image or pixel edits without modifying game files.")]
    public object preview_editable_image_write(
        [Description("Editable image target request.")]
        EditableImageTargetRequest request,
        [Description("Optional replacement image path. Relative paths resolve from workspace, project root, then cwd.")]
        string? replacement_path = null,
        [Description("Optional small pixel edits when replacement_path is omitted.")]
        List<PixelEditUpdate>? pixel_edits = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewEditableImageWrite(game_root, request, replacement_path, pixel_edits);

    [McpServerTool]
    [Description("Write an editable image target from a replacement image or pixel edits. Creates backup, report, and reread validation.")]
    public object write_editable_image(
        [Description("Editable image target request.")]
        EditableImageTargetRequest request,
        [Description("Optional replacement image path. Relative paths resolve from workspace, project root, then cwd.")]
        string? replacement_path = null,
        [Description("Optional small pixel edits when replacement_path is omitted.")]
        List<PixelEditUpdate>? pixel_edits = null,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.WriteEditableImage(game_root, request, replacement_path, pixel_edits, write_mode);

    [McpServerTool]
    [Description("List known portrait frame assets from bundled/workspace/project directories. Read-only.")]
    public object list_portrait_frames(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional frame root directory.")]
        string? root = null,
        [Description("Maximum frames to return. Defaults to 200; capped at 2000.")]
        int limit = 200)
        => runtime.ListPortraitFrames(game_root, root, limit);

    [McpServerTool]
    [Description("Preview applying a portrait frame to Face.e5 targets without writing game files.")]
    public object preview_apply_portrait_frame(
        [Description("Portrait frame image path. Relative paths resolve from workspace, project root, then cwd.")]
        string frame_path,
        [Description("Face targets with row_id/display_name/face_id.")]
        List<PortraitFrameTargetUpdate> targets,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewApplyPortraitFrame(game_root, frame_path, targets);

    [McpServerTool]
    [Description("Apply a portrait frame to Face.e5 targets. Creates backup, aggregate report, and reread validation.")]
    public object apply_portrait_frame(
        [Description("Portrait frame image path. Relative paths resolve from workspace, project root, then cwd.")]
        string frame_path,
        [Description("Face targets with row_id/display_name/face_id.")]
        List<PortraitFrameTargetUpdate> targets,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ApplyPortraitFrame(game_root, frame_path, targets, write_mode);

    [McpServerTool]
    [Description("Replace a non-core project resource file. Core files must use dedicated tools.")]
    public object replace_resource(
        [Description("Project-relative target resource path.")]
        string target_relative_path,
        [Description("Replacement file path. Relative paths resolve from workspace root first.")]
        string replacement_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null,
        [Description("Require target and replacement extensions to match.")]
        bool require_same_extension = true)
        => runtime.ReplaceResource(game_root, target_relative_path, replacement_path, write_mode, require_same_extension);

    [McpServerTool]
    [Description("List CCZModStudio local knowledge-base markdown files.")]
    public object list_knowledge_entries()
        => runtime.ListKnowledgeEntries();

    [McpServerTool]
    [Description("Search CCZModStudio local knowledge-base markdown files by keyword.")]
    public object search_knowledge_entries(
        [Description("Required keyword to search in markdown content.")]
        string keyword,
        [Description("Maximum matches to return. Defaults to 50; capped at 500.")]
        int limit = 50,
        [Description("Context lines before and after each match. Defaults to 1; capped at 3.")]
        int context_lines = 1)
        => runtime.SearchKnowledgeEntries(keyword, limit, context_lines);

    [McpServerTool]
    [Description("Read one CCZModStudio local knowledge-base markdown file by name.")]
    public object read_knowledge_entry(
        [Description("Markdown file name or relative path, for example 00-总览与规范/开发执行规则.md.")]
        string name)
        => runtime.ReadKnowledgeEntry(name);

    [McpServerTool]
    [Description("List CheatMaker CMF old-tool knowledge sources from the old tool bundle. CMF projects are high-trust sources, but writable rules still require field extraction and reread validation.")]
    public object list_cmf_evidence(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.ListCmfEvidence(game_root);

    [McpServerTool]
    [Description("Read one CheatMaker CMF probe by path relative to the old tool bundle, including segment analysis.")]
    public object read_cmf_evidence(
        [Description("CMF path relative to 老版游戏制作工具, for example Star6.6X 引擎.cmf.")]
        string relative_path,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.ReadCmfEvidence(game_root, relative_path);

    [McpServerTool]
    [Description("Compare two CheatMaker CMF files. This reports project differences and never enables writes by itself.")]
    public object compare_cmf_evidence(
        [Description("Left CMF path relative to 老版游戏制作工具.")]
        string left_relative_path,
        [Description("Right CMF path relative to 老版游戏制作工具.")]
        string right_relative_path,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.CompareCmfEvidence(game_root, left_relative_path, right_relative_path);

    [McpServerTool]
    [Description("Extract structured high-trust knowledge from a CMF project using static segment analysis. Does not modify CheatMaker or game files.")]
    public object extract_cmf_knowledge(
        [Description("CMF path relative to 老版游戏制作工具, for example Star6.5引擎exe修改器.cmf.")]
        string relative_path,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.ExtractCmfKnowledge(game_root, relative_path);

    [McpServerTool]
    [Description("Import a normal CheatMaker exported data/address list for one CMF project. This upgrades static CMF candidates with field metadata, but remains read-only.")]
    public object import_cmf_export_knowledge(
        [Description("CMF path relative to 老版游戏制作工具, for example Star6.5引擎exe修改器.cmf.")]
        string relative_path,
        [Description("Path to a CheatMaker exported text/CSV/TSV data or address list.")]
        string export_path,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.ImportCmfExportKnowledge(game_root, relative_path, export_path);

    [McpServerTool]
    [Description("List CMF-derived feature candidates for effects, EXE modifications, global settings, variables, and resources.")]
    public object list_cmf_features(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional category or target subsystem filter, such as Effect, GlobalSetting, EffectPackageService.")]
        string? category = null,
        [Description("Optional keyword across name/category/subsystem/source.")]
        string? keyword = null,
        [Description("Maximum features to return. Defaults to 100; capped at 1000.")]
        int limit = 100)
        => runtime.ListCmfFeatures(game_root, category, keyword, limit);

    [McpServerTool]
    [Description("Read one CMF-derived feature candidate by feature id.")]
    public object read_cmf_feature(
        [Description("Feature id from list_cmf_features.")]
        string feature_id,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.ReadCmfFeature(game_root, feature_id);

    [McpServerTool]
    [Description("Promote a CMF-derived feature candidate into a rule draft report. This never writes game files.")]
    public object promote_cmf_feature_candidate(
        [Description("Feature id from list_cmf_features.")]
        string feature_id,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.PromoteCmfFeatureCandidate(game_root, feature_id);

    [McpServerTool]
    [Description("Detect the official B image assigner oracle for the current project. Read-only.")]
    public object detect_image_assigner_oracle(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.DetectImageAssignerOracle(game_root);

    [McpServerTool]
    [Description("Read the official B image assigner System.ini oracle config. Read-only.")]
    public object read_image_assigner_oracle_config(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.ReadImageAssignerOracleConfig(game_root);

    [McpServerTool]
    [Description("Compare CCZModStudio table assumptions against the official B image assigner oracle. Read-only.")]
    public object compare_image_assigner_oracle(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Include pending global numeric settings as NeedsUiOrDiffExtraction candidates.")]
        bool include_global_candidates = false)
        => runtime.CompareImageAssignerOracle(game_root, include_global_candidates);

    [McpServerTool]
    [Description("Build a safe test-copy validation plan for comparing official image assigner output with CCZModStudio output. Read-only.")]
    public object plan_image_assigner_validation(
        [Description("Change kind, for example r_image_assignment, s_image_assignment, face_assignment, or global_numeric_setting.")]
        string change_kind,
        [Description("Optional row/person id for row-specific byte ranges.")]
        int? row_id = null,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.PlanImageAssignerValidation(game_root, change_kind, row_id);

    [McpServerTool]
    [Description("Run a read-only official image assigner oracle smoke: static, launch_only, or ui_probe. No save action is performed.")]
    public object run_image_assigner_oracle_smoke(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Smoke mode: static, launch_only, or ui_probe. Defaults to static.")]
        string? mode = "static")
        => runtime.RunImageAssignerOracleSmoke(game_root, mode);

    [McpServerTool]
    [Description("Compare before/official-after/CCZ-after test-copy outputs for official image assigner validation. Does not write files.")]
    public object compare_image_assigner_output(
        [Description("Before test-copy root.")]
        string before_root,
        [Description("Official-tool modified test-copy root.")]
        string official_after_root,
        [Description("CCZModStudio modified test-copy root.")]
        string ccz_after_root)
        => runtime.CompareImageAssignerOutput(before_root, official_after_root, ccz_after_root);

    [McpServerTool]
    [Description("Run a controlled test-copy R/S image assignment experiment: official oracle offset write vs CCZ HexTable write, then compare bytes. Does not modify the original project.")]
    public object run_image_assigner_assignment_experiment(
        [Description("Change kind: r_image_assignment or s_image_assignment.")]
        string change_kind,
        [Description("Person row id to edit in the generated test copies.")]
        int row_id,
        [Description("Optional new UInt16 value. If omitted, the experiment uses original+1.")]
        int? new_value = null,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.RunImageAssignerAssignmentExperiment(game_root, change_kind, row_id, new_value);

    [McpServerTool]
    [Description("List item/job/personal/patch/cmf effects from CCZModStudio catalogs, tables, manifests, and CMF-derived old-tool candidates.")]
    public object list_effects(
        [Description("Effect domain: item, job, personal, patch, or cmf.")]
        string domain,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional keyword across effect id, name, description, source, and status.")]
        string? keyword = null,
        [Description("Maximum effects to return. Defaults to 100; capped at 1000.")]
        int limit = 100)
        => runtime.ListEffects(game_root, domain, keyword, limit);

    [McpServerTool]
    [Description("Read one effect entry by domain and effect id.")]
    public object read_effect(
        [Description("Effect domain: item, job, personal, patch, or cmf.")]
        string domain,
        [Description("Effect id / fixed row id.")]
        int effect_id,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ReadEffect(game_root, domain, effect_id);

    [McpServerTool]
    [Description("Export one effect as an EffectPackage object.")]
    public object export_effect_package(
        [Description("Effect domain: item, job, personal, patch, or cmf.")]
        string domain,
        [Description("Effect id / fixed row id.")]
        int effect_id,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ExportEffectPackage(game_root, domain, effect_id);

    [McpServerTool]
    [Description("Preview importing, replacing, or deleting an EffectPackage without writing.")]
    public object preview_effect_package(
        [Description("EffectPackage JSON object.")]
        EffectPackage package,
        [Description("Operation mode: import, replace, or delete.")]
        string? mode = "import",
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewEffectPackage(game_root, package, mode);

    [McpServerTool]
    [Description("Apply an EffectPackage to the current project. Writes backups, reports, and an effect manifest.")]
    public object apply_effect_package(
        [Description("EffectPackage JSON object.")]
        EffectPackage package,
        [Description("Operation mode: import, replace, or delete.")]
        string? mode = "import",
        [Description("Default direct writes the detected project.")]
        string? write_mode = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ApplyEffectPackage(game_root, package, mode, write_mode);

    [McpServerTool]
    [Description("Analyze a natural-language request into a ModDesign draft for table/resource/effect/R-S package automation.")]
    public object analyze_mod_request(
        [Description("Natural-language MOD request.")]
        string prompt,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional mod name.")]
        string? name = null,
        [Description("Optional package id.")]
        string? package_id = null)
        => runtime.AnalyzeModRequest(game_root, prompt, name, package_id);

    [McpServerTool]
    [Description("Compile a ModDesign into a ModPackage draft with slot planning.")]
    public object compile_mod_package(
        [Description("ModDesign object from analyze_mod_request or manual input.")]
        ModDesign design,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Maximum available slots to inspect. Defaults to 64; capped at 200.")]
        int slot_limit = 64)
        => runtime.CompileModPackage(game_root, design, slot_limit);

    [McpServerTool]
    [Description("Analyze a natural-language request into a standalone R+S scenario design draft.")]
    public object analyze_standalone_scenario_request(
        [Description("Natural-language standalone scenario request.")]
        string prompt,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional scenario title.")]
        string? title = null,
        [Description("Optional package id.")]
        string? package_id = null)
        => runtime.AnalyzeStandaloneScenarioRequest(game_root, prompt, title, package_id);

    [McpServerTool]
    [Description("Compile a standalone scenario design into a force-open ModPackage draft.")]
    public object compile_standalone_scenario_package(
        [Description("Standalone scenario design object from analyze_standalone_scenario_request or manual input.")]
        StandaloneScenarioDesign design,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Maximum available slots to inspect. Defaults to 64; capped at 200.")]
        int slot_limit = 64)
        => runtime.CompileStandaloneScenarioPackage(game_root, design, slot_limit);

    [McpServerTool]
    [Description("List likely available person/job/item/effect/scenario/image slots for ModPackage planning.")]
    public object list_available_slots(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Maximum slots per group. Defaults to 64; capped at 200.")]
        int limit = 64)
        => runtime.ListAvailableSlots(game_root, limit);

    [McpServerTool]
    [Description("Preview a ModPackage without writing.")]
    public object preview_mod_package(
        [Description("ModPackage JSON object.")]
        ModPackage package,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Automation mode: direct, force_preview_report, or force_open.")]
        string? automation_mode = "direct")
        => runtime.PreviewModPackage(game_root, package, automation_mode);

    [McpServerTool]
    [Description("Apply a ModPackage through dedicated writers. Defaults to direct writes with backups and reports.")]
    public object apply_mod_package(
        [Description("ModPackage JSON object.")]
        ModPackage package,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null,
        [Description("Automation mode: direct, force_preview_report, or force_open.")]
        string? automation_mode = "direct")
        => runtime.ApplyModPackage(game_root, package, write_mode, automation_mode);

    [McpServerTool]
    [Description("Analyze, compile, preview, write the detected project directly, and emit reports for a natural-language MOD request.")]
    public object auto_make_mod(
        [Description("Natural-language MOD request.")]
        string prompt,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Automation mode: direct or force_open.")]
        string? automation_mode = "direct",
        [Description("Maximum automatic repair attempts. Defaults to 3; capped at 10.")]
        int max_repair_attempts = 3)
        => runtime.AutoMakeMod(game_root, prompt, automation_mode, max_repair_attempts);

    [McpServerTool]
    [Description("Validate a ModPackage preview and optionally run required SmokeTests. Any required smoke failure blocks pass.")]
    public object auto_validate_mod(
        [Description("ModPackage JSON object.")]
        ModPackage package,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("When true, run required SmokeTests commands.")]
        bool run_smokes = false)
        => runtime.AutoValidateMod(game_root, package, run_smokes);

    [McpServerTool]
    [Description("Validate a ModPackage and report required smoke/manual checks.")]
    public object validate_mod_package(
        [Description("ModPackage JSON object.")]
        ModPackage package,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("When true, run required SmokeTests commands.")]
        bool run_smokes = false)
        => runtime.ValidateModPackage(game_root, package, run_smokes);

    [McpServerTool]
    [Description("Export a ModPackage preview/report under CCZModStudio_Reports/ModPackages.")]
    public object export_mod_report(
        [Description("ModPackage JSON object.")]
        ModPackage package,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Report kind label.")]
        string? report_kind = "preview")
        => runtime.ExportModReport(game_root, package, report_kind);

    [McpServerTool]
    [Description("Compile a small scenario patch request into a ModScenarioPatch draft.")]
    public object compile_scenario_patch(
        [Description("Scenario patch compile request.")]
        ScenarioPatchCompileRequest request,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.CompileScenarioPatch(game_root, request);

    [McpServerTool]
    [Description("Preview a ModScenarioPatch without writing.")]
    public object preview_scenario_patch(
        [Description("ModScenarioPatch JSON object.")]
        ModScenarioPatch patch,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewScenarioPatch(game_root, patch);

    [McpServerTool]
    [Description("Apply a conservative ModScenarioPatch through LegacyScenarioWriter.")]
    public object apply_scenario_patch(
        [Description("ModScenarioPatch JSON object.")]
        ModScenarioPatch patch,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.ApplyScenarioPatch(game_root, patch, write_mode);

    [McpServerTool]
    [Description("Apply a structural ModScenarioPatch in aggressive mode. Defaults to direct writes with backups and reports.")]
    public object apply_scenario_patch_aggressive(
        [Description("ModScenarioPatch JSON object.")]
        ModScenarioPatch patch,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = "direct")
        => runtime.ApplyScenarioPatchAggressive(game_root, patch, write_mode);

    [McpServerTool]
    [Description("List declarative effect templates for AI-assisted EffectPackage creation.")]
    public object list_effect_templates()
        => runtime.ListEffectTemplates();

    [McpServerTool]
    [Description("Build a draft EffectPackage from a named template and string parameters.")]
    public object build_effect_package_from_template(
        [Description("Template id from list_effect_templates.")]
        string template_id,
        [Description("String parameters keyed by template field names.")]
        Dictionary<string, string>? parameters = null)
        => runtime.BuildEffectPackageFromTemplate(template_id, parameters);

    [McpServerTool]
    [Description("Preview patch segments in a patch-domain EffectPackage.")]
    public object preview_effect_patch(
        [Description("Patch-domain EffectPackage JSON object.")]
        EffectPackage package,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewEffectPatch(game_root, package);

    [McpServerTool]
    [Description("Scan executable PE sections for candidate code caves. Read-only; candidates still require patch preview checks.")]
    public object scan_exe_code_caves(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Target executable file under the game root. Defaults to Ekd5.exe.")]
        string? target_file = "Ekd5.exe",
        [Description("Minimum continuous fill length. Defaults to 8.")]
        int min_length = 8,
        [Description("Include continuous 00 fill candidates. Defaults false because zero fill is low-confidence.")]
        bool include_zero = false,
        [Description("Include mixed 90/00/CC fill candidates. Defaults false because mixed fill is manual-inspection only.")]
        bool include_mixed = false)
        => runtime.ScanExeCodeCaves(game_root, target_file, min_length, include_zero, include_mixed);

    [McpServerTool]
    [Description("Create a structured assembly patch draft from a natural-language request. Draft-only; does not compile or write.")]
    public object draft_assembly_patch(
        [Description("Natural-language patch request.")]
        string prompt,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional engine version hint, such as 6.5.")]
        string? engine_version = null,
        [Description("Optional effect id for the generated package.")]
        int? effect_id = null,
        [Description("Optional hook hint label.")]
        string? hook_hint = null)
        => runtime.DraftAssemblyPatch(game_root, prompt, engine_version, effect_id, hook_hint);

    [McpServerTool]
    [Description("Compile an AssemblyPatchDraft, allocate a code cave, and preview the generated EffectPackage without writing.")]
    public object preview_assembly_patch(
        [Description("AssemblyPatchDraft JSON object.")]
        AssemblyPatchDraft draft,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Allocator policy: smallest-fit or largest-fit.")]
        string? allocator_policy = "smallest-fit")
        => runtime.PreviewAssemblyPatch(game_root, draft, allocator_policy);

    [McpServerTool]
    [Description("Apply a compiled assembly patch package produced by preview_assembly_patch. Uses backups, reports, and manifests.")]
    public object apply_assembly_patch(
        [Description("EffectPackage returned by preview_assembly_patch.")]
        EffectPackage compiled_package,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ApplyAssemblyPatch(game_root, compiled_package, write_mode);

    [McpServerTool]
    [Description("Create a draft inline special-skill patch with four logical modules: hook jump, stub/body, personal id point, item id point. Draft-only.")]
    public object draft_special_skill_patch(
        [Description("Natural-language special-skill request.")]
        string prompt,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional effect id for the generated package.")]
        int? effect_id = null,
        [Description("Optional hook/template hint from the engine profile.")]
        string? hook_hint = null,
        [Description("Optional personal special-skill id, 0x00-0xFF.")]
        int? personal_effect_id = null,
        [Description("Optional item/equipment special-skill id, 0x00-0xFF.")]
        int? item_effect_id = null,
        [Description("Template mode. Defaults to damage-adjust.")]
        string? mode = "damage-adjust")
        => runtime.DraftSpecialSkillPatch(game_root, prompt, effect_id, hook_hint, personal_effect_id, item_effect_id, mode);

    [McpServerTool]
    [Description("Compile and preview an InlineSpecialSkillPatchDraft without writing files. Emits a patch-domain EffectPackage with non-overlapping physical segments.")]
    public object preview_special_skill_patch(
        [Description("InlineSpecialSkillPatchDraft JSON object returned by draft_special_skill_patch.")]
        InlineSpecialSkillPatchDraft draft,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Allocator policy: smallest-fit or largest-fit.")]
        string? allocator_policy = "smallest-fit")
        => runtime.PreviewSpecialSkillPatch(game_root, draft, allocator_policy);

    [McpServerTool]
    [Description("Apply a compiled special-skill patch package produced by preview_special_skill_patch. Uses backups, reports, manifests, and re-preview locks.")]
    public object apply_special_skill_patch(
        [Description("EffectPackage returned by preview_special_skill_patch.")]
        EffectPackage compiled_package,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ApplySpecialSkillPatch(game_root, compiled_package, write_mode);

    [McpServerTool]
    [Description("Preview a parameter-only rebind for a previously applied inline special-skill package. Returns a patch package; apply it with apply_effect_patch.")]
    public object rebind_special_skill_params(
        [Description("Manifest id, package id, or manifest file stem from the original special-skill apply.")]
        string manifest_id_or_package_id,
        [Description("Optional new personal special-skill id, 0x00-0xFF.")]
        int? personal_effect_id = null,
        [Description("Optional new item/equipment special-skill id, 0x00-0xFF.")]
        int? item_effect_id = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.RebindSpecialSkillParams(game_root, manifest_id_or_package_id, personal_effect_id, item_effect_id);

    [McpServerTool]
    [Description("Apply patch segments in a patch-domain EffectPackage using PatchApplyService.")]
    public object apply_effect_patch(
        [Description("Patch-domain EffectPackage JSON object.")]
        EffectPackage package,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ApplyEffectPatch(game_root, package, write_mode);

    [McpServerTool]
    [Description("Read effect pseudo-resources such as ccz://effects/schema, ccz://effects/catalog/item, ccz://effects/templates, ccz://effects/manifests, and ccz://knowledge/effects.")]
    public object read_effect_resource(
        [Description("Effect resource URI.")]
        string uri,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ReadEffectResource(game_root, uri);

    [McpServerTool]
    [Description("Read AI prompt instructions for make_ccz_effect, import_ccz_effect, or delete_ccz_effect.")]
    public object read_effect_prompt(
        [Description("Prompt name, or empty to list prompts.")]
        string name = "")
        => runtime.ReadEffectPrompt(name);

    [McpServerTool]
    [Description("List saved map workbench drafts under CCZModStudio_Notes. Read-only.")]
    public object list_map_drafts(string? game_root = null, int limit = 100)
        => runtime.ListMapDrafts(game_root, limit);

    [McpServerTool]
    [Description("Read one map workbench draft with missing-asset diagnostics. Read-only.")]
    public object read_map_draft(string draft_id, string? game_root = null)
        => runtime.ReadMapDraft(game_root, draft_id);

    [McpServerTool]
    [Description("Save a map workbench draft JSON under CCZModStudio_Notes. Does not modify game resources.")]
    public object save_map_draft(MapDraftSaveRequest request, string? game_root = null)
        => runtime.SaveMapDraft(game_root, request);

    [McpServerTool]
    [Description("Compose a map workbench draft preview PNG under CCZModStudio_Exports/MapWorkbench. Read-only.")]
    public object preview_map_canvas(string draft_id, string? game_root = null, bool show_terrain = false, bool show_grid = true, int terrain_opacity_percent = 55)
        => runtime.PreviewMapCanvas(game_root, draft_id, show_terrain, show_grid, terrain_opacity_percent);

    [McpServerTool]
    [Description("Export a map workbench draft as JPEG. Does not modify project Map/*.jpg.")]
    public object export_map_canvas_jpeg(string draft_id, string? game_root = null, string? output_path = null)
        => runtime.ExportMapCanvasJpeg(game_root, draft_id, output_path);

    [McpServerTool]
    [Description("Publish a map workbench draft to a project Map/*.jpg using backups and reports.")]
    public object publish_map_canvas_to_map_image(string draft_id, string map_id = "", string? game_root = null, string? write_mode = null)
        => runtime.PublishMapCanvasToMapImage(game_root, draft_id, map_id, write_mode);

    [McpServerTool]
    [Description("Publish a map workbench draft as both Map/Mxxx.jpg and the matching Hexzmap.e5 terrain block with backups and reports.")]
    public object publish_map_workbench_bundle(string draft_id, string? map_id = null, string? game_root = null)
        => runtime.PublishMapWorkbenchBundle(game_root, draft_id, map_id);

    [McpServerTool]
    [Description("List indexed terrain/building/scenery material assets. Read-only.")]
    public object list_material_assets(string? game_root = null, string? material_root = null, string? asset_type = null, string? keyword = null, int limit = 200)
        => runtime.ListMaterialAssets(game_root, material_root, asset_type, keyword, limit);

    [McpServerTool]
    [Description("Preview legacy material-library migration without copying files.")]
    public object migrate_material_library_preview(string old_root, string? new_root = null, string? hex_tex_path = null, string? game_root = null)
        => runtime.MigrateMaterialLibraryPreview(game_root, old_root, new_root, hex_tex_path);

    [McpServerTool]
    [Description("Migrate a legacy material library into the new terrain/building/scenery layout.")]
    public object migrate_material_library(string old_root, string new_root, string? game_root = null, string? hex_tex_path = null, bool overwrite = false, string? write_mode = null)
        => runtime.MigrateMaterialLibrary(game_root, old_root, new_root, hex_tex_path, overwrite, write_mode);

    [McpServerTool]
    [Description("Analyze terrain derivation from material-driven map workbench layers. Read-only.")]
    public object analyze_material_driven_terrain(string draft_id, string? game_root = null)
        => runtime.AnalyzeMaterialDrivenTerrain(game_root, draft_id);

    [McpServerTool]
    [Description("Derive terrain cells from material layers; optionally save only the draft JSON.")]
    public object derive_material_terrain_cells(string draft_id, string? game_root = null, bool save_draft = false, string? write_mode = null)
        => runtime.DeriveMaterialTerrainCells(game_root, draft_id, save_draft, write_mode);

    [McpServerTool]
    [Description("Generate material map cells from terrain cells and export a preview; optionally save only the draft JSON.")]
    public object generate_terrain_driven_map(string draft_id, string? game_root = null, bool save_draft = false, string? write_mode = null)
        => runtime.GenerateTerrainDrivenMap(game_root, draft_id, save_draft, write_mode);

    [McpServerTool]
    [Description("Render a beautified terrain map preview PNG. Read-only.")]
    public object beautify_terrain_map_preview(string draft_id, string? game_root = null)
        => runtime.BeautifyTerrainMapPreview(game_root, draft_id);

    [McpServerTool]
    [Description("Preview extracting map material tiles from a map draft into a material library/export directory. Does not create files.")]
    public object preview_extract_map_materials(MapMaterialExtractionMcpRequest request, string? game_root = null)
        => runtime.PreviewExtractMapMaterials(game_root, request);

    [McpServerTool]
    [Description("Extract map material tiles from a map draft. Writes only material library/export image files; does not modify game resources.")]
    public object extract_map_materials(MapMaterialExtractionMcpRequest request, string? game_root = null, string? write_mode = null)
        => runtime.ExtractMapMaterials(game_root, request, write_mode);

    [McpServerTool]
    [Description("Preview terrain beautify filter output for a map draft and export preview PNG. Does not modify draft/game files.")]
    public object preview_terrain_beautify_filter(string draft_id, string? filter = null, int strength = 0, string? game_root = null)
        => runtime.PreviewTerrainBeautifyFilter(game_root, draft_id, filter, strength);

    [McpServerTool]
    [Description("Save terrain beautify settings to a map draft JSON only; game resources are not modified.")]
    public object apply_terrain_beautify_to_draft(string draft_id, string? filter = null, int strength = 0, string? game_root = null, string? write_mode = null)
        => runtime.ApplyTerrainBeautifyToDraft(game_root, draft_id, filter, strength, write_mode);

    [McpServerTool]
    [Description("Read shop editor rows composed from campaign names and shop data. Read-only.")]
    public object read_shop_editor(string? game_root = null, string? keyword = null, int limit = 80)
        => runtime.ReadShopEditor(game_root, keyword, limit);

    [McpServerTool]
    [Description("Diagnose explicit shops, auto-shop bytes, and R_00/R_01 shop runtime candidates. Read-only.")]
    public object diagnose_shop_runtime(string? game_root = null, int limit = 120)
        => runtime.DiagnoseShopRuntime(game_root, limit);

    [McpServerTool]
    [Description("Preview campaign-name/shop-data row updates without writing.")]
    public object preview_write_shop_rows(List<ShopRowUpdate> updates, string? game_root = null)
        => runtime.PreviewWriteShopRows(game_root, updates);

    [McpServerTool]
    [Description("Write campaign-name/shop-data row updates using HexTableWriter backups and reports.")]
    public object write_shop_rows(List<ShopRowUpdate> updates, string? game_root = null, string? write_mode = null)
        => runtime.WriteShopRows(game_root, updates, write_mode);

    [McpServerTool]
    [Description("Read global settings, editable name tables, title, and evidence. Read-only.")]
    public object read_global_settings(string? game_root = null, int limit = 80)
        => runtime.ReadGlobalSettings(game_root, limit);

    [McpServerTool]
    [Description("Preview global setting updates without writing.")]
    public object preview_write_global_settings(GlobalSettingsUpdate update, string? game_root = null)
        => runtime.PreviewWriteGlobalSettings(game_root, update);

    [McpServerTool]
    [Description("Write supported global settings using backups, reports, and reread validation.")]
    public object write_global_settings(GlobalSettingsUpdate update, string? game_root = null, string? write_mode = null)
        => runtime.WriteGlobalSettings(game_root, update, write_mode);

    [McpServerTool]
    [Description("Prepare a safe manual diff experiment for global numeric settings. Creates test copies and a JSON report only.")]
    public object run_global_numeric_discovery(string? game_root = null)
        => runtime.RunGlobalNumericDiscovery(game_root);

    [McpServerTool]
    [Description("Prepare low-risk global numeric manual diff cases: noop plus seven leaf field cases. Creates test copies only.")]
    public object run_global_numeric_low_risk_discovery(string? game_root = null)
        => runtime.RunGlobalNumericLowRiskDiscovery(game_root);

    [McpServerTool]
    [Description("Compare noop_case against low-risk global numeric case folders and write a read-only diff report.")]
    public object compare_global_numeric_low_risk_diffs(string evidence_root, string? game_root = null)
        => runtime.CompareGlobalNumericLowRiskDiffs(game_root, evidence_root);

    [McpServerTool]
    [Description("Query static candidates for global numeric definitions. Read-only; does not promote pending fields.")]
    public object query_global_numeric_definitions(string? game_root = null)
        => runtime.QueryGlobalNumericDefinitions(game_root);

    [McpServerTool]
    [Description("Scan ability-tier calculation/display patch points in Ekd5.exe. Read-only; writes require signature-matched preview.")]
    public object query_ability_tier_patch_points(string? game_root = null)
        => runtime.QueryAbilityTierPatchPoints(game_root);

    [McpServerTool]
    [Description("Preview an ability-tier profile patch. Supports 4-7 tier branch-merge previews; 8-10 tier profiles report relocation requirements.")]
    public object preview_write_ability_tier_profile(AbilityTierProfileUpdate update, string? game_root = null)
        => runtime.PreviewWriteAbilityTierProfile(game_root, update);

    [McpServerTool]
    [Description("Write a verified 4-7 tier ability profile to Ekd5.exe with backup, SHA256 report, and reread validation.")]
    public object write_ability_tier_profile(AbilityTierProfileUpdate update, string? game_root = null, string? write_mode = null)
        => runtime.WriteAbilityTierProfile(game_root, update, write_mode);

    [McpServerTool]
    [Description("Parse AI scenario text import markup into legacy command previews. Read-only.")]
    public object parse_scenario_text_import(string input, string? game_root = null, string? scenario_kind = "R")
        => runtime.ParseScenarioTextImport(game_root, input, scenario_kind);

    [McpServerTool]
    [Description("Compile AI scenario text import markup into R/S structural commands and write it directly with backups and reports.")]
    public object apply_scenario_text_import(
        string input,
        string relative_path,
        string? game_root = null,
        string? scenario_kind = "R")
        => runtime.ApplyScenarioTextImport(game_root, input, relative_path, scenario_kind);

    [McpServerTool]
    [Description("Read the AI scenario text import template. Read-only.")]
    public object read_scenario_text_import_template(string? game_root = null)
        => runtime.ReadScenarioTextImportTemplate(game_root);

    [McpServerTool]
    [Description("Export scenario texts to CSV or TXT under CCZModStudio_Exports/ScenarioTexts.")]
    public object export_scenario_texts(string relative_path, string? game_root = null, string format = "csv")
        => runtime.ExportScenarioTexts(game_root, relative_path, format);

    [McpServerTool]
    [Description("Scan R/S scripts for script variable usage. Read-only.")]
    public object scan_script_variables(string? game_root = null, string? relative_path = null, int limit = 200, int max_files = 200)
        => runtime.ScanScriptVariables(game_root, relative_path, limit, max_files);

    [McpServerTool]
    [Description("Read script variable values up to a command for requested addresses. Read-only.")]
    public object read_script_variable_snapshot(string relative_path, string? game_root = null, int? scene_index = null, int? section_index = null, int? command_index = null, List<int>? addresses = null)
        => runtime.ReadScriptVariableSnapshot(game_root, relative_path, scene_index, section_index, command_index, addresses);

    [McpServerTool]
    [Description("Read an R-scene visual draft from CCZModStudio_Notes. Read-only.")]
    public object read_rscene_draft(string scenario_file_name, string? game_root = null)
        => runtime.ReadRSceneDraft(game_root, scenario_file_name);

    [McpServerTool]
    [Description("Save an R-scene visual draft JSON under CCZModStudio_Notes.")]
    public object save_rscene_draft(RSceneDraftSaveRequest request, string? game_root = null)
        => runtime.SaveRSceneDraft(game_root, request);

    [McpServerTool]
    [Description("Publish a saved R-scene visual draft to RS/R_*.eex as background and actor placement commands with backups and reports.")]
    public object publish_rscene_draft_to_scenario(string scenario_file_name, string? game_root = null)
        => runtime.PublishRSceneDraftToScenario(game_root, scenario_file_name);

    [McpServerTool]
    [Description("List R-scene visual command candidates and scene state candidates. Read-only.")]
    public object list_rscene_command_candidates(string relative_path, string? game_root = null, int limit = 200)
        => runtime.ListRSceneCommandCandidates(game_root, relative_path, limit);

    [McpServerTool]
    [Description("List writable battlefield deployment slots from S-script 46/47/4B commands. Read-only.")]
    public object list_battlefield_deployment_slots(string relative_path, string? game_root = null, int limit = 300)
        => runtime.ListBattlefieldDeploymentSlots(game_root, relative_path, limit);

    [McpServerTool]
    [Description("Preview battlefield deployment writes and report which records are writable.")]
    public object preview_battlefield_deployment_write(string relative_path, List<BattlefieldDeploymentUpdate> updates, string? game_root = null)
        => runtime.PreviewBattlefieldDeploymentWrite(game_root, relative_path, updates);

    [McpServerTool]
    [Description("Write battlefield deployment coordinates/person/AI for verified S-script records using backups and reread validation.")]
    public object write_battlefield_deployment(string relative_path, List<BattlefieldDeploymentUpdate> updates, string? game_root = null, string? write_mode = null)
        => runtime.WriteBattlefieldDeployment(game_root, relative_path, updates, write_mode);

    [McpServerTool]
    [Description("Inspect EEX archive header/section candidates. Read-only.")]
    public object inspect_eex_entries(string relative_path, string? game_root = null, string? category = null, int limit = 200)
        => runtime.InspectEexEntries(game_root, relative_path, category, limit);

    [McpServerTool]
    [Description("Compare an EEX archive against nearby/related project archives. Read-only.")]
    public object compare_eex_archives(string relative_path, string? game_root = null, int max_peers = 8)
        => runtime.CompareEexArchives(game_root, relative_path, max_peers);

    [McpServerTool]
    [Description("Export field/table annotations for one HexTable to CCZModStudio_Exports/TableAnnotations.")]
    public object export_table_annotations(string table_name, string? game_root = null)
        => runtime.ExportTableAnnotations(game_root, table_name);

    [McpServerTool]
    [Description("Diagnose Qing'er 6.6 revised-layout project evidence, tables, resources, and warnings. Read-only.")]
    public object diagnose_qinger66_project(string? game_root = null)
        => runtime.DiagnoseQinger66Project(game_root);

    [McpServerTool]
    [Description("Audit Qing'er 6.6 item layout, hidden name tails, icon resources, and effect resolution. Read-only.")]
    public object audit_qinger66_items(string? game_root = null, int limit = 200)
        => runtime.AuditQinger66Items(game_root, limit);

    [McpServerTool]
    [Description("List legacy MFC/old-tool dialog or resource evidence files. Read-only; does not expose GUI state.")]
    public object list_legacy_mfc_dialogs(string? game_root = null, int limit = 100)
        => runtime.ListLegacyMfcDialogs(game_root, limit);

    [McpServerTool]
    [Description("Read one legacy MFC/old-tool dialog or resource evidence file. Read-only.")]
    public object read_legacy_mfc_dialog(string relative_path, string? game_root = null, int max_chars = 12000)
        => runtime.ReadLegacyMfcDialog(game_root, relative_path, max_chars);

    [McpServerTool]
    [Description("Read scenario reference checklist entries for one or more R/S scripts. Read-only.")]
    public object read_scenario_reference_checklist(string? relative_path = null, string? game_root = null, int limit = 50)
        => runtime.ReadScenarioReferenceChecklist(game_root, relative_path, limit);

    [McpServerTool]
    [Description("List the project-side item effect catalog. Read-only.")]
    public object list_item_effect_catalog(string? game_root = null)
        => runtime.ListItemEffectCatalog(game_root);

    [McpServerTool]
    [Description("Save the project-side item effect catalog JSON. Does not write fixed-width game tables.")]
    public object save_item_effect_catalog(List<ItemEffectCatalogEntry> entries, string? game_root = null, string? write_mode = null)
        => runtime.SaveItemEffectCatalog(game_root, entries, write_mode);

    [McpServerTool]
    [Description("Write one 6.6 Ekd5.exe item-effect name slot at 0x9E800 + slot_index * 16. Does not change effect-id bindings.")]
    public object write_item_effect_name_66_slot(
        [Description("0-based 6.6 item-effect name slot index in Ekd5.exe.")]
        int slot_index,
        [Description("New GBK name. Must fit in one 16-byte slot including NUL terminator.")]
        string name,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes with backup/report/reread validation.")]
        string? write_mode = null)
        => runtime.WriteItemEffectName66Slot(game_root, slot_index, name, write_mode);

    [McpServerTool]
    [Description("Compatibility alias for write_item_effect_name_66_slot.")]
    public object write_item_effect_name66_slot(
        [Description("0-based 6.6 item-effect name slot index in Ekd5.exe.")]
        int slot_index,
        [Description("New GBK name. Must fit in one 16-byte slot including NUL terminator.")]
        string name,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes with backup/report/reread validation.")]
        string? write_mode = null)
        => runtime.WriteItemEffectName66Slot(game_root, slot_index, name, write_mode);

    [McpServerTool]
    [Description("Read project equipment type profile and evidence. Read-only.")]
    public object read_equipment_type_profile(string? game_root = null)
        => runtime.ReadEquipmentTypeProfile(game_root);

    [McpServerTool]
    [Description("Read integrated job settings: job series, detailed jobs, growth/equipment permissions, pierce, restraint matrix, and job attribute matrix. Read-only.")]
    public object read_job_settings(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional detailed job IDs to return.")]
        List<int>? job_ids = null,
        [Description("Optional job-series IDs to return.")]
        List<int>? job_series_ids = null,
        [Description("Include 40x40 restraint matrix and 8x40 attribute matrix.")]
        bool include_matrices = true,
        [Description("Maximum job/job-series rows to return. Defaults to 80; capped at 500.")]
        int limit = 80)
        => runtime.ReadJobSettings(game_root, job_ids, job_series_ids, include_matrices, limit);

    [McpServerTool]
    [Description("Preview job setting updates without writing files. Supports job series names, detailed job name/description/growth/pierce, restraint matrix, and attribute matrix.")]
    public object preview_job_settings(
        [Description("Job settings update payload.")]
        JobSettingsUpdate update,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewJobSettings(game_root, update);

    [McpServerTool]
    [Description("Write job setting updates through HexTableWriter backups/reports. Defaults to direct writes with backups and reports.")]
    public object write_job_settings(
        [Description("Job settings update payload.")]
        JobSettingsUpdate update,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default direct writes the detected project.")]
        string? write_mode = null)
        => runtime.WriteJobSettings(game_root, update, write_mode);

    [McpServerTool]
    [Description("Read accessory equipment multi-job-series groups from Ekd5.exe OD address 0044C341. Read-only.")]
    public object read_accessory_job_groups(string? game_root = null)
        => runtime.ReadAccessoryJobGroups(game_root);

    [McpServerTool]
    [Description("Preview accessory equipment multi-job-series group bytes for Ekd5.exe OD address 0044C341. Does not write files.")]
    public object preview_accessory_job_groups(List<List<int>> groups, string? game_root = null)
        => runtime.PreviewAccessoryJobGroups(game_root, groups);

    [McpServerTool]
    [Description("Write accessory equipment multi-job-series groups to Ekd5.exe OD address 0044C341 using fixed-length overwrite. Defaults to direct writes with backups and reports.")]
    public object write_accessory_job_groups(List<List<int>> groups, string? game_root = null, string? write_mode = null)
        => runtime.WriteAccessoryJobGroups(game_root, groups, write_mode);

    [McpServerTool]
    [Description("Render attack/pierce area preview PNG under CCZModStudio_Exports/EffectPreviews.")]
    public object preview_attack_area(string column_name, int field_value, string? game_root = null, int canvas_size = 192)
        => runtime.PreviewAttackArea(game_root, column_name, field_value, canvas_size);

    [McpServerTool]
    [Description("Render strategy animation preview frames under CCZModStudio_Exports/EffectPreviews.")]
    public object preview_strategy_animation(int animation_value, string? game_root = null, string? kind = "small_meff", int canvas_size = 160)
        => runtime.PreviewStrategyAnimation(game_root, animation_value, kind, canvas_size);
}

