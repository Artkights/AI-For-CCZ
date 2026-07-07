# CCZModStudio MCP 配置

本目录保存 CCZModStudio 本地 stdio MCP Server 的客户端接入配置、模板和生成脚本。

文档按 UTF-8 保存。Windows PowerShell 5.1 读取无 BOM Markdown 时可能显示乱码；请使用 `Get-Content -Encoding utf8`，或使用当前脚本生成/验证输出作为准确信息来源。

## 文件说明

- `start-ccz-mcp.ps1`：稳定启动入口。脚本会切到 `工具整合包` 目录，必要时构建 `CCZModStudio.McpServer`，再启动 stdio MCP Server。
- `generate-mcp-config.ps1`：按当前机器路径生成可直接复制的客户端配置。
- `templates/`：可迁移模板，使用 `<TOOL_ROOT>` 和 `<GAME_ROOT>` 占位。
- `_generated/`：本机生成结果目录，由脚本创建，不纳入 Git。

## 一键生成

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\generate-mcp-config.ps1" -Build
```

如果要固定到其他游戏目录：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\generate-mcp-config.ps1" -GameRoot "F:\...\基底\刘备传加强版6.5" -Build
```

生成结果位于：

```text
工具整合包\MCP配置\_generated
```

## Codex 接入

把 `_generated\codex-config-snippet.toml` 的内容合并到 Codex 的 `config.toml`，或放入项目 `.codex\config.toml` 后重启 Codex。

配置形态：

```toml
[mcp_servers.cczmodstudio]
command = 'powershell.exe'
args = ['-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', '<TOOL_ROOT>\MCP配置\start-ccz-mcp.ps1', '-GameRoot', '<GAME_ROOT>']
startup_timeout_sec = 120

[mcp_servers.cczmodstudio.env]
CCZMODSTUDIO_TOOL_ROOT = '<TOOL_ROOT>'
CCZMODSTUDIO_GAME_ROOT = '<GAME_ROOT>'
```

如果同时接入 x32dbg / x64dbg MCP，用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\generate-mcp-config.ps1" -GameRoot ".\基底\加强版6.5未加密版" -Build -IncludeX64dbg
```

生成的 Codex 片段会额外包含：

```toml
[mcp_servers.x64dbg]
command = 'cmd'
args = ['/c', 'npx', '-y', 'x64dbg-mcp-server']
startup_timeout_sec = 120

[mcp_servers.x64dbg.env]
X64DBG_MCP_HOST = '127.0.0.1'
X64DBG_MCP_PORT = '27042'
```

x64dbg MCP Server 只负责连接已经启动的 x32dbg/x64dbg 插件；32 位 `Ekd5.exe` 应使用 `x32\x32dbg.exe` 打开。若启用插件 Token，需在本机 Codex 配置或环境变量中设置 `X64DBG_MCP_TOKEN`，不要提交到仓库。

## 6.5 战场自动采证

打开正式 6.5 未加密基底：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\start-x32dbg-6.5.ps1"
```

启动只读战场事件监控：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\watch-6.5-battle-events.ps1" -DurationSec 1800 -PollIntervalMs 500 -Label "merit-auto" -SaveMemoryOnEvent
```

监控器默认优先使用只读 `ReadProcessMemory` 读取 `Ekd5.exe`，不设置断点、不暂停游戏、不写游戏文件；如果 x32dbg MCP bridge 在线，会额外记录 bridge 在线状态和暂停点证据。输出位于：

```text
CCZModStudio_Reports\DebugEvidence\merit-auto-YYYYMMDD-HHMMSS
```

主要产物：

- `events.md`：人工阅读的事件流水。
- `events.jsonl`：机器可处理的单位差分事件。
- `summary.json`：轮询次数、事件数、错误数和输出路径。
- `event-0001/` 等目录：启用 `-SaveMemoryOnEvent` 时保存事件瞬间的关键内存块。

推荐流程是先开监控器，再正常游玩。监控器会自动记录战术单位坐标、HP、MP、行动状态和属性档位变化；需要精确函数调用栈时，再临时启用有限断点或使用 `run-x32dbg-until-planned-hit.ps1`。

### 6.5 自动地址定位实验台

在已有战场日志或前后快照的基础上生成候选地址与写断点计划：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\auto-locate-6.5-addresses.ps1" -UseLatestEventLog -Label "merit-locator"
```

