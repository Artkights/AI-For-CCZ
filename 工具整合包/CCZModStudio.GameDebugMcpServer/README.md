# CCZModStudio.GameDebugMcpServer

Local stdio MCP server for audited Ekd5.exe runtime/debug automation.

This server is intentionally separate from `CCZModStudio.McpServer`. It can start or inspect the game/debugger, read battle memory with `ReadProcessMemory`, collect x32dbg evidence through the local x64dbg MCP bridge, and optionally capture screenshots only when explicitly requested. It does not write game files, patch `Ekd5.exe`, or write target process memory.

## Build

```powershell
dotnet build .\CCZModStudio.GameDebugMcpServer\CCZModStudio.GameDebugMcpServer.csproj
```

## Run

Use `宸ュ叿鏁村悎鍖卄 as the working directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\MCP閰嶇疆\start-ccz-game-debug-mcp.ps1" -GameRoot "..\鍩哄簳\鍔犲己鐗?.5鏈姞瀵嗙増"
```

The server writes MCP JSON-RPC messages on stdout. Diagnostics and build output must go to stderr.

## Tool Groups

- Session/process/debugger: `game_process_start`, `debug_session_start`, `debug_session_state`.
- Window and capture: `game_window_prepare`, `game_capture_frame`.
- Read-only state: `game_read_battle_state`, `game_read_battlefield_runtime_snapshot`, `game_runtime_state_classify`, `game_runtime_wait_for_state`, `game_rscene_text_anchor_scan`, `game_rscene_script_window_scan`, `game_battle_state_match`.
- Battlefield automation v1: `game_battle_calibrate_grid`, `game_battle_grid_to_client`, `game_battle_verify_click_target`, `game_battle_select_unit`, `game_battle_move_unit`, `game_battle_attack`, `game_battle_wait`, `game_battle_end_turn`, `game_battle_auto_step`, `game_battle_auto_run`, `debug_battle_auto_probe_run`.
- Guarded input: `game_click_grid`, `game_click_ui`, `game_key_sequence`; real input requires `allow_input=true`.
- R_00 route analysis: `debug_r00_mode_route_analyze` reads Xu Zijiang mode-selection choice/case evidence and emits a keyboard-only route candidate; `debug_r00_actor_route_analyze` records the Xu Zijiang actor placement/click prerequisite and latest no-input probe result; `debug_rscene_command_handler_scan` builds offline command-handler breakpoint candidates for `2D`, `12`, `07`, and `13`; `debug_r00_runtime_invoke_candidate_plan` links R_00 script offsets to `.text` handler breakpoint candidates and latest live evidence without treating script offsets as callable code; `debug_r00_runtime_handler_probe_run` wraps that generated plan into a live-ready x32dbg probe orchestration without mouse, screenshots, injection, or process-memory writes; `debug_rscene_load_transition_scan` builds title-to-R-scene load transition candidates from R/S EEX path references plus optional runtime script-residency scans; `debug_rscene_load_transition_probe_run` wraps those transition candidates into a focused live-ready probe plan; `debug_title_menu_dispatch_scan` scans title/menu Win32 dispatch call sites and writes a breakpoint-only probe plan; `debug_title_wndproc_dispatch_scan` scans WndProc-style `WM_*` and `VK_*` compare sites and function entries as higher-priority breakpoint-only title dispatcher candidates; `debug_title_menu_dispatch_probe_run` wraps that plan into a live-ready route probe with optional x32dbg start/continue and optional keyboard-only trigger; `debug_title_menu_dispatch_matrix_run` builds the Start/Load/Settings/Exit route comparison matrix; `debug_r00_startup_route_probe_run` ties the route analysis, script-window scan, handler plan, optional x32dbg probing, and optional keyboard-only trigger into one audited startup-route probe; `debug_keyboard_exploration_run` tries bounded keyboard-only startup sequences and scans internal R_00/battle state after each one.
- x32dbg bridge orchestration: `debug_breakpoint_plan_apply`, `debug_run_until_event`, `debug_capture_evidence`; screenshot capture is opt-in with `include_screenshot=true`.
- Offline/staged address catalog: `debug_function_catalog`, `debug_static_xref_scan`, `debug_address_report`, `debug_address_report_probe_plan`, `debug_address_probe_run`, `debug_battle_profile_probe_run`, `debug_live_probe_readiness`, `debug_live_probe_auto_run`, `debug_phase_probe_plan`.
- Safe no-input automation orchestration: `debug_autonomy_plan`, `debug_autonomy_run` with optional `start_game` and `wait_for_state`.
- Internal probes without mouse/screenshot dependency: `debug_internal_probe_plan`, `debug_internal_probe_run`, `debug_transition_probe_run`.
- Full internal automation and promotion gates: `debug_full_auto_run`, `debug_runtime_invoke_plan`, `debug_runtime_invoke_run`, `debug_menu_route_run`, `debug_address_verify_run`, `debug_knowledge_promote`.
- Knowledge review drafts from evidence: `debug_write_knowledge_draft`.
- High-level audited script entry: `debug_script_run`.

