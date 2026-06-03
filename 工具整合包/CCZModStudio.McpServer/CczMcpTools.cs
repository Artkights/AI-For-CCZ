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
    [Description("Read one CCZModStudio local knowledge-base markdown file by name.")]
    public object read_knowledge_entry(
        [Description("Markdown file name, for example 08_开发执行规则.md.")]
        string name)
        => runtime.ReadKnowledgeEntry(name);
}