也可以对两个快照做差分：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\auto-locate-6.5-addresses.ps1" -BeforeSnapshot "CCZModStudio_Reports\DebugEvidence\...\merit-snapshot-before" -AfterSnapshot "CCZModStudio_Reports\DebugEvidence\...\merit-snapshot-after" -UseLatestEventLog
```

输出位于：

```text
CCZModStudio_Reports\DebugEvidence\auto-locate-YYYYMMDD-HHMMSS
```

主要产物：

- `auto-locate-summary.md`：人工阅读的候选归纳。
- `auto-locate-candidates.json`：候选地址、评分、证据来源和原因。
- `watchpoint-plan.json`：分批硬件写断点计划，适合后续脚本读取。
- `x32dbg-watchpoint-plan.txt`：可在 x32dbg 中逐批执行的命令计划。
- `merit-watchpoint-plan.json` / `x32dbg-merit-watchpoint-plan.txt`：功勋优先计划，把已知 `+1` 种子和 `battle_globals` 精确 `+1` 候选前置。

默认只做离线分析和计划生成，不写游戏进程、不修改 EXE。若 x32dbg bridge 在线且确实要把第一批硬件写断点下到调试器，可追加 `-ApplyFirstBatch`；每批最多使用 4 个硬件断点槽，命中后再用 `run-x32dbg-until-planned-hit.ps1 -CaptureCurrentPaused` 导出寄存器、栈、反汇编和内存证据。

自动应用功勋优先写断点并等待命中：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\run-6.5-merit-watchpoint-experiment.ps1" -PlanJson "CCZModStudio_Reports\DebugEvidence\merit-physical-locator-20260609-222320\merit-watchpoint-plan.json" -ClearHardwareFirst
```

无调试器时可先验证计划解析：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\run-6.5-merit-watchpoint-experiment.ps1" -PlanJson "CCZModStudio_Reports\DebugEvidence\merit-physical-locator-20260609-222320\merit-watchpoint-plan.json" -DryRun -ApplyOnly
```

x32dbg 命令语义：`bphws ADDRESS,w,SIZE` 是设置硬件写断点；`bphwc` 是删除硬件断点，不要用旧计划中的 `bphwc` 当作设置命令。

推荐使用一键编排入口做预检、可选启动 x32dbg、可选运行实验：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\start-6.5-merit-auto-experiment.ps1" -DryRun -RunExperiment -ApplyOnly
```

当 x32dbg bridge 在线且游戏位于可复现功勋事件前，再运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\start-6.5-merit-auto-experiment.ps1" -RunExperiment -ClearHardwareFirst
```

如果还未打开调试器，可加 `-StartX32dbg` 让脚本调用 `start-x32dbg-6.5.ps1`。脚本会生成 `merit-auto-experiment-YYYYMMDD-HHMMSS` 预检报告，并把子实验输出放在 `watchpoint-run` 子目录。

命中后或 DryRun 后，可汇总写断点证据：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\summarize-6.5-merit-watchpoint-hits.ps1" -InputDir "CCZModStudio_Reports\DebugEvidence\merit-auto-experiment-YYYYMMDD-HHMMSS\watchpoint-run" -GameplayEventLabel "cao-physical-attack-merit"
```

`start-6.5-merit-auto-experiment.ps1` 默认会在运行实验后自动调用该汇总器。汇总报告会写出 `merit-watchpoint-hit-summary.md/json`，并在 `CCZModStudio_Notes\DebugKnowledgeDrafts` 生成知识库审查草稿：若没有 `merit-watchpoint-hit-YYYYMMDD-HHMMSS.json`，结论保持 `needs-write-breakpoint`；若有命中，则仍需要把 CIP、反汇编、寄存器、候选内存和明确的游戏动作标签一起审查后，才能升级知识库证据等级。

## 通用 MCP JSON 接入

支持 `mcpServers` JSON 的客户端可使用：

```text
工具整合包\MCP配置\_generated\mcp-servers.json
```

Claude Desktop 可使用同形态 JSON：

```text
工具整合包\MCP配置\_generated\claude_desktop_config.json
```

如果客户端能可靠设置工作目录和环境变量，也可以使用：

```text
工具整合包\MCP配置\_generated\mcp-servers-direct-dotnet.json
```

推荐默认使用 PowerShell 启动脚本版本，减少 `cwd` 差异。

