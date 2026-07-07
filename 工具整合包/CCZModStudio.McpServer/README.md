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

- Capability discovery: `read_mcp_capability_manifest` is the authoritative runtime manifest. It reports the service name, build/document version, tool count, groups, aliases, removed tools, safety notes, and the full sorted tool list. Use it when a client UI truncates `tools/list`; current validation expects 206 authoring tools and requires the manifest count to match `tools/list`.
- Project detection: `detect_project`.
- ModPackage pipeline: `analyze_mod_request`, `compile_mod_package`, `analyze_standalone_scenario_request`, `compile_standalone_scenario_package`, `auto_make_mod`, `auto_validate_mod`, `list_available_slots`, `preview_mod_package`, `apply_mod_package`, `validate_mod_package`, `export_mod_report`, `compile_scenario_patch`, `preview_scenario_patch`, `apply_scenario_patch`, `apply_scenario_patch_aggressive`, `apply_scenario_text_import`, `publish_rscene_draft_to_scenario`.
- Game settings data: `list_tables`, `read_table`, `write_table_rows`, `read_table_schema`, `read_table_derived_display`, `export_table_csv`, `preview_import_table_csv`, `apply_import_table_csv`, `read_job_settings`, `preview_job_settings`, `write_job_settings`, `read_accessory_job_groups`, `preview_accessory_job_groups`, `write_accessory_job_groups`, `list_effects`, `read_effect`, `export_effect_package`, `preview_effect_package`, `apply_effect_package`, `list_effect_templates`, `build_effect_package_from_template`, `preview_effect_patch`, `apply_effect_patch`, `scan_exe_code_caves`, `draft_assembly_patch`, `preview_assembly_patch`, `apply_assembly_patch`, `draft_special_skill_patch`, `preview_special_skill_patch`, `apply_special_skill_patch`, `rebind_special_skill_params`, `read_effect_resource`, `read_effect_prompt`.
- Role editor data: `read_role_editor`, `preview_write_roles`, `write_roles`, `read_role_texts`, `preview_write_role_texts`, `write_role_texts`. These tools aggregate the GUI role editor surface across the person table, R/S image tables, job/equipment choices, role biographies, critical quotes, retreat quotes, and special critical quote slot mapping.
- Image assignment workflow: `find_free_image_assignment_ids`, `preview_image_assignment_update`, `write_image_assignment_update`. These tools expose free Face/R/S IDs, resource presence, conflict previews, B image assigner oracle evidence, and safe HexTable writeback/reread.
- Official image assigner oracle: `detect_image_assigner_oracle`, `read_image_assigner_oracle_config`, `compare_image_assigner_oracle`, `plan_image_assigner_validation`, `run_image_assigner_oracle_smoke`, `compare_image_assigner_output`, `run_image_assigner_assignment_experiment`.
- Game production data: `list_scenario_files`, `read_scenario_commands`, `search_scenario_scripts`, `read_scenario_texts`, `write_scenario_texts`, `list_battlefield_unit_status_targets`, `read_battlefield_unit_status`, `write_battlefield_unit_status`, `list_scenario_command_templates`, `read_scenario_command_template`, `list_hexzmap_blocks`, `read_hexzmap_block`, `write_hexzmap_block`, `preview_map_image`, `replace_map_image`.
- Resource replacement: `preview_resource_replace`, `replace_resource`.
- Image catalog, previews, and BMP exports: `list_image_resources`, `list_image_resource_entries`, `export_image_resource_preview`, `export_bmp_assets`.
- Editable image targets: `read_editable_image_target`, `preview_editable_image_write`, `write_editable_image`. Targets can be addressed by semantic item/strategy icon rows, Face/R/S assignment rows, or by resource path plus image number/icon index; writes go through `EditableImageCodecService`.
- Portrait frames: `list_portrait_frames`, `preview_apply_portrait_frame`, `apply_portrait_frame`. Frame application targets Face/Data IDs and writes through the E5 replacement path with backup/report/reread evidence.
- AI image assets: `list_ccz_image_asset_presets`, `build_ccz_image_prompt`, `prepare_ccz_generated_image`, `draw_ccz_image_asset`, `draw_and_replace_ccz_image_asset`, `build_rs_pixel_character_design`.
- Local R/S pixel edit workspace: `create_rs_pixel_edit_workspace`, `build_rs_pixel_edit_plan`, `apply_rs_pixel_frame_edits`, `export_rs_pixel_contact_sheets`, `validate_rs_pixel_edit_workspace`. These tools use the local CCZModStudio C# image-editing path to prepare and audit R/S BMP workspaces; they write only under `CCZModStudio_Exports/RS_PixelDesign/<PackageId>` and never write `Pmapobj.e5` or `Unit_*.e5`.
- E5 image entries: `list_e5_image_entries`, `preview_e5_image_replace`, `replace_e5_image_entry`, `preview_e5_image_batch_replace`, `replace_e5_image_batch`.
- Role image true-color tools: `validate_rs_pixel_material_package`, `preview_r_image_raw_replace`, `replace_r_image_raw`, `preview_r_image_raw_batch_replace`, `replace_r_image_raw_batch`, `preview_s_image_raw_replace`, `replace_s_image_raw`, `preview_s_image_raw_batch_replace`, `replace_s_image_raw_batch`, `preview_job_s_image_raw_replace`, `replace_job_s_image_raw`, `preview_job_s_image_raw_batch_replace`, `replace_job_s_image_raw_batch_replace`, `replace_job_s_image_raw_batch` write R/S entries as PNG true-color data despite the legacy `raw` tool names. `replace_job_s_image_raw_batch` is a compatibility alias for `replace_job_s_image_raw_batch_replace`; both names stay registered for old clients. `validate_rs_pixel_material_package` is the read-only gate for R/S pixel packages: it checks `front.bmp`/`back.bmp`/`mov.bmp`/`atk.bmp`/`spc.bmp`, strict CCZ 6.5 dimensions, magenta-key risk, R/S mapping, and RAW->PNG test-copy risk before any write. RAW maintenance remains available through `preview_e5_role_raw_normalize` and `normalize_e5_role_raw`.
- Face and icon batch import: `preview_role_face_batch_import`, `replace_role_face_batch_import`, `preview_item_icon_batch_import`, `replace_item_icon_batch_import`, `preview_strategy_icon_batch_import`, `replace_strategy_icon_batch_import`.
- DLL bitmap icons: `preview_dll_icon_replace`, `replace_dll_icon`, `preview_clear_dll_icon`, `clear_dll_icon`.
- Map workbench and materials: `list_map_drafts`, `read_map_draft`, `save_map_draft`, `preview_map_canvas`, `export_map_canvas_jpeg`, `publish_map_canvas_to_map_image`, `publish_map_workbench_bundle`, `list_material_assets`, `migrate_material_library_preview`, `migrate_material_library`, `analyze_material_driven_terrain`, `derive_material_terrain_cells`, `generate_terrain_driven_map`, `beautify_terrain_map_preview`, `preview_extract_map_materials`, `extract_map_materials`, `preview_terrain_beautify_filter`, `apply_terrain_beautify_to_draft`.
- Diagnostics and read-only references: `diagnose_qinger66_project`, `audit_qinger66_items`, `list_legacy_mfc_dialogs`, `read_legacy_mfc_dialog`, `read_scenario_reference_checklist`. Qing'er 6.6/6.6x tools are diagnostics only and do not create direct writes.
- Knowledge base: `list_knowledge_entries`, `search_knowledge_entries`, `read_knowledge_entry`.

