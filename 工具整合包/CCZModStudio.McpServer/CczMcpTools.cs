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
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
        => runtime.WriteTableRows(game_root, table_name, updates, write_mode);

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
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
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
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
        => runtime.WriteScenarioTexts(game_root, relative_path, updates, write_mode);

    [McpServerTool]
    [Description("Write Hexzmap terrain cells for a map block. Creates a backup and structured report.")]
    public object write_hexzmap_block(
        [Description("Map ID, for example M000.")]
        string map_id,
        [Description("Cell changes using x/y grid coordinates and terrain_id 0..255.")]
        List<HexzmapCellUpdate> changes,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
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
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
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
        int? height = null)
        => runtime.BuildCczImagePrompt(game_root, preset, description, target_relative_path, image_number, r_image_id, s_image_id, face_id, job_id, faction_slot, output_format, width, height);

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
        bool dry_run = true)
        => runtime.DrawCczImageAsset(game_root, preset, description, target_relative_path, image_number, r_image_id, s_image_id, face_id, job_id, faction_slot, output_format, width, height, dry_run);

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
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct",
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
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
        => runtime.ReplaceE5ImageBatch(game_root, target_relative_path, updates, write_mode);

    [McpServerTool]
    [Description("Preview raw R actor image replacement from a material folder without writing.")]
    public object preview_r_image_raw_replace(
        [Description("R image id. r_actor maps R=n to Pmapobj.e5 images 2n+1/2n+2.")]
        int r_image_id,
        [Description("Material folder containing source raw image files. Relative paths resolve from workspace, project root, then cwd.")]
        string material_folder,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewRImageRawReplace(game_root, r_image_id, material_folder);

    [McpServerTool]
    [Description("Replace raw R actor image assets from a material folder. Creates backup and structured report.")]
    public object replace_r_image_raw(
        [Description("R image id. r_actor maps R=n to Pmapobj.e5 images 2n+1/2n+2.")]
        int r_image_id,
        [Description("Material folder containing source raw image files. Relative paths resolve from workspace, project root, then cwd.")]
        string material_folder,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
        => runtime.ReplaceRImageRaw(game_root, r_image_id, material_folder, write_mode);

    [McpServerTool]
    [Description("Preview batch raw R actor image replacement from a material root without writing. Subfolders use R12, R_12, or 12 and contain front.bmp/back.bmp.")]
    public object preview_r_image_raw_batch_replace(
        [Description("Material root containing numbered R image subfolders. Relative paths resolve from workspace, project root, then cwd.")]
        string material_root,
        [Description("Optional R image ids to include. When omitted or empty, all valid numbered subfolders are considered.")]
        List<int>? allowed_r_image_ids = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewRImageRawBatchReplace(game_root, material_root, allowed_r_image_ids);

    [McpServerTool]
    [Description("Replace batch raw R actor image assets from a material root. Creates backup and structured aggregate report.")]
    public object replace_r_image_raw_batch(
        [Description("Material root containing numbered R image subfolders. Relative paths resolve from workspace, project root, then cwd.")]
        string material_root,
        [Description("Optional R image ids to include. When omitted or empty, all valid numbered subfolders are considered.")]
        List<int>? allowed_r_image_ids = null,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
        => runtime.ReplaceRImageRawBatch(game_root, material_root, allowed_r_image_ids, write_mode);

    [McpServerTool]
    [Description("Preview raw S unit image replacement from a material folder without writing.")]
    public object preview_s_image_raw_replace(
        [Description("S image id. s_unit maps compact S ids to Unit image numbers.")]
        int s_image_id,
        [Description("Material folder containing source raw image files. Relative paths resolve from workspace, project root, then cwd.")]
        string material_folder,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional job id for S=0 default unit mapping.")]
        int? job_id = null,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1)
        => runtime.PreviewSImageRawReplace(game_root, s_image_id, material_folder, job_id, faction_slot);

    [McpServerTool]
    [Description("Replace raw S unit image assets from a material folder. Creates backup and structured report.")]
    public object replace_s_image_raw(
        [Description("S image id. s_unit maps compact S ids to Unit image numbers.")]
        int s_image_id,
        [Description("Material folder containing source raw image files. Relative paths resolve from workspace, project root, then cwd.")]
        string material_folder,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional job id for S=0 default unit mapping.")]
        int? job_id = null,
        [Description("Faction slot for S=0 default unit mapping: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1,
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
        => runtime.ReplaceSImageRaw(game_root, s_image_id, material_folder, job_id, faction_slot, write_mode);

    [McpServerTool]
    [Description("Preview batch raw S unit image replacement from a material root without writing. Subfolders use S12, S_12, or 12 and contain mov.bmp/atk.bmp/spc.bmp.")]
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
    [Description("Replace batch raw S unit image assets from a material root. Creates backups and structured aggregate report.")]
    public object replace_s_image_raw_batch(
        [Description("Material root containing numbered S image subfolders. Relative paths resolve from workspace, project root, then cwd.")]
        string material_root,
        [Description("Optional S usages to include. Use job_id for S=0 default unit mapping.")]
        List<BatchSImageUsageUpdate>? allowed_usages = null,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Default faction slot for S=0 when allowed_usages is omitted: 1=ally, 2=friendly, 3=enemy.")]
        int faction_slot = 1,
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
        => runtime.ReplaceSImageRawBatch(game_root, material_root, allowed_usages, faction_slot, write_mode);

    [McpServerTool]
    [Description("Preview batch item icon import without writing. 6.5 writes Itemicon.dll; 6.6 writes E5/Item.e5. Item table icon fields are not changed.")]
    public object preview_item_icon_batch_import(
        [Description("Source image files. Relative paths resolve from workspace, project root, then cwd.")]
        List<string> source_files,
        [Description("Optional item rows with icon indexes. If supplied, source_files count must match target_rows count and order is used.")]
        List<BatchItemIconTargetRowUpdate>? target_rows = null,
        [Description("Optional match mode label, defaults to auto.")]
        string? match_mode = "auto",
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.PreviewItemIconBatchImport(game_root, source_files, target_rows, match_mode);

    [McpServerTool]
    [Description("Batch import item icons. Creates backup and structured aggregate report. Item table icon fields are not changed.")]
    public object replace_item_icon_batch_import(
        [Description("Source image files. Relative paths resolve from workspace, project root, then cwd.")]
        List<string> source_files,
        [Description("Optional item rows with icon indexes. If supplied, source_files count must match target_rows count and order is used.")]
        List<BatchItemIconTargetRowUpdate>? target_rows = null,
        [Description("Optional match mode label, defaults to auto.")]
        string? match_mode = "auto",
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
        => runtime.ReplaceItemIconBatchImport(game_root, source_files, target_rows, match_mode, write_mode);

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
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
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
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
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
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
        => runtime.ClearDllIcon(game_root, target_relative_path, icon_index, write_mode);

    [McpServerTool]
    [Description("Replace a non-core project resource file. Core files must use dedicated tools.")]
    public object replace_resource(
        [Description("Project-relative target resource path.")]
        string target_relative_path,
        [Description("Replacement file path. Relative paths resolve from workspace root first.")]
        string replacement_path,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct",
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
    [Description("List item/job/personal/patch effects from CCZModStudio catalogs and 6.5 tables.")]
    public object list_effects(
        [Description("Effect domain: item, job, personal, or patch.")]
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
        [Description("Effect domain: item, job, personal, or patch.")]
        string domain,
        [Description("Effect id / fixed row id.")]
        int effect_id,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.ReadEffect(game_root, domain, effect_id);

    [McpServerTool]
    [Description("Export one effect as an EffectPackage object.")]
    public object export_effect_package(
        [Description("Effect domain: item, job, personal, or patch.")]
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
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct",
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
        [Description("Automation mode: safe, aggressive_test_copy, force_preview_report, or force_open.")]
        string? automation_mode = "safe")
        => runtime.PreviewModPackage(game_root, package, automation_mode);

    [McpServerTool]
    [Description("Apply a ModPackage through dedicated writers. aggressive_test_copy creates a test copy automatically.")]
    public object apply_mod_package(
        [Description("ModPackage JSON object.")]
        ModPackage package,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct",
        [Description("Automation mode: safe, aggressive_test_copy, force_preview_report, or force_open.")]
        string? automation_mode = "safe")
        => runtime.ApplyModPackage(game_root, package, write_mode, automation_mode);

    [McpServerTool]
    [Description("Analyze, compile, preview, write a test copy, and emit reports for a natural-language MOD request.")]
    public object auto_make_mod(
        [Description("Natural-language MOD request.")]
        string prompt,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Automation mode: aggressive_test_copy or force_open.")]
        string? automation_mode = "aggressive_test_copy",
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
    [Description("Promote a validated ModPackage from a test-copy workflow into the formal base after explicit confirmation.")]
    public object promote_test_copy_mod(
        [Description("ModPackage JSON object.")]
        ModPackage package,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Must be true to write the formal base.")]
        bool confirm_promote = false)
        => runtime.PromoteTestCopyMod(game_root, package, confirm_promote);

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
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct")
        => runtime.ApplyScenarioPatch(game_root, patch, write_mode);

    [McpServerTool]
    [Description("Apply a structural ModScenarioPatch in aggressive mode, intended for test copies.")]
    public object apply_scenario_patch_aggressive(
        [Description("ModScenarioPatch JSON object.")]
        ModScenarioPatch patch,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "test_copy")
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
    [Description("Apply patch segments in a patch-domain EffectPackage using PatchApplyService.")]
    public object apply_effect_patch(
        [Description("Patch-domain EffectPackage JSON object.")]
        EffectPackage package,
        [Description("direct writes the detected project; test_copy requires _CCZModStudio_TestCopy.txt.")]
        string? write_mode = "direct",
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
}
