using System.ComponentModel;
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
    [Description("Read rows from a CCZ 6.5 HexTable-backed data table.")]
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
    [Description("List indexed project resources such as E5, Map, RS eex, E5S, WAV, and MP3 files. Read-only.")]
    public object list_project_resources(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional category filter, for example 地图图片, E5资源, R剧本EEX, S剧本EEX, WAV音效.")]
        string? category = null,
        [Description("Optional keyword across file name, id, category, format hint, annotation, and path.")]
        string? keyword = null,
        [Description("Maximum resources to return. Defaults to 200; capped at 5000.")]
        int limit = 200)
        => runtime.ListProjectResources(game_root, category, keyword, limit);

    [McpServerTool]
    [Description("Run read-only project resource diagnostics before replacement or release.")]
    public object run_resource_diagnostics(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional severity filter: Error, Warn, Info, or comma-separated values.")]
        string? severity = null,
        [Description("Optional category filter, for example 地图图片 or R剧本EEX.")]
        string? category = null,
        [Description("Optional keyword across diagnostic fields.")]
        string? keyword = null,
        [Description("Write a UTF-8 resource diagnostic report under CCZModStudio_Reports.")]
        bool write_report = false,
        [Description("Maximum diagnostic items to return. Defaults to 200; capped at 2000.")]
        int limit = 200)
        => runtime.RunResourceDiagnostics(game_root, severity, category, keyword, write_report, limit);

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
    [Description("Run the project audit used by the creator workflow. Optionally writes an audit report.")]
    public object audit_project(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Write a project audit report under CCZModStudio_Reports.")]
        bool write_report = false,
        [Description("Maximum audit items to return. Defaults to 200; capped at 1000.")]
        int limit = 200)
        => runtime.AuditProject(game_root, write_report, limit);

    [McpServerTool]
    [Description("Create a complete CCZModStudio test copy for safe editing. Refuses nested creation from an existing test copy.")]
    public object create_test_copy(
        [Description("Optional source game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.CreateTestCopy(game_root);

    [McpServerTool]
    [Description("Analyze differences between a test copy and its recorded source project. Optionally writes a diff report.")]
    public object diff_test_copy(
        [Description("Test-copy game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Write a diff report under the test copy _CCZModStudio_Reports directory.")]
        bool write_report = false,
        [Description("Maximum diff rows to return. Defaults to 200; capped at 2000.")]
        int limit = 200)
        => runtime.DiffTestCopy(game_root, write_report, limit);

    [McpServerTool]
    [Description("Create a clean release copy from a CCZModStudio test copy, excluding test markers, backups, reports, and exports.")]
    public object create_release_copy(
        [Description("Test-copy game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.CreateReleaseCopy(game_root);

    [McpServerTool]
    [Description("List project-side creator notes used to track design intent, risks, TODOs, rollback points, and verification. Read-only.")]
    public object list_creator_notes(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional scope filter, for example 资源诊断, R/S命令, Hexzmap地形块, 全局项目.")]
        string? scope = null,
        [Description("Optional keyword across title, content, tags, target key, source hint, and scope.")]
        string? keyword = null,
        [Description("Maximum notes to return. Defaults to 100; capped at 1000.")]
        int limit = 100)
        => runtime.ListCreatorNotes(game_root, scope, keyword, limit);

    [McpServerTool]
    [Description("Create or update a project-side creator note. Writes only CCZModStudio_Notes, never game files.")]
    public object upsert_creator_note(
        [Description("Required note content. Use it to record intent, evidence, risk, rollback point, or real-game verification result.")]
        string content,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional existing note id. Omit to create a new note.")]
        string? id = null,
        [Description("Optional note scope, for example 资源诊断, R/S命令, Hexzmap地形块, 全局项目.")]
        string? scope = "全局项目",
        [Description("Optional stable target key, for example 资源诊断#分类=地图图片#规则=连续编号缺口#编号=Map#对象=M000.jpg.")]
        string? target_key = "",
        [Description("Optional note title. Defaults from scope and target key.")]
        string? title = "",
        [Description("Optional comma-separated tags such as 风险,待办,已验证.")]
        string? tags = "",
        [Description("Optional source hint, for example MCP, 实机测试, 旧工具截图.")]
        string? source_hint = "MCP")
        => runtime.UpsertCreatorNote(game_root, id, scope, target_key, title, content, tags, source_hint);

    [McpServerTool]
    [Description("Delete a project-side creator note by id. Writes only CCZModStudio_Notes, never game files.")]
    public object delete_creator_note(
        [Description("Creator note id to delete.")]
        string id,
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null)
        => runtime.DeleteCreatorNote(game_root, id);

    [McpServerTool]
    [Description("Export project-side creator notes to CSV under CCZModStudio_Exports/CreatorNotes.")]
    public object export_creator_notes_csv(
        [Description("Optional game root. Defaults to CCZMODSTUDIO_GAME_ROOT or auto-detection.")]
        string? game_root = null,
        [Description("Optional scope filter.")]
        string? scope = null,
        [Description("Optional keyword filter.")]
        string? keyword = null)
        => runtime.ExportCreatorNotesCsv(game_root, scope, keyword);

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
        [Description("Markdown file name, for example 08_开发执行规则.md.")]
        string name)
        => runtime.ReadKnowledgeEntry(name);
}