`write_item_effect_name66_slot` is a compatibility alias for `write_item_effect_name_66_slot`. `promote_test_copy_mod` remains intentionally removed and is forbidden by validation.

High-risk writes continue to use the shared service layer for backups, reread verification, structured reports, and version guards, but MCP write gates now default to direct writes on the detected project. The `write_mode` parameter is retained for old clients; all accepted values are normalized to direct writes. `read_job_settings` integrates 6.5 job-series names, detailed job names/descriptions/growth/equipment permissions/pierce, the 40x40 restraint matrix, the 8x40 job-attribute matrix, and B image assigner `UserXK` evidence. The job-attribute matrix is the official B image assigner Option1 order: `移动声音/移动速度/攻击声音/远程兵种/攻击延迟/兵种类型/策略伤害/参与围攻`; `row0` is no longer described as `兵种大类`. `AttributeRows` now reports GUI editor hints: rows `0/1/2/3/4/5/7` are `combo` rows with `ValueChoices` displayed as `中文名称：数字`, while row `6 策略伤害` is `numeric` with range `0..255` and common values `90/100/110/120/125/130`. The desktop GUI transposes this table for editing as `40 job-series rows x 8 attribute columns`, but MCP keeps the stable raw matrix coordinates. `preview_job_settings` and `write_job_settings` accept `attribute_matrix` cells as `{ row_id, column_id, value }`, validate `row_id=0..7`, `column_id=0..39`, `value=0..255`, and return `FileOffsetHex` using `0xA38C0 + row_id*40 + column_id`; display helper fields such as `OldDisplayValue`/`NewDisplayValue` are descriptive only and are not accepted as write inputs. The old `UserXK` tail `0xA3A00..0xA3A27` is reported as `write_enabled=false` evidence only. `write_job_settings` writes supported HexTable-backed fields/matrix cells directly. Accessory equipment multi-job-series grouping is exposed through `read_accessory_job_groups`, `preview_accessory_job_groups`, and `write_accessory_job_groups`; direct writes are enabled there too. `ModPackage` is the preferred AI output format for multi-file work. Automation-first flow is `analyze_mod_request` -> `compile_mod_package` -> `preview_mod_package` -> `apply_mod_package(automation_mode=direct)` -> `auto_validate_mod` -> `export_mod_report`; standalone single-stage generation uses `analyze_standalone_scenario_request` -> `compile_standalone_scenario_package` and marks the package with `constraint_mode=force_open`. `auto_make_mod` writes the detected project directly by default. Default behavior applies table updates, R/S parameter patches, structural R/S append/insert, effect packages, map/image bundles, and resource updates through existing dedicated writers; `force_open` packages allow structural R/S append/insert and non-whitelisted known command IDs to compile, while still using backups, rereads, and reports. The server no longer exposes project maintenance, map-linking, creator-note, resource-diagnostic, release-copy, test-copy diff, promotion, or automatic restore tools. Recovery is handled through the backup files created by write operations.
The official B image assigner oracle is a read-only referee for validation, not a second write path. `detect_project` includes `ImageAssignerOracle`; `read_table`/`write_table_rows` include an `Oracle` block for R/S image assignment tables. `compare_image_assigner_oracle` checks `RFileHead/FileHead/UserXK/DefID/AssID/SMagic` against current table assumptions, and 6.6x `Mg*` strategy-extension addresses are exposed only as read-only candidates. `run_image_assigner_oracle_smoke` defaults to `static`; `launch_only` and `ui_probe` never click save. `compare_image_assigner_output` compares before/official-after/CCZ-after test-copy directories and never launches the official tool. `run_image_assigner_assignment_experiment` is a controlled test-copy experiment: the official side writes the `System.ini` R/S offset directly in `official_case`, the CCZ side writes through HexTable in `ccz_case`, then MCP compares offsets, bytes, and reread results.
Image preview exports write only under `CCZModStudio_Exports/ImagePreviews`. `export_bmp_assets` writes importable BMP material files to the caller-selected output folder and does not modify game resources. AI image assets write generated files, manifests, and raw responses under `CCZModStudio_Exports/AiImageAssets`; `draw_and_replace_ccz_image_asset` can continue from generation through E5/DLL replacement in one direct write operation with backups and reread validation. R/S image presets can use RetroDiffusion through `RETRO_DIFFUSION_BASE_URL`, `RETRO_DIFFUSION_API_KEY`, and `RETRO_DIFFUSION_MODEL`, while `build_rs_pixel_character_design` is the MCP-first AI draft entry when external generation is allowed. When external generation is not allowed, use the local R/S pixel edit workspace tools instead; they perform deterministic frame-level BMP edits, write `reports/edit_log.jsonl`, export contact sheets, and validate the package without writing game resources. Backgrounds, faces, and DLL icons continue to use Image Studio env vars: `IMAGE_STUDIO_BASE_URL`, `IMAGE_STUDIO_API_KEY`, `IMAGE_STUDIO_TEXT_MODEL`, `IMAGE_STUDIO_IMAGE_MODEL`, and `IMAGE_STUDIO_API_MODE`. E5 batch replacement writes one backup/report for multiple entries and verifies each entry by rereading. R/S replacement tools write `Pmapobj.e5` and `Unit_*.e5` as PNG true-color entries through the same E5 batch service; job S batch replacement consumes `Job{jobId}` folders and writes Unit resources through the same guarded path. Face, item-icon, and strategy-icon batch imports consume either explicit files or importable export roots. `normalize_e5_role_raw` skips compressed or unknown entries and converts only role images whose dimensions match the RAW rules. Battlefield unit status writes are limited to `46/47` deployment records returned by `list_battlefield_unit_status_targets`; structural R/S append/insert is exposed through `apply_scenario_patch_aggressive`, `apply_scenario_text_import`, and `publish_rscene_draft_to_scenario`. DLL icon writes are limited to `Itemicon.dll`, `Mgcicon.dll`, and `Cmdicon.dll` RT_BITMAP resources.

