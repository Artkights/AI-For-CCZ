using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CCZModStudio.GameDebugMcpServer;

[McpServerToolType]
public sealed class GameDebugTools(GameDebugRuntime runtime)
{
    [McpServerTool]
    [Description("Start or attach the 6.5 Ekd5.exe debug session through x32dbg, verify the target hash, and wait for the x64dbg MCP bridge.")]
    public object debug_session_start(
        [Description("Optional game root. Defaults to CCZGAME_DEBUG_GAME_ROOT, CCZMODSTUDIO_GAME_ROOT, or auto-detection.")]
        string? game_root = null,
        [Description("Optional x32dbg.exe path. Defaults to CCZGAME_DEBUG_X32DBG_PATH or CCZModStudio_Exports/DebugTools/x64dbg/release/x32/x32dbg.exe.")]
        string? x32dbg_path = null,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Maximum milliseconds to wait for the bridge to report healthy.")]
        int wait_ms = 10000,
        [Description("Start x32dbg hidden. Default false keeps the debug session visible.")]
        bool hidden = false)
        => runtime.DebugSessionStart(game_root, x32dbg_path, host_name, port, wait_ms, hidden);

    [McpServerTool]
    [Description("Start or inspect Ekd5.exe directly without x32dbg, mouse input, screenshots, or process-memory writes. Real launch requires allow_launch=true.")]
    public object game_process_start(
        [Description("Optional game root. Defaults to CCZGAME_DEBUG_GAME_ROOT, CCZMODSTUDIO_GAME_ROOT, or auto-detection.")]
        string? game_root = null,
        [Description("Explicit safety gate. Must be true to launch Ekd5.exe when it is not already running.")]
        bool allow_launch = true,
        [Description("Maximum milliseconds to wait for the process/window after launch.")]
        int wait_ms = 10000,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameProcessStart(game_root, allow_launch, wait_ms, output_dir);

    [McpServerTool]
    [Description("Return process, window, foreground, x32dbg bridge, debugger state, and a short read-only battle-state summary.")]
    public object debug_session_state(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042)
        => runtime.DebugSessionState(game_root, host_name, port);

    [McpServerTool]
    [Description("Prepare the Ekd5.exe game window by restoring, moving/resizing, and optionally foregrounding it.")]
    public object game_window_prepare(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Window left coordinate.")]
        int x = 40,
        [Description("Window top coordinate.")]
        int y = 40,
        [Description("Window width.")]
        int width = 960,
        [Description("Window height.")]
        int height = 720,
        [Description("Whether to move/resize the window.")]
        bool allow_resize = true,
        [Description("Whether to bring the window to the foreground.")]
        bool bring_to_front = true)
        => runtime.GameWindowPrepare(game_root, x, y, width, height, allow_resize, bring_to_front);

    [McpServerTool]
    [Description("Capture the Ekd5.exe window client area to a PNG under CCZModStudio_Reports/DebugEvidence.")]
    public object game_capture_frame(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Short label used in the screenshot file name.")]
        string label = "frame")
        => runtime.GameCaptureFrame(game_root, output_dir, label);

    [McpServerTool]
    [Description("Read the verified profile-derived battle state from Ekd5.exe with read-only ReadProcessMemory.")]
    public object game_read_battle_state(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Maximum tactical unit rows to decode. Defaults to 160.")]
        int max_units = 160,
        [Description("When true, export selected raw memory ranges to the evidence directory.")]
        bool include_raw_ranges = false,
        [Description("Optional output directory for raw memory exports.")]
        string? output_dir = null)
        => runtime.GameReadBattleState(game_root, max_units, include_raw_ranges, output_dir);

    [McpServerTool]
    [Description("Classify the current Ekd5.exe runtime phase using process state, x32dbg state, and read-only battle memory; no screenshots or input.")]
    public object game_runtime_state_classify(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Minimum decoded units required before classifying as battle_loaded.")]
        int min_battle_units = 8,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameRuntimeStateClassify(game_root, host_name, port, min_battle_units, output_dir);

    [McpServerTool]
    [Description("Poll game_runtime_state_classify until one of the requested classifications appears or a timeout occurs; no screenshots, input, or memory writes.")]
    public object game_runtime_wait_for_state(
        [Description("Comma-separated classifications to stop on, for example process_no_battle_signal,battle_loaded. Use any to accept the first classification.")]
        string target_classifications = "process_no_battle_signal,battle_loaded",
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Timeout in milliseconds.")]
        int timeout_ms = 30000,
        [Description("Polling interval in milliseconds.")]
        int poll_interval_ms = 500,
        [Description("Minimum decoded units required before classifying as battle_loaded.")]
        int min_battle_units = 8,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameRuntimeWaitForState(game_root, host_name, port, target_classifications, timeout_ms, poll_interval_ms, min_battle_units, output_dir);

    [McpServerTool]
    [Description("Read-only scan of Ekd5.exe process memory for GBK R-scene text anchors such as Xu Zijiang/mode-selection/start-game; no screenshots or input.")]
    public object game_rscene_text_anchor_scan(
        [Description("Comma-separated GBK text anchors. Leave empty for Xu Zijiang/mode-selection/start-game defaults.")]
        string anchors = "",
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Maximum readable memory bytes to scan across committed pages.")]
        int max_scan_bytes = 67108864,
        [Description("Maximum hits per anchor.")]
        int max_hits_per_anchor = 8,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameRSceneTextAnchorScan(anchors, game_root, max_scan_bytes, max_hits_per_anchor, output_dir);

    [McpServerTool]
    [Description("Read-only broad runtime anchor sweep for title/R-scene/battle startup progress. Scans GBK and ASCII anchors; no screenshots, input, or memory writes.")]
    public object game_runtime_anchor_sweep(
        [Description("Anchor profile: startup, rscene, battle, or all.")]
        string profile = "all",
        [Description("Optional additional GBK anchors, comma/semicolon separated.")]
        string gbk_anchors = "",
        [Description("Optional additional ASCII anchors, comma/semicolon separated.")]
        string ascii_anchors = "",
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Maximum readable memory bytes to scan across committed pages.")]
        int max_scan_bytes = 67108864,
        [Description("Maximum hits per anchor.")]
        int max_hits_per_anchor = 4,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameRuntimeAnchorSweep(profile, gbk_anchors, ascii_anchors, game_root, max_scan_bytes, max_hits_per_anchor, output_dir);

    [McpServerTool]
    [Description("Read-only scan of Ekd5.exe memory for R_00 script byte windows around the Xu Zijiang actor-click and mode-choice route; no screenshots or input.")]
    public object game_rscene_script_window_scan(
        [Description("Route profile. v1 supports regular_start.")]
        string route = "regular_start",
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Bytes before/after each command anchor to include in the script-window pattern.")]
        int context_bytes = 16,
        [Description("Maximum readable memory bytes to scan across committed pages.")]
        int max_scan_bytes = 67108864,
        [Description("Maximum hits per script window.")]
        int max_hits_per_window = 4,
        [Description("Also scan readable memory for uint32 references to matched command/base addresses.")]
        bool include_pointer_refs = true,
        [Description("Maximum pointer references to return per searched address.")]
        int max_pointer_refs = 16,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameRSceneScriptWindowScan(route, game_root, context_bytes, max_scan_bytes, max_hits_per_window, include_pointer_refs, max_pointer_refs, output_dir);

    [McpServerTool]
    [Description("Match the current read-only battle memory against a known internal scenario profile such as yingchuan_cao_zhangliang; no screenshots or input.")]
    public object game_battle_state_match(
        [Description("Scenario profile. v1 supports yingchuan_cao_zhangliang.")]
        string profile = "yingchuan_cao_zhangliang",
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameBattleStateMatch(profile, game_root, output_dir);

    [McpServerTool]
    [Description("Read the unified battlefield runtime snapshot: phase, units, controllable units, occupied cells, and combat context; read-only.")]
    public object game_read_battlefield_runtime_snapshot(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory. When provided, writes a JSON evidence file.")]
        string? output_dir = null)
        => runtime.GameReadBattlefieldRuntimeSnapshot(game_root, output_dir);

    [McpServerTool]
    [Description("Create or verify tactical grid calibration from default values and optional known grid/client points; read-only.")]
    public object game_battle_calibrate_grid(
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Client-space x origin for grid.")]
        int origin_x = 76,
        [Description("Client-space y origin for grid.")]
        int origin_y = 44,
        [Description("Grid cell width in pixels.")]
        int cell_width = 32,
        [Description("Grid cell height in pixels.")]
        int cell_height = 32,
        [Description("Optional known points formatted as 'gridX,gridY,clientX,clientY;...'. Two or more points infer origin/cell size.")]
        string known_points = "",
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameBattleCalibrateGrid(game_root, origin_x, origin_y, cell_width, cell_height, known_points, output_dir);

    [McpServerTool]
    [Description("Convert a tactical grid coordinate to current client/screen coordinates using calibration; read-only.")]
    public object game_battle_grid_to_client(
        [Description("Tactical grid x coordinate.")]
        int grid_x,
        [Description("Tactical grid y coordinate.")]
        int grid_y,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Client-space x origin for grid.")]
        int origin_x = 76,
        [Description("Client-space y origin for grid.")]
        int origin_y = 44,
        [Description("Grid cell width in pixels.")]
        int cell_width = 32,
        [Description("Grid cell height in pixels.")]
        int cell_height = 32,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameBattleGridToClient(grid_x, grid_y, game_root, origin_x, origin_y, cell_width, cell_height, output_dir);

    [McpServerTool]
    [Description("Verify whether a tactical grid coordinate is in-window and occupied as expected before any high-level automation click; read-only.")]
    public object game_battle_verify_click_target(
        [Description("Tactical grid x coordinate.")]
        int grid_x,
        [Description("Tactical grid y coordinate.")]
        int grid_y,
        [Description("Whether an occupied target cell is allowed.")]
        bool allow_occupied = false,
        [Description("Expected occupying unit index, or -1 to not require one.")]
        int expected_unit_index = -1,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Client-space x origin for grid.")]
        int origin_x = 76,
        [Description("Client-space y origin for grid.")]
        int origin_y = 44,
        [Description("Grid cell width in pixels.")]
        int cell_width = 32,
        [Description("Grid cell height in pixels.")]
        int cell_height = 32,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameBattleVerifyClickTarget(grid_x, grid_y, allow_occupied, expected_unit_index, game_root, origin_x, origin_y, cell_width, cell_height, output_dir);

    [McpServerTool]
    [Description("Select one controllable side=0 battle unit. allow_input defaults true; pass false for dry-run.")]
    public object game_battle_select_unit(
        [Description("Tactical unit index from the unit array.")]
        int unit_index,
        [Description("Explicit safety gate. Must be true to send mouse input.")]
        bool allow_input = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Client-space x origin for grid.")]
        int origin_x = 76,
        [Description("Client-space y origin for grid.")]
        int origin_y = 44,
        [Description("Grid cell width in pixels.")]
        int cell_width = 32,
        [Description("Grid cell height in pixels.")]
        int cell_height = 32,
        [Description("Milliseconds to wait after real input before reading post-state.")]
        int settle_ms = 500,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameBattleSelectUnit(unit_index, allow_input, game_root, origin_x, origin_y, cell_width, cell_height, settle_ms, output_dir);

    [McpServerTool]
    [Description("Move one controllable side=0 battle unit to a conservatively validated empty cell and wait. Real mouse input requires allow_input=true.")]
    public object game_battle_move_unit(
        [Description("Tactical unit index from the unit array.")]
        int unit_index,
        [Description("Destination grid x.")]
        int to_x,
        [Description("Destination grid y.")]
        int to_y,
        [Description("Explicit safety gate. Must be true to send mouse input.")]
        bool allow_input = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Client-space x origin for grid.")]
        int origin_x = 76,
        [Description("Client-space y origin for grid.")]
        int origin_y = 44,
        [Description("Grid cell width in pixels.")]
        int cell_width = 32,
        [Description("Grid cell height in pixels.")]
        int cell_height = 32,
        [Description("Milliseconds to wait after real input before reading post-state.")]
        int settle_ms = 700,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameBattleMoveUnit(unit_index, to_x, to_y, allow_input, game_root, origin_x, origin_y, cell_width, cell_height, settle_ms, output_dir);

    [McpServerTool]
    [Description("Execute a conservative physical attack plan. v1 only sends adjacent melee attack input; non-adjacent targets are reported as range_unknown.")]
    public object game_battle_attack(
        [Description("Attacker tactical unit index.")]
        int attacker_index,
        [Description("Target tactical unit index.")]
        int target_index,
        [Description("Allow planning move-first routes. v1 still blocks unverified move-then-attack input.")]
        bool move_first = true,
        [Description("Explicit safety gate. Must be true to send mouse input.")]
        bool allow_input = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Client-space x origin for grid.")]
        int origin_x = 76,
        [Description("Client-space y origin for grid.")]
        int origin_y = 44,
        [Description("Grid cell width in pixels.")]
        int cell_width = 32,
        [Description("Grid cell height in pixels.")]
        int cell_height = 32,
        [Description("Milliseconds to wait after real input before reading post-state.")]
        int settle_ms = 1200,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameBattleAttack(attacker_index, target_index, move_first, allow_input, game_root, origin_x, origin_y, cell_width, cell_height, settle_ms, output_dir);

    [McpServerTool]
    [Description("Select one controllable side=0 battle unit and click wait. Real mouse input requires allow_input=true.")]
    public object game_battle_wait(
        [Description("Tactical unit index from the unit array.")]
        int unit_index,
        [Description("Explicit safety gate. Must be true to send mouse input.")]
        bool allow_input = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Client-space x origin for grid.")]
        int origin_x = 76,
        [Description("Client-space y origin for grid.")]
        int origin_y = 44,
        [Description("Grid cell width in pixels.")]
        int cell_width = 32,
        [Description("Grid cell height in pixels.")]
        int cell_height = 32,
        [Description("Milliseconds to wait after real input before reading post-state.")]
        int settle_ms = 700,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameBattleWait(unit_index, allow_input, game_root, origin_x, origin_y, cell_width, cell_height, settle_ms, output_dir);

    [McpServerTool]
    [Description("Click the registered end-turn UI area. Real mouse input requires allow_input=true.")]
    public object game_battle_end_turn(
        [Description("Explicit safety gate. Must be true to send mouse input.")]
        bool allow_input = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Milliseconds to wait after real input before reading post-state.")]
        int settle_ms = 700,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameBattleEndTurn(allow_input, game_root, settle_ms, output_dir);

    [McpServerTool]
    [Description("Plan or execute one safe battlefield automation step. v1 policy safe_attack attacks adjacent lowest-HP enemy or waits.")]
    public object game_battle_auto_step(
        [Description("Automation policy. v1 supports safe_attack.")]
        string policy = "safe_attack",
        [Description("Explicit safety gate. Must be true to send mouse input.")]
        bool allow_input = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Client-space x origin for grid.")]
        int origin_x = 76,
        [Description("Client-space y origin for grid.")]
        int origin_y = 44,
        [Description("Grid cell width in pixels.")]
        int cell_width = 32,
        [Description("Grid cell height in pixels.")]
        int cell_height = 32,
        [Description("Milliseconds to wait after real input before reading post-state.")]
        int settle_ms = 1000,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
    {
        try
        {
            return runtime.GameBattleAutoStep(policy, allow_input, game_root, origin_x, origin_y, cell_width, cell_height, settle_ms, output_dir);
        }
        catch (Exception ex) when (!allow_input)
        {
            return new
            {
                status = "dry-run-tool-error",
                tool = "game_battle_auto_step",
                error = ex.Message,
                error_type = ex.GetType().FullName,
                allow_input,
                safety = "Dry-run catch at MCP tool boundary; no input was sent."
            };
        }
    }

    [McpServerTool]
    [Description("Plan or execute repeated safe battlefield automation steps. Dry-run by default and stops after one planned step.")]
    public object game_battle_auto_run(
        [Description("Maximum number of automation steps.")]
        int max_steps = 3,
        [Description("Stop when phase is not a known player-input phase.")]
        bool stop_on_unknown = true,
        [Description("Automation policy. v1 supports safe_attack.")]
        string policy = "safe_attack",
        [Description("Explicit safety gate. Must be true to send mouse input.")]
        bool allow_input = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Client-space x origin for grid.")]
        int origin_x = 76,
        [Description("Client-space y origin for grid.")]
        int origin_y = 44,
        [Description("Grid cell width in pixels.")]
        int cell_width = 32,
        [Description("Grid cell height in pixels.")]
        int cell_height = 32,
        [Description("Milliseconds to wait after real input before reading post-state.")]
        int settle_ms = 1000,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameBattleAutoRun(max_steps, stop_on_unknown, policy, allow_input, game_root, origin_x, origin_y, cell_width, cell_height, settle_ms, output_dir);

    [McpServerTool]
    [Description("Click a tactical grid coordinate after pre/post state capture. Real input requires allow_input=true.")]
    public object game_click_grid(
        [Description("Tactical grid x coordinate.")]
        int grid_x,
        [Description("Tactical grid y coordinate.")]
        int grid_y,
        [Description("Explicit safety gate. Must be true to send mouse input.")]
        bool allow_input = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Client-space x origin for grid (calibration value).")]
        int origin_x = 76,
        [Description("Client-space y origin for grid (calibration value).")]
        int origin_y = 44,
        [Description("Grid cell width in pixels.")]
        int cell_width = 32,
        [Description("Grid cell height in pixels.")]
        int cell_height = 32,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameClickGrid(game_root, grid_x, grid_y, allow_input, origin_x, origin_y, cell_width, cell_height, output_dir);

    [McpServerTool]
    [Description("Click a registered UI area such as attack, strategy, item, wait, end_turn, or title_start_game. Real input requires allow_input=true.")]
    public object game_click_ui(
        [Description("Registered UI area key: attack, strategy, item, wait, end_turn, title_start_game, title_load_game, title_settings, title_exit, menu_top_1, menu_top_2, menu_top_3, menu_top_4.")]
        string ui_area,
        [Description("Explicit safety gate. Must be true to send mouse input.")]
        bool allow_input = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameClickUi(game_root, ui_area, allow_input, output_dir);

    [McpServerTool]
    [Description("Post a keyboard sequence to the Ekd5.exe main window without mouse control or screenshots. Real input requires allow_input=true.")]
    public object game_key_sequence(
        [Description("Comma/space separated key sequence. Supports enter, esc, up, down, left, right, space, tab, f1..f12, digits, and single letters.")]
        string sequence,
        [Description("Explicit safety gate. Must be true to post keyboard messages.")]
        bool allow_input = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Delay after each key in milliseconds.")]
        int delay_ms = 250,
        [Description("Whether to bring the game window to the foreground before posting messages.")]
        bool bring_to_front = true,
        [Description("Keyboard delivery mode: post_message (default) or send_input. send_input is still keyboard-only and requires allow_input=true.")]
        string delivery = "post_message",
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.GameKeySequence(sequence, allow_input, game_root, delay_ms, bring_to_front, delivery, host_name, port, output_dir);

    [McpServerTool]
    [Description("Read-only analysis of RS/R_00.eex Xu Zijiang mode-selection route; emits choice/case/variable evidence and a keyboard-only route candidate.")]
    public object debug_r00_mode_route_analyze(
        [Description("Route profile. v1 supports regular_start, which selects regular mode then Start Game.")]
        string route = "regular_start",
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null)
        => runtime.DebugR00ModeRouteAnalyze(route, game_root, output_dir);

    [McpServerTool]
    [Description("Read-only R_00 Xu Zijiang actor route analysis: actor placement/movement/click prerequisite, choice route, and latest no-input probe evidence.")]
    public object debug_r00_actor_route_analyze(
        [Description("Route profile. v1 supports regular_start.")]
        string route = "regular_start",
        [Description("Person id for the R-scene actor. Defaults to Xu Zijiang, person 157.")]
        int person_id = 157,
        [Description("Optional r00-startup-route-probe-summary.json to summarize. Defaults to the latest one when available.")]
        string? evidence_path = null,
        [Description("When true, include the latest live no-input probe summary if evidence_path is omitted.")]
        bool include_latest_evidence = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null)
        => runtime.DebugR00ActorRouteAnalyze(route, person_id, evidence_path, include_latest_evidence, game_root, output_dir);

    [McpServerTool]
    [Description("Offline scan Ekd5.exe .text for R-scene command-id comparison candidates such as 2D actor-click and 12 choice-box; optionally writes an internal probe plan.")]
    public object debug_rscene_command_handler_scan(
        [Description("Comma-separated command ids to scan, for example 2D,12,07,13.")]
        string command_ids = "2D,12,07,13",
        [Description("Maximum candidates to keep per command id.")]
        int max_candidates_per_command = 32,
        [Description("Bytes of nearby code to include in each offline evidence row.")]
        int context_bytes = 16,
        [Description("When true, also write an InternalProbePlan consumable by debug_internal_probe_run.")]
        bool write_probe_plan = true,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugRSceneCommandHandlerScan(command_ids, max_candidates_per_command, context_bytes, write_probe_plan, output_dir, game_root);

    [McpServerTool]
    [Description("Offline+runtime read-only scan for the title-to-R-scene load transition: R/S EEX path refs, R_00 script residency, pointer refs, and a transition probe plan.")]
    public object debug_rscene_load_transition_scan(
        [Description("Route profile. v1 supports regular_start.")]
        string route = "regular_start",
        [Description("Comma-separated ASCII path/format anchors to find in Ekd5.exe. Defaults cover RS R/S EEX format strings.")]
        string anchors = @"RS\R_%s.EEX,RS\S_%s.EEX,.EEX",
        [Description("Maximum static string-reference candidates to keep.")]
        int max_candidates = 64,
        [Description("Bytes of nearby code to include in each static evidence row.")]
        int context_bytes = 16,
        [Description("When true, also write an InternalProbePlan for transition candidates.")]
        bool write_probe_plan = true,
        [Description("When true and Ekd5.exe is running, scan process memory for R_00 anchors/script windows and pointer refs.")]
        bool include_runtime_scan = true,
        [Description("Probe target filter. Use all, anchor_functions,direct_refs,no_imports, anchor_functions_only, direct_refs_only, or api_calls_only. The static report still includes every candidate class.")]
        string candidate_filter = "all",
        [Description("Maximum readable memory bytes to scan when include_runtime_scan=true.")]
        int max_scan_bytes = 67108864,
        [Description("Maximum pointer references to return per searched script address.")]
        int max_pointer_refs = 16,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugRSceneLoadTransitionScan(route, anchors, max_candidates, context_bytes, write_probe_plan, include_runtime_scan, candidate_filter, max_scan_bytes, max_pointer_refs, output_dir, game_root);

    [McpServerTool]
    [Description("Live-ready title-to-R_00 load transition probe: generate R/S EEX loader candidates, optionally run x32dbg breakpoints during startup, then verify R_00 script-window residency. No mouse, screenshots, injection, or process-memory writes.")]
    public object debug_rscene_load_transition_probe_run(
        [Description("Route profile. v1 supports regular_start.")]
        string route = "regular_start",
        [Description("Whether to start/attach x32dbg through debug_session_start before probing.")]
        bool start_debugger = false,
        [Description("Whether to actually set/run x32dbg breakpoints from the generated R-scene load-transition probe plan.")]
        bool run_probes = false,
        [Description("After breakpoints are set, issue limited x32dbg run commands until the Ekd5 main window appears.")]
        bool continue_startup = false,
        [Description("Maximum x32dbg run commands for continue_startup. Clamped to 1..8.")]
        int startup_continue_max_runs = 4,
        [Description("Comma-separated ASCII path/format anchors to find in Ekd5.exe. Defaults cover RS R/S EEX format strings.")]
        string anchors = @"RS\R_%s.EEX,RS\S_%s.EEX,.EEX",
        [Description("Maximum static string-reference candidates to keep.")]
        int max_candidates = 64,
        [Description("Bytes of nearby code to include in each static evidence row.")]
        int context_bytes = 16,
        [Description("Probe target filter. Default focuses live probing on R/S path references and containing functions, excluding generic file API call sites.")]
        string candidate_filter = "anchor_functions,direct_refs,no_imports",
        [Description("Maximum planned transition hits to collect if run_probes=true.")]
        int max_hits = 12,
        [Description("Timeout in milliseconds if run_probes=true or continue_startup=true.")]
        int timeout_ms = 60000,
        [Description("Disable a hit breakpoint after capture to keep running.")]
        bool disable_after_hit = true,
        [Description("Ignore the normal Ekd5 entry-point pause and keep running.")]
        bool continue_after_entry_point_pause = true,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Maximum readable memory bytes to scan when checking R_00 runtime state.")]
        int max_scan_bytes = 67108864,
        [Description("Maximum pointer references to return per searched script address.")]
        int max_pointer_refs = 16,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugRSceneLoadTransitionProbeRun(route, start_debugger, run_probes, continue_startup, startup_continue_max_runs, anchors, max_candidates, context_bytes, candidate_filter, max_hits, timeout_ms, disable_after_hit, continue_after_entry_point_pause, host_name, port, max_scan_bytes, max_pointer_refs, output_dir, game_root);

    [McpServerTool]
    [Description("Offline scan for title/menu internal dispatch candidates from Win32 message/menu API call sites. Writes a focused probe plan without launching, input, screenshots, injection, or memory writes.")]
    public object debug_title_menu_dispatch_scan(
        [Description("Maximum call-site candidates to keep per API.")]
        int max_candidates_per_api = 8,
        [Description("Bytes of nearby code to include in each evidence row.")]
        int context_bytes = 16,
        [Description("When true, also add containing function-entry candidates for call sites.")]
        bool include_function_entries = true,
        [Description("When true, write an InternalProbePlan consumable by debug_internal_probe_run.")]
        bool write_probe_plan = true,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugTitleMenuDispatchScan(max_candidates_per_api, context_bytes, include_function_entries, write_probe_plan, output_dir, game_root);

    [McpServerTool]
    [Description("Offline scan for title WndProc/message-dispatch candidates from x86 compares against WM_* and VK_* constants. Writes a breakpoint-only probe plan without launching, input, screenshots, injection, or memory writes.")]
    public object debug_title_wndproc_dispatch_scan(
        [Description("Maximum compare candidates to keep per WM_/VK_ constant.")]
        int max_candidates_per_constant = 8,
        [Description("Bytes of nearby code to include in each evidence row.")]
        int context_bytes = 16,
        [Description("When true, also add containing function-entry candidates for compare sites.")]
        bool include_function_entries = true,
        [Description("When true, write an InternalProbePlan consumable by debug_internal_probe_run.")]
        bool write_probe_plan = true,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugTitleWndProcDispatchScan(max_candidates_per_constant, context_bytes, include_function_entries, write_probe_plan, output_dir, game_root);

    [McpServerTool]
    [Description("Live-ready title-menu dispatch probe wrapper: static dispatch scan, optional x32dbg start/continue, optional breakpoint probing, optional keyboard-only trigger, and read-only state evidence. Safe defaults do not launch, run probes, or send input.")]
    public object debug_title_menu_dispatch_probe_run(
        [Description("Title route under review: title_menu, title_start_game, title_load_game, title_settings, settings_return, or title_exit.")]
        string route = "title_menu",
        [Description("Optional keyboard-only sequence to trigger the route after breakpoints are prepared. Empty means no trigger.")]
        string trigger_sequence = "",
        [Description("Explicit safety gate. Must be true to send keyboard input.")]
        bool allow_input = true,
        [Description("Whether to start/attach x32dbg through debug_session_start before probing.")]
        bool start_debugger = false,
        [Description("After starting/attaching x32dbg, issue limited run commands until the Ekd5 main window appears.")]
        bool continue_startup = false,
        [Description("Maximum x32dbg run commands for continue_startup. Clamped to 1..8.")]
        int startup_continue_max_runs = 4,
        [Description("Whether to actually set/run x32dbg breakpoints from the generated title-menu dispatch probe plan.")]
        bool run_probes = false,
        [Description("Maximum call-site candidates to keep per API in the static title dispatch scan.")]
        int max_candidates_per_api = 8,
        [Description("Bytes of nearby code to include in each static evidence row.")]
        int context_bytes = 16,
        [Description("Keyboard delivery mode for the optional trigger: post_message or send_input.")]
        string key_delivery = "post_message",
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Maximum planned dispatch hits to collect if run_probes=true.")]
        int max_hits = 12,
        [Description("Timeout in milliseconds if run_probes=true or continue_startup=true.")]
        int timeout_ms = 60000,
        [Description("Disable a hit breakpoint after capture to keep running.")]
        bool disable_after_hit = true,
        [Description("Ignore the normal Ekd5 entry-point pause and keep running.")]
        bool continue_after_entry_point_pause = true,
        [Description("Maximum readable memory bytes to scan when checking R_00 runtime state.")]
        int max_scan_bytes = 67108864,
        [Description("Maximum pointer references to return per searched script address.")]
        int max_pointer_refs = 16,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugTitleMenuDispatchProbeRun(route, trigger_sequence, allow_input, start_debugger, continue_startup, startup_continue_max_runs, run_probes, max_candidates_per_api, context_bytes, key_delivery, host_name, port, max_hits, timeout_ms, disable_after_hit, continue_after_entry_point_pause, max_scan_bytes, max_pointer_refs, output_dir, game_root);

    [McpServerTool]
    [Description("Build or run a title-menu dispatch route matrix for Start, Load, Settings, and Exit. Safe defaults only generate per-route probe evidence; live input and exit route require explicit gates.")]
    public object debug_title_menu_dispatch_matrix_run(
        [Description("Comma/semicolon-separated title routes, or title_menu/all for Start, Load, Settings, Exit.")]
        string routes = "title_menu",
        [Description("Explicit safety gate. Must be true to send keyboard input for route triggers.")]
        bool allow_input = true,
        [Description("Explicit safety gate for the title_exit route when allow_input=true.")]
        bool allow_exit_route = true,
        [Description("When allow_input=true, use built-in keyboard-only trigger sequences for each route.")]
        bool use_default_triggers = true,
        [Description("Whether to start/attach x32dbg through debug_session_start before probing each route.")]
        bool start_debugger = false,
        [Description("After starting/attaching x32dbg, issue limited run commands until the Ekd5 main window appears.")]
        bool continue_startup = false,
        [Description("Maximum x32dbg run commands for continue_startup. Clamped to 1..8.")]
        int startup_continue_max_runs = 4,
        [Description("Whether to actually set/run x32dbg breakpoints from the generated title-menu dispatch probe plan.")]
        bool run_probes = false,
        [Description("Maximum call-site candidates to keep per API in the static title dispatch scan.")]
        int max_candidates_per_api = 8,
        [Description("Bytes of nearby code to include in each static evidence row.")]
        int context_bytes = 16,
        [Description("Keyboard delivery mode for optional triggers: post_message or send_input.")]
        string key_delivery = "post_message",
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Maximum planned dispatch hits to collect per route if run_probes=true.")]
        int max_hits_per_route = 8,
        [Description("Timeout in milliseconds if run_probes=true or continue_startup=true.")]
        int timeout_ms = 60000,
        [Description("Disable a hit breakpoint after capture to keep running.")]
        bool disable_after_hit = true,
        [Description("Ignore the normal Ekd5 entry-point pause and keep running.")]
        bool continue_after_entry_point_pause = true,
        [Description("Maximum readable memory bytes to scan when checking R_00 runtime state.")]
        int max_scan_bytes = 67108864,
        [Description("Maximum pointer references to return per searched script address.")]
        int max_pointer_refs = 16,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugTitleMenuDispatchMatrixRun(routes, allow_input, allow_exit_route, use_default_triggers, start_debugger, continue_startup, startup_continue_max_runs, run_probes, max_candidates_per_api, context_bytes, key_delivery, host_name, port, max_hits_per_route, timeout_ms, disable_after_hit, continue_after_entry_point_pause, max_scan_bytes, max_pointer_refs, output_dir, game_root);

    [McpServerTool]
    [Description("Orchestrate the no-mouse/no-screenshot R_00 startup-route probe: route analysis, script-window scan, command-handler probe plan, optional x32dbg run, optional keyboard-only trigger, and final evidence.")]
    public object debug_r00_startup_route_probe_run(
        [Description("Route profile. v1 supports regular_start.")]
        string route = "regular_start",
        [Description("Keyboard sequence to send after breakpoints are set. Defaults to the regular_start candidate after the actor-click prerequisite is satisfied.")]
        string sequence = "enter,down,down,down,down,down,enter",
        [Description("Explicit safety gate. Must be true to send keyboard input.")]
        bool allow_input = true,
        [Description("Whether to start/attach x32dbg through debug_session_start before probing.")]
        bool start_debugger = false,
        [Description("After starting/attaching x32dbg, issue limited run commands until the Ekd5 main window appears.")]
        bool continue_startup = false,
        [Description("Maximum x32dbg run commands for continue_startup. Clamped to 1..8.")]
        int startup_continue_max_runs = 4,
        [Description("Whether to actually set/run x32dbg breakpoints from the generated R-scene command-handler probe plan.")]
        bool run_probes = false,
        [Description("When true, run the generated handler probe plan before continue_startup so early R-scene interpreter hits are not missed.")]
        bool probe_before_startup_continue = false,
        [Description("Comma-separated R-scene command ids for handler candidate scanning.")]
        string command_ids = "2D,12,07,13",
        [Description("Maximum handler candidates to keep per command id.")]
        int max_candidates_per_command = 8,
        [Description("Keyboard delivery mode for the trigger: post_message or send_input.")]
        string key_delivery = "post_message",
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Maximum planned handler hits to collect if run_probes=true.")]
        int max_hits = 12,
        [Description("Timeout in milliseconds if run_probes=true.")]
        int timeout_ms = 60000,
        [Description("Disable a hit breakpoint after capture to keep running.")]
        bool disable_after_hit = true,
        [Description("Ignore the normal Ekd5 entry-point pause and keep running.")]
        bool continue_after_entry_point_pause = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.DebugR00StartupRouteProbeRun(route, sequence, allow_input, start_debugger, continue_startup, startup_continue_max_runs, run_probes, probe_before_startup_continue, command_ids, max_candidates_per_command, key_delivery, host_name, port, max_hits, timeout_ms, disable_after_hit, continue_after_entry_point_pause, game_root, output_dir);

    [McpServerTool]
    [Description("Create a read-only R_00 runtime invoke candidate plan that links script offsets to .text command-handler breakpoint candidates and latest live evidence. It does not execute or inject.")]
    public object debug_r00_runtime_invoke_candidate_plan(
        [Description("Route: regular_start, custom_mode, or mode_help.")]
        string route = "regular_start",
        [Description("Comma-separated R-scene command ids to scan. Script-required ids are always included.")]
        string command_ids = "2D,12,07,13",
        [Description("Maximum static handler candidates to keep per command id.")]
        int max_candidates_per_command = 8,
        [Description("Optional r00-startup-route-probe-summary.json to summarize. Defaults to the latest one when include_latest_evidence=true.")]
        string? evidence_path = null,
        [Description("When true, include the latest startup-route probe summary if evidence_path is omitted.")]
        bool include_latest_evidence = true,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugR00RuntimeInvokeCandidatePlan(route, command_ids, max_candidates_per_command, evidence_path, include_latest_evidence, output_dir, game_root);

    [McpServerTool]
    [Description("Generate the R_00 runtime handler probe plan and optionally run it through x32dbg before/while startup continues. No mouse, screenshots, runtime injection, or process-memory writes.")]
    public object debug_r00_runtime_handler_probe_run(
        [Description("Route: regular_start, custom_mode, or mode_help.")]
        string route = "regular_start",
        [Description("Whether to start/attach x32dbg through debug_session_start before probing.")]
        bool start_debugger = false,
        [Description("After starting/attaching x32dbg, issue limited run commands until the Ekd5 main window appears.")]
        bool continue_startup = false,
        [Description("Maximum x32dbg run commands for continue_startup. Clamped to 1..8.")]
        int startup_continue_max_runs = 4,
        [Description("Whether to actually set/run x32dbg breakpoints from the generated R_00 runtime handler probe plan.")]
        bool run_probes = false,
        [Description("When true, run the generated handler probe plan before continue_startup so early R-scene interpreter hits are not missed.")]
        bool probe_before_startup_continue = true,
        [Description("Comma-separated R-scene command ids for handler candidate scanning. Script-required ids are always included.")]
        string command_ids = "2D,12,07,13",
        [Description("Maximum handler candidates to keep per command id.")]
        int max_candidates_per_command = 8,
        [Description("Optional r00-startup-route-probe-summary.json to summarize. Defaults to the latest one when include_latest_evidence=true.")]
        string? evidence_path = null,
        [Description("When true, include the latest startup-route probe summary if evidence_path is omitted.")]
        bool include_latest_evidence = true,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Maximum planned handler hits to collect if run_probes=true.")]
        int max_hits = 12,
        [Description("Timeout in milliseconds if run_probes=true.")]
        int timeout_ms = 60000,
        [Description("Disable a hit breakpoint after capture to keep running.")]
        bool disable_after_hit = true,
        [Description("Ignore the normal Ekd5 entry-point pause and keep running.")]
        bool continue_after_entry_point_pause = true,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugR00RuntimeHandlerProbeRun(route, start_debugger, continue_startup, startup_continue_max_runs, run_probes, probe_before_startup_continue, command_ids, max_candidates_per_command, evidence_path, include_latest_evidence, host_name, port, max_hits, timeout_ms, disable_after_hit, continue_after_entry_point_pause, output_dir, game_root);

    [McpServerTool]
    [Description("Bounded no-mouse/no-screenshot keyboard exploration for title/R_00 startup: launch/inspect, try candidate key sequences, scan runtime/script-window state after each sequence, and stop on R_00 residency or battle_loaded.")]
    public object debug_keyboard_exploration_run(
        [Description("Route profile. v1 supports regular_start.")]
        string route = "regular_start",
        [Description("Semicolon-separated keyboard sequences. Each sequence is comma/space separated, for example enter;space;enter,down,enter.")]
        string sequences = "enter;space;esc;enter,down,enter;enter,down,down,down,down,down,enter",
        [Description("Explicit safety gate. Must be true to launch Ekd5.exe if not running.")]
        bool allow_launch = true,
        [Description("Explicit safety gate. Must be true to send keyboard input.")]
        bool allow_input = true,
        [Description("Whether to start/attach x32dbg through debug_session_start before exploration.")]
        bool start_debugger = false,
        [Description("After starting/attaching x32dbg, issue limited run commands until the Ekd5 main window appears.")]
        bool continue_startup = false,
        [Description("Maximum x32dbg run commands for continue_startup. Clamped to 1..8.")]
        int startup_continue_max_runs = 4,
        [Description("Keyboard delivery mode: post_message or send_input.")]
        string key_delivery = "post_message",
        [Description("Delay after each key in milliseconds.")]
        int delay_ms = 250,
        [Description("Delay after each sequence before scanning state in milliseconds.")]
        int settle_ms = 750,
        [Description("Maximum candidate sequences to try. Clamped to 1..32.")]
        int max_sequences = 8,
        [Description("Maximum readable memory bytes for runtime R_00 scans.")]
        int max_scan_bytes = 67108864,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.DebugKeyboardExplorationRun(route, sequences, allow_launch, allow_input, start_debugger, continue_startup, startup_continue_max_runs, key_delivery, delay_ms, settle_ms, max_sequences, max_scan_bytes, host_name, port, game_root, output_dir);

    [McpServerTool]
    [Description("Apply a breakpoint/watchpoint plan to the active x32dbg bridge. Supports baseline Breakpoints and auto-locate Batches plans.")]
    public object debug_breakpoint_plan_apply(
        [Description("Plan JSON path. Defaults to latest breakpoints-6.5-baseline.json under DebugEvidence.")]
        string? plan_path = null,
        [Description("Batch index for watchpoint plans. Ignored for baseline Breakpoints plans.")]
        int batch_index = 1,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Clear hardware breakpoints before applying a watchpoint batch.")]
        bool clear_hardware_first = false)
        => runtime.DebugBreakpointPlanApply(plan_path, batch_index, host_name, port, clear_hardware_first);

    [McpServerTool]
    [Description("Export a staged Ekd5.exe function catalog with file offsets and original bytes for autonomous internal debugging.")]
    public object debug_function_catalog(
        [Description("Stage filter: startup, settings, battle_entry, attack_before, attack_execute, attack_after, turn_end, battle, or all.")]
        string stage = "all",
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugFunctionCatalog(stage, output_dir, game_root);

    [McpServerTool]
    [Description("Offline scan Ekd5.exe .text for x86 relative call/jump references to staged functions; writes static xref evidence and breakpoint candidates.")]
    public object debug_static_xref_scan(
        [Description("Stage filter: startup, settings, battle_entry, attack_before, attack_execute, attack_after, turn_end, battle, or all.")]
        string stage = "battle",
        [Description("Nearby target window in bytes for non-direct xref hints.")]
        int near_bytes = 64,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugStaticXrefScan(stage, near_bytes, output_dir, game_root);

    [McpServerTool]
    [Description("Build a cross-stage function address report for attack/turn debugging from catalogs and static xrefs, with recommended breakpoint candidates.")]
    public object debug_address_report(
        [Description("Comma-separated stages, battle, or all. Defaults to attack and turn stages.")]
        string stages = "attack_before,attack_execute,attack_after,turn_end",
        [Description("Nearby target window in bytes for non-direct xref hints.")]
        int near_bytes = 64,
        [Description("Maximum direct caller candidates per function.")]
        int max_candidates_per_function = 8,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugAddressReport(stages, near_bytes, max_candidates_per_function, output_dir, game_root);

    [McpServerTool]
    [Description("Convert a function address report into an InternalProbePlan consumable by debug_internal_probe_run.")]
    public object debug_address_report_probe_plan(
        [Description("Optional function-address-report.json path. If omitted, a fresh report is generated.")]
        string? report_path = null,
        [Description("Comma-separated stages when report_path is omitted.")]
        string stages = "attack_before,attack_execute,attack_after,turn_end",
        [Description("Nearby target window in bytes when generating a fresh report.")]
        int near_bytes = 64,
        [Description("Maximum caller candidates per function when generating a fresh report.")]
        int max_candidates_per_function = 8,
        [Description("Include target function entry breakpoints.")]
        bool include_targets = true,
        [Description("Include caller breakpoints from recommended_breakpoints.")]
        bool include_callers = true,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugAddressReportProbePlan(report_path, stages, near_bytes, max_candidates_per_function, include_targets, include_callers, output_dir, game_root);

    [McpServerTool]
    [Description("One-shot address probe orchestration: optional game/debugger start, optional runtime wait, address report probe-plan generation, and optional x32dbg probe run.")]
    public object debug_address_probe_run(
        [Description("Optional function-address-report.json path. If omitted, a fresh report is generated.")]
        string? report_path = null,
        [Description("Comma-separated stages when report_path is omitted.")]
        string stages = "attack_before,attack_execute,attack_after,turn_end",
        [Description("Whether to start Ekd5.exe directly before probing.")]
        bool start_game = false,
        [Description("Explicit safety gate for start_game. Must be true to launch Ekd5.exe.")]
        bool allow_launch = true,
        [Description("Whether to start/attach x32dbg through debug_session_start.")]
        bool start_debugger = false,
        [Description("Optional runtime classifications to wait for before probe planning, or none.")]
        string? wait_for_state = null,
        [Description("Whether to actually set/run x32dbg breakpoints. Requires bridge online.")]
        bool run_probes = false,
        [Description("Maximum hits to collect if run_probes=true.")]
        int max_hits = 16,
        [Description("Timeout in milliseconds if run_probes=true.")]
        int timeout_ms = 60000,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugAddressProbeRun(report_path, stages, start_game, allow_launch, start_debugger, wait_for_state, run_probes, max_hits, timeout_ms, host_name, port, output_dir, game_root);

    [McpServerTool]
    [Description("Scenario-gated address probe orchestration: read-only battle profile match, address report probe-plan generation, and optional x32dbg probe run.")]
    public object debug_battle_profile_probe_run(
        [Description("Battle profile. v1 supports yingchuan_cao_zhangliang.")]
        string profile = "yingchuan_cao_zhangliang",
        [Description("Comma-separated stages, battle, or all. Defaults to attack and turn stages.")]
        string stages = "attack_before,attack_execute,attack_after,turn_end",
        [Description("Whether to start Ekd5.exe directly before profile/probe planning.")]
        bool start_game = false,
        [Description("Explicit safety gate for start_game. Must be true to launch Ekd5.exe.")]
        bool allow_launch = true,
        [Description("Maximum milliseconds to wait for Ekd5.exe after start_game.")]
        int game_start_wait_ms = 10000,
        [Description("Whether to start/attach x32dbg through debug_session_start.")]
        bool start_debugger = false,
        [Description("Optional runtime classifications to wait for before profile matching, or none.")]
        string? wait_for_state = null,
        [Description("When true, dynamic probes are skipped unless the battle profile reports profile-matched or attack_after_observed.")]
        bool require_profile_match = false,
        [Description("Whether to actually set/run x32dbg breakpoints after profile gating. Requires bridge online.")]
        bool run_probes = false,
        [Description("Maximum hits to collect if run_probes=true and profile gate allows it.")]
        int max_hits = 16,
        [Description("Timeout in milliseconds for waiting/probe running.")]
        int timeout_ms = 60000,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugBattleProfileProbeRun(profile, stages, start_game, allow_launch, game_start_wait_ms, start_debugger, wait_for_state, require_profile_match, run_probes, max_hits, timeout_ms, host_name, port, output_dir, game_root);

    [McpServerTool]
    [Description("Read-only readiness check for live dynamic probing: target process/window, x32dbg bridge, runtime phase, battle profile, and address probe plan.")]
    public object debug_live_probe_readiness(
        [Description("Battle profile. v1 supports yingchuan_cao_zhangliang.")]
        string profile = "yingchuan_cao_zhangliang",
        [Description("Comma-separated stages, battle, or all. Defaults to attack and turn stages.")]
        string stages = "attack_before,attack_execute,attack_after,turn_end",
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugLiveProbeReadiness(profile, stages, host_name, port, output_dir, game_root);

    [McpServerTool]
    [Description("One-call live probe orchestration: optional game/debugger start, readiness check, and optional profile-gated dynamic x32dbg probe.")]
    public object debug_live_probe_auto_run(
        [Description("Battle profile. v1 supports yingchuan_cao_zhangliang.")]
        string profile = "yingchuan_cao_zhangliang",
        [Description("Comma-separated stages, battle, or all. Defaults to attack and turn stages.")]
        string stages = "attack_before,attack_execute,attack_after,turn_end",
        [Description("Whether to start Ekd5.exe directly before readiness.")]
        bool start_game = false,
        [Description("Explicit safety gate for start_game. Must be true to launch Ekd5.exe.")]
        bool allow_launch = true,
        [Description("Maximum milliseconds to wait for Ekd5.exe after start_game.")]
        int game_start_wait_ms = 10000,
        [Description("Whether to start/attach x32dbg through debug_session_start before readiness.")]
        bool start_debugger = false,
        [Description("After starting/attaching x32dbg, issue limited run commands until the Ekd5 main window appears. No input, screenshot, memory write, or file patching.")]
        bool continue_startup = false,
        [Description("Maximum x32dbg run commands for continue_startup. Clamped to 1..8.")]
        int startup_continue_max_runs = 4,
        [Description("Whether to actually set/run x32dbg breakpoints after readiness.")]
        bool run_probes = false,
        [Description("When true, dynamic probes are skipped unless debug_live_probe_readiness reports ready_for_dynamic_probe=true.")]
        bool require_ready = true,
        [Description("Maximum hits to collect if run_probes=true and readiness allows it.")]
        int max_hits = 16,
        [Description("Timeout in milliseconds for debugger startup/probe running.")]
        int timeout_ms = 60000,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugLiveProbeAutoRun(profile, stages, start_game, allow_launch, game_start_wait_ms, start_debugger, continue_startup, startup_continue_max_runs, run_probes, require_ready, max_hits, timeout_ms, host_name, port, output_dir, game_root);

    [McpServerTool]
    [Description("Battle automation probe loop: writes a fixed hook-candidate probe plan, optionally runs x32dbg breakpoints, and runs dry/controlled auto steps.")]
    public object debug_battle_auto_probe_run(
        [Description("Maximum battle auto steps to plan or execute.")]
        int max_steps = 3,
        [Description("Automation policy. v1 supports safe_attack.")]
        string policy = "safe_attack",
        [Description("Whether to mutate x32dbg breakpoints and run the debuggee.")]
        bool run_probes = false,
        [Description("Explicit safety gate. Must be true to send mouse input.")]
        bool allow_input = true,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Maximum breakpoint hits if run_probes=true.")]
        int max_hits = 16,
        [Description("Timeout in milliseconds for probe running if run_probes=true.")]
        int timeout_ms = 60000,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.DebugBattleAutoProbeRun(max_steps, policy, run_probes, allow_input, host_name, port, max_hits, timeout_ms, game_root, output_dir);

    [McpServerTool]
    [Description("Full audited automation orchestrator for Ekd5.exe startup, menu routes, runtime invoke planning, address verification planning, and knowledge drafting. Final path avoids mouse and screenshots.")]
    public object debug_full_auto_run(
        [Description("Automation profile. v1 supports full_menu_yingchuan.")]
        string profile = "full_menu_yingchuan",
        [Description("Allow debugger-mediated internal function invocation attempts when bridge and plans are ready.")]
        bool allow_debug_invoke = true,
        [Description("Explicit gate for temporary runtime code injection. Defaults true for live-capable runs; pass false for plan-only validation.")]
        bool allow_runtime_injection = true,
        [Description("Whether to create persistent patch-package recommendations. GameDebug never directly patches the EXE.")]
        bool allow_persistent_patch = true,
        [Description("Whether to start Ekd5.exe directly before orchestration.")]
        bool start_game = false,
        [Description("Explicit safety gate for start_game. Must be true to launch Ekd5.exe.")]
        bool allow_launch = true,
        [Description("Whether to start/attach x32dbg through debug_session_start.")]
        bool start_debugger = false,
        [Description("After starting/attaching x32dbg, issue limited run commands until the Ekd5 main window appears.")]
        bool continue_startup = false,
        [Description("Whether to actually set/run x32dbg probes. Safe validation should keep this false.")]
        bool run_probes = false,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugFullAutoRun(profile, allow_debug_invoke, allow_runtime_injection, allow_persistent_patch, start_game, allow_launch, start_debugger, continue_startup, run_probes, host_name, port, output_dir, game_root);

    [McpServerTool]
    [Description("Create an audited plan for debugger-mediated internal calls or temporary runtime injection. Does not write process memory.")]
    public object debug_runtime_invoke_plan(
        [Description("Stage: startup, settings, battle_entry, attack_before, attack_execute, attack_after, turn_end, menu, or all.")]
        string stage = "startup",
        [Description("Route: full_menu_yingchuan, title_menu, r00_regular_start, r00_custom_mode, r00_mode_help, yingchuan_cao_attack_zhangliang, or turn_end.")]
        string route = "full_menu_yingchuan",
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugRuntimeInvokePlan(stage, route, output_dir, game_root);

    [McpServerTool]
    [Description("Run or dry-run a runtime invoke plan. Real temporary injection requires allow_runtime_injection=true and an online local x32dbg bridge.")]
    public object debug_runtime_invoke_run(
        [Description("runtime-invoke-plan.json path.")]
        string plan_path,
        [Description("Explicit safety gate for temporary runtime injection.")]
        bool allow_runtime_injection = true,
        [Description("Allow debugger-mediated register/stack commands when available.")]
        bool allow_debug_invoke = true,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.DebugRuntimeInvokeRun(plan_path, allow_runtime_injection, allow_debug_invoke, host_name, port, game_root, output_dir);

    [McpServerTool]
    [Description("Build and optionally dry-run an internal no-mouse/no-screenshot menu route for title, settings, load-save, exit, and R_00 mode coverage.")]
    public object debug_menu_route_run(
        [Description("Route: full_menu, title_menu, settings_roundtrip, load_save_entry, exit_entry, r00_regular_start, r00_custom_mode, or r00_mode_help.")]
        string route = "full_menu",
        [Description("Whether to execute temporary runtime injection for route steps. Defaults true; pass false to write only plans/evidence.")]
        bool allow_runtime_injection = true,
        [Description("Allow debugger-mediated invoke attempts when bridge is ready.")]
        bool allow_debug_invoke = true,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.DebugMenuRouteRun(route, allow_runtime_injection, allow_debug_invoke, host_name, port, game_root, output_dir);

    [McpServerTool]
    [Description("Verify or plan verification of staged dynamic addresses against a trigger script and battle-state diff gates.")]
    public object debug_address_verify_run(
        [Description("Comma-separated stages to verify.")]
        string stages = "attack_before,attack_execute,attack_after,turn_end",
        [Description("Trigger script. v1 supports yingchuan_cao_attack_zhangliang and turn_end.")]
        string trigger_script = "yingchuan_cao_attack_zhangliang",
        [Description("Whether to run x32dbg probes. Defaults false so address verification remains plan-only unless requested.")]
        bool run_probes = false,
        [Description("Whether to require battle/profile gate before running probes.")]
        bool require_profile_match = false,
        [Description("Maximum hits to collect when run_probes=true.")]
        int max_hits = 16,
        [Description("Timeout in milliseconds for dynamic probe run.")]
        int timeout_ms = 60000,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugAddressVerifyRun(stages, trigger_script, run_probes, require_profile_match, max_hits, timeout_ms, host_name, port, output_dir, game_root);

    [McpServerTool]
    [Description("Promote reviewed dynamic evidence into the formal local knowledge base. With allow_write=true, incomplete evidence is appended as pending.")]
    public object debug_knowledge_promote(
        [Description("Evidence directory or JSON summary to promote.")]
        string evidence_path,
        [Description("Knowledge topic: function-index, automation-flow, merit, or custom.")]
        string topic = "function-index",
        [Description("When false, only writes knowledge-promotions.json without editing the formal knowledge base.")]
        bool allow_write = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for promotion report.")]
        string? output_dir = null)
        => runtime.DebugKnowledgePromote(evidence_path, topic, allow_write, game_root, output_dir);

    [McpServerTool]
    [Description("Create a staged internal breakpoint probe plan from the built-in function catalog.")]
    public object debug_phase_probe_plan(
        [Description("Stage filter: startup, settings, battle_entry, attack_before, attack_execute, attack_after, turn_end, battle, or all.")]
        string stage = "battle",
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugPhaseProbePlan(stage, output_dir, game_root);

    [McpServerTool]
    [Description("Create an audited no-mouse/no-screenshot internal automation plan for starting, probing, and collecting staged evidence.")]
    public object debug_autonomy_plan(
        [Description("Scenario key, for example yingchuan_cao_attack_zhangliang.")]
        string scenario = "yingchuan_cao_attack_zhangliang",
        [Description("Comma-separated stages, or all. Supported: startup, settings, battle_entry, attack_before, attack_execute, attack_after, turn_end.")]
        string stages = "startup,battle_entry,attack_before,attack_execute,attack_after,turn_end",
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugAutonomyPlan(scenario, stages, output_dir, game_root);

    [McpServerTool]
    [Description("Run the safe internal automation plan steps: start/session state, function catalogs, phase probe plans, optional probe run, and knowledge draft.")]
    public object debug_autonomy_run(
        [Description("Autonomy plan path. If omitted, creates a fresh plan.")]
        string? plan_path = null,
        [Description("Scenario key when plan_path is omitted.")]
        string scenario = "yingchuan_cao_attack_zhangliang",
        [Description("Comma-separated stages when plan_path is omitted.")]
        string stages = "startup,battle_entry,attack_before,attack_execute,attack_after,turn_end",
        [Description("Whether to start Ekd5.exe directly without x32dbg before state/probe planning.")]
        bool start_game = false,
        [Description("Explicit safety gate for start_game. Must be true to launch Ekd5.exe.")]
        bool allow_launch = true,
        [Description("Maximum milliseconds to wait for Ekd5.exe after start_game.")]
        int game_start_wait_ms = 10000,
        [Description("Whether to start/attach x32dbg through debug_session_start.")]
        bool start_debugger = false,
        [Description("After starting/attaching x32dbg, issue limited run commands until the Ekd5 main window appears. No input, screenshot, memory write, or file patching.")]
        bool continue_startup = false,
        [Description("Maximum x32dbg run commands for continue_startup. Clamped to 1..8.")]
        int startup_continue_max_runs = 4,
        [Description("When true and run_probes=true, run startup-stage probes before continue_startup so early startup functions are not missed.")]
        bool probe_before_startup_continue = false,
        [Description("Whether to apply and run probe breakpoints. Requires x32dbg bridge online.")]
        bool run_probes = false,
        [Description("Maximum hits per stage when run_probes=true.")]
        int max_hits_per_stage = 3,
        [Description("Timeout per stage in milliseconds when run_probes=true.")]
        int timeout_ms_per_stage = 15000,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null,
        [Description("Optional comma-separated runtime classifications to wait for after optional debugger start. Empty or none skips waiting.")]
        string? wait_for_state = null,
        [Description("Timeout in milliseconds for wait_for_state.")]
        int wait_timeout_ms = 30000,
        [Description("Polling interval in milliseconds for wait_for_state.")]
        int wait_poll_interval_ms = 500)
        => runtime.DebugAutonomyRun(plan_path, scenario, stages, start_game, allow_launch, game_start_wait_ms, start_debugger, continue_startup, startup_continue_max_runs, probe_before_startup_continue, run_probes, max_hits_per_stage, timeout_ms_per_stage, host_name, port, game_root, output_dir, wait_for_state, wait_timeout_ms, wait_poll_interval_ms);

    [McpServerTool]
    [Description("Create a built-in internal function probe plan for key game phases without using mouse input or screenshots.")]
    public object debug_internal_probe_plan(
        [Description("Probe profile: core, battle, turn, or all.")]
        string profile = "all",
        [Description("Optional output directory. Defaults to a game-auto evidence session.")]
        string? output_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugInternalProbePlan(profile, output_dir, game_root);

    [McpServerTool]
    [Description("Apply built-in probe breakpoints, run x32dbg, and collect internal hit evidence without mouse input or screenshots.")]
    public object debug_internal_probe_run(
        [Description("Probe plan path. If omitted, creates a fresh built-in plan.")]
        string? plan_path = null,
        [Description("Probe profile when plan_path is omitted: core, battle, turn, or all.")]
        string profile = "all",
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Maximum hits to collect before stopping.")]
        int max_hits = 12,
        [Description("Timeout in milliseconds.")]
        int timeout_ms = 60000,
        [Description("Disable a hit breakpoint after capture to keep running.")]
        bool disable_after_hit = true,
        [Description("Ignore the normal Ekd5 entry-point pause and keep running. Useful for startup probes after x32dbg launch.")]
        bool continue_after_entry_point_pause = false,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.DebugInternalProbeRun(plan_path, profile, host_name, port, max_hits, timeout_ms, disable_after_hit, continue_after_entry_point_pause, game_root, output_dir);

    [McpServerTool]
    [Description("Apply a stage probe plan, run x32dbg, trigger a no-mouse keyboard sequence, and collect internal evidence.")]
    public object debug_transition_probe_run(
        [Description("Stage filter for the probe plan: startup, settings, battle_entry, attack_before, attack_execute, attack_after, turn_end, or battle.")]
        string stage = "startup",
        [Description("Keyboard sequence to post after breakpoints are set. Supports the same keys as game_key_sequence.")]
        string sequence = "enter",
        [Description("Explicit safety gate. Must be true to post keyboard messages.")]
        bool allow_input = true,
        [Description("Whether to start/attach x32dbg through debug_session_start before probing.")]
        bool start_debugger = false,
        [Description("After starting/attaching x32dbg, issue limited run commands until the Ekd5 main window appears.")]
        bool continue_startup = false,
        [Description("Maximum x32dbg run commands for continue_startup. Clamped to 1..8.")]
        int startup_continue_max_runs = 4,
        [Description("Keyboard delivery mode for the trigger: post_message (default) or send_input.")]
        string key_delivery = "post_message",
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Maximum hits to collect after trigger.")]
        int max_hits = 8,
        [Description("Timeout in milliseconds.")]
        int timeout_ms = 60000,
        [Description("Disable a hit breakpoint after capture to keep running.")]
        bool disable_after_hit = true,
        [Description("Ignore the normal Ekd5 entry-point pause and keep running.")]
        bool continue_after_entry_point_pause = false,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.DebugTransitionProbeRun(stage, sequence, allow_input, start_debugger, continue_startup, startup_continue_max_runs, key_delivery, host_name, port, max_hits, timeout_ms, disable_after_hit, continue_after_entry_point_pause, game_root, output_dir);

    [McpServerTool]
    [Description("Run the active debugger and wait for a planned breakpoint, any debugger pause, a memory event, or timeout.")]
    public object debug_run_until_event(
        [Description("Optional plan JSON used to decide whether a paused CIP is planned.")]
        string? plan_path = null,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Timeout in milliseconds.")]
        int timeout_ms = 30000,
        [Description("Polling interval in milliseconds.")]
        int poll_interval_ms = 250,
        [Description("Also stop when read-only battle-state memory changes.")]
        bool stop_on_memory_event = true,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.DebugRunUntilEvent(plan_path, host_name, port, timeout_ms, poll_interval_ms, stop_on_memory_event, output_dir);

    [McpServerTool]
    [Description("Capture debugger registers, stack, disassembly, memory, and read-only battle state into an internal evidence bundle. Screenshots are opt-in.")]
    public object debug_capture_evidence(
        [Description("Reason label for the evidence bundle.")]
        string reason = "manual-capture",
        [Description("Optional address to disassemble/read. Defaults to current CIP.")]
        string? address = null,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null,
        [Description("When true, also capture the game client area. Default false keeps the workflow independent from screenshots.")]
        bool include_screenshot = false)
        => runtime.DebugCaptureEvidence(reason, address, host_name, port, game_root, output_dir, include_screenshot);

    [McpServerTool]
    [Description("Summarize debug evidence into a local knowledge-base draft without promoting unverified facts.")]
    public object debug_write_knowledge_draft(
        [Description("Evidence directory or JSON file to summarize.")]
        string evidence_path,
        [Description("Knowledge topic: function-index, automation-flow, merit, or custom.")]
        string topic = "function-index",
        [Description("Optional draft directory. Defaults to CCZModStudio_Notes/DebugKnowledgeDrafts.")]
        string? draft_dir = null,
        [Description("Optional game root.")]
        string? game_root = null)
        => runtime.DebugWriteKnowledgeDraft(evidence_path, topic, draft_dir, game_root);

    [McpServerTool]
    [Description("Run a named high-level audited debug script. v1 supports yj_cao_attack_zhangliang as a guarded skeleton.")]
    public object debug_script_run(
        [Description("Script key, for example yj_cao_attack_zhangliang.")]
        string script,
        [Description("Explicit safety gate. Must be true for scripts that send input.")]
        bool allow_input = true,
        [Description("Optional game root.")]
        string? game_root = null,
        [Description("x64dbg MCP bridge host. Must remain local.")]
        string host_name = "127.0.0.1",
        [Description("x64dbg MCP bridge port.")]
        int port = 27042,
        [Description("Optional output directory for evidence.")]
        string? output_dir = null)
        => runtime.DebugScriptRun(script, allow_input, game_root, host_name, port, output_dir);
}