## 可用工具分组

- 权威能力清单：`read_mcp_capability_manifest`。当客户端 UI 截断 `tools/list` 时，以该 manifest 的 `ToolCount`、`Groups`、`Aliases` 和 `Tools` 为准；当前 authoring MCP 校验口径为 206 个工具，且 manifest 数量必须与 `tools/list` 完全一致。
- 项目识别：`detect_project`
- 数据表/CSV/schema：`list_tables`、`read_table`、`write_table_rows`、`read_table_schema`、`read_table_derived_display`、`export_table_csv`、`preview_import_table_csv`、`apply_import_table_csv`
- 角色编辑组合视图：`read_role_editor`、`preview_write_roles`、`write_roles`、`read_role_texts`、`preview_write_role_texts`、`write_role_texts`
- 形象分配：`find_free_image_assignment_ids`、`preview_image_assignment_update`、`write_image_assignment_update`
- R/S 剧本：`list_scenario_files`、`read_scenario_commands`、`search_scenario_scripts`、`read_scenario_texts`、`write_scenario_texts`
- 指令模板与知识库：`list_scenario_command_templates`、`read_scenario_command_template`、`list_knowledge_entries`、`search_knowledge_entries`、`read_knowledge_entry`
- 整包制作：`analyze_mod_request`、`compile_mod_package`、`analyze_standalone_scenario_request`、`compile_standalone_scenario_package`、`preview_mod_package`、`apply_mod_package`、`auto_make_mod`、`auto_validate_mod`、`validate_mod_package`、`export_mod_report`
- R/S 结构写回：`compile_scenario_patch`、`preview_scenario_patch`、`apply_scenario_patch`、`apply_scenario_patch_aggressive`、`parse_scenario_text_import`、`apply_scenario_text_import`、`read_scenario_text_import_template`、`read_rscene_draft`、`save_rscene_draft`、`publish_rscene_draft_to_scenario`
- 地图和资源：`list_hexzmap_blocks`、`read_hexzmap_block`、`write_hexzmap_block`、`preview_map_image`、`replace_map_image`、`preview_resource_replace`、`replace_resource`
- 地图工作台：`list_map_drafts`、`read_map_draft`、`save_map_draft`、`preview_map_canvas`、`export_map_canvas_jpeg`、`publish_map_canvas_to_map_image`、`publish_map_workbench_bundle`、`preview_extract_map_materials`、`extract_map_materials`、`preview_terrain_beautify_filter`、`apply_terrain_beautify_to_draft`
- 图片资源目录/预览/BMP 导出：`list_image_resources`、`list_image_resource_entries`、`export_image_resource_preview`、`export_bmp_assets`
- 可编辑图片与头像框：`read_editable_image_target`、`preview_editable_image_write`、`write_editable_image`、`list_portrait_frames`、`preview_apply_portrait_frame`、`apply_portrait_frame`
- AI 绘图素材：`list_ccz_image_asset_presets`、`build_ccz_image_prompt`、`prepare_ccz_generated_image`、`draw_ccz_image_asset`、`draw_and_replace_ccz_image_asset`
- E5 图片条目：`list_e5_image_entries`、`preview_e5_image_replace`、`replace_e5_image_entry`、`preview_e5_image_batch_replace`、`replace_e5_image_batch`
- R/S/头像/图标批量素材：`preview_r_image_raw_batch_replace`、`replace_r_image_raw_batch`、`preview_s_image_raw_batch_replace`、`replace_s_image_raw_batch`、`preview_job_s_image_raw_batch_replace`、`replace_job_s_image_raw_batch_replace`、`replace_job_s_image_raw_batch`、`preview_role_face_batch_import`、`replace_role_face_batch_import`、`preview_item_icon_batch_import`、`replace_item_icon_batch_import`、`preview_strategy_icon_batch_import`、`replace_strategy_icon_batch_import`
- DLL 图标：`preview_dll_icon_replace`、`replace_dll_icon`、`preview_clear_dll_icon`、`clear_dll_icon`
- 青儿/旧工具只读诊断：`diagnose_qinger66_project`、`audit_qinger66_items`、`list_legacy_mfc_dialogs`、`read_legacy_mfc_dialog`、`read_scenario_reference_checklist`
- x32dbg/x64dbg 动态调试（独立 MCP）：断点、寄存器、内存、反汇编、搜索、trace 和调试命令；不替代 CCZModStudio 的写入护栏。