## Safety Boundary

- Default target is the current 6.5 unencrypted `Ekd5.exe`.
- `game_process_start` and `debug_session_start` refuse unexpected target SHA256.
- x64dbg bridge hosts are restricted to `127.0.0.1`, `localhost`, or `::1`.
- Game file writes and EXE patching remain in the existing CCZModStudio patch/backup/reread workflow.
- Evidence is written under `CCZModStudio_Reports\DebugEvidence\game-auto-*`.
- Top-level orchestration tools also write `action-chain.jsonl` for audited high-level actions. Address verification writes `probe-hits.jsonl`; plan-only runs create planning/summary rows, while live breakpoint hits add `breakpoint-hit` rows that point to the per-hit x32dbg JSON evidence.

## Validation

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\MCP閰嶇疆\validate-ccz-game-debug-mcp.ps1" -GameRoot "..\鍩哄簳\鍔犲己鐗?.5鏈姞瀵嗙増"
```

Expected output:

```text
GAME_DEBUG_MCP_VALIDATE_OK server=CCZModStudio.GameDebugMcpServer protocol=2025-06-18 tools=65
GAME_DEBUG_MCP_STATE_OK contentItems=1
GAME_DEBUG_MCP_PROCESS_START_OK dryRun=True started=False
GAME_DEBUG_MCP_CATALOG_OK entries=...
GAME_DEBUG_MCP_XREF_OK targets=5 candidates=22
GAME_DEBUG_MCP_ADDRESS_REPORT_OK targets=13 candidates=25
GAME_DEBUG_MCP_ADDRESS_PROBE_PLAN_OK targets=37
GAME_DEBUG_MCP_ADDRESS_PROBE_RUN_OK status=plan-ready targets=49
GAME_DEBUG_MCP_RUNTIME_WAIT_OK status=matched final=not_running samples=1
GAME_DEBUG_MCP_RSCENE_ANCHOR_SCAN_OK status=not-running anchors=5 hits=0
GAME_DEBUG_MCP_RSCENE_SCRIPT_WINDOW_OK status=not-running windows=3 hits=0 refs=0
GAME_DEBUG_MCP_RSCENE_HANDLER_SCAN_OK status=handler-candidates-found commands=4 candidates=...
GAME_DEBUG_MCP_RSCENE_LOAD_TRANSITION_OK status=transition-candidates-found anchors=4 candidates=2 targets=20
GAME_DEBUG_MCP_RSCENE_LOAD_TRANSITION_PROBE_OK status=plan-ready targets=4 filter=anchor_functions,direct_refs,no_imports runProbes=False hits=0
GAME_DEBUG_MCP_TITLE_MENU_DISPATCH_SCAN_OK status=title-menu-dispatch-candidates-found calls=... functions=... targets=...
GAME_DEBUG_MCP_TITLE_WNDPROC_DISPATCH_SCAN_OK status=title-wndproc-dispatch-candidates-found compares=... functions=... targets=...
GAME_DEBUG_MCP_TITLE_MENU_DISPATCH_PROBE_OK status=plan-ready route=title_menu targets=... runProbes=False hits=0
GAME_DEBUG_MCP_TITLE_MENU_DISPATCH_MATRIX_OK status=title-menu-dispatch-matrix-plan-ready routes=4 targets=... runProbes=False hits=0
GAME_DEBUG_MCP_R00_ACTOR_ROUTE_OK status=actor-route-ready person=157 commands=6 click=002DF5 latest=plan-ready
GAME_DEBUG_MCP_R00_RUNTIME_INVOKE_CANDIDATE_OK status=r00-runtime-invoke-candidate-plan-ready actions=3 candidates=16 targets=16 verifiedHits=0
GAME_DEBUG_MCP_R00_RUNTIME_HANDLER_PROBE_OK status=plan-ready candidates=16 targets=16 runProbes=False hits=0
GAME_DEBUG_MCP_INTERNAL_EVIDENCE_OK includeScreenshot=False
GAME_DEBUG_MCP_SCRIPT_DRY_RUN_OK status=dry-run
GAME_DEBUG_MCP_BATTLE_MATCH_OK status=not_running profile=yingchuan_cao_zhangliang
GAME_DEBUG_MCP_BATTLE_PROFILE_PROBE_OK status=profile-not-ready-plan-ready profileStatus=not_running targets=49
GAME_DEBUG_MCP_LIVE_READINESS_OK status=not-running ready=False targets=49
GAME_DEBUG_MCP_LIVE_AUTO_RUN_OK status=readiness-recorded ready=False targets=49
GAME_DEBUG_MCP_BATTLE_AUTO_STEP_OK status=dry-run-no-process
GAME_DEBUG_MCP_BATTLE_AUTO_RUN_OK status=dry-run-no-process
GAME_DEBUG_MCP_BATTLE_AUTO_PROBE_OK status=plan-only targets=13
GAME_DEBUG_MCP_KEY_SEQUENCE_OK status=dry-run-no-process keys=1
GAME_DEBUG_MCP_TRANSITION_PROBE_OK status=dry-run targets=4
GAME_DEBUG_MCP_R00_ROUTE_OK status=route-plan-ready sequence=enter,down,down,down,down,down,enter prerequisitePerson=157 options=3/6
GAME_DEBUG_MCP_R00_STARTUP_PROBE_OK status=plan-ready targets=16 allowInput=False runProbes=False
GAME_DEBUG_MCP_KEYBOARD_EXPLORATION_OK status=plan-ready stop=dry-run-input-disabled sequences=2 final=not_running text=not-running script=not-running
GAME_DEBUG_MCP_RUNTIME_INVOKE_PLAN_OK actions=15
GAME_DEBUG_MCP_RUNTIME_INVOKE_RUN_OK status=runtime-invoke-plan-only actions=15
GAME_DEBUG_MCP_MENU_ROUTE_OK status=menu-route-recorded routes=...
GAME_DEBUG_MCP_ADDRESS_VERIFY_OK status=address-verification-plan-ready
GAME_DEBUG_MCP_KNOWLEDGE_PROMOTE_OK status=promotion-preview canPromote=False
GAME_DEBUG_MCP_FULL_AUTO_OK status=full-auto-plan-ready steps=6
```

`debug_address_probe_run` is the one-shot safe entry for the address workflow. With `run_probes=false`, `start_game=false`, and `start_debugger=false`, it only builds the report/plan and writes `address-probe-run-summary.json/md`. With `run_probes=true`, it consumes the generated plan through `debug_internal_probe_run` and requires the x32dbg bridge to be online at the intended gameplay state.

`debug_capture_evidence` now defaults to internal evidence only: x32dbg state, registers, breakpoints, disassembly, stack, target memory, and read-only battle state. It will not capture a screenshot unless `include_screenshot=true` is explicitly passed.

`game_battle_state_match(profile="yingchuan_cao_zhangliang")` is the read-only internal gate for the 2026-06-09 Yingchuan sample. It matches Cao Cao at `unit[0] (10,6)` and Zhang Liang at `unit[61] (10,5)`, and reports `attack_after_observed` when Zhang Liang HP is below the recorded initial value `168`. This is scenario-state evidence only; function semantics still require x32dbg dynamic hits tied to the same action.

`game_read_battlefield_runtime_snapshot` is the generic battle-state surface for automation. It keeps the decoded unit array at `004A7B20` / stride `30`, derives `controllable_units` from side `0`, alive HP, and the current action-bit heuristic, builds `occupied_cells`, and reads the combat context head at `004927F0` plus offsets `+84`, `+428`, `+42C`, `+430`, `+604`, `+608`, and `+614`. Phase classification is intentionally conservative: side-0 controllable units imply `player_control`; no controllable units plus latest attacker side can imply `ally_auto` or `enemy_auto`; otherwise it reports `turn_end_prompt`, `not_battle`, or `unknown` with reasons.

Battlefield automation v1 uses guarded external input first, then continues internal ABI discovery separately. `game_battle_auto_step(policy="safe_attack")` controls only side `0` units, attacks the adjacent lowest-HP side `2` enemy when Manhattan distance is `1`, and waits when range or movement cannot be confirmed. `game_battle_attack` refuses non-adjacent move-then-attack input as `range_unknown` until movement range, weapon range, and route ABI are verified. `game_battle_auto_run` repeats this step and stops on unknown/automatic phases. Action tools now default to `allow_input=true`; callers can still pass `allow_input=false` for a dry-run that writes `action-chain.jsonl` and evidence summaries.

Grid automation is explicit. `game_battle_calibrate_grid` records the current window/client dimensions and can infer `origin_x`, `origin_y`, `cell_width`, and `cell_height` from known `gridX,gridY,clientX,clientY` points. `game_battle_grid_to_client` and `game_battle_verify_click_target` are read-only preflight helpers. High-level actions resolve planned grid/UI points and block real input if the phase, window bounds, or occupancy checks fail.

`debug_battle_auto_probe_run` writes a fixed battle-auto probe plan and then optionally runs the safe external auto loop. With `run_probes=false` it does not mutate x32dbg. With `run_probes=true`, it uses the existing `debug_internal_probe_run` path for log/evidence breakpoints at the current hook candidates: physical post-damage `00405AD5`, HP setter `0043F70C`, EXP cache writes `00406043/00406054/00406074`, double-attack candidates `00406555/0040655C/00406520`, counter chain `0041797E/004064DA/00406581`, and turn/action restore `0044AF8F/00406690`.

`debug_battle_profile_probe_run` ties that profile gate to the address-probe workflow. Safe validation mode does not launch the game, start x32dbg, or set breakpoints; it writes `battle-profile-probe-run-summary.json/md` plus the generated address report and probe plan. With `run_probes=true`, dynamic probes are skipped unless the profile reports `profile-matched` or `attack_after_observed`, unless `require_profile_match=false` is explicitly passed.

`debug_live_probe_readiness` is the standard preflight before live dynamic probing. It checks the verified target hash, process/window presence, local x32dbg bridge health, runtime phase, battle profile, and generated probe plan, then writes `live-probe-readiness.json/md`. It does not launch the game, send input, capture screenshots, write memory, patch files, or change x32dbg breakpoints.

`debug_live_probe_auto_run` is the one-call live path: optional direct game launch, optional x32dbg attach/start, optional controlled startup continuation, readiness check, then optional profile-gated dynamic probe. Safe validation mode uses `start_game=false`, `start_debugger=false`, `continue_startup=false`, and `run_probes=false`; real launch still requires `allow_launch=true`, startup continuation only sends limited local x32dbg `run` commands, and breakpoint collection still requires `run_probes=true` plus readiness.

`game_key_sequence` is the no-mouse/no-screenshot trigger primitive for title, mode-selection, and battle UI exploration. It supports `delivery=post_message` (`WM_KEYDOWN/WM_KEYUP`) and `delivery=send_input` (keyboard-only `SendInput`), then records before/after runtime classification plus read-only battle memory. Pass `allow_input=false` to write only a dry-run evidence file.

`game_click_ui` includes title-screen semantic regions: `title_start_game`, `title_load_game`, `title_settings`, and `title_exit`. The title slots are calibrated against the live 640x440 game client, using the user-confirmed functional order from top to bottom: start game, load save, environment settings, and exit game. The legacy aliases `menu_top_1..menu_top_4` map to those same four title slots. The click report records the base region, scaled client point, current client size, and a `within_client` guard; real input is blocked if the resolved point is outside the current client.

`debug_transition_probe_run` combines a staged probe plan, optional x32dbg startup continuation, optional keyboard sequence trigger, and internal hit collection. Passing `allow_input=false` keeps this as a dry-run that only generates the probe plan and summary. Use `key_delivery=send_input` when the target requires foreground keyboard input instead of posted window messages. Use this tool to explore transitions such as title entry and Xu Zijiang/mode-selection without relying on mouse coordinates.

`debug_r00_mode_route_analyze(route="regular_start")` is the current no-screenshot/no-mouse bridge from R_00 script structure to startup automation. It statically reads `RS/R_00.eex`, records the prerequisite `2D` actor-click test for person `157` (Xu Zijiang), locates the first mode choice at `002E97`, the regular-mode configuration choice at `003273`, extracts `case` branches and regular-mode variable writes, then proposes `enter,down,down,down,down,down,enter` for selecting regular mode and `[C3A寮€濮嬫父鎴廬` after the actor interaction has been triggered. Treat that sequence as a route candidate until a live run reaches `battle_loaded` and passes `game_battle_state_match`; the actor-click prerequisite still needs an internal R-scene event trigger or state probe before this is a complete autonomous start path.

`debug_r00_actor_route_analyze(route="regular_start", person_id=157)` is the current static actor-route ledger for that prerequisite. It records Xu Zijiang's R_00 actor commands, including appearance/movement and the single `2D` click test at `002DF5`, then attaches the latest no-input startup probe summary when available. The latest live no-input probe `game-auto-20260610-212925-698` launched x32dbg/Ekd5, installed 16 generated handler breakpoints before startup continuation, and still ended as `probe-timeout-no-hit` with final runtime `process_no_battle_signal`; it did not prove R_00 script loading or actor-click execution.

`game_rscene_text_anchor_scan` is a live read-only progress probe for the no-screenshot path. It scans committed readable `Ekd5.exe` process memory for GBK anchors such as Xu Zijiang, mode-selection, regular-mode, and start-game text, then writes `rscene-text-anchor-scan.json/md`. Leave `anchors` empty to use the built-in Unicode defaults. Hits include `region_kind` (`image`, `private`, `heap_candidate`, or `mapped`) so static EXE strings can be separated from runtime resource data. Hits are only a coarse phase signal; character-name hits such as Xu Zijiang can come from loaded Data resources and do not prove the exact interpreter command pointer, selected choice index, or R_00 mode-selection state.

`game_runtime_anchor_sweep(profile="all")` performs the broader no-screenshot startup sweep. Its phase inference treats strong runtime anchors such as `妯″紡閫夋嫨`, `寮€濮嬫父鎴廯, `R_00`, `RS\R_`, `[C97]`, and `[C3A]` more strongly than generic character names. A live check on 2026-06-10 found `璁稿瓙灏哷 and `寮犳` in `heap_candidate` memory while `game_rscene_script_window_scan` still returned `script-windows-not-found`; that is resource-residency evidence only, not autonomous R_00 entry.

`game_rscene_script_window_scan(route="regular_start")` is the next read-only runtime probe for the same path. It extracts byte windows around the R_00 `2D` Xu Zijiang actor-click command, the first `12` mode-choice command, and the second `12` regular-config command, scans committed readable `Ekd5.exe` memory for those exact windows, and optionally scans for uint32 pointer references to matched script addresses. A hit proves script data is resident, not that the interpreter is executing that command.

`debug_rscene_command_handler_scan(command_ids="2D,12,07,13")` is an offline `.text` byte-pattern scan for x86 command-id comparisons. It writes `rscene-command-handler-scan.json/md` and, by default, `rscene-command-handler-probe-plan.json/md` for `debug_internal_probe_run`. Treat every row as a breakpoint candidate until a live R_00 run captures CIP, stack, disassembly, and route-state evidence.

`debug_r00_runtime_invoke_candidate_plan(route="regular_start")` is the bridge between R_00 script offsets and runtime invoke planning. It reads the static script actions (`2D` Xu Zijiang actor-click at `002DF5`, `12` mode choice at `002E97`, and `12` regular start-game choice at `003273`), scans `.text` command-id compare candidates, attaches the latest startup-route probe summary when present, and writes `r00-runtime-invoke-candidate-plan.json/md`. It also writes `r00-runtime-handler-probe-plan.json/md`, an `InternalProbePlan` consumable by `debug_internal_probe_run` for live handler breakpoint evidence. The generated runtime actions use `r_scene_handler_candidate_probe`, carry `candidate_source`, `candidate_confidence`, and `evidence_gate`, and keep `requires_runtime_injection=false` until a live handler hit provides enough ABI evidence. Validation on 2026-06-11 produced 16 handler candidates for the regular-start route and no live verified handler hits; current first static candidates include `00408AFF` for `2D` and `00414EB3` for `12`.

`debug_r00_runtime_handler_probe_run(route="regular_start")` is the live-ready wrapper for that R_00 handler plan. In safe validation mode (`start_debugger=false`, `continue_startup=false`, `run_probes=false`) it writes `r00-runtime-invoke-candidate-plan.json/md`, `r00-runtime-handler-probe-plan.json/md`, and `r00-runtime-handler-probe-summary.json/md` without starting x32dbg, setting breakpoints, sending input, scanning screenshots, injecting code, or writing target memory. In live mode, use `start_debugger=true`, `run_probes=true`, `probe_before_startup_continue=true`, and optionally `continue_startup=true` to install handler breakpoints before the startup continuation can pass R-scene interpreter execution. A hit from this tool is still only handler-candidate evidence until script-window residency, registers, stack, disassembly, and route-state transition are reviewed together.

`debug_rscene_load_transition_scan(route="regular_start")` is the current bridge for the gap before command-handler probes. It scans `Ekd5.exe` sections for `RS\R_%s.EEX` / `RS\S_%s.EEX` path anchors, finds `.code` references and containing function-entry candidates (`0041648E`, `00417EEA`), scans selected import API call sites (`GetFileSize`, `CloseHandle`, `wsprintfA`, etc.), writes `rscene-load-transition-scan.json/md`, and emits `rscene-load-transition-probe-plan.json/md`. Treat all rows as title-to-R-scene load transition candidates only; promote none as a loader or interpreter function until a live x32dbg hit is tied to R_00 script residency or script-pointer/path-buffer evidence.

`debug_rscene_load_transition_probe_run(route="regular_start")` is the live-ready wrapper for that transition scan. It accepts `candidate_filter`; validation uses `anchor_functions,direct_refs,no_imports`, which keeps the direct R/S path refs and containing function entries while excluding generic import/API call sites. A no-input live run on 2026-06-11 at `CCZModStudio_Reports\DebugEvidence\game-auto-20260611-064827-570` hit only `00406A31 CloseHandle`, ended with `script-windows-not-found`, and must remain weak negative evidence rather than loader/interpreter proof. The later focused no-input run `CCZModStudio_Reports\DebugEvidence\game-auto-20260611-070334-771` used `anchor_functions,direct_refs,no_imports`, targeted only `0041648E`, `004164C7`, `00417EEA`, and `00417F16`, and timed out with no hits; this narrows the next gap to title menu dispatch/internal selection.

`debug_title_menu_dispatch_scan` is the current static bridge for the title menu dispatch gap. It scans offline `Ekd5.exe` Win32 message/menu API call sites and writes `title-menu-dispatch-probe-plan.json`; evidence `CCZModStudio_Reports\DebugEvidence\game-auto-20260611-072611-778` produced 11 breakpoint-only targets including `00401491`, `004014BE`, `00401126`, `004011D1`, `004011F6`, `00401854`, `00401883`, `00401E5D`, `00401EBE`, `0040582F`, and `004058BA`. These are `pending-breakpoint` / `needs-live-run` candidates only. `debug_runtime_invoke_plan(stage="menu", route="full_menu")` now consumes this scan and emits `title_menu_dispatch_breakpoint_probe` actions for Start/Load/Settings/Exit without runtime injection or process-memory writes.

`debug_title_wndproc_dispatch_scan` is the stronger static scan for the same title-menu gap. It scans offline `.text` for WndProc-style compares against `WM_COMMAND`, keyboard/mouse `WM_*` messages, and `VK_RETURN`/arrow/escape/space `wParam` values, then writes `title-wndproc-dispatch-probe-plan.json`. Validation on 2026-06-11 found 78 compare candidates, 73 containing-function candidates, and 30 probe targets. These are still `pending-breakpoint` / `needs-live-run` candidates only; they are useful as higher-priority breakpoints for route comparison, not as verified menu dispatcher ABI. `debug_runtime_invoke_plan(stage="menu", route="full_menu")` now prefers these WndProc/message candidates and supplements them with the Win32 API call-site scan.

`debug_title_menu_dispatch_probe_run(route="title_menu")` is the live-ready wrapper for the title dispatch plan. Safe validation mode writes `title-menu-dispatch-scan.json/md`, `title-menu-dispatch-probe-plan.json/md`, `title-menu-dispatch-probe-summary.json/md`, and `action-chain.jsonl` without launching x32dbg, setting breakpoints, sending input, scanning screenshots, injecting code, or writing process memory. Live experiments can set `start_debugger=true`, `continue_startup=true`, `run_probes=true`, and, only when explicitly needed for route comparison, `allow_input=true` with a keyboard-only `trigger_sequence`. Hits remain pending until route-specific Start/Load/Settings/Exit state evidence is reviewed.

`debug_title_menu_dispatch_matrix_run(routes="title_menu")` creates a shared title dispatch scan plus per-route probe summaries for Start, Load, Settings, and Exit. Safe validation mode only creates the matrix evidence and per-route plans; live mode can compare route-specific dispatch hits. The exit route defaults to enabled through `allow_exit_route=true`; pass false only when intentionally suppressing exit-route input.

`debug_r00_startup_route_probe_run(route="regular_start")` is the safe orchestration wrapper for the same startup path. With validation toggles disabled (`start_debugger=false`, `run_probes=false`, `allow_input=false`) it only emits route evidence, script-window scan results, a handler probe plan, and `r00-startup-route-probe-summary.json/md`. For live no-input experiments, use `start_debugger=true`, `run_probes=true`, `continue_startup=true`, `probe_before_startup_continue=true`, and `allow_input=false` so handler breakpoints are installed before the debugger is continued to the R scene. Enabling `allow_input=true` must be treated as a separate live input experiment; it still does not write process memory or patch files, and it does not prove autonomous battle entry unless the final runtime reaches `battle_loaded` and `game_battle_state_match(profile="yingchuan_cao_zhangliang")` passes.

`debug_keyboard_exploration_run(route="regular_start")` is the bounded keyboard-only exploration wrapper for the title/R_00 gap. It can optionally launch the verified `Ekd5.exe`, optionally start/continue x32dbg, then tries semicolon-separated key sequences such as `enter;space;enter,down,enter`; after each sequence it runs `game_runtime_state_classify`, `game_rscene_text_anchor_scan`, `game_rscene_script_window_scan`, and `game_battle_state_match`. It stops on `battle_loaded`, a matched battle profile, R_00 script-window residency, or R-scene text-anchor residency. Pass `allow_launch=false` and `allow_input=false` for a validation-only run that writes `keyboard-exploration-summary.json/md` without launching or sending input.

`debug_full_auto_run(profile="full_menu_yingchuan")` is the top-level automation surface for startup, menu coverage, R_00 runtime handler probe planning, runtime invoke planning, address verification planning, and knowledge promotion preview. Plan-only validation keeps `start_game=false`, `start_debugger=false`, and `run_probes=false`; `allow_debug_invoke` and `allow_runtime_injection` now default to enabled for live runs. In safe mode it uses the lightweight R_00 candidate-plan path; in live mode it calls `debug_r00_runtime_handler_probe_run`.

`debug_full_auto_run`, `debug_runtime_invoke_run`, `debug_menu_route_run`, and `debug_address_verify_run` write an `action-chain.jsonl` ledger in their session directory. Each row records the tool, action, status, and optional before/after internal state. This is the stable review surface for no-mouse/no-screenshot automation chains; `events.jsonl` remains the lower-level diagnostic stream.

`debug_runtime_invoke_plan` creates `runtime-invoke-plan.json/md` for debugger-mediated internal calls or temporary runtime injection candidates. v1 covers title menu routes, settings/load/exit entries, R_00 Xu Zijiang `person=157` actor-click/mode choices, Yingchuan attack verification, and turn-end verification. `debug_runtime_invoke_run` executes only when the local bridge is ready and the relevant gates are explicitly enabled; otherwise it records plan-only skip reasons.

Runtime invoke plans now carry `invoke_strategy`, `calling_convention`, `register_setup`, `stack_arguments`, `requires_paused_debuggee`, `candidate_source`, `candidate_confidence`, and `evidence_gate`. Temporary runtime invoke execution is enabled by `allow_runtime_injection=true` and a local online x32dbg bridge; plan metadata still records candidate confidence and evidence gates. Menu actions are now `title_menu_dispatch_breakpoint_probe` candidates sourced from static WndProc/message compare candidates first and Win32 dispatch API call sites second; they do not require runtime injection and must not be promoted until route-specific live hits prove Start/Load/Settings/Exit semantics. R_00 actions are handler breakpoint candidates (`rscene_handler_breakpoint_probe`) until a live hit plus ABI review upgrades them; script offsets such as `002DF5` are never treated as callable code addresses.

When a stub call is allowed, `debug_runtime_invoke_run` writes x32dbg evidence under `x32dbg/runtime-invoke/`: pre-state, registers, stack, the generated `alloc`/`asm`/`eip`/`run` command sequence, post-state, disassembly, battle state, and restore/free attempts. This is still a debugger-temporary call path only; it does not persistently patch `Ekd5.exe`.

`debug_menu_route_run` records no-mouse/no-screenshot menu route coverage. If no fixed save file is available, load-save coverage is reported as `blocked: missing-save-fixture` while the rest of the route can still be planned and verified.

`debug_address_verify_run` ties staged address plans to trigger scripts such as `yingchuan_cao_attack_zhangliang`. It does not promote function semantics unless probe hits, trigger/battle diff, registers, stack, and disassembly evidence are all present.

`debug_address_verify_run` also writes `probe-hits.jsonl`. In plan-only validation the file contains planning and summary rows so downstream validators can depend on a fixed evidence path. Live breakpoint hits are appended by `debug_internal_probe_run` through `CaptureInternalProbeHit` as `breakpoint-hit` rows, including address, phase, evidence level, per-hit evidence path, and battle-state summary.

`debug_knowledge_promote` is the formal knowledge promotion gate. With `allow_write=false`, it writes `knowledge-promotions.json` only. With `allow_write=true`, it appends to the selected local knowledge-base page; incomplete evidence is recorded as pending. Persistent EXE changes remain outside GameDebug and must use EffectPackage preview/apply workflows.

