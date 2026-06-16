﻿# CCZModStudio.McpServer

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

- Project detection: `detect_project`.
- ModPackage pipeline: `analyze_mod_request`, `compile_mod_package`, `analyze_standalone_scenario_request`, `compile_standalone_scenario_package`, `auto_make_mod`, `auto_validate_mod`, `promote_test_copy_mod`, `list_available_slots`, `preview_mod_package`, `apply_mod_package`, `validate_mod_package`, `export_mod_report`, `compile_scenario_patch`, `preview_scenario_patch`, `apply_scenario_patch`, `apply_scenario_patch_aggressive`.
- Game settings data: `list_tables`, `read_table`, `write_table_rows`, `list_effects`, `read_effect`, `export_effect_package`, `preview_effect_package`, `apply_effect_package`, `list_effect_templates`, `build_effect_package_from_template`, `preview_effect_patch`, `apply_effect_patch`, `read_effect_resource`, `read_effect_prompt`.
- Game production data: `list_scenario_files`, `read_scenario_commands`, `search_scenario_scripts`, `read_scenario_texts`, `write_scenario_texts`, `list_battlefield_unit_status_targets`, `read_battlefield_unit_status`, `write_battlefield_unit_status`, `list_scenario_command_templates`, `read_scenario_command_template`, `list_hexzmap_blocks`, `read_hexzmap_block`, `write_hexzmap_block`, `preview_map_image`, `replace_map_image`.
- Resource replacement: `preview_resource_replace`, `replace_resource`.
- Image catalog and previews: `list_image_resources`, `list_image_resource_entries`, `export_image_resource_preview`.
- AI image assets: `list_ccz_image_asset_presets`, `build_ccz_image_prompt`, `prepare_ccz_generated_image`, `draw_ccz_image_asset`.
- E5 image entries: `list_e5_image_entries`, `preview_e5_image_replace`, `replace_e5_image_entry`, `preview_e5_image_batch_replace`, `replace_e5_image_batch`.
- Role image RAW tools: `preview_r_image_raw_replace`, `replace_r_image_raw`, `preview_s_image_raw_replace`, `replace_s_image_raw`, `preview_e5_role_raw_normalize`, `normalize_e5_role_raw`.
- DLL bitmap icons: `preview_dll_icon_replace`, `replace_dll_icon`, `preview_clear_dll_icon`, `clear_dll_icon`.
- Knowledge base: `list_knowledge_entries`, `search_knowledge_entries`, `read_knowledge_entry`.

High-risk writes continue to use the shared service layer for backups, reread verification, structured reports, and version guards. `write_mode=test_copy` requires `_CCZModStudio_TestCopy.txt` in the target project root. `ModPackage` is the preferred AI output format for multi-file work. Automation-first flow is `analyze_mod_request` -> `compile_mod_package` -> `preview_mod_package` -> `apply_mod_package(write_mode=test_copy, automation_mode=aggressive_test_copy)` -> `auto_validate_mod` -> `export_mod_report`; standalone single-stage generation uses `analyze_standalone_scenario_request` -> `compile_standalone_scenario_package` and marks the package with `constraint_mode=force_open`. `auto_make_mod(automation_mode=force_open)` selects that standalone R+S pipeline automatically. Formal base promotion is isolated behind `promote_test_copy_mod(confirm_promote=true)`. Default V1.1 behavior still applies table updates, whitelisted R/S parameter patches, whitelisted structural R/S append/insert in aggressive test-copy mode, effect packages, and resource updates through existing dedicated writers; `force_open` packages allow structural R/S append/insert and non-whitelisted known command IDs to compile, while still using backups, rereads, and reports. The server no longer exposes project maintenance, map-linking, creator-note, resource-diagnostic, release-copy, test-copy diff, or automatic restore tools. Recovery is handled through the backup files created by write operations.
Image preview exports write only under `CCZModStudio_Exports/ImagePreviews`. AI image assets write generated files, manifests, and raw responses under `CCZModStudio_Exports/AiImageAssets`, then call existing preview tools without writing game resources. R/S image presets use RetroDiffusion by default through `RETRO_DIFFUSION_BASE_URL`, `RETRO_DIFFUSION_API_KEY`, and `RETRO_DIFFUSION_MODEL`; set `CCZ_PIXEL_IMAGE_PROVIDER=image_studio` to temporarily fall back. Backgrounds, faces, and DLL icons continue to use Image Studio env vars: `IMAGE_STUDIO_BASE_URL`, `IMAGE_STUDIO_API_KEY`, `IMAGE_STUDIO_TEXT_MODEL`, `IMAGE_STUDIO_IMAGE_MODEL`, and `IMAGE_STUDIO_API_MODE`. E5 batch replacement writes one backup/report for multiple entries and verifies each entry by rereading. R/S RAW replacement tools write only `Pmapobj.e5` and `Unit_*.e5` through the same E5 batch service; `normalize_e5_role_raw` skips compressed or unknown entries and converts only role images whose dimensions match the RAW rules. Battlefield unit status writes are limited to `46/47` deployment records returned by `list_battlefield_unit_status_targets`; arbitrary R/S command structure editing is not exposed. DLL icon writes are limited to `Itemicon.dll`, `Mgcicon.dll`, and `Cmdicon.dll` RT_BITMAP resources.