兼容别名：

- `replace_job_s_image_raw_batch` -> `replace_job_s_image_raw_batch_replace`
- `write_item_effect_name66_slot` -> `write_item_effect_name_66_slot`

`promote_test_copy_mod`、`create_test_copy`、`diff_test_copy`、`create_release_copy` 仍属于已移除/禁用入口，验证脚本会拒绝它们重新暴露。

## 验证

先构建：

```powershell
dotnet build ".\工具整合包\CCZModStudio.McpServer\CCZModStudio.McpServer.csproj" -v:minimal
```

再运行本地协议验证：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\validate-mcp-config.ps1"
```

该脚本会验证 `tools/list`、`read_mcp_capability_manifest`、resources、resource templates 和 prompts。默认不再依赖固定工具数阈值，而是要求 manifest 数量与 `tools/list` 一致，并检查必备工具清单和 forbidden tools；如需临时保留下限保护，可传入 `-MinimumToolCount <n>`。

新增 authoring 工具的协议级只读/预览 smoke：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\smoke-authoring-mcp-tools.ps1"
```

该 smoke 会调用 manifest、table schema/CSV、角色组合读取、角色文本读取、形象分配预览、可编辑图片读取/预览、头像框目录、地图预览、青儿诊断和旧工具只读参考。默认不执行写回；可选素材缺失时只跳过对应预览段。

应输出：

```text
MCP_VALIDATE_OK server=CCZModStudio.McpServer protocol=2025-06-18 tools=<当前工具数>
```

客户端重启后可继续调用：

```text
detect_project
list_knowledge_entries
list_tables
```

`detect_project` 应返回工作区、游戏根目录、`HexTable.xml` 和核心文件状态。

## 安全边界

- MCP Server 只通过 stdout 输出 JSON-RPC，普通日志不得写 stdout。
- `replace_resource` 已开放核心文件整文件替换，仍会走备份、结构化报告和写后校验。
- E5 图片写入开放可读取 `110` 索引表的图片载荷资源；批量写入使用单次备份和逐条复读校验。
- DLL 图标写入开放 DLL RT_BITMAP 位图资源，不再限定在固定 DLL 文件名。
- AI 绘图素材默认先写 `CCZModStudio_Exports\AiImageAssets`；`draw_and_replace_ccz_image_asset` 可把生成、预处理和 E5/DLL 替换串成一次直接写回，仍生成备份、报告和复读校验。上游 API Key 只能通过本机环境变量或非提交配置提供。
- `export_image_resource_preview` 只写 `CCZModStudio_Exports\ImagePreviews`，不修改游戏文件。
- 写入工具继续走备份、结构化报告、写后复读/哈希校验和版本护栏。
- `write_mode` 默认 `direct`；参数仅为旧客户端兼容，测试副本语义会被忽略并按 direct 写入当前检测项目。
- `CCZMODSTUDIO_GAME_ROOT` 用于固定游戏目录；未设置时按工作区自动检测。
- x64dbg MCP 插件应只监听 `127.0.0.1`；不要开放到局域网或公网。动态调试允许启动游戏、设置断点和执行已启用的运行时调用；持久 EXE 字节写入走对应写入工具的备份、预览和复读流程。

## Game Debug MCP add-on

`CCZModStudio.GameDebugMcpServer` is a separate stdio MCP server for audited runtime control of `Ekd5.exe`.
It is intentionally not part of the normal production-write MCP server.

