# CCZModStudio.McpServer

Local stdio MCP server for CCZModStudio.

## Build

```powershell
dotnet build .\CCZModStudio.McpServer\CCZModStudio.McpServer.csproj
```

## Run

```powershell
dotnet ".\CCZModStudio.McpServer\bin\Debug\net8.0-windows\CCZModStudio.McpServer.dll"
```

Use `工具整合包` as the working directory. To pin a game project instead of using auto-detection, set:

```text
CCZMODSTUDIO_GAME_ROOT=<game-root>
```

The server writes MCP JSON-RPC messages on stdout. Do not add diagnostic console output to stdout.

## Tool Groups

- Project workflow: `detect_project`, `audit_project`, `create_test_copy`, `diff_test_copy`, `create_release_copy`.
- Data editing: `list_tables`, `read_table`, `write_table_rows`.
- Scenario structure, text, and command knowledge: `list_scenario_files`, `read_scenario_commands`, `search_scenario_scripts`, `read_scenario_texts`, `write_scenario_texts`, `list_scenario_command_templates`, `read_scenario_command_template`.
- Map and resources: `list_project_resources`, `run_resource_diagnostics`, `list_hexzmap_blocks`, `read_hexzmap_block`, `write_hexzmap_block`, `replace_map_image`, `replace_resource`.
- E5 image entries: `list_e5_image_entries`, `preview_e5_image_replace`, `replace_e5_image_entry`.
- Creator notes: `list_creator_notes`, `upsert_creator_note`, `delete_creator_note`, `export_creator_notes_csv`.
- Knowledge base: `list_knowledge_entries`, `search_knowledge_entries`, `read_knowledge_entry`.

High-risk writes continue to use the shared service layer for backups, reread verification, structured reports, and version guards. `create_test_copy` refuses nested test copies; release copies must be created from a marked test copy.
Creator notes are project-side records under `CCZModStudio_Notes`; CSV exports go under `CCZModStudio_Exports/CreatorNotes`. They are excluded from release copies and never modify game files.
