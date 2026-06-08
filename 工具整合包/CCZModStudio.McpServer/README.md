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

## Client Config

Recommended local entry:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\MCP配置\start-ccz-mcp.ps1"
```

Generate Codex, Claude Desktop, and generic `mcpServers` snippets:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\MCP配置\generate-mcp-config.ps1" -Build
```

Validate stdio startup and tool registration:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\MCP配置\validate-mcp-config.ps1"
```

Generated files are written under `MCP配置\_generated` and are intentionally ignored by Git because they contain machine-local absolute paths. Source-controlled templates live in `MCP配置\templates`; detailed setup notes are in `MCP配置\README.md`.

## Tool Groups

- Project workflow: `detect_project`, `audit_project`, `create_test_copy`, `diff_test_copy`, `create_release_copy`.
- Data editing: `list_tables`, `read_table`, `write_table_rows`.
- Scenario structure, text, and command knowledge: `list_scenario_files`, `read_scenario_commands`, `search_scenario_scripts`, `read_scenario_texts`, `write_scenario_texts`, `list_scenario_command_templates`, `read_scenario_command_template`.
- Map and resources: `list_project_resources`, `run_resource_diagnostics`, `list_hexzmap_blocks`, `read_hexzmap_block`, `write_hexzmap_block`, `replace_map_image`, `preview_resource_replace`, `replace_resource`.
- Image catalog and previews: `list_image_resources`, `list_image_resource_entries`, `export_image_resource_preview`.
- AI image assets: `list_ccz_image_asset_presets`, `build_ccz_image_prompt`, `prepare_ccz_generated_image`, `draw_ccz_image_asset`.
- E5 image entries: `list_e5_image_entries`, `preview_e5_image_replace`, `replace_e5_image_entry`, `preview_e5_image_batch_replace`, `replace_e5_image_batch`.
- DLL bitmap icons: `preview_dll_icon_replace`, `replace_dll_icon`, `preview_clear_dll_icon`, `clear_dll_icon`.
- Creator notes: `list_creator_notes`, `upsert_creator_note`, `delete_creator_note`, `export_creator_notes_csv`.
- Knowledge base: `list_knowledge_entries`, `search_knowledge_entries`, `read_knowledge_entry`.

High-risk writes continue to use the shared service layer for backups, reread verification, structured reports, and version guards. `create_test_copy` refuses nested test copies; release copies can be created from the current project or from a marked test copy.
Image preview exports write only under `CCZModStudio_Exports/ImagePreviews`. AI image assets write generated files, manifests, and raw responses under `CCZModStudio_Exports/AiImageAssets`, then call existing preview tools without writing game resources. R/S image presets use RetroDiffusion by default through `RETRO_DIFFUSION_BASE_URL`, `RETRO_DIFFUSION_API_KEY`, and `RETRO_DIFFUSION_MODEL`; set `CCZ_PIXEL_IMAGE_PROVIDER=image_studio` to temporarily fall back. Backgrounds, faces, and DLL icons continue to use Image Studio env vars: `IMAGE_STUDIO_BASE_URL`, `IMAGE_STUDIO_API_KEY`, `IMAGE_STUDIO_TEXT_MODEL`, `IMAGE_STUDIO_IMAGE_MODEL`, and `IMAGE_STUDIO_API_MODE`. E5 batch replacement writes one backup/report for multiple entries and verifies each entry by rereading. DLL icon writes are limited to `Itemicon.dll`, `Mgcicon.dll`, and `Cmdicon.dll` RT_BITMAP resources.
Creator notes are project-side records under `CCZModStudio_Notes`; CSV exports go under `CCZModStudio_Exports/CreatorNotes`. They are excluded from release copies and never modify game files.