Generate a three-server config (`cczmodstudio`, `cczgame_debug`, and `x64dbg`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\generate-mcp-config.ps1" -GameRoot ".\基底\加强版6.5未加密版" -Build -IncludeGameDebug -IncludeX64dbg
```

Validate the game-debug MCP protocol surface:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\validate-ccz-game-debug-mcp.ps1" -GameRoot ".\基底\加强版6.5未加密版"
```

Expected output:

```text
GAME_DEBUG_MCP_VALIDATE_OK server=CCZModStudio.GameDebugMcpServer protocol=2025-06-18 tools=65
GAME_DEBUG_MCP_STATE_OK contentItems=1
GAME_DEBUG_MCP_PROCESS_START_OK dryRun=True started=False
GAME_DEBUG_MCP_XREF_OK targets=5 candidates=22
GAME_DEBUG_MCP_ADDRESS_REPORT_OK targets=13 candidates=25
GAME_DEBUG_MCP_ADDRESS_PROBE_PLAN_OK targets=37
GAME_DEBUG_MCP_ADDRESS_PROBE_RUN_OK status=plan-ready targets=49
GAME_DEBUG_MCP_RUNTIME_WAIT_OK status=matched final=not_running samples=1
GAME_DEBUG_MCP_RSCENE_ANCHOR_SCAN_OK status=not-running anchors=5 hits=0
GAME_DEBUG_MCP_RSCENE_SCRIPT_WINDOW_OK status=not-running windows=3 hits=0 refs=0
GAME_DEBUG_MCP_RSCENE_HANDLER_SCAN_OK status=handler-candidates-found commands=4 candidates=32 targets=32
GAME_DEBUG_MCP_RSCENE_LOAD_TRANSITION_OK status=transition-candidates-found anchors=4 candidates=2 targets=20
GAME_DEBUG_MCP_R00_ACTOR_ROUTE_OK status=actor-route-ready person=157 commands=6 click=002DF5 latest=plan-ready
GAME_DEBUG_MCP_INTERNAL_EVIDENCE_OK includeScreenshot=False
GAME_DEBUG_MCP_SCRIPT_DRY_RUN_OK status=dry-run
GAME_DEBUG_MCP_BATTLE_MATCH_OK status=not_running profile=yingchuan_cao_zhangliang
GAME_DEBUG_MCP_BATTLE_PROFILE_PROBE_OK status=profile-not-ready-plan-ready profileStatus=not_running targets=49
GAME_DEBUG_MCP_LIVE_READINESS_OK status=not-running ready=False targets=49
GAME_DEBUG_MCP_LIVE_AUTO_RUN_OK status=readiness-recorded ready=False targets=49
GAME_DEBUG_MCP_KEY_SEQUENCE_OK status=dry-run-no-process keys=1
GAME_DEBUG_MCP_TRANSITION_PROBE_OK status=dry-run targets=4
GAME_DEBUG_MCP_R00_ROUTE_OK status=route-plan-ready sequence=enter,down,down,down,down,down,enter prerequisitePerson=157 options=3/6
GAME_DEBUG_MCP_R00_STARTUP_PROBE_OK status=plan-ready targets=16 allowInput=False runProbes=False
GAME_DEBUG_MCP_KEYBOARD_EXPLORATION_OK status=plan-ready stop=dry-run-input-disabled sequences=2 final=not_running text=not-running script=not-running
```

Tools exposed by `cczgame_debug`:

- `game_process_start`, `debug_session_start`, `debug_session_state`
- `game_window_prepare`, `game_capture_frame`, `game_read_battle_state`, `game_runtime_state_classify`, `game_runtime_wait_for_state`, `game_rscene_text_anchor_scan`, `game_rscene_script_window_scan`, `game_battle_state_match`
- `game_click_grid`, `game_click_ui`, `game_key_sequence`
- `debug_r00_mode_route_analyze`, `debug_r00_actor_route_analyze`, `debug_rscene_command_handler_scan`, `debug_r00_startup_route_probe_run`, `debug_keyboard_exploration_run`
- `debug_breakpoint_plan_apply`, `debug_function_catalog`, `debug_static_xref_scan`, `debug_address_report`, `debug_address_report_probe_plan`, `debug_address_probe_run`, `debug_battle_profile_probe_run`, `debug_live_probe_readiness`, `debug_live_probe_auto_run`, `debug_phase_probe_plan`
- `debug_autonomy_plan`, `debug_autonomy_run` with optional `start_game` and `wait_for_state`
- `debug_internal_probe_plan`, `debug_internal_probe_run`, `debug_transition_probe_run`
- `debug_run_until_event`, `debug_capture_evidence`, `debug_write_knowledge_draft`
- `debug_script_run`

Safety boundary:

- `game_process_start` and `debug_session_start` verify the current 6.5 unencrypted `Ekd5.exe` SHA256.
- x64dbg bridge hosts are restricted to local loopback.
- Real mouse input requires `allow_input=true`.
- The server writes evidence under `CCZModStudio_Reports\DebugEvidence\game-auto-*`; it does not patch `Ekd5.exe`, write game files, or write target process memory.

`debug_address_probe_run` is the recommended one-shot address workflow entry. In its default safe mode it does not launch the game, does not start x32dbg, and does not set breakpoints; it writes `address-probe-run-summary.json/md` plus the generated address report and probe plan. Enable `start_game`, `start_debugger`, and `run_probes` only when the local bridge is ready and the game is positioned at the intended action.

`debug_capture_evidence` defaults to internal evidence only and writes no screenshot unless `include_screenshot=true` is explicitly passed. `debug_script_run(..., allow_input=false)` is validated as a no-input/no-screenshot dry-run path.

`game_click_ui` title-screen aliases are calibrated for the live 640x440 game client: `title_start_game`, `title_load_game`, `title_settings`, and `title_exit`. The visible right-side stylized labels map functionally from top to bottom to start game, load save, environment settings, and exit game; `menu_top_1..menu_top_4` remain compatibility aliases for those four slots. The tool reports the scaled client point and blocks real input if that point is outside the current client. Real clicking still requires `allow_input=true`.

`game_battle_state_match(profile="yingchuan_cao_zhangliang")` is validated as a read-only internal battle-state gate. It writes `battle-state-match.json/md`, matches the known Cao Cao/Zhang Liang positions from the 2026-06-09 Yingchuan sample, and uses Zhang Liang HP `< 168` only as attack-after state evidence, not as proof of function semantics.

`debug_battle_profile_probe_run` is validated as the profile-gated probe orchestration entry. In safe mode it writes `battle-profile-probe-run-summary.json/md` and an address probe plan without launching the game, starting x32dbg, or setting breakpoints. Dynamic probe running remains gated by `run_probes=true`, local x32dbg bridge availability, and the battle profile unless `require_profile_match=false` is explicitly passed.

`debug_live_probe_readiness` is validated as the live dynamic-probe preflight. It writes `live-probe-readiness.json/md`, reports missing gates such as process, window, bridge, battle phase, or profile, and does not launch the game or change debugger state.

`debug_live_probe_auto_run` is validated as the one-call live orchestration path. Safe mode records readiness only; with explicit `start_game=true, allow_launch=true`, `start_debugger=true`, and `run_probes=true`, it can launch/attach, re-check readiness, and then run the profile-gated probe path.

`game_rscene_script_window_scan` and `debug_rscene_command_handler_scan` are the current read-only R_00 startup-route probes. The first scans runtime memory for exact byte windows around the Xu Zijiang `2D` actor-click and two `12` choice commands; the second scans `Ekd5.exe` `.text` for command-id comparison candidates and writes a probe plan. They do not prove the actor-click event has been triggered; dynamic x32dbg hits are still required.

`debug_r00_startup_route_probe_run` is validated as the safe wrapper for that route. With `run_probes=false` and `allow_input=false`, it only builds evidence and plans. For live no-input experiments, set `probe_before_startup_continue=true` so the generated R-scene command-handler breakpoints are installed before x32dbg continues past the entry-point pause. Keyboard-only input remains a separate explicit opt-in experiment, and attack/turn semantics still require `battle_loaded` plus `game_battle_state_match(profile="yingchuan_cao_zhangliang")`.

`debug_keyboard_exploration_run` is validated as the bounded keyboard-only startup exploration wrapper. In safe mode (`allow_launch=false`, `allow_input=false`) it records candidate sequences and internal probes without launching or sending input. In live mode it can try semicolon-separated sequences, then after each sequence run runtime classification, R-scene text-anchor scan, R_00 script-window scan, and the Yingchuan battle profile gate; it stops as soon as R_00 residency or `battle_loaded` is observed.

`debug_r00_actor_route_analyze` is validated as the static ledger for the Xu Zijiang prerequisite. It confirms person `157`, the `2D` actor-click test at `002DF5`, six decoded actor commands, and attaches the latest no-input startup probe status when present. This narrows the next live target to the title-entry/R-scene-load transition and live interpreter script pointer; it is not battle-entry evidence.

`game_runtime_anchor_sweep` now records `region_kind` for every anchor hit and keeps its phase inference conservative. A live launch on 2026-06-10 found `许子将` and `张梁` in `heap_candidate` memory, but `game_rscene_script_window_scan` still reported `script-windows-not-found` and runtime remained `process_no_battle_signal`. Treat such character-name heap hits as resource residency only; strong R_00 progress still requires mode/start-game anchors, exact R_00 script windows, command-handler hits, or battle/profile gates.
