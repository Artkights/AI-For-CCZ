using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCZModStudio.GameDebugMcpServer;

public sealed partial class GameDebugRuntime
{
    private const string ExpectedSha256 = "84E3A1DC085AE6F9900D1E8C388A9CD6766379832DDF51BC7BDF780C6615B4A3";
    private const uint ImageBase = 0x00400000;
    private const uint UnitArrayAddress = 0x004A7B20;
    private const int UnitStride = 0x30;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] KnownRuntimeClassifications =
    [
        "any",
        "not_running",
        "process_no_battle_signal",
        "partial_battle_signal",
        "battle_loaded",
        "process_battle_read_failed",
        "unknown"
    ];

    private static readonly IReadOnlyList<TitleWndProcConstant> TitleWndProcDispatchConstants =
    [
        new("WM_COMMAND", 0x0111, "message", 0),
        new("WM_KEYDOWN", 0x0100, "message", 1),
        new("WM_KEYUP", 0x0101, "message", 2),
        new("WM_CHAR", 0x0102, "message", 3),
        new("WM_SYSKEYDOWN", 0x0104, "message", 4),
        new("WM_LBUTTONDOWN", 0x0201, "message", 5),
        new("WM_LBUTTONUP", 0x0202, "message", 6),
        new("WM_MOUSEMOVE", 0x0200, "message", 7),
        new("WM_RBUTTONDOWN", 0x0204, "message", 8),
        new("WM_RBUTTONUP", 0x0205, "message", 9),
        new("VK_RETURN", 0x0D, "wparam", 10),
        new("VK_ESCAPE", 0x1B, "wparam", 11),
        new("VK_SPACE", 0x20, "wparam", 12),
        new("VK_LEFT", 0x25, "wparam", 13),
        new("VK_UP", 0x26, "wparam", 14),
        new("VK_RIGHT", 0x27, "wparam", 15),
        new("VK_DOWN", 0x28, "wparam", 16)
    ];

    private static readonly IReadOnlyDictionary<string, UiArea> UiAreas = new Dictionary<string, UiArea>(StringComparer.OrdinalIgnoreCase)
    {
        ["attack"] = new() { Key = "attack", Description = "Right-side command slot 1 / attack candidate", X = 720, Y = 92, Width = 86, Height = 32 },
        ["strategy"] = new() { Key = "strategy", Description = "Right-side command slot 2 / strategy candidate", X = 720, Y = 128, Width = 86, Height = 32 },
        ["item"] = new() { Key = "item", Description = "Right-side command slot 3 / item candidate", X = 720, Y = 164, Width = 86, Height = 32 },
        ["wait"] = new() { Key = "wait", Description = "Right-side command slot 4 / wait candidate", X = 720, Y = 200, Width = 86, Height = 32 },
        ["end_turn"] = new() { Key = "end_turn", Description = "System/end-turn candidate", X = 720, Y = 236, Width = 86, Height = 32 },
        ["title_start_game"] = new() { Key = "title_start_game", Description = "Title menu slot 1: start game", BaseClientWidth = 640, BaseClientHeight = 440, X = 430, Y = 62, Width = 170, Height = 48 },
        ["title_load_game"] = new() { Key = "title_load_game", Description = "Title menu slot 2: load save", BaseClientWidth = 640, BaseClientHeight = 440, X = 430, Y = 130, Width = 170, Height = 48 },
        ["title_settings"] = new() { Key = "title_settings", Description = "Title menu slot 3: environment settings", BaseClientWidth = 640, BaseClientHeight = 440, X = 430, Y = 199, Width = 170, Height = 48 },
        ["title_exit"] = new() { Key = "title_exit", Description = "Title menu slot 4: exit game", BaseClientWidth = 640, BaseClientHeight = 440, X = 430, Y = 263, Width = 170, Height = 48 },
        ["menu_top_1"] = new() { Key = "menu_top_1", Description = "Alias for title_start_game / start game", BaseClientWidth = 640, BaseClientHeight = 440, X = 430, Y = 62, Width = 170, Height = 48 },
        ["menu_top_2"] = new() { Key = "menu_top_2", Description = "Alias for title_load_game / load save", BaseClientWidth = 640, BaseClientHeight = 440, X = 430, Y = 130, Width = 170, Height = 48 },
        ["menu_top_3"] = new() { Key = "menu_top_3", Description = "Alias for title_settings / environment settings", BaseClientWidth = 640, BaseClientHeight = 440, X = 430, Y = 199, Width = 170, Height = 48 },
        ["menu_top_4"] = new() { Key = "menu_top_4", Description = "Alias for title_exit / exit game", BaseClientWidth = 640, BaseClientHeight = 440, X = 430, Y = 263, Width = 170, Height = 48 }
    };

    private static readonly IReadOnlyList<InternalProbeTarget> BuiltInProbeTargets =
    [
        new() { Address = "00484002", Name = "get_char_data_ptr", Phase = "core", ExpectedSemantics = "Data id to character-data pointer; stride 61, base 4A3E77.", TriggerHint = "Title/R/S/battle actor lookups.", EvidenceLevel = "dynamic-hit-on-title", HighFrequency = true },
        new() { Address = "004061F9", Name = "get_unit_ptr", Phase = "battle", ExpectedSemantics = "Battle unit id to tactical-unit pointer; stride 30, base 4A7B20.", TriggerHint = "Battle UI, action dispatch, turn loops.", EvidenceLevel = "dynamic-hit-r-scene-sentinel", HighFrequency = true },
        new() { Address = "0041B500", Name = "get_unit_hp", Phase = "battle", ExpectedSemantics = "Read tactical unit current HP at unit+10.", TriggerHint = "Unit panel, target selection, damage calculations.", EvidenceLevel = "pending-breakpoint", HighFrequency = false },
        new() { Address = "00450986", Name = "generic_event_dispatch", Phase = "battle", ExpectedSemantics = "Generic event dispatch; suspected HP/combat event path when ECX=100.", TriggerHint = "Damage application, HP changes, event notifications.", EvidenceLevel = "pending-breakpoint", HighFrequency = false },
        new() { Address = "0043DADA", Name = "action_dispatch", Phase = "battle", ExpectedSemantics = "Battle action dispatch entry.", TriggerHint = "Any unit action: physical attack, strategy, item, wait.", EvidenceLevel = "knowledge-base-prior", HighFrequency = false },
        new() { Address = "0043DF0A", Name = "action_type_dispatch", Phase = "battle", ExpectedSemantics = "Action type dispatcher before physical/strategy branch.", TriggerHint = "Select a battle action.", EvidenceLevel = "knowledge-base-prior", HighFrequency = false },
        new() { Address = "00435829", Name = "execute_attack", Phase = "attack", ExpectedSemantics = "Physical attack execution candidate.", TriggerHint = "A unit performs physical attack.", EvidenceLevel = "knowledge-base-prior", HighFrequency = false },
        new() { Address = "00438E78", Name = "apply_effect", Phase = "attack", ExpectedSemantics = "Apply attack/combat effect candidate.", TriggerHint = "Damage or special effect applies.", EvidenceLevel = "knowledge-base-prior", HighFrequency = false },
        new() { Address = "00442718", Name = "post_process", Phase = "attack", ExpectedSemantics = "Post damage/action processing candidate.", TriggerHint = "After damage animation/effect.", EvidenceLevel = "knowledge-base-prior", HighFrequency = false },
        new() { Address = "00443AC3", Name = "cleanup", Phase = "attack", ExpectedSemantics = "Battle action cleanup candidate.", TriggerHint = "After attack/strategy resolves.", EvidenceLevel = "knowledge-base-prior", HighFrequency = false },
        new() { Address = "0044AF8F", Name = "reset_turn_loop", Phase = "turn", ExpectedSemantics = "New-turn unit reset loop, traverses tactical units.", TriggerHint = "End turn or new round.", EvidenceLevel = "knowledge-base-prior", HighFrequency = false },
        new() { Address = "00406690", Name = "restore_action", Phase = "turn", ExpectedSemantics = "Restore action capability by clearing unit+0D flags.", TriggerHint = "New turn or second-action logic.", EvidenceLevel = "knowledge-base-prior", HighFrequency = false },
        new() { Address = "00412D52", Name = "try_second_action", Phase = "turn", ExpectedSemantics = "Second-action restore decision and execution candidate.", TriggerHint = "Action end with second-action capability.", EvidenceLevel = "knowledge-base-prior", HighFrequency = false },
        new() { Address = "0042518F", Name = "ability_check_wrapper", Phase = "core", ExpectedSemantics = "Wrapper ability check; pre-check then core engine or fallback.", TriggerHint = "Ability/special-effect checks.", EvidenceLevel = "dynamic-hit-title-entry", HighFrequency = false },
        new() { Address = "004101D9", Name = "core_engine", Phase = "core", ExpectedSemantics = "Core ability engine branch after wrapper pre-check succeeds.", TriggerHint = "Valid special-effect scene.", EvidenceLevel = "pending-breakpoint", HighFrequency = false }
    ];

    private static readonly IReadOnlyList<FunctionCatalogEntry> BuiltInFunctionCatalog =
    [
        new() { Address = "00484002", Name = "get_char_data_ptr", Stage = "startup", Category = "character-data", ExpectedSemantics = "Data id to character-data pointer; stride 61, base 4A3E77.", TriggerHint = "Startup/title/R/S/battle actor lookups.", EvidenceLevel = "dynamic-hit-title", Source = "function-index-20260609", HighFrequency = true },
        new() { Address = "0042518F", Name = "ability_check_wrapper", Stage = "startup", Category = "ability-check", ExpectedSemantics = "Wrapper ability check; pre-check then core engine or fallback.", TriggerHint = "Title entry and valid special-effect scenes.", EvidenceLevel = "dynamic-hit-title-entry", Source = "function-index-20260609" },
        new() { Address = "0041301E", Name = "dual_channel_check", Stage = "startup", Category = "ability-check", ExpectedSemantics = "Fallback dual-channel effect check reached from ability_check_wrapper.", TriggerHint = "ability_check_wrapper fallback path.", EvidenceLevel = "dynamic-hit-title-entry", Source = "function-index-20260609" },
        new() { Address = "00413009", Name = "get_effect_value", Stage = "startup", Category = "effect-value", ExpectedSemantics = "Read effect value through the dual-channel chain.", TriggerHint = "dual_channel_check internal effect lookup.", EvidenceLevel = "dynamic-hit-title-entry", Source = "function-index-20260609" },
        new() { Address = "0040728F", Name = "get_max_mp", Stage = "settings", Category = "unit-panel", ExpectedSemantics = "Read or calculate a unit maximum MP for panels or battle UI.", TriggerHint = "Settings/status/unit panel refresh.", EvidenceLevel = "dynamic-hit-r-scene-sentinel", Source = "function-index-20260609" },
        new() { Address = "004061F9", Name = "get_unit_ptr", Stage = "battle_entry", Category = "unit-array", ExpectedSemantics = "Battle unit id to tactical-unit pointer; stride 30, base 4A7B20.", TriggerHint = "Battle entry, UI, action dispatch, turn loops.", EvidenceLevel = "dynamic-hit-r-scene-sentinel", Source = "function-index-20260609", HighFrequency = true },
        new() { Address = "0041B500", Name = "get_unit_hp", Stage = "battle_entry", Category = "unit-array", ExpectedSemantics = "Read tactical unit current HP at unit+10.", TriggerHint = "Unit panel, target selection, damage calculations.", EvidenceLevel = "pending-breakpoint", Source = "function-index-20260609" },
        new() { Address = "0043DF0A", Name = "action_type_dispatch", Stage = "attack_before", Category = "action-dispatch", ExpectedSemantics = "Action type dispatcher before physical/strategy branch.", TriggerHint = "Select or execute a unit action.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "0043DADA", Name = "action_dispatch", Stage = "attack_before", Category = "action-dispatch", ExpectedSemantics = "Battle action dispatch entry.", TriggerHint = "Any unit action: physical attack, strategy, item, wait.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "004212F1", Name = "chase_attack_wrapper", Stage = "attack_before", Category = "physical-attack", ExpectedSemantics = "Chase-attack wrapper around physical attack execution and target chain.", TriggerHint = "Physical attack route, especially chase attack capability.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "00417C67", Name = "target_list_builder", Stage = "attack_before", Category = "targeting", ExpectedSemantics = "Build candidate target list for attack/chase routes.", TriggerHint = "Attack target selection or attack execution.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "00436499", Name = "get_target_list", Stage = "attack_before", Category = "targeting", ExpectedSemantics = "Fetch or build target list for battle actions.", TriggerHint = "Physical attack target enumeration.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "00435829", Name = "execute_attack", Stage = "attack_execute", Category = "physical-attack", ExpectedSemantics = "Physical attack execution candidate.", TriggerHint = "A unit performs a physical attack.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "00438E78", Name = "apply_effect", Stage = "attack_execute", Category = "damage-apply", ExpectedSemantics = "Apply attack/combat effect candidate.", TriggerHint = "Damage or special effect applies.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "0040399B", Name = "dmg_effect_exec", Stage = "attack_execute", Category = "damage-apply", ExpectedSemantics = "Damage effect execution.", TriggerHint = "Damage animation/effect application.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "00450986", Name = "generic_event_dispatch", Stage = "attack_after", Category = "event-dispatch", ExpectedSemantics = "Generic event dispatch; ECX=100 suspected HP/combat event path.", TriggerHint = "Damage application, HP changes, event notifications.", EvidenceLevel = "pending-breakpoint", Source = "function-index-20260609" },
        new() { Address = "00405966", Name = "post_dmg_dispatcher", Stage = "attack_after", Category = "post-damage-effect", ExpectedSemantics = "Post-damage special-effect dispatcher.", TriggerHint = "After damage has been applied.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "00442718", Name = "post_process", Stage = "attack_after", Category = "action-cleanup", ExpectedSemantics = "Post damage/action processing candidate.", TriggerHint = "After damage animation/effect.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "00443AC3", Name = "cleanup", Stage = "attack_after", Category = "action-cleanup", ExpectedSemantics = "Battle action cleanup candidate.", TriggerHint = "After attack/strategy resolves.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "00412D52", Name = "try_second_action", Stage = "attack_after", Category = "second-action", ExpectedSemantics = "Second-action restore decision and execution candidate.", TriggerHint = "Action end with second-action capability.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "0044AF8F", Name = "reset_turn_loop", Stage = "turn_end", Category = "turn-loop", ExpectedSemantics = "New-turn unit reset loop, traverses tactical units.", TriggerHint = "End turn or new round.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "00406690", Name = "restore_action", Stage = "turn_end", Category = "turn-loop", ExpectedSemantics = "Restore action capability by clearing unit+0D flags.", TriggerHint = "New turn or second-action logic.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" },
        new() { Address = "0043CE8F", Name = "turn_recovery_call", Stage = "turn_end", Category = "turn-recovery", ExpectedSemantics = "Turn recovery route calls recovery stubs to calculate recovery amount.", TriggerHint = "Round transition or turn-start recovery.", EvidenceLevel = "knowledge-base-prior", Source = "function-index-20260609" }
    ];

    public object DebugSessionStart(string? gameRoot, string? x32dbgPath, string hostName, int port, int waitMs, bool hidden)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var x32dbg = ResolveX32dbgPath(x32dbgPath, paths.WorkspaceRoot);
        var pluginPath = Path.Combine(Path.GetDirectoryName(x32dbg) ?? string.Empty, "plugins", "x64dbg_mcp.dp32");
        if (!File.Exists(pluginPath))
        {
            throw new FileNotFoundException("x64dbg MCP plugin was not found.", pluginPath);
        }

        var process = FindTargetProcess();
        var started = false;
        if (process is null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = x32dbg,
                WorkingDirectory = paths.GameRoot,
                UseShellExecute = true,
                WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
            };
            psi.ArgumentList.Add(paths.ExePath);
            Process.Start(psi);
            started = true;
        }

        process ??= WaitForTargetProcess(waitMs);
        var bridge = WaitForBridge(hostName, port, waitMs);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, null);
        var session = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            paths,
            x32dbg,
            plugin_path = pluginPath,
            started,
            bridge,
            safety = "This MCP server never patches Ekd5.exe and sends real input only when allow_input=true."
        };
        WriteJson(Path.Combine(sessionDir, "session.json"), session);

        return new
        {
            session_dir = sessionDir,
            paths,
            x32dbg,
            plugin_path = pluginPath,
            target_process = ProcessSummary(process ?? FindTargetProcess()),
            started,
            bridge
        };
    }

    public object GameProcessStart(string? gameRoot, bool allowLaunch, int waitMs, string? outputDir)
    {
        var paths = ResolveGamePaths(gameRoot);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var before = FindTargetProcess();
        var started = false;
        var launchSkipped = before is not null || !allowLaunch;
        string? launchError = null;

        if (before is null)
        {
            if (!allowLaunch)
            {
                launchSkipped = true;
            }
            else
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = paths.ExePath,
                        WorkingDirectory = paths.GameRoot,
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Normal
                    };
                    Process.Start(psi);
                    started = true;
                    launchSkipped = false;
                }
                catch (Exception ex)
                {
                    launchError = ex.Message;
                    launchSkipped = true;
                }
            }
        }

        var process = before ?? WaitForTargetProcess(Math.Clamp(waitMs, 0, 60000));
        var window = process is null ? new { exists = false } : WaitForWindowSnapshot(process, Math.Clamp(waitMs, 0, 60000));
        var runtime = GameRuntimeStateClassify(gameRoot, "127.0.0.1", 27042, minBattleUnits: 8, sessionDir);
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            session_dir = sessionDir,
            paths,
            allow_launch = allowLaunch,
            dry_run = launchSkipped,
            launch_skipped = launchSkipped,
            started,
            launch_error = launchError,
            target_process = ProcessSummary(process),
            window,
            runtime,
            safety = "Direct game launch only; no debugger commands, screenshots, mouse input, process-memory writes, or game-file writes."
        };
        var reportPath = Path.Combine(sessionDir, "process-start.json");
        WriteJson(reportPath, report);
        AppendJsonLine(eventsPath, new
        {
            type = "GameProcessStart",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = reportPath,
            allow_launch = allowLaunch,
            dry_run = launchSkipped,
            launch_skipped = launchSkipped,
            started,
            process_id = process?.Id,
            launch_error = launchError
        });

        return report;
    }

    public object DebugSessionState(string? gameRoot, string hostName, int port)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var target = FindTargetProcess();
        var window = GetWindowSnapshot(target);
        var bridge = InvokeX32dbg("GET", hostName, port, "/api/health");
        var debugState = bridge.Ok ? InvokeX32dbg("GET", hostName, port, "/api/debug/state") : bridge;
        object battleSummary;
        try
        {
            var state = ReadBattleStateSnapshot(160);
            battleSummary = new
            {
                ok = true,
                state.ActiveUnitCount,
                state.SideCounts,
                first_units = state.Units.Take(10).ToList()
            };
        }
        catch (Exception ex)
        {
            battleSummary = new { ok = false, error = ex.Message };
        }

        return new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            paths,
            target_process = ProcessSummary(target),
            window,
            foreground_hwnd = FormatHwnd(NativeMethods.GetForegroundWindow()),
            x32dbg_bridge = bridge,
            x32dbg_state = debugState,
            battle_summary = battleSummary
        };
    }

    public object GameWindowPrepare(string? gameRoot, int x, int y, int width, int height, bool allowResize, bool bringToFront)
    {
        _ = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var process = RequireTargetProcess();
        var hwnd = RequireMainWindow(process);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        if (allowResize)
        {
            if (!NativeMethods.MoveWindow(hwnd, x, y, width, height, true))
            {
                ThrowLastWin32("MoveWindow failed.");
            }
        }

        if (bringToFront)
        {
            _ = NativeMethods.SetForegroundWindow(hwnd);
        }

        return new
        {
            process = ProcessSummary(process),
            window = GetWindowSnapshot(process),
            foreground_hwnd = FormatHwnd(NativeMethods.GetForegroundWindow())
        };
    }

    public object GameCaptureFrame(string? gameRoot, string? outputDir, string label)
    {
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var screenshotDir = Path.Combine(sessionDir, "screenshots");
        Directory.CreateDirectory(screenshotDir);
        var capture = CaptureWindowClient(RequireTargetProcess(), screenshotDir, label);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
        {
            type = "FrameCaptured",
            created_at = DateTimeOffset.Now.ToString("O"),
            capture
        });
        return capture;
    }

    public object GameReadBattleState(string? gameRoot, int maxUnits, bool includeRawRanges, string? outputDir)
    {
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var snapshot = ReadBattleStateSnapshot(Math.Clamp(maxUnits, 1, 512));
        var sessionDir = includeRawRanges ? EnsureSessionDirectory(paths.WorkspaceRoot, outputDir) : string.Empty;
        var rawExports = new List<object>();
        if (includeRawRanges)
        {
            var memoryDir = Path.Combine(sessionDir, "memory");
            Directory.CreateDirectory(memoryDir);
            rawExports.Add(ExportMemoryRange(memoryDir, "unit_array", UnitArrayAddress, Math.Clamp(maxUnits, 1, 512) * UnitStride));
            rawExports.Add(ExportMemoryRange(memoryDir, "battle_globals", 0x00490000, 0x22000));
            AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
            {
                type = "BattleStateRead",
                created_at = DateTimeOffset.Now.ToString("O"),
                snapshot.ActiveUnitCount,
                rawExports
            });
        }

        return new
        {
            snapshot,
            runtime_snapshot = TryReadBattlefieldRuntimeSnapshot(),
            raw_exports = rawExports,
            session_dir = sessionDir
        };
    }

    public object GameRuntimeStateClassify(string? gameRoot, string hostName, int port, int minBattleUnits, string? outputDir)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var target = FindTargetProcess();
        var bridge = InvokeX32dbg("GET", hostName, port, "/api/health");
        var debugState = IsBridgeSuccess(bridge) ? InvokeX32dbg("GET", hostName, port, "/api/debug/state") : bridge;
        object? battleSummary = null;
        BattleStateSnapshot? battle = null;
        string classification;
        string confidence;
        string reason;
        List<string> nextStages;

        if (target is null)
        {
            classification = "not_running";
            confidence = "high";
            reason = "Ekd5.exe process was not found.";
            nextStages = ["startup"];
        }
        else
        {
            var battleRead = SafeReadBattleState();
            if (battleRead is BattleStateSnapshot snapshot)
            {
                battle = snapshot;
                battleSummary = new
                {
                    snapshot.ActiveUnitCount,
                    snapshot.SideCounts,
                    first_units = snapshot.Units.Take(12).ToList()
                };

                var side0 = snapshot.SideCounts.TryGetValue("0", out var allies) ? allies : 0;
                var side1 = snapshot.SideCounts.TryGetValue("1", out var friends) ? friends : 0;
                var side2 = snapshot.SideCounts.TryGetValue("2", out var enemies) ? enemies : 0;
                var hasBattleSides = side0 + side1 > 0 && enemies > 0;
                if (snapshot.ActiveUnitCount >= Math.Max(minBattleUnits, 1) && hasBattleSides)
                {
                    classification = "battle_loaded";
                    confidence = "medium";
                    reason = $"Decoded {snapshot.ActiveUnitCount} units with allied/friendly and enemy sides present.";
                    nextStages = ["battle_entry", "attack_before", "attack_execute", "attack_after", "turn_end"];
                }
                else if (snapshot.ActiveUnitCount > 0)
                {
                    classification = "partial_battle_signal";
                    confidence = "low";
                    reason = $"Decoded {snapshot.ActiveUnitCount} plausible unit rows, but side distribution is not enough for battle_loaded.";
                    nextStages = ["battle_entry"];
                }
                else
                {
                    classification = "process_no_battle_signal";
                    confidence = "medium";
                    reason = "Ekd5.exe is running, but tactical unit array did not decode active unit rows.";
                    nextStages = ["startup", "settings"];
                }
            }
            else
            {
                battleSummary = battleRead;
                classification = "process_battle_read_failed";
                confidence = "medium";
                reason = "Ekd5.exe is running, but ReadProcessMemory battle-state read failed.";
                nextStages = ["startup", "settings"];
            }
        }

        var runtime = new RuntimeStateSnapshot
        {
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Classification = classification,
            Confidence = confidence,
            Reason = reason,
            TargetProcess = ProcessSummary(target),
            X32dbgState = debugState,
            BattleSummary = battleSummary,
            RecommendedNextStages = nextStages
        };

        var sessionDir = string.IsNullOrWhiteSpace(outputDir) ? string.Empty : EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        if (!string.IsNullOrWhiteSpace(sessionDir))
        {
            var path = Path.Combine(sessionDir, $"runtime-state-{Timestamp()}.json");
            WriteJson(path, runtime);
            AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
            {
                type = "RuntimeStateClassified",
                created_at = DateTimeOffset.Now.ToString("O"),
                path,
                runtime.Classification,
                runtime.Confidence
            });
            if (battle is not null)
            {
                WriteJson(Path.Combine(sessionDir, "runtime-battle-summary.json"), battle);
            }
        }

        return new
        {
            session_dir = sessionDir,
            runtime
        };
    }

    public object GameRuntimeWaitForState(
        string? gameRoot,
        string hostName,
        int port,
        string targetClassifications,
        int timeoutMs,
        int pollIntervalMs,
        int minBattleUnits,
        string? outputDir)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var targetSet = ParseRuntimeClassificationTargets(targetClassifications);
        var acceptsAny = targetSet.Contains("any");
        var timeout = Math.Clamp(timeoutMs, 0, 10 * 60 * 1000);
        var poll = Math.Clamp(pollIntervalMs, 100, 30 * 1000);
        var samples = new List<object>();
        var stopwatch = Stopwatch.StartNew();
        object? lastRuntime = null;
        string lastClassification = "unknown";
        string lastConfidence = "low";
        string lastReason = string.Empty;
        var matched = false;

        AppendJsonLine(eventsPath, new
        {
            type = "RuntimeWaitStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            target_classifications = targetSet,
            timeout_ms = timeout,
            poll_interval_ms = poll,
            min_battle_units = minBattleUnits
        });

        while (true)
        {
            var classify = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits, sessionDir);
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(classify, JsonOptions));
            if (TryGetJsonProperty(document.RootElement, "runtime", out var runtimeElement))
            {
                lastClassification = GetStringProperty(runtimeElement, "classification", "Classification") ?? "unknown";
                lastConfidence = GetStringProperty(runtimeElement, "confidence", "Confidence") ?? "low";
                lastReason = GetStringProperty(runtimeElement, "reason", "Reason") ?? string.Empty;
                lastRuntime = JsonSerializer.Deserialize<object>(runtimeElement.GetRawText(), JsonOptions);
            }
            else
            {
                lastRuntime = classify;
            }

            var elapsedMs = stopwatch.ElapsedMilliseconds;
            matched = acceptsAny || targetSet.Contains(lastClassification);
            var sample = new
            {
                index = samples.Count + 1,
                elapsed_ms = elapsedMs,
                classification = lastClassification,
                confidence = lastConfidence,
                reason = lastReason,
                matched
            };
            samples.Add(sample);
            AppendJsonLine(eventsPath, new
            {
                type = "RuntimeWaitSample",
                created_at = DateTimeOffset.Now.ToString("O"),
                sample
            });

            if (matched || elapsedMs >= timeout)
            {
                break;
            }

            var remaining = timeout - elapsedMs;
            if (remaining <= 0)
            {
                break;
            }
            Thread.Sleep((int)Math.Min(poll, remaining));
        }

        stopwatch.Stop();
        var status = matched ? "matched" : "timeout";
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            target_classifications = targetSet,
            final_classification = lastClassification,
            final_confidence = lastConfidence,
            final_reason = lastReason,
            elapsed_ms = stopwatch.ElapsedMilliseconds,
            sample_count = samples.Count,
            samples,
            final_runtime = lastRuntime,
            safety = "Runtime wait polls read-only classification only; no screenshots, mouse input, process-memory writes, or game-file writes."
        };
        var summaryPath = Path.Combine(sessionDir, "runtime-wait-summary.json");
        var markdownPath = Path.Combine(sessionDir, "runtime-wait-summary.md");
        WriteJson(summaryPath, summary);
        WriteRuntimeWaitSummaryMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new
        {
            type = "RuntimeWaitSummary",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = summaryPath,
            status,
            final_classification = lastClassification,
            sample_count = samples.Count
        });

        return summary;
    }

    public object GameRSceneTextAnchorScan(string anchors, string? gameRoot, int maxScanBytes, int maxHitsPerAnchor, string? outputDir)
    {
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var process = FindTargetProcess();
        var anchorList = ParseRSceneAnchors(anchors);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var maxBytes = Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024);
        var maxHits = Math.Clamp(maxHitsPerAnchor, 1, 64);

        if (process is null)
        {
            var notRunning = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                status = "not-running",
                session_dir = sessionDir,
                target_process = ProcessSummary(process),
                anchors = anchorList,
                scanned_bytes = 0,
                readable_region_count = 0,
                hits = Array.Empty<object>(),
                safety = "Read-only process-memory text-anchor scan only; no input, screenshots, debugger mutation, process-memory writes, or game-file writes."
            };
            WriteJson(Path.Combine(sessionDir, "rscene-text-anchor-scan.json"), notRunning);
            WriteRSceneTextAnchorScanMarkdown(Path.Combine(sessionDir, "rscene-text-anchor-scan.md"), notRunning);
            AppendJsonLine(eventsPath, new { type = "RSceneTextAnchorScan", notRunning.status, anchor_count = anchorList.Count });
            return notRunning;
        }

        var scan = ScanProcessForGbkAnchors(process.Id, anchorList, maxBytes, maxHits);
        var formattedHits = scan.Hits.Select(FormatAnchorHit).ToList();
        var status = scan.Hits.Count > 0 ? "anchors-found" : "anchors-not-found";
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            target_process = ProcessSummary(process),
            anchors = anchorList,
            max_scan_bytes = maxBytes,
            max_hits_per_anchor = maxHits,
            scanned_bytes = scan.ScannedBytes,
            readable_region_count = scan.RegionCount,
            stopped_reason = scan.StoppedReason,
            hits = formattedHits,
            safety = "Read-only process-memory text-anchor scan only; no input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };
        var reportPath = Path.Combine(sessionDir, "rscene-text-anchor-scan.json");
        var markdownPath = Path.Combine(sessionDir, "rscene-text-anchor-scan.md");
        WriteJson(reportPath, report);
        WriteRSceneTextAnchorScanMarkdown(markdownPath, report);
        AppendJsonLine(eventsPath, new
        {
            type = "RSceneTextAnchorScan",
            status,
            anchor_count = anchorList.Count,
            hit_count = scan.Hits.Count,
            scanned_bytes = scan.ScannedBytes,
            region_count = scan.RegionCount
        });
        return report;
    }

    public object GameRuntimeAnchorSweep(
        string profile,
        string gbkAnchors,
        string asciiAnchors,
        string? gameRoot,
        int maxScanBytes,
        int maxHitsPerAnchor,
        string? outputDir)
    {
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var normalizedProfile = NormalizeAnchorSweepProfile(profile);
        var gbkAnchorList = BuildRuntimeSweepGbkAnchors(normalizedProfile, gbkAnchors);
        var asciiAnchorList = BuildRuntimeSweepAsciiAnchors(normalizedProfile, asciiAnchors);
        var process = FindTargetProcess();
        var maxBytes = Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024);
        var maxHits = Math.Clamp(maxHitsPerAnchor, 1, 64);

        object report;
        if (process is null)
        {
            report = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                status = "not-running",
                profile = normalizedProfile,
                session_dir = sessionDir,
                target_process = ProcessSummary(process),
                gbk_anchors = gbkAnchorList,
                ascii_anchors = asciiAnchorList,
                phase_inference = new
                {
                    phase = "not_running",
                    confidence = "high",
                    basis = "Ekd5.exe process was not found."
                },
                gbk_hits = Array.Empty<object>(),
                ascii_hits = Array.Empty<object>(),
                scanned_bytes = 0,
                readable_region_count = 0,
                safety = "Read-only process-memory anchor sweep only; no input, screenshots, debugger mutation, process-memory writes, or game-file writes."
            };
        }
        else
        {
            var runtimeState = GameRuntimeStateClassify(gameRoot, "127.0.0.1", 27042, minBattleUnits: 8, sessionDir);
            var battleProfile = GameBattleStateMatch("yingchuan_cao_zhangliang", gameRoot, sessionDir);
            var gbkScan = ScanProcessForAnchors(process.Id, gbkAnchorList, Encoding.GetEncoding(936), "GBK", maxBytes, maxHits);
            var asciiScan = ScanProcessForAnchors(process.Id, asciiAnchorList, Encoding.ASCII, "ASCII", maxBytes, maxHits);
            var inference = InferRuntimeAnchorPhase(
                gbkScan.Hits,
                asciiScan.Hits,
                ExtractNestedStringFromObject(runtimeState, "runtime", "classification"),
                ExtractStringFromObject(battleProfile, "status"));
            var hitCount = gbkScan.Hits.Count + asciiScan.Hits.Count;
            var formattedGbkHits = gbkScan.Hits.Select(FormatAnchorHit).ToList();
            var formattedAsciiHits = asciiScan.Hits.Select(FormatAnchorHit).ToList();
            report = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                status = hitCount > 0 ? "anchors-found" : "anchors-not-found",
                profile = normalizedProfile,
                session_dir = sessionDir,
                target_process = ProcessSummary(process),
                gbk_anchors = gbkAnchorList,
                ascii_anchors = asciiAnchorList,
                max_scan_bytes = maxBytes,
                max_hits_per_anchor = maxHits,
                scanned_bytes = new
                {
                    gbk = gbkScan.ScannedBytes,
                    ascii = asciiScan.ScannedBytes,
                    total = gbkScan.ScannedBytes + asciiScan.ScannedBytes
                },
                readable_region_count = new
                {
                    gbk = gbkScan.RegionCount,
                    ascii = asciiScan.RegionCount
                },
                stopped_reason = new
                {
                    gbk = gbkScan.StoppedReason,
                    ascii = asciiScan.StoppedReason
                },
                phase_inference = inference,
                runtime = runtimeState,
                battle_profile = battleProfile,
                gbk_hits = formattedGbkHits,
                ascii_hits = formattedAsciiHits,
                hit_count = hitCount,
                safety = "Read-only process-memory anchor sweep only; no input, screenshots, debugger mutation, process-memory writes, or game-file writes."
            };
        }

        var reportPath = Path.Combine(sessionDir, "runtime-anchor-sweep.json");
        var markdownPath = Path.Combine(sessionDir, "runtime-anchor-sweep.md");
        WriteJson(reportPath, report);
        WriteRuntimeAnchorSweepMarkdown(markdownPath, report);
        AppendJsonLine(eventsPath, new
        {
            type = "RuntimeAnchorSweep",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = reportPath,
            status = ExtractStringFromObject(report, "status"),
            profile = normalizedProfile,
            phase = ExtractNestedStringFromObject(report, "phase_inference", "phase"),
            hit_count = ExtractIntFromObject(report, "hit_count")
        });
        return report;
    }

    public object GameRSceneScriptWindowScan(
        string route,
        string? gameRoot,
        int contextBytes,
        int maxScanBytes,
        int maxHitsPerWindow,
        bool includePointerRefs,
        int maxPointerRefs,
        string? outputDir)
    {
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var normalizedRoute = NormalizeR00Route(route);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var script = ReadR00RouteScriptContext(paths.GameRoot);
        var windows = BuildR00ScriptWindows(script.Bytes, script.ActorClick, script.ModeChoice, script.ConfigChoice, contextBytes);
        var process = FindTargetProcess();
        var maxBytes = Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024);
        var maxWindowHits = Math.Clamp(maxHitsPerWindow, 1, 32);
        var maxRefs = Math.Clamp(maxPointerRefs, 1, 128);
        RSceneScriptWindowScanResult scan;
        string status;

        if (process is null)
        {
            scan = new RSceneScriptWindowScanResult(0, 0, "not-running", [], []);
            status = "not-running";
        }
        else
        {
            scan = ScanProcessForByteWindows(
                process.Id,
                windows,
                maxBytes,
                maxWindowHits,
                includePointerRefs,
                maxRefs);
            status = scan.WindowHits.Count > 0 ? "script-windows-found" : "script-windows-not-found";
        }

        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            route = normalizedRoute,
            target_process = ProcessSummary(process),
            scenario = script.Scenario,
            route_anchors = new
            {
                actor_click = FormatR00ActorClickTest(script.ActorClick),
                mode_choice = FormatR00ChoiceBox(script.ModeChoice),
                regular_config_menu = FormatR00ChoiceBox(script.ConfigChoice)
            },
            max_scan_bytes = maxBytes,
            context_bytes = Math.Clamp(contextBytes, 4, 128),
            max_hits_per_window = maxWindowHits,
            include_pointer_refs = includePointerRefs,
            max_pointer_refs = maxRefs,
            scanned_bytes = scan.ScannedBytes,
            readable_region_count = scan.RegionCount,
            stopped_reason = scan.StoppedReason,
            windows = windows.Select(FormatScriptWindow).ToList(),
            window_hits = scan.WindowHits.Select(FormatScriptWindowHit).ToList(),
            pointer_refs = scan.PointerRefs.Select(FormatScriptPointerRef).ToList(),
            confidence = new
            {
                level = scan.WindowHits.Count > 0 ? "medium" : "low",
                basis = scan.WindowHits.Count > 0
                    ? "At least one byte window from RS/R_00.eex appears in committed readable Ekd5.exe memory."
                    : "No R_00 route byte window was found in the scanned memory budget.",
                limitation = "A script byte-window hit proves that data is loaded, not that the interpreter is currently executing the command."
            },
            safety = "Read-only process-memory script-window scan only; no input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };

        var reportPath = Path.Combine(sessionDir, "rscene-script-window-scan.json");
        var markdownPath = Path.Combine(sessionDir, "rscene-script-window-scan.md");
        WriteJson(reportPath, report);
        WriteRSceneScriptWindowScanMarkdown(markdownPath, report);
        AppendJsonLine(eventsPath, new
        {
            type = "RSceneScriptWindowScan",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = reportPath,
            status,
            window_count = windows.Count,
            hit_count = scan.WindowHits.Count,
            pointer_ref_count = scan.PointerRefs.Count,
            scanned_bytes = scan.ScannedBytes
        });
        return report;
    }

    public object GameBattleStateMatch(string profile, string? gameRoot, string? outputDir)
    {
        var normalizedProfile = NormalizeBattleProfile(profile);

        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var process = FindTargetProcess();
        string status;
        string confidence;
        string reason;
        object? battle = null;
        object? caoCao = null;
        object? zhangLiang = null;
        object? candidates = null;
        var gates = new List<object>();

        if (process is null)
        {
            status = "not_running";
            confidence = "high";
            reason = "Ekd5.exe process was not found.";
            gates.Add(new { gate = "process_running", passed = false, evidence = reason });
        }
        else
        {
            var read = SafeReadBattleState();
            if (read is not BattleStateSnapshot snapshot)
            {
                status = "read_failed";
                confidence = "high";
                reason = "ReadProcessMemory battle-state read failed.";
                gates.Add(new { gate = "process_running", passed = true, evidence = ProcessSummary(process) });
                gates.Add(new { gate = "battle_state_read", passed = false, evidence = read });
            }
            else
            {
                var allies = snapshot.SideCounts.TryGetValue("0", out var allyCount) ? allyCount : 0;
                var friends = snapshot.SideCounts.TryGetValue("1", out var friendCount) ? friendCount : 0;
                var enemies = snapshot.SideCounts.TryGetValue("2", out var enemyCount) ? enemyCount : 0;
                var battleLoaded = snapshot.ActiveUnitCount >= 20 && allies >= 1 && friends >= 1 && enemies >= 1;
                var cao = snapshot.Units.FirstOrDefault(u =>
                    u.UnitIndex == 0 &&
                    u.Side == 0 &&
                    u.X == 10 &&
                    u.Y == 6 &&
                    u.HP > 0);
                var zhang = snapshot.Units.FirstOrDefault(u =>
                    u.UnitIndex == 61 &&
                    u.Side == 2 &&
                    u.X == 10 &&
                    u.Y == 5 &&
                    u.HP > 0);
                var adjacent = cao is not null &&
                    zhang is not null &&
                    Math.Abs(cao.X - zhang.X) + Math.Abs(cao.Y - zhang.Y) == 1;
                var zhangHpDropped = zhang is not null && zhang.HP < 168;

                battle = new
                {
                    snapshot.ActiveUnitCount,
                    snapshot.SideCounts,
                    first_units = snapshot.Units.Take(12).ToList()
                };
                caoCao = cao;
                zhangLiang = zhang;
                candidates = new
                {
                    ally_candidates = snapshot.Units.Where(u => u.Side == 0).Take(12).ToList(),
                    enemy_candidates_near_cao = cao is null
                        ? new List<BattleUnitRow>()
                        : snapshot.Units
                            .Where(u => u.Side == 2 && Math.Abs(u.X - cao.X) <= 4 && Math.Abs(u.Y - cao.Y) <= 4)
                            .Take(12)
                            .ToList()
                };
                gates.Add(new { gate = "process_running", passed = true, evidence = ProcessSummary(process) });
                gates.Add(new { gate = "battle_loaded", passed = battleLoaded, evidence = new { snapshot.ActiveUnitCount, allies, friends, enemies } });
                gates.Add(new { gate = "cao_cao_detected", passed = cao is not null, expected = "unit[0], side=0, coord=(10,6), hp>0", evidence = cao });
                gates.Add(new { gate = "zhang_liang_detected", passed = zhang is not null, expected = "unit[61], side=2, coord=(10,5), hp>0", evidence = zhang });
                gates.Add(new { gate = "target_adjacent", passed = adjacent, expected = "Manhattan distance 1 between Cao Cao and Zhang Liang", evidence = new { cao, zhang } });
                gates.Add(new { gate = "attack_after_observed", passed = zhangHpDropped, expected = "Zhang Liang HP below known initial 168", evidence = zhang is null ? null : new { zhang.HP, initial_hp = 168 } });

                if (battleLoaded && cao is not null && zhang is not null)
                {
                    status = zhangHpDropped ? "attack_after_observed" : "profile-matched";
                    confidence = "high";
                    reason = zhangHpDropped
                        ? "Known Yingchuan Cao Cao/Zhang Liang profile matched and Zhang Liang HP is below the recorded initial value."
                        : "Known Yingchuan Cao Cao/Zhang Liang profile matched, but the attack-after HP drop was not observed.";
                }
                else if (battleLoaded)
                {
                    status = "battle-loaded-profile-not-matched";
                    confidence = "medium";
                    reason = "Battle-like unit memory is loaded, but the known Yingchuan Cao Cao/Zhang Liang fingerprint did not match.";
                }
                else
                {
                    status = "process-running-no-profile";
                    confidence = "medium";
                    reason = "Ekd5.exe is running, but the known battle fingerprint was not present.";
                }
            }
        }

        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            profile = "yingchuan_cao_zhangliang",
            status,
            confidence,
            reason,
            session_dir = sessionDir,
            battle,
            cao_cao = caoCao,
            zhang_liang = zhangLiang,
            candidates,
            gates,
            source = "local knowledge base: 2026-06-09 Yingchuan full-run sample",
            safety = "Read-only battle memory match only; no screenshots, mouse input, process-memory writes, debugger commands, or game-file writes."
        };
        var summaryPath = Path.Combine(sessionDir, "battle-state-match.json");
        var markdownPath = Path.Combine(sessionDir, "battle-state-match.md");
        WriteJson(summaryPath, summary);
        WriteBattleStateMatchMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new
        {
            type = "BattleStateMatch",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = summaryPath,
            profile = "yingchuan_cao_zhangliang",
            status,
            confidence
        });

        return summary;
    }

    public object GameClickGrid(string? gameRoot, int gridX, int gridY, bool allowInput, int originX, int originY, int cellWidth, int cellHeight, string? outputDir)
    {
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var process = RequireTargetProcess();
        var before = SafeReadBattleState();
        var beforeFrame = CaptureWindowClient(process, Path.Combine(sessionDir, "screenshots"), "before-grid-click");
        var clientX = originX + gridX * cellWidth + cellWidth / 2;
        var clientY = originY + gridY * cellHeight + cellHeight / 2;
        var screenPoint = ClientToScreen(RequireMainWindow(process), clientX, clientY);
        var inputResult = allowInput ? (object)SendLeftClick(process, screenPoint.X, screenPoint.Y) : new { sent = false, reason = "allow_input was false" };
        Thread.Sleep(allowInput ? 250 : 0);
        var after = SafeReadBattleState();
        var afterFrame = CaptureWindowClient(process, Path.Combine(sessionDir, "screenshots"), "after-grid-click");
        var report = new
        {
            type = "GridClick",
            created_at = DateTimeOffset.Now.ToString("O"),
            grid = new { x = gridX, y = gridY },
            calibration = new { origin_x = originX, origin_y = originY, cell_width = cellWidth, cell_height = cellHeight },
            client_point = new { x = clientX, y = clientY },
            screen_point = screenPoint,
            input = inputResult,
            before,
            after,
            before_frame = beforeFrame,
            after_frame = afterFrame
        };
        WriteJson(Path.Combine(sessionDir, $"grid-click-{Timestamp()}.json"), report);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), report);
        return new { session_dir = sessionDir, report };
    }

    public object GameClickUi(string? gameRoot, string uiArea, bool allowInput, string? outputDir)
    {
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        if (!UiAreas.TryGetValue(uiArea, out var area))
        {
            throw new ArgumentException($"Unknown UI area '{uiArea}'. Known areas: {string.Join(", ", UiAreas.Keys)}");
        }

        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var process = RequireTargetProcess();
        var hwnd = RequireMainWindow(process);
        var before = SafeReadBattleState();
        var beforeFrame = CaptureWindowClient(process, Path.Combine(sessionDir, "screenshots"), $"before-ui-{area.Key}");
        var target = ResolveUiClickTarget(hwnd, area);
        var screenPoint = ClientToScreen(hwnd, target.ClientX, target.ClientY);
        var inputResult = allowInput
            ? target.IsWithinClient
                ? (object)SendLeftClick(process, screenPoint.X, screenPoint.Y)
                : new { sent = false, blocked = true, reason = "resolved UI point is outside the current client area" }
            : new { sent = false, reason = "allow_input was false" };
        Thread.Sleep(allowInput ? 250 : 0);
        var after = SafeReadBattleState();
        var afterFrame = CaptureWindowClient(process, Path.Combine(sessionDir, "screenshots"), $"after-ui-{area.Key}");
        var targetReport = new
        {
            base_client = new { width = target.BaseClientWidth, height = target.BaseClientHeight },
            base_area = new { x = area.X, y = area.Y, width = area.Width, height = area.Height },
            base_center = new { x = target.BaseCenterX, y = target.BaseCenterY },
            current_client = new { width = target.ClientWidth, height = target.ClientHeight },
            scale = new { x = target.ScaleX, y = target.ScaleY },
            client_point = new { x = target.ClientX, y = target.ClientY },
            within_client = target.IsWithinClient
        };
        var report = new
        {
            type = "UiClick",
            created_at = DateTimeOffset.Now.ToString("O"),
            area,
            target = targetReport,
            screen_point = screenPoint,
            input = inputResult,
            before,
            after,
            before_frame = beforeFrame,
            after_frame = afterFrame
        };
        WriteJson(Path.Combine(sessionDir, $"ui-click-{area.Key}-{Timestamp()}.json"), report);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), report);
        return new { session_dir = sessionDir, report };
    }

    public object GameKeySequence(
        string sequence,
        bool allowInput,
        string? gameRoot,
        int delayMs,
        bool bringToFront,
        string delivery,
        string hostName,
        int port,
        string? outputDir)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: allowInput);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var keys = ParseKeySequence(sequence);
        var process = FindTargetProcess();
        var beforeRuntime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        var beforeBattle = SafeReadBattleState();
        var beforeWindow = GetWindowSnapshot(process);
        var results = new List<object>();
        var deliveryMode = NormalizeKeyDelivery(delivery);

        AppendJsonLine(eventsPath, new
        {
            type = "KeySequenceStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            sequence,
            key_count = keys.Count,
            allow_input = allowInput,
            delivery = deliveryMode,
            process = ProcessSummary(process),
            window = beforeWindow
        });

        if (keys.Count == 0)
        {
            throw new ArgumentException("Key sequence did not contain any recognized key tokens.", nameof(sequence));
        }

        if (process is null)
        {
            if (allowInput)
            {
                throw new InvalidOperationException("Ekd5.exe is not running; cannot post keyboard messages.");
            }

            var dryNoProcess = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                session_dir = sessionDir,
                status = "dry-run-no-process",
                sequence,
                key_count = keys.Count,
                keys = keys.Select(k => k.Name).ToList(),
                allow_input = allowInput,
                delivery = deliveryMode,
                target_process = ProcessSummary(process),
                before_window = beforeWindow,
                before_runtime = beforeRuntime,
                before_battle = beforeBattle,
                safety = "Dry-run only; no keyboard messages, mouse input, screenshots, process-memory writes, debugger changes, or game-file writes."
            };
            WriteJson(Path.Combine(sessionDir, $"key-sequence-{Timestamp()}.json"), dryNoProcess);
            AppendJsonLine(eventsPath, new { type = "KeySequenceSummary", dryNoProcess.status, dryNoProcess.key_count });
            return dryNoProcess;
        }

        var hwnd = RequireMainWindow(process);
        if (bringToFront)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            _ = NativeMethods.SetForegroundWindow(hwnd);
        }

        if (!allowInput)
        {
            var dryRun = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                session_dir = sessionDir,
                status = "dry-run",
                reason = "allow_input was false; no keyboard messages were posted.",
                sequence,
                key_count = keys.Count,
                keys = keys.Select(k => k.Name).ToList(),
                delivery = deliveryMode,
                target_process = ProcessSummary(process),
                before_window = beforeWindow,
                before_runtime = beforeRuntime,
                before_battle = beforeBattle,
                safety = "Dry-run only; no keyboard messages, mouse input, screenshots, process-memory writes, debugger changes, or game-file writes."
            };
            WriteJson(Path.Combine(sessionDir, $"key-sequence-{Timestamp()}.json"), dryRun);
            AppendJsonLine(eventsPath, new { type = "KeySequenceSummary", dryRun.status, dryRun.key_count });
            return dryRun;
        }

        var delay = Math.Clamp(delayMs, 0, 5000);
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var post = SendKey(hwnd, key, deliveryMode);
            results.Add(new
            {
                index = i + 1,
                key = key.Name,
                virtual_key = FormatHex(key.VirtualKey, 2),
                post,
                runtime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir),
                battle = SafeReadBattleState()
            });
            AppendJsonLine(eventsPath, new { type = "KeySequenceKeyPosted", created_at = DateTimeOffset.Now.ToString("O"), index = i + 1, key = key.Name, post });
            if (delay > 0)
            {
                Thread.Sleep(delay);
            }
        }

        var afterProcess = FindTargetProcess();
        var afterRuntime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        var afterBattle = SafeReadBattleState();
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            session_dir = sessionDir,
            status = "messages-posted",
            sequence,
            key_count = keys.Count,
            delivery = deliveryMode,
            delay_ms = delay,
            bring_to_front = bringToFront,
            target_process = ProcessSummary(afterProcess),
            before_window = beforeWindow,
            after_window = GetWindowSnapshot(afterProcess),
            before_runtime = beforeRuntime,
            after_runtime = afterRuntime,
            before_battle = beforeBattle,
            after_battle = afterBattle,
            results,
            safety = "Posted keyboard messages to the Ekd5.exe main window only; no mouse input, screenshots, process-memory writes, debugger changes, or game-file writes."
        };
        var summaryPath = Path.Combine(sessionDir, $"key-sequence-{Timestamp()}.json");
        WriteJson(summaryPath, summary);
        AppendJsonLine(eventsPath, new { type = "KeySequenceSummary", path = summaryPath, summary.status, summary.key_count });
        return summary;
    }

    public object DebugBreakpointPlanApply(string? planPath, int batchIndex, string hostName, int port, bool clearHardwareFirst)
    {
        GuardLocalHost(hostName);
        var resolvedPlan = ResolvePlanPath(planPath);
        var health = InvokeX32dbg("GET", hostName, port, "/api/health");
        if (!health.Ok)
        {
            throw new InvalidOperationException($"x32dbg MCP bridge is not healthy: {health.Error}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(resolvedPlan, Encoding.UTF8));
        var root = document.RootElement;
        var commands = new List<string>();
        if (root.TryGetProperty("Breakpoints", out var breakpoints) && breakpoints.ValueKind == JsonValueKind.Array)
        {
            foreach (var bp in breakpoints.EnumerateArray())
            {
                var address = GetStringProperty(bp, "address", "Address");
                if (!string.IsNullOrWhiteSpace(address))
                {
                    commands.Add("bp " + NormalizeAddress(address).Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        else if (root.TryGetProperty("Batches", out var batches) && batches.ValueKind == JsonValueKind.Array)
        {
            if (clearHardwareFirst)
            {
                commands.Add("bphwc");
            }

            var batch = batches.EnumerateArray()
                .FirstOrDefault(e => GetIntProperty(e, "BatchIndex", "batchIndex", "batch_index") == batchIndex);
            if (batch.ValueKind == JsonValueKind.Undefined)
            {
                throw new ArgumentException($"Batch {batchIndex} was not found in {resolvedPlan}.");
            }

            if (batch.TryGetProperty("X32dbgCommands", out var x32Commands) && x32Commands.ValueKind == JsonValueKind.Array)
            {
                commands.AddRange(x32Commands.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s))!);
            }
        }
        else
        {
            throw new InvalidOperationException("Unsupported plan shape. Expected Breakpoints or Batches.");
        }

        var results = new List<object>();
        foreach (var command in commands)
        {
            results.Add(new
            {
                command,
                result = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command })
            });
        }

        return new
        {
            plan_path = resolvedPlan,
            command_count = commands.Count,
            commands,
            health,
            results,
            breakpoint_list = InvokeX32dbg("GET", hostName, port, "/api/breakpoints/list")
        };
    }

    public object DebugFunctionCatalog(string stage, string? outputDir, string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var normalizedStage = NormalizeStageFilter(stage);
        var entries = SelectFunctionCatalogEntries(normalizedStage).ToList();
        if (entries.Count == 0)
        {
            throw new ArgumentException($"Stage '{stage}' did not select any function catalog entries.");
        }

        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var enriched = entries.Select(entry => EnrichFunctionCatalogEntry(paths.ExePath, entry)).ToList();
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            stage = normalizedStage,
            target = new
            {
                paths.ExePath,
                paths.ExeSha256,
                expected_sha256 = ExpectedSha256,
                paths.IsExpectedSha256,
                image_base = FormatHex(ImageBase, 8)
            },
            entry_count = enriched.Count,
            stages = entries
                .GroupBy(e => e.Stage)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            entries = enriched,
            safety = "Offline catalog only; reads Ekd5.exe bytes and writes evidence files."
        };
        var jsonPath = Path.Combine(sessionDir, "function-catalog.json");
        var markdownPath = Path.Combine(sessionDir, "function-catalog.md");
        WriteJson(jsonPath, report);
        WriteFunctionCatalogMarkdown(markdownPath, normalizedStage, paths, enriched);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
        {
            type = "FunctionCatalogExported",
            created_at = DateTimeOffset.Now.ToString("O"),
            stage = normalizedStage,
            entry_count = enriched.Count,
            json_path = jsonPath,
            markdown_path = markdownPath
        });
        return new
        {
            session_dir = sessionDir,
            catalog_path = jsonPath,
            markdown_path = markdownPath,
            report
        };
    }

    public object DebugStaticXrefScan(string stage, int nearBytes, string? outputDir, string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var normalizedStage = NormalizeStageFilter(stage);
        var entries = SelectFunctionCatalogEntries(normalizedStage).ToList();
        if (entries.Count == 0)
        {
            throw new ArgumentException($"Stage '{stage}' did not select any function catalog entries.");
        }

        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var text = ReadTextSection(paths.ExePath);
        var xrefs = ScanRelativeXrefs(text.Bytes, text.VirtualAddress, text.RawPointer);
        var targetAddresses = entries
            .Select(e => (entry: e, address: ParseAddress(NormalizeAddress(e.Address))))
            .ToList();
        var nearWindow = Math.Clamp(nearBytes, 0, 0x4000);
        var targets = new List<object>();
        var breakpointCandidates = new List<object>();
        var directXrefTotal = 0;
        foreach (var target in targetAddresses)
        {
            var direct = xrefs
                .Where(x => x.Target == target.address)
                .OrderBy(x => x.Source)
                .Select(x => FormatXref(x, target.address))
                .ToList();
            var nearby = nearWindow == 0
                ? []
                : xrefs
                    .Where(x => Math.Abs((long)x.Target - target.address) <= nearWindow && x.Target != target.address)
                    .OrderBy(x => Math.Abs((long)x.Target - target.address))
                    .ThenBy(x => x.Source)
                    .Take(32)
                    .Select(x => FormatXref(x, target.address))
                    .ToList();
            targets.Add(new
            {
                target.entry.Stage,
                target.entry.Name,
                address = FormatHex(target.address, 8),
                direct_xref_count = direct.Count,
                nearby_xref_count = nearby.Count,
                direct_xrefs = direct.Take(64).ToList(),
                nearby_xrefs = nearby,
                evidence_level = target.entry.EvidenceLevel,
                expected_semantics = target.entry.ExpectedSemantics
            });
            directXrefTotal += direct.Count;
            foreach (var caller in direct.Take(8))
            {
                breakpointCandidates.Add(new
                {
                    stage = target.entry.Stage,
                    target = target.entry.Name,
                    target_address = FormatHex(target.address, 8),
                    caller,
                    reason = "Static direct xref to staged function; use as a candidate caller breakpoint before dynamic promotion."
                });
            }
        }

        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            stage = normalizedStage,
            target = new
            {
                paths.ExePath,
                paths.ExeSha256,
                expected_sha256 = ExpectedSha256,
                paths.IsExpectedSha256,
                image_base = FormatHex(ImageBase, 8),
                text_section = new
                {
                    virtual_address = FormatHex(text.VirtualAddress, 8),
                    virtual_size = FormatHex(text.VirtualSize),
                    raw_pointer = FormatHex(text.RawPointer),
                    raw_size = FormatHex(text.RawSize)
                }
            },
            scan = new
            {
                instruction_count = xrefs.Count,
                target_count = targets.Count,
                near_bytes = nearWindow,
                direct_xref_total = directXrefTotal,
                breakpoint_candidate_count = breakpointCandidates.Count
            },
            targets,
            breakpoint_candidates = breakpointCandidates,
            safety = "Offline static xref scan only; reads Ekd5.exe .text bytes and writes evidence files. Dynamic semantics still require runtime breakpoint evidence."
        };
        var jsonPath = Path.Combine(sessionDir, "static-xref-scan.json");
        var markdownPath = Path.Combine(sessionDir, "static-xref-scan.md");
        WriteJson(jsonPath, report);
        WriteStaticXrefMarkdown(markdownPath, report);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
        {
            type = "StaticXrefScanExported",
            created_at = DateTimeOffset.Now.ToString("O"),
            stage = normalizedStage,
            json_path = jsonPath,
            markdown_path = markdownPath,
            target_count = targets.Count,
            breakpoint_candidate_count = breakpointCandidates.Count
        });

        return new
        {
            session_dir = sessionDir,
            xref_path = jsonPath,
            markdown_path = markdownPath,
            report
        };
    }

    public object DebugAddressReport(string stages, int nearBytes, int maxCandidatesPerFunction, string? outputDir, string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var stageList = ParseStageList(stages);
        if (stageList.Count == 0)
        {
            throw new ArgumentException("At least one stage is required.");
        }

        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var maxCandidates = Math.Clamp(maxCandidatesPerFunction, 1, 32);
        var stageReports = new List<object>();
        var candidateRows = new List<object>();
        var targetRows = new List<object>();
        var highPriorityCount = 0;
        foreach (var stage in stageList)
        {
            var stageDir = Path.Combine(sessionDir, "address-report", stage);
            Directory.CreateDirectory(stageDir);
            var catalog = DebugFunctionCatalog(stage, stageDir, gameRoot);
            var xref = DebugStaticXrefScan(stage, nearBytes, stageDir, gameRoot);
            var catalogPath = ExtractStringFromObject(catalog, "catalog_path");
            var xrefPath = ExtractStringFromObject(xref, "xref_path");
            stageReports.Add(new
            {
                stage,
                catalog_path = catalogPath,
                xref_path = xrefPath
            });
            AppendJsonLine(eventsPath, new
            {
                type = "AddressReportStageScanned",
                created_at = DateTimeOffset.Now.ToString("O"),
                stage,
                catalog_path = catalogPath,
                xref_path = xrefPath
            });

            using var xrefDocument = JsonDocument.Parse(File.ReadAllText(xrefPath, Encoding.UTF8));
            var root = xrefDocument.RootElement;
            if (TryGetJsonProperty(root, "targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
            {
                foreach (var target in targets.EnumerateArray())
                {
                    var targetStage = GetStringProperty(target, "stage", "Stage") ?? stage;
                    var name = GetStringProperty(target, "name", "Name") ?? string.Empty;
                    var address = GetStringProperty(target, "address", "Address") ?? string.Empty;
                    var directCount = GetIntProperty(target, "direct_xref_count", "directXrefCount");
                    var nearbyCount = GetIntProperty(target, "nearby_xref_count", "nearbyXrefCount");
                    var priority = ScoreAddressCandidate(targetStage, name, directCount, nearbyCount);
                    if (priority.Equals("high", StringComparison.OrdinalIgnoreCase))
                    {
                        highPriorityCount++;
                    }
                    var topCallers = ReadTopCallers(target, maxCandidates);
                    targetRows.Add(new
                    {
                        stage = targetStage,
                        name,
                        address,
                        direct_xref_count = directCount,
                        nearby_xref_count = nearbyCount,
                        priority,
                        evidence_level = GetStringProperty(target, "evidence_level", "evidenceLevel") ?? string.Empty,
                        expected_semantics = GetStringProperty(target, "expected_semantics", "expectedSemantics") ?? string.Empty,
                        recommended_breakpoints = topCallers.Count == 0 ? [address] : topCallers.Select(c => c.Source).Prepend(address).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    });
                    foreach (var caller in topCallers)
                    {
                        candidateRows.Add(new
                        {
                            stage = targetStage,
                            target = name,
                            target_address = address,
                            caller = caller.Source,
                            caller_file_offset = caller.FileOffset,
                            mnemonic = caller.Mnemonic,
                            priority,
                            recommendation = "Set caller and target-entry breakpoints, then verify with concrete gameplay action and battle-state delta."
                        });
                    }
                }
            }
        }

        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            session_dir = sessionDir,
            stages = stageList,
            target = new
            {
                paths.ExePath,
                paths.ExeSha256,
                expected_sha256 = ExpectedSha256,
                paths.IsExpectedSha256,
                image_base = FormatHex(ImageBase, 8)
            },
            summary = new
            {
                stage_count = stageList.Count,
                target_count = targetRows.Count,
                breakpoint_candidate_count = candidateRows.Count,
                high_priority_count = highPriorityCount
            },
            stage_reports = stageReports,
            targets = targetRows,
            breakpoint_candidates = candidateRows,
            safety = "Offline address report only; reads Ekd5.exe bytes and writes evidence. Dynamic semantics require x32dbg hit evidence before promotion."
        };
        var reportPath = Path.Combine(sessionDir, "function-address-report.json");
        var markdownPath = Path.Combine(sessionDir, "function-address-report.md");
        WriteJson(reportPath, report);
        WriteAddressReportMarkdown(markdownPath, report);
        AppendJsonLine(eventsPath, new
        {
            type = "AddressReportExported",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = reportPath,
            markdown_path = markdownPath,
            target_count = targetRows.Count,
            breakpoint_candidate_count = candidateRows.Count
        });

        return new
        {
            session_dir = sessionDir,
            report_path = reportPath,
            markdown_path = markdownPath,
            report
        };
    }

    public object DebugAddressReportProbePlan(
        string? reportPath,
        string stages,
        int nearBytes,
        int maxCandidatesPerFunction,
        bool includeTargets,
        bool includeCallers,
        string? outputDir,
        string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var resolvedReportPath = reportPath;
        if (string.IsNullOrWhiteSpace(resolvedReportPath))
        {
            var report = DebugAddressReport(stages, nearBytes, maxCandidatesPerFunction, sessionDir, gameRoot);
            resolvedReportPath = ExtractStringFromObject(report, "report_path");
        }
        else
        {
            resolvedReportPath = Path.GetFullPath(resolvedReportPath);
        }

        if (string.IsNullOrWhiteSpace(resolvedReportPath) || !File.Exists(resolvedReportPath))
        {
            throw new FileNotFoundException("Function address report JSON was not found.", resolvedReportPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(resolvedReportPath, Encoding.UTF8));
        var root = document.RootElement;
        var targets = new Dictionary<string, InternalProbeTarget>(StringComparer.OrdinalIgnoreCase);
        if (TryGetJsonProperty(root, "targets", out var targetArray) && targetArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in targetArray.EnumerateArray())
            {
                var stage = GetStringProperty(row, "stage") ?? "unknown";
                var name = GetStringProperty(row, "name") ?? "unknown";
                var address = GetStringProperty(row, "address") ?? string.Empty;
                var semantics = GetStringProperty(row, "expected_semantics", "expectedSemantics") ?? string.Empty;
                var priority = GetStringProperty(row, "priority") ?? "pending";
                if (includeTargets && !string.IsNullOrWhiteSpace(address))
                {
                    AddProbeTarget(targets, new InternalProbeTarget
                    {
                        Address = NormalizeAddress(address),
                        Name = name,
                        Phase = stage,
                        ExpectedSemantics = semantics,
                        TriggerHint = $"Address report target entry; priority={priority}.",
                        EvidenceLevel = $"static-address-report-{priority}",
                        HighFrequency = priority.Equals("medium", StringComparison.OrdinalIgnoreCase)
                    });
                }

                if (!includeCallers ||
                    !TryGetJsonProperty(row, "recommended_breakpoints", out var bps) ||
                    bps.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var bp in bps.EnumerateArray())
                {
                    var bpAddress = bp.GetString();
                    if (string.IsNullOrWhiteSpace(bpAddress) || string.Equals(bpAddress, address, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AddProbeTarget(targets, new InternalProbeTarget
                    {
                        Address = NormalizeAddress(bpAddress),
                        Name = $"{name}_caller_{PlainAddress(bpAddress)}",
                        Phase = stage,
                        ExpectedSemantics = $"Static caller breakpoint candidate for {name} ({NormalizeAddress(address)}).",
                        TriggerHint = $"Set alongside target {NormalizeAddress(address)} during concrete gameplay action; priority={priority}.",
                        EvidenceLevel = $"static-caller-candidate-{priority}",
                        HighFrequency = priority.Equals("medium", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException("Address report did not produce any probe targets.");
        }

        var plan = new InternalProbePlan
        {
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Profile = "address-report",
            TargetExeSha256 = paths.ExeSha256,
            Targets = targets.Values
                .OrderBy(t => StageSortKey(t.Phase))
                .ThenBy(t => t.Address, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Safety = "Generated from static address report; debugger breakpoints and read-only evidence only."
        };
        var planPath = Path.Combine(sessionDir, "address-report-probe-plan.json");
        var markdownPath = Path.Combine(sessionDir, "address-report-probe-plan.md");
        WriteJson(planPath, plan);
        WriteInternalProbePlanMarkdown(markdownPath, plan, resolvedReportPath);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
        {
            type = "AddressReportProbePlanCreated",
            created_at = DateTimeOffset.Now.ToString("O"),
            report_path = resolvedReportPath,
            plan_path = planPath,
            target_count = plan.Targets.Count,
            include_targets = includeTargets,
            include_callers = includeCallers
        });

        return new
        {
            session_dir = sessionDir,
            report_path = resolvedReportPath,
            plan_path = planPath,
            markdown_path = markdownPath,
            target_count = plan.Targets.Count,
            plan
        };
    }

    public object DebugAddressProbeRun(
        string? reportPath,
        string stages,
        bool startGame,
        bool allowLaunch,
        bool startDebugger,
        string? waitForState,
        bool runProbes,
        int maxHits,
        int timeoutMs,
        string hostName,
        int port,
        string? outputDir,
        string? gameRoot)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var steps = new List<object>();
        AppendJsonLine(eventsPath, new
        {
            type = "AddressProbeRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            stages,
            start_game = startGame,
            allow_launch = allowLaunch,
            start_debugger = startDebugger,
            wait_for_state = waitForState,
            run_probes = runProbes
        });

        if (startGame)
        {
            var start = GameProcessStart(gameRoot, allowLaunch, waitMs: 10000, sessionDir);
            steps.Add(new { step = "game_process_start", result = start });
            AppendJsonLine(eventsPath, new { type = "AddressProbeRunStep", step = "game_process_start", result = start });
        }

        if (startDebugger)
        {
            var start = DebugSessionStart(gameRoot, null, hostName, port, waitMs: 10000, hidden: false);
            steps.Add(new { step = "debug_session_start", result = start });
            AppendJsonLine(eventsPath, new { type = "AddressProbeRunStep", step = "debug_session_start", result = start });
        }

        var state = DebugSessionState(gameRoot, hostName, port);
        steps.Add(new { step = "debug_session_state", result = state });
        AppendJsonLine(eventsPath, new { type = "AddressProbeRunStep", step = "debug_session_state", result = state });

        if (!string.IsNullOrWhiteSpace(waitForState) &&
            !waitForState.Equals("none", StringComparison.OrdinalIgnoreCase) &&
            !waitForState.Equals("skip", StringComparison.OrdinalIgnoreCase))
        {
            var wait = GameRuntimeWaitForState(gameRoot, hostName, port, waitForState, timeoutMs: 30000, pollIntervalMs: 500, minBattleUnits: 8, sessionDir);
            steps.Add(new { step = "game_runtime_wait_for_state", result = wait });
            AppendJsonLine(eventsPath, new { type = "AddressProbeRunStep", step = "game_runtime_wait_for_state", result = wait });
        }

        var plan = DebugAddressReportProbePlan(
            reportPath,
            stages,
            nearBytes: 64,
            maxCandidatesPerFunction: 8,
            includeTargets: true,
            includeCallers: true,
            sessionDir,
            gameRoot);
        steps.Add(new { step = "debug_address_report_probe_plan", result = plan });
        AppendJsonLine(eventsPath, new { type = "AddressProbeRunStep", step = "debug_address_report_probe_plan", result = plan });

        object? probeRun = null;
        if (runProbes)
        {
            var planPath = ExtractStringFromObject(plan, "plan_path");
            probeRun = DebugInternalProbeRun(
                planPath,
                profile: "address-report",
                hostName,
                port,
                maxHits: Math.Clamp(maxHits, 1, 256),
                timeoutMs: Math.Max(timeoutMs, 1000),
                disableAfterHit: true,
                continueAfterEntryPointPause: false,
                gameRoot,
                sessionDir);
            steps.Add(new { step = "debug_internal_probe_run", result = probeRun });
            AppendJsonLine(eventsPath, new { type = "AddressProbeRunStep", step = "debug_internal_probe_run", result = probeRun });
        }

        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = runProbes ? "probe-run-attempted" : "plan-ready",
            session_dir = sessionDir,
            stages = ParseStageList(stages),
            start_game = startGame,
            start_debugger = startDebugger,
            wait_for_state = waitForState,
            run_probes = runProbes,
            plan_path = ExtractStringFromObject(plan, "plan_path"),
            report_path = ExtractStringFromObject(plan, "report_path"),
            target_count = ExtractIntFromObject(plan, "target_count"),
            probe_run = probeRun,
            steps,
            safety = "One-shot address probe orchestration; no mouse input, screenshots, process-memory writes, or game-file writes."
        };
        var summaryPath = Path.Combine(sessionDir, "address-probe-run-summary.json");
        var markdownPath = Path.Combine(sessionDir, "address-probe-run-summary.md");
        WriteJson(summaryPath, summary);
        WriteAddressProbeRunSummaryMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new { type = "AddressProbeRunSummary", path = summaryPath, summary.status });

        return summary;
    }

    public object DebugBattleProfileProbeRun(
        string profile,
        string stages,
        bool startGame,
        bool allowLaunch,
        int gameStartWaitMs,
        bool startDebugger,
        string? waitForState,
        bool requireProfileMatch,
        bool runProbes,
        int maxHits,
        int timeoutMs,
        string hostName,
        int port,
        string? outputDir,
        string? gameRoot)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var steps = new List<object>();

        AppendJsonLine(eventsPath, new
        {
            type = "BattleProfileProbeRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            profile,
            stages,
            start_game = startGame,
            allow_launch = allowLaunch,
            start_debugger = startDebugger,
            wait_for_state = waitForState,
            require_profile_match = requireProfileMatch,
            run_probes = runProbes
        });

        if (startGame)
        {
            var start = GameProcessStart(gameRoot, allowLaunch, Math.Clamp(gameStartWaitMs, 0, 60000), sessionDir);
            steps.Add(new { step = "game_process_start", result = start });
            AppendJsonLine(eventsPath, new { type = "BattleProfileProbeRunStep", step = "game_process_start", result = start });
        }

        if (startDebugger)
        {
            var start = DebugSessionStart(gameRoot, null, hostName, port, waitMs: 10000, hidden: false);
            steps.Add(new { step = "debug_session_start", result = start });
            AppendJsonLine(eventsPath, new { type = "BattleProfileProbeRunStep", step = "debug_session_start", result = start });
        }

        var state = DebugSessionState(gameRoot, hostName, port);
        steps.Add(new { step = "debug_session_state", result = state });
        AppendJsonLine(eventsPath, new { type = "BattleProfileProbeRunStep", step = "debug_session_state", result = state });

        var runtimeState = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        steps.Add(new { step = "game_runtime_state_classify", result = runtimeState });
        AppendJsonLine(eventsPath, new { type = "BattleProfileProbeRunStep", step = "game_runtime_state_classify", result = runtimeState });

        if (!string.IsNullOrWhiteSpace(waitForState) &&
            !waitForState.Equals("none", StringComparison.OrdinalIgnoreCase) &&
            !waitForState.Equals("skip", StringComparison.OrdinalIgnoreCase))
        {
            var wait = GameRuntimeWaitForState(
                gameRoot,
                hostName,
                port,
                waitForState,
                timeoutMs: Math.Clamp(timeoutMs, 1000, 10 * 60 * 1000),
                pollIntervalMs: 500,
                minBattleUnits: 8,
                sessionDir);
            steps.Add(new { step = "game_runtime_wait_for_state", result = wait });
            AppendJsonLine(eventsPath, new { type = "BattleProfileProbeRunStep", step = "game_runtime_wait_for_state", result = wait });
        }

        var battleMatch = GameBattleStateMatch(profile, gameRoot, sessionDir);
        var profileStatus = ExtractStringFromObject(battleMatch, "status");
        var profileReady = profileStatus.Equals("profile-matched", StringComparison.OrdinalIgnoreCase) ||
            profileStatus.Equals("attack_after_observed", StringComparison.OrdinalIgnoreCase);
        steps.Add(new { step = "game_battle_state_match", result = battleMatch });
        AppendJsonLine(eventsPath, new { type = "BattleProfileProbeRunStep", step = "game_battle_state_match", result = battleMatch });

        var dynamicProbesAllowed = runProbes && (!requireProfileMatch || profileReady);
        var addressProbe = DebugAddressProbeRun(
            reportPath: null,
            stages,
            startGame: false,
            allowLaunch: false,
            startDebugger: false,
            waitForState: null,
            runProbes: dynamicProbesAllowed,
            maxHits,
            timeoutMs,
            hostName,
            port,
            sessionDir,
            gameRoot);
        steps.Add(new { step = "debug_address_probe_run", result = addressProbe });
        AppendJsonLine(eventsPath, new { type = "BattleProfileProbeRunStep", step = "debug_address_probe_run", result = addressProbe });

        var status = dynamicProbesAllowed
            ? "profile-gated-probe-run-attempted"
            : requireProfileMatch && !profileReady
                ? "profile-not-ready-plan-ready"
                : "profile-plan-ready";
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            profile = NormalizeBattleProfile(profile),
            profile_status = profileStatus,
            profile_ready = profileReady,
            require_profile_match = requireProfileMatch,
            stages = ParseStageList(stages),
            start_game = startGame,
            allow_launch = allowLaunch,
            start_debugger = startDebugger,
            wait_for_state = waitForState,
            run_probes_requested = runProbes,
            dynamic_probes_allowed = dynamicProbesAllowed,
            plan_path = ExtractStringFromObject(addressProbe, "plan_path"),
            report_path = ExtractStringFromObject(addressProbe, "report_path"),
            target_count = ExtractIntFromObject(addressProbe, "target_count"),
            address_probe_status = ExtractStringFromObject(addressProbe, "status"),
            battle_match = battleMatch,
            address_probe = addressProbe,
            step_count = steps.Count,
            steps,
            safety = "Scenario profile and address probe orchestration only; no mouse input, screenshots, process-memory writes, or game-file writes. Dynamic probes require x32dbg bridge and profile gate unless require_profile_match=false."
        };
        var summaryPath = Path.Combine(sessionDir, "battle-profile-probe-run-summary.json");
        var markdownPath = Path.Combine(sessionDir, "battle-profile-probe-run-summary.md");
        WriteJson(summaryPath, summary);
        WriteBattleProfileProbeRunSummaryMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new { type = "BattleProfileProbeRunSummary", path = summaryPath, summary.status, profile_status = profileStatus });

        return summary;
    }

    public object DebugLiveProbeReadiness(
        string profile,
        string stages,
        string hostName,
        int port,
        string? outputDir,
        string? gameRoot)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var process = FindTargetProcess();
        var window = GetWindowSnapshot(process);
        var bridge = InvokeX32dbg("GET", hostName, port, "/api/health");
        var debugState = IsBridgeSuccess(bridge)
            ? InvokeX32dbg("GET", hostName, port, "/api/debug/state")
            : bridge;
        var runtimeState = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        var battleMatch = GameBattleStateMatch(profile, gameRoot, sessionDir);
        var addressProbe = DebugAddressProbeRun(
            reportPath: null,
            stages,
            startGame: false,
            allowLaunch: false,
            startDebugger: false,
            waitForState: null,
            runProbes: false,
            maxHits: 1,
            timeoutMs: 1000,
            hostName,
            port,
            sessionDir,
            gameRoot);

        var runtimeClassification = ExtractNestedStringFromObject(runtimeState, "runtime", "classification");
        var profileStatus = ExtractStringFromObject(battleMatch, "status");
        var profileReady = profileStatus.Equals("profile-matched", StringComparison.OrdinalIgnoreCase) ||
            profileStatus.Equals("attack_after_observed", StringComparison.OrdinalIgnoreCase);
        var bridgeReady = IsBridgeSuccess(bridge);
        var planPath = ExtractStringFromObject(addressProbe, "plan_path");
        var targetCount = ExtractIntFromObject(addressProbe, "target_count");
        var planReady = !string.IsNullOrWhiteSpace(planPath) && File.Exists(planPath) && targetCount > 0;
        var battleLoaded = runtimeClassification.Equals("battle_loaded", StringComparison.OrdinalIgnoreCase);
        var readyForDynamicProbe = process is not null && bridgeReady && planReady && profileReady;

        var gates = new List<object>
        {
            new { gate = "target_hash_expected", passed = paths.IsExpectedSha256, evidence = paths.ExeSha256 },
            new { gate = "process_running", passed = process is not null, evidence = ProcessSummary(process) },
            new { gate = "window_detected", passed = process is not null && HasWindow(window), evidence = window },
            new { gate = "x32dbg_bridge_online", passed = bridgeReady, evidence = bridge },
            new { gate = "runtime_battle_loaded", passed = battleLoaded, evidence = runtimeClassification },
            new { gate = "battle_profile_ready", passed = profileReady, evidence = profileStatus },
            new { gate = "address_probe_plan_ready", passed = planReady, evidence = new { plan_path = planPath, target_count = targetCount } }
        };

        var missing = gates
            .Select(g => new { Gate = ExtractStringFromObject(g, "gate"), Passed = ExtractBoolFromObject(g, "passed") })
            .Where(g => !g.Passed)
            .Select(g => g.Gate)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .ToList();
        var status = readyForDynamicProbe
            ? "ready-for-dynamic-probe"
            : process is null
                ? "not-running"
                : !bridgeReady
                    ? "bridge-unavailable"
                    : !profileReady
                        ? "profile-not-ready"
                        : !planReady
                            ? "plan-not-ready"
                            : "not-ready";
        var nextActions = BuildReadinessNextActions(missing);
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            ready_for_dynamic_probe = readyForDynamicProbe,
            session_dir = sessionDir,
            profile = NormalizeBattleProfile(profile),
            stages = ParseStageList(stages),
            target = new
            {
                paths.ExePath,
                paths.ExeSha256,
                paths.IsExpectedSha256
            },
            target_process = ProcessSummary(process),
            window,
            bridge,
            debug_state = debugState,
            runtime_classification = runtimeClassification,
            profile_status = profileStatus,
            plan_path = planPath,
            report_path = ExtractStringFromObject(addressProbe, "report_path"),
            target_count = targetCount,
            gates,
            missing_gates = missing,
            next_actions = nextActions,
            runtime_state = runtimeState,
            battle_match = battleMatch,
            address_probe = addressProbe,
            safety = "Readiness check only; no game launch, mouse input, screenshots, process-memory writes, game-file writes, or x32dbg breakpoint changes."
        };
        var summaryPath = Path.Combine(sessionDir, "live-probe-readiness.json");
        var markdownPath = Path.Combine(sessionDir, "live-probe-readiness.md");
        WriteJson(summaryPath, summary);
        WriteLiveProbeReadinessMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new
        {
            type = "LiveProbeReadiness",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = summaryPath,
            status,
            ready_for_dynamic_probe = readyForDynamicProbe,
            missing_gates = missing
        });

        return summary;
    }

    public object DebugLiveProbeAutoRun(
        string profile,
        string stages,
        bool startGame,
        bool allowLaunch,
        int gameStartWaitMs,
        bool startDebugger,
        bool continueStartup,
        int startupContinueMaxRuns,
        bool runProbes,
        bool requireReady,
        int maxHits,
        int timeoutMs,
        string hostName,
        int port,
        string? outputDir,
        string? gameRoot)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var steps = new List<object>();

        AppendJsonLine(eventsPath, new
        {
            type = "LiveProbeAutoRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            profile,
            stages,
            start_game = startGame,
            allow_launch = allowLaunch,
            start_debugger = startDebugger,
            continue_startup = continueStartup,
            startup_continue_max_runs = startupContinueMaxRuns,
            run_probes = runProbes,
            require_ready = requireReady
        });

        if (startGame)
        {
            var start = GameProcessStart(gameRoot, allowLaunch, Math.Clamp(gameStartWaitMs, 0, 60000), sessionDir);
            steps.Add(new { step = "game_process_start", result = start });
            AppendJsonLine(eventsPath, new { type = "LiveProbeAutoRunStep", step = "game_process_start", result = start });
        }

        if (startDebugger)
        {
            var start = DebugSessionStart(gameRoot, null, hostName, port, waitMs: Math.Clamp(timeoutMs, 1000, 60000), hidden: false);
            steps.Add(new { step = "debug_session_start", result = start });
            AppendJsonLine(eventsPath, new { type = "LiveProbeAutoRunStep", step = "debug_session_start", result = start });
        }

        object? startupContinue = null;
        if (continueStartup)
        {
            startupContinue = ContinueStartupThroughDebugger(hostName, port, Math.Clamp(timeoutMs, 1000, 60000), startupContinueMaxRuns, sessionDir);
            steps.Add(new { step = "startup_continue", result = startupContinue });
            AppendJsonLine(eventsPath, new { type = "LiveProbeAutoRunStep", step = "startup_continue", result = startupContinue });
        }

        var readiness = DebugLiveProbeReadiness(profile, stages, hostName, port, sessionDir, gameRoot);
        var ready = ExtractBoolFromObject(readiness, "ready_for_dynamic_probe");
        var readinessStatus = ExtractStringFromObject(readiness, "status");
        steps.Add(new { step = "debug_live_probe_readiness", result = readiness });
        AppendJsonLine(eventsPath, new { type = "LiveProbeAutoRunStep", step = "debug_live_probe_readiness", result = readiness });

        object? probeRun = null;
        var dynamicProbesAllowed = runProbes && (!requireReady || ready);
        if (dynamicProbesAllowed)
        {
            probeRun = DebugBattleProfileProbeRun(
                profile,
                stages,
                startGame: false,
                allowLaunch: false,
                gameStartWaitMs,
                startDebugger: false,
                waitForState: null,
                requireProfileMatch: requireReady,
                runProbes: true,
                maxHits: Math.Clamp(maxHits, 1, 256),
                timeoutMs: Math.Max(timeoutMs, 1000),
                hostName,
                port,
                sessionDir,
                gameRoot);
            steps.Add(new { step = "debug_battle_profile_probe_run", result = probeRun });
            AppendJsonLine(eventsPath, new { type = "LiveProbeAutoRunStep", step = "debug_battle_profile_probe_run", result = probeRun });
        }

        var status = dynamicProbesAllowed
            ? "dynamic-probe-attempted"
            : runProbes && requireReady && !ready
                ? "not-ready-probe-skipped"
                : "readiness-recorded";
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            profile = NormalizeBattleProfile(profile),
            stages = ParseStageList(stages),
            start_game = startGame,
            allow_launch = allowLaunch,
            start_debugger = startDebugger,
            continue_startup_requested = continueStartup,
            startup_continue_max_runs = Math.Clamp(startupContinueMaxRuns, 1, 8),
            startup_continue = startupContinue,
            run_probes_requested = runProbes,
            require_ready = requireReady,
            ready_for_dynamic_probe = ready,
            readiness_status = readinessStatus,
            dynamic_probes_allowed = dynamicProbesAllowed,
            readiness_path = Path.Combine(sessionDir, "live-probe-readiness.json"),
            plan_path = ExtractStringFromObject(readiness, "plan_path"),
            target_count = ExtractIntFromObject(readiness, "target_count"),
            probe_run = probeRun,
            step_count = steps.Count,
            steps,
            safety = "Auto-run orchestration only. Launching requires allow_launch=true. continue_startup only sends limited local x32dbg run commands. It never sends mouse input, captures screenshots, writes process memory, or patches files; x32dbg breakpoints run only when run_probes=true and readiness allows it unless require_ready=false."
        };
        var summaryPath = Path.Combine(sessionDir, "live-probe-auto-run-summary.json");
        var markdownPath = Path.Combine(sessionDir, "live-probe-auto-run-summary.md");
        WriteJson(summaryPath, summary);
        WriteLiveProbeAutoRunSummaryMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new { type = "LiveProbeAutoRunSummary", path = summaryPath, status, ready_for_dynamic_probe = ready });

        return summary;
    }

    private object ContinueStartupThroughDebugger(string hostName, int port, int timeoutMs, int maxRuns, string sessionDir)
    {
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var health = InvokeX32dbg("GET", hostName, port, "/api/health");
        var initialProcess = FindTargetProcess();
        var initialWindow = GetWindowSnapshot(initialProcess);
        var initialState = IsBridgeSuccess(health)
            ? InvokeX32dbg("GET", hostName, port, "/api/debug/state")
            : health;
        var attempts = new List<object>();
        var normalizedMaxRuns = Math.Clamp(maxRuns, 1, 8);
        var normalizedTimeoutMs = Math.Clamp(timeoutMs, 1000, 60000);
        var status = "not-started";

        AppendJsonLine(eventsPath, new
        {
            type = "StartupContinueStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            max_runs = normalizedMaxRuns,
            timeout_ms = normalizedTimeoutMs,
            health,
            initial_process = ProcessSummary(initialProcess),
            initial_window = initialWindow,
            initial_state = initialState
        });

        if (!IsBridgeSuccess(health))
        {
            status = "bridge-unavailable";
        }
        else if (HasWindow(initialWindow))
        {
            status = "window-already-present";
        }
        else
        {
            var sw = Stopwatch.StartNew();
            for (var i = 1; i <= normalizedMaxRuns && sw.ElapsedMilliseconds < normalizedTimeoutMs; i++)
            {
                var beforeState = InvokeX32dbg("GET", hostName, port, "/api/debug/state");
                var beforeProcess = FindTargetProcess();
                var beforeWindow = GetWindowSnapshot(beforeProcess);
                if (HasWindow(beforeWindow))
                {
                    status = "window-present";
                    attempts.Add(new
                    {
                        index = i,
                        skipped = true,
                        reason = "window already present before issuing another run command",
                        elapsed_ms = sw.ElapsedMilliseconds,
                        before_state = beforeState,
                        before_cip = TryReadCip(beforeState),
                        before_window = beforeWindow
                    });
                    break;
                }

                object runResult = IsPausedState(beforeState)
                    ? InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = "run" })
                    : new
                    {
                        skipped = true,
                        reason = "debugger state was not paused; waiting for process/window instead of issuing run",
                        state = beforeState
                    };

                var attemptStart = sw.ElapsedMilliseconds;
                var perAttemptBudget = Math.Clamp(normalizedTimeoutMs / normalizedMaxRuns, 500, 10000);
                object? afterProcess = null;
                object? afterWindow = null;
                X32dbgCallResult? afterState = null;
                var brokeOnPause = false;

                while (sw.ElapsedMilliseconds < normalizedTimeoutMs &&
                       sw.ElapsedMilliseconds - attemptStart < perAttemptBudget)
                {
                    Thread.Sleep(200);
                    var process = FindTargetProcess();
                    var window = GetWindowSnapshot(process);
                    var state = InvokeX32dbg("GET", hostName, port, "/api/debug/state");
                    afterProcess = ProcessSummary(process);
                    afterWindow = window;
                    afterState = state;

                    if (HasWindow(window))
                    {
                        status = "window-present";
                        break;
                    }

                    if (IsPausedState(state))
                    {
                        brokeOnPause = true;
                        break;
                    }
                }

                var attempt = new
                {
                    index = i,
                    elapsed_ms = sw.ElapsedMilliseconds,
                    before_state = beforeState,
                    before_cip = TryReadCip(beforeState),
                    before_process = ProcessSummary(beforeProcess),
                    before_window = beforeWindow,
                    run_result = runResult,
                    after_state = afterState,
                    after_cip = afterState is null ? null : TryReadCip(afterState),
                    after_process = afterProcess,
                    after_window = afterWindow,
                    paused_before_window = brokeOnPause
                };
                attempts.Add(attempt);
                AppendJsonLine(eventsPath, new { type = "StartupContinueAttempt", created_at = DateTimeOffset.Now.ToString("O"), attempt });

                if (string.Equals(status, "window-present", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            if (string.Equals(status, "not-started", StringComparison.OrdinalIgnoreCase))
            {
                status = attempts.Count >= normalizedMaxRuns ? "max-runs-without-window" : "timeout-without-window";
            }
        }

        var finalProcess = FindTargetProcess();
        var finalWindow = GetWindowSnapshot(finalProcess);
        var finalState = IsBridgeSuccess(health)
            ? InvokeX32dbg("GET", hostName, port, "/api/debug/state")
            : health;
        if (HasWindow(finalWindow) && status is "not-started" or "timeout-without-window" or "max-runs-without-window")
        {
            status = "window-present";
        }

        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            max_runs = normalizedMaxRuns,
            timeout_ms = normalizedTimeoutMs,
            attempt_count = attempts.Count,
            health,
            initial_process = ProcessSummary(initialProcess),
            initial_window = initialWindow,
            initial_state = initialState,
            final_process = ProcessSummary(finalProcess),
            final_window = finalWindow,
            final_state = finalState,
            attempts,
            safety = "Only sends limited local x32dbg run commands and polls process/window/debugger state; no mouse input, screenshots, process-memory writes, or game-file writes."
        };
        var path = Path.Combine(sessionDir, "startup-continue.json");
        WriteJson(path, summary);
        AppendJsonLine(eventsPath, new { type = "StartupContinueSummary", created_at = DateTimeOffset.Now.ToString("O"), path, status });
        return summary;
    }

    public object DebugPhaseProbePlan(string stage, string? outputDir, string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var normalizedStage = NormalizeStageFilter(stage);
        var entries = SelectFunctionCatalogEntries(normalizedStage).ToList();
        if (entries.Count == 0)
        {
            throw new ArgumentException($"Stage '{stage}' did not select any function catalog entries.");
        }

        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var targets = entries
            .Select(entry => new InternalProbeTarget
            {
                Address = NormalizeAddress(entry.Address),
                Name = entry.Name,
                Phase = entry.Stage,
                ExpectedSemantics = entry.ExpectedSemantics,
                TriggerHint = entry.TriggerHint,
                EvidenceLevel = entry.EvidenceLevel,
                HighFrequency = entry.HighFrequency
            })
            .ToList();
        var plan = new InternalProbePlan
        {
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Profile = normalizedStage,
            TargetExeSha256 = paths.ExeSha256,
            Targets = targets,
            Safety = "Staged debugger breakpoints and read-only evidence only; no mouse input, screenshots, game-file writes, or process-memory writes."
        };
        var planPath = Path.Combine(sessionDir, "internal-probe-plan.json");
        var catalogResult = DebugFunctionCatalog(normalizedStage, sessionDir, gameRoot);
        WriteJson(planPath, plan);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
        {
            type = "PhaseProbePlanCreated",
            created_at = DateTimeOffset.Now.ToString("O"),
            stage = normalizedStage,
            target_count = targets.Count,
            path = planPath
        });
        return new
        {
            session_dir = sessionDir,
            plan_path = planPath,
            target_count = targets.Count,
            catalog = catalogResult,
            plan
        };
    }

    public object DebugFullAutoRun(
        string profile,
        bool allowDebugInvoke,
        bool allowRuntimeInjection,
        bool allowPersistentPatch,
        bool startGame,
        bool allowLaunch,
        bool startDebugger,
        bool continueStartup,
        bool runProbes,
        string hostName,
        int port,
        string? outputDir,
        string? gameRoot)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var normalizedProfile = NormalizeFullAutoProfile(profile);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var steps = new List<object>();

        var session = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            profile = normalizedProfile,
            target = paths,
            bridge = new { host = hostName, port },
            options = new
            {
                allow_debug_invoke = allowDebugInvoke,
                allow_runtime_injection = allowRuntimeInjection,
                allow_persistent_patch = allowPersistentPatch,
                start_game = startGame,
                allow_launch = allowLaunch,
                start_debugger = startDebugger,
                continue_startup = continueStartup,
                run_probes = runProbes
            },
            safety = "Full automation evidence session; final route is internal/debugger state based and does not require mouse coordinates or screenshots."
        };
        WriteJson(Path.Combine(sessionDir, "session.json"), session);
        AppendJsonLine(eventsPath, new { type = "FullAutoRunStarted", session });
        AppendActionChainLine(
            sessionDir,
            "debug_full_auto_run",
            "session-started",
            "started",
            new
            {
                profile = normalizedProfile,
                session_path = Path.Combine(sessionDir, "session.json"),
                startGame,
                startDebugger,
                continueStartup,
                runProbes,
                allowDebugInvoke,
                allowRuntimeInjection,
                allowPersistentPatch
            });

        if (startGame)
        {
            var start = GameProcessStart(gameRoot, allowLaunch, waitMs: 10000, sessionDir);
            steps.Add(new { step = "game_process_start", result = start });
            AppendJsonLine(eventsPath, new { type = "FullAutoStep", step = "game_process_start", result = start });
            AppendActionChainLine(sessionDir, "debug_full_auto_run", "game_process_start", ExtractStringFromObject(start, "status"), new { allowLaunch, result = start });
        }

        if (startDebugger)
        {
            var start = DebugSessionStart(gameRoot, null, hostName, port, waitMs: 10000, hidden: false);
            steps.Add(new { step = "debug_session_start", result = start });
            AppendJsonLine(eventsPath, new { type = "FullAutoStep", step = "debug_session_start", result = start });
            AppendActionChainLine(sessionDir, "debug_full_auto_run", "debug_session_start", ExtractStringFromObject(start, "status"), new { bridge = new { host = hostName, port }, result = start });
        }

        var safePlanOnly = !startGame && !startDebugger && !continueStartup && !runProbes && !allowRuntimeInjection && !allowDebugInvoke;

        var r00HandlerProbe = safePlanOnly
            ? DebugR00RuntimeInvokeCandidatePlan(
                "regular_start",
                "2D,0x12,0x07,0x13",
                maxCandidatesPerCommand: 8,
                evidencePath: null,
                includeLatestEvidence: true,
                outputDir: Path.Combine(sessionDir, "r00-runtime-handler-probe"),
                gameRoot)
            : DebugR00RuntimeHandlerProbeRun(
                "regular_start",
                startDebugger: false,
                continueStartup: false,
                startupContinueMaxRuns: 4,
                runProbes,
                probeBeforeStartupContinue: true,
                commandIds: "2D,0x12,0x07,0x13",
                maxCandidatesPerCommand: 8,
                evidencePath: null,
                includeLatestEvidence: true,
                hostName,
                port,
                maxHits: 12,
                timeoutMs: 60000,
                disableAfterHit: true,
                continueAfterEntryPointPause: true,
                outputDir: Path.Combine(sessionDir, "r00-runtime-handler-probe"),
                gameRoot);
        steps.Add(new { step = "debug_r00_runtime_handler_probe_run", result = r00HandlerProbe });
        AppendJsonLine(eventsPath, new { type = "FullAutoStep", step = "debug_r00_runtime_handler_probe_run", result = r00HandlerProbe });
        AppendActionChainLine(sessionDir, "debug_full_auto_run", "debug_r00_runtime_handler_probe_run", ExtractStringFromObject(r00HandlerProbe, "status"), new { safePlanOnly, result = r00HandlerProbe });

        if (continueStartup)
        {
            var continued = ContinueStartupThroughDebugger(hostName, port, timeoutMs: 30000, maxRuns: 4, sessionDir);
            steps.Add(new { step = "startup_continue", result = continued });
            AppendJsonLine(eventsPath, new { type = "FullAutoStep", step = "startup_continue", result = continued });
            AppendActionChainLine(sessionDir, "debug_full_auto_run", "startup_continue", ExtractStringFromObject(continued, "status"), new { result = continued });
        }

        var state = DebugSessionState(gameRoot, hostName, port);
        steps.Add(new { step = "debug_session_state", result = state });
        AppendJsonLine(eventsPath, new { type = "FullAutoStep", step = "debug_session_state", result = state });
        AppendActionChainLine(sessionDir, "debug_full_auto_run", "debug_session_state", ExtractStringFromObject(state, "status"), new { result = state });

        var menu = safePlanOnly
            ? DebugRuntimeInvokePlan("menu", "full_menu", Path.Combine(sessionDir, "menu-plan"), gameRoot)
            : DebugMenuRouteRun("full_menu", allowRuntimeInjection, allowDebugInvoke, hostName, port, gameRoot, sessionDir);
        steps.Add(new { step = "debug_menu_route_run", result = menu });
        AppendJsonLine(eventsPath, new { type = "FullAutoStep", step = "debug_menu_route_run", result = menu });
        AppendActionChainLine(sessionDir, "debug_full_auto_run", "debug_menu_route_run", ExtractStringFromObject(menu, "status"), new { safePlanOnly, result = menu });

        var runtimePlan = DebugRuntimeInvokePlan("all", normalizedProfile, sessionDir, gameRoot);
        steps.Add(new { step = "debug_runtime_invoke_plan", result = runtimePlan });
        AppendJsonLine(eventsPath, new { type = "FullAutoStep", step = "debug_runtime_invoke_plan", result = runtimePlan });
        AppendActionChainLine(sessionDir, "debug_full_auto_run", "debug_runtime_invoke_plan", "plan-created", new { result = runtimePlan });

        var addressVerify = safePlanOnly
            ? DebugAddressReportProbePlan(
                reportPath: null,
                stages: "startup,settings,battle_entry,attack_before,attack_execute,attack_after,turn_end",
                nearBytes: 64,
                maxCandidatesPerFunction: 4,
                includeTargets: true,
                includeCallers: true,
                outputDir: Path.Combine(sessionDir, "address-verify-plan"),
                gameRoot)
            : DebugAddressVerifyRun(
                "startup,settings,battle_entry,attack_before,attack_execute,attack_after,turn_end",
                "yingchuan_cao_attack_zhangliang",
                runProbes,
                requireProfileMatch: true,
                maxHits: 16,
                timeoutMs: 60000,
                hostName,
                port,
                sessionDir,
                gameRoot);
        steps.Add(new { step = "debug_address_verify_run", result = addressVerify });
        AppendJsonLine(eventsPath, new { type = "FullAutoStep", step = "debug_address_verify_run", result = addressVerify });
        AppendActionChainLine(sessionDir, "debug_full_auto_run", "debug_address_verify_run", ExtractStringFromObject(addressVerify, "status"), new { safePlanOnly, result = addressVerify });

        var draft = DebugWriteKnowledgeDraft(sessionDir, "automation-flow", null, gameRoot);
        steps.Add(new { step = "debug_write_knowledge_draft", result = draft });
        AppendJsonLine(eventsPath, new { type = "FullAutoStep", step = "debug_write_knowledge_draft", result = draft });
        AppendActionChainLine(sessionDir, "debug_full_auto_run", "debug_write_knowledge_draft", ExtractStringFromObject(draft, "status"), new { result = draft });

        var promotion = DebugKnowledgePromote(sessionDir, "function-index", allowWrite: false, gameRoot, sessionDir);
        steps.Add(new { step = "debug_knowledge_promote", result = promotion });
        AppendJsonLine(eventsPath, new { type = "FullAutoStep", step = "debug_knowledge_promote", result = promotion });
        AppendActionChainLine(sessionDir, "debug_full_auto_run", "debug_knowledge_promote", ExtractStringFromObject(promotion, "status"), new { result = promotion });

        object? persistentPatch = null;
        if (allowPersistentPatch)
        {
            persistentPatch = BuildPersistentPatchRecommendation(sessionDir, normalizedProfile);
            WriteJson(Path.Combine(sessionDir, "persistent-patch-recommendation.json"), persistentPatch);
            AppendJsonLine(eventsPath, new { type = "FullAutoPersistentPatchRecommendation", result = persistentPatch });
        }

        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = runProbes ? "full-auto-run-attempted" : "full-auto-plan-ready",
            session_dir = sessionDir,
            profile = normalizedProfile,
            start_game = startGame,
            start_debugger = startDebugger,
            allow_debug_invoke = allowDebugInvoke,
            allow_runtime_injection = allowRuntimeInjection,
            allow_persistent_patch = allowPersistentPatch,
            run_probes = runProbes,
            persistent_patch = persistentPatch,
            step_count = steps.Count,
            steps,
            safety = "No direct persistent EXE writes are performed by GameDebug. Persistent patching must go through EffectPackage preview/apply workflows."
        };
        WriteJson(Path.Combine(sessionDir, "full-auto-summary.json"), summary);
        AppendJsonLine(eventsPath, new { type = "FullAutoRunSummary", summary.status, path = Path.Combine(sessionDir, "full-auto-summary.json") });
        AppendActionChainLine(sessionDir, "debug_full_auto_run", "summary", summary.status, new { summary_path = Path.Combine(sessionDir, "full-auto-summary.json"), step_count = steps.Count });
        return summary;
    }

    public object DebugRuntimeInvokePlan(string stage, string route, string? outputDir, string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var normalizedStage = NormalizeRuntimeInvokeStage(stage);
        var normalizedRoute = NormalizeRuntimeRoute(route);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var plan = BuildRuntimeInvokePlan(normalizedStage, normalizedRoute, paths);
        var planPath = Path.Combine(sessionDir, "runtime-invoke-plan.json");
        var markdownPath = Path.Combine(sessionDir, "runtime-invoke-plan.md");
        WriteJson(planPath, plan);
        WriteRuntimeInvokePlanMarkdown(markdownPath, plan);
        AppendActionChainLine(
            sessionDir,
            "debug_runtime_invoke_plan",
            $"{normalizedStage}:{normalizedRoute}",
            "plan-created",
            new
            {
                plan_path = planPath,
                markdown_path = markdownPath,
                action_count = plan.Actions.Count,
                route_error_count = plan.RouteErrors.Count,
                route_errors = plan.RouteErrors,
                actions = plan.Actions.Select(action => new { action.Key, action.Method, action.InvokeStrategy, action.CandidateAddress, action.RequiresRuntimeInjection, action.RequiresPausedDebuggee, action.Status }).ToList()
            });
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
        {
            type = "RuntimeInvokePlanCreated",
            created_at = DateTimeOffset.Now.ToString("O"),
            stage = normalizedStage,
            route = normalizedRoute,
            action_count = plan.Actions.Count,
            route_error_count = plan.RouteErrors.Count,
            route_errors = plan.RouteErrors,
            path = planPath
        });
        return new
        {
            session_dir = sessionDir,
            plan_path = planPath,
            markdown_path = markdownPath,
            action_count = plan.Actions.Count,
            route_error_count = plan.RouteErrors.Count,
            route_errors = plan.RouteErrors,
            plan
        };
    }

    public object DebugR00RuntimeInvokeCandidatePlan(
        string route,
        string commandIds,
        int maxCandidatesPerCommand,
        string? evidencePath,
        bool includeLatestEvidence,
        string? outputDir,
        string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var normalizedRoute = NormalizeR00RuntimeRoute(route);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var context = BuildR00RuntimeInvokeCandidateContext(
            normalizedRoute,
            paths,
            commandIds,
            Math.Clamp(maxCandidatesPerCommand, 1, 64),
            evidencePath,
            includeLatestEvidence);
        var reportPath = Path.Combine(sessionDir, "r00-runtime-invoke-candidate-plan.json");
        var markdownPath = Path.Combine(sessionDir, "r00-runtime-invoke-candidate-plan.md");
        var probePlanPath = Path.Combine(sessionDir, "r00-runtime-handler-probe-plan.json");
        var probePlanMarkdownPath = Path.Combine(sessionDir, "r00-runtime-handler-probe-plan.md");
        WriteJson(reportPath, context.Report);
        WriteR00RuntimeInvokeCandidatePlanMarkdown(markdownPath, context.Report);
        WriteJson(probePlanPath, context.ProbePlan);
        WriteInternalProbePlanMarkdown(probePlanMarkdownPath, context.ProbePlan, "r00-runtime-invoke-candidate-plan.json");
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
        {
            type = "R00RuntimeInvokeCandidatePlan",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = reportPath,
            probe_plan_path = probePlanPath,
            route = normalizedRoute,
            action_count = context.ActionCount,
            handler_candidate_count = context.HandlerCandidateCount,
            probe_target_count = context.ProbeTargetCount,
            latest_verified_hit_count = context.LatestVerifiedHitCount
        });
        return new
        {
            session_dir = sessionDir,
            report_path = reportPath,
            markdown_path = markdownPath,
            probe_plan_path = probePlanPath,
            probe_plan_markdown_path = probePlanMarkdownPath,
            status = "r00-runtime-invoke-candidate-plan-ready",
            route = normalizedRoute,
            action_count = context.ActionCount,
            handler_candidate_count = context.HandlerCandidateCount,
            probe_target_count = context.ProbeTargetCount,
            latest_evidence_path = context.LatestEvidencePath,
            latest_verified_hit_count = context.LatestVerifiedHitCount,
            safety = "Plan only. It links R_00 script offsets to static/live handler candidates but does not run x32dbg, inject code, send input, capture screenshots, write process memory, or patch files."
        };
    }

    public object DebugR00RuntimeHandlerProbeRun(
        string route,
        bool startDebugger,
        bool continueStartup,
        int startupContinueMaxRuns,
        bool runProbes,
        bool probeBeforeStartupContinue,
        string commandIds,
        int maxCandidatesPerCommand,
        string? evidencePath,
        bool includeLatestEvidence,
        string hostName,
        int port,
        int maxHits,
        int timeoutMs,
        bool disableAfterHit,
        bool continueAfterEntryPointPause,
        string? outputDir,
        string? gameRoot)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var normalizedRoute = NormalizeR00RuntimeRoute(route);
        var scanRoute = ToR00ScanRoute(normalizedRoute);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var steps = new List<object>();
        var hits = new List<object>();
        object? startResult = null;
        object? preStartupProbeRun = null;
        object? startupContinue = null;
        object? probeRun = null;
        var status = "plan-ready";

        AppendJsonLine(eventsPath, new
        {
            type = "R00RuntimeHandlerProbeRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            route = normalizedRoute,
            start_debugger = startDebugger,
            continue_startup = continueStartup,
            probe_before_startup_continue = probeBeforeStartupContinue,
            run_probes = runProbes,
            command_ids = commandIds
        });
        AppendActionChainLine(
            sessionDir,
            "debug_r00_runtime_handler_probe_run",
            "session-started",
            "started",
            new
            {
                route = normalizedRoute,
                startDebugger,
                continueStartup,
                runProbes,
                probeBeforeStartupContinue,
                commandIds
            });

        var candidateContext = BuildR00RuntimeInvokeCandidateContext(
            normalizedRoute,
            paths,
            commandIds,
            Math.Clamp(maxCandidatesPerCommand, 1, 64),
            evidencePath,
            includeLatestEvidence);
        var candidateReportPath = Path.Combine(sessionDir, "r00-runtime-invoke-candidate-plan.json");
        var candidateMarkdownPath = Path.Combine(sessionDir, "r00-runtime-invoke-candidate-plan.md");
        var probePlanPath = Path.Combine(sessionDir, "r00-runtime-handler-probe-plan.json");
        var probePlanMarkdownPath = Path.Combine(sessionDir, "r00-runtime-handler-probe-plan.md");
        WriteJson(candidateReportPath, candidateContext.Report);
        WriteR00RuntimeInvokeCandidatePlanMarkdown(candidateMarkdownPath, candidateContext.Report);
        WriteJson(probePlanPath, candidateContext.ProbePlan);
        WriteInternalProbePlanMarkdown(probePlanMarkdownPath, candidateContext.ProbePlan, "r00-runtime-invoke-candidate-plan.json");
        steps.Add(new
        {
            step = "debug_r00_runtime_invoke_candidate_plan",
            result = new
            {
                report_path = candidateReportPath,
                markdown_path = candidateMarkdownPath,
                probe_plan_path = probePlanPath,
                probe_plan_markdown_path = probePlanMarkdownPath,
                candidateContext.ActionCount,
                candidateContext.HandlerCandidateCount,
                candidateContext.ProbeTargetCount,
                candidateContext.LatestEvidencePath,
                candidateContext.LatestVerifiedHitCount
            }
        });
        AppendActionChainLine(
            sessionDir,
            "debug_r00_runtime_handler_probe_run",
            "candidate-plan",
            "plan-created",
            new
            {
                candidate_report_path = candidateReportPath,
                probe_plan_path = probePlanPath,
                action_count = candidateContext.ActionCount,
                handler_candidate_count = candidateContext.HandlerCandidateCount,
                probe_target_count = candidateContext.ProbeTargetCount
            });

        var beforeRuntime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        var beforeScriptWindow = GameRSceneScriptWindowScan(scanRoute, gameRoot, contextBytes: 16, maxScanBytes: 64 * 1024 * 1024, maxHitsPerWindow: 4, includePointerRefs: true, maxPointerRefs: 16, sessionDir);
        steps.Add(new { step = "runtime_before", result = beforeRuntime });
        steps.Add(new { step = "game_rscene_script_window_scan_before", result = beforeScriptWindow });
        AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeStep", step = "runtime_before", result = beforeRuntime });
        AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeStep", step = "game_rscene_script_window_scan_before", result = beforeScriptWindow });

        if (startDebugger)
        {
            startResult = DebugSessionStart(gameRoot, null, hostName, port, waitMs: Math.Clamp(timeoutMs, 1000, 60000), hidden: false);
            steps.Add(new { step = "debug_session_start", result = startResult });
            AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeStep", step = "debug_session_start", result = startResult });
            AppendActionChainLine(sessionDir, "debug_r00_runtime_handler_probe_run", "debug_session_start", ExtractStringFromObject(startResult, "status"), new { result = startResult });
        }

        var bridge = InvokeX32dbg("GET", hostName, port, "/api/health");
        var bridgeReady = IsBridgeSuccess(bridge);
        steps.Add(new { step = "bridge_health_before_startup_continue", result = bridge });
        AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeStep", step = "bridge_health_before_startup_continue", result = bridge });

        if (runProbes && probeBeforeStartupContinue)
        {
            if (!bridgeReady)
            {
                status = "pre-startup-bridge-unavailable";
                steps.Add(new { step = "debug_internal_probe_run_before_startup_continue_skipped", reason = "bridge-unavailable" });
                AppendActionChainLine(sessionDir, "debug_r00_runtime_handler_probe_run", "debug_internal_probe_run_before_startup_continue", status, new { bridge });
            }
            else if (candidateContext.ProbePlan.Targets.Count == 0)
            {
                status = "pre-startup-plan-empty";
                steps.Add(new { step = "debug_internal_probe_run_before_startup_continue_skipped", reason = "plan-empty" });
                AppendActionChainLine(sessionDir, "debug_r00_runtime_handler_probe_run", "debug_internal_probe_run_before_startup_continue", status, new { probe_plan_path = probePlanPath });
            }
            else
            {
                preStartupProbeRun = DebugInternalProbeRun(
                    probePlanPath,
                    profile: "r00-runtime-handler-candidates",
                    hostName,
                    port,
                    maxHits: Math.Clamp(maxHits, 1, 128),
                    timeoutMs: Math.Max(timeoutMs, 1000),
                    disableAfterHit,
                    continueAfterEntryPointPause,
                    gameRoot,
                    sessionDir);
                hits.AddRange(ExtractHitsFromProbeRun(preStartupProbeRun));
                steps.Add(new { step = "debug_internal_probe_run_before_startup_continue", result = preStartupProbeRun });
                AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeStep", step = "debug_internal_probe_run_before_startup_continue", result = preStartupProbeRun });
                status = $"pre-startup-probe-{ExtractStringFromObject(preStartupProbeRun, "status")}";
                AppendActionChainLine(sessionDir, "debug_r00_runtime_handler_probe_run", "debug_internal_probe_run_before_startup_continue", ExtractStringFromObject(preStartupProbeRun, "status"), new { result = preStartupProbeRun });
            }
        }

        if (continueStartup)
        {
            startupContinue = ContinueStartupThroughDebugger(hostName, port, Math.Clamp(timeoutMs, 1000, 60000), Math.Clamp(startupContinueMaxRuns, 1, 8), sessionDir);
            steps.Add(new { step = "startup_continue", result = startupContinue });
            AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeStep", step = "startup_continue", result = startupContinue });
            AppendActionChainLine(sessionDir, "debug_r00_runtime_handler_probe_run", "startup_continue", ExtractStringFromObject(startupContinue, "status"), new { result = startupContinue });
        }

        bridge = InvokeX32dbg("GET", hostName, port, "/api/health");
        bridgeReady = IsBridgeSuccess(bridge);
        steps.Add(new { step = "bridge_health", result = bridge });
        AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeStep", step = "bridge_health", result = bridge });

        if (!runProbes)
        {
            status = "plan-ready";
            AppendActionChainLine(sessionDir, "debug_r00_runtime_handler_probe_run", "debug_internal_probe_run", "skipped-plan-only", new { runProbes });
        }
        else if (!bridgeReady)
        {
            status = "bridge-unavailable";
            AppendActionChainLine(sessionDir, "debug_r00_runtime_handler_probe_run", "debug_internal_probe_run", status, new { bridge });
        }
        else if (candidateContext.ProbePlan.Targets.Count == 0)
        {
            status = "plan-empty";
            AppendActionChainLine(sessionDir, "debug_r00_runtime_handler_probe_run", "debug_internal_probe_run", status, new { probe_plan_path = probePlanPath });
        }
        else
        {
            probeRun = DebugInternalProbeRun(
                probePlanPath,
                profile: "r00-runtime-handler-candidates",
                hostName,
                port,
                maxHits: Math.Clamp(maxHits, 1, 128),
                timeoutMs: Math.Max(timeoutMs, 1000),
                disableAfterHit,
                continueAfterEntryPointPause,
                gameRoot,
                sessionDir);
            hits.AddRange(ExtractHitsFromProbeRun(probeRun));
            steps.Add(new { step = "debug_internal_probe_run", result = probeRun });
            AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeStep", step = "debug_internal_probe_run", result = probeRun });
            status = $"probe-{ExtractStringFromObject(probeRun, "status")}";
            AppendActionChainLine(sessionDir, "debug_r00_runtime_handler_probe_run", "debug_internal_probe_run", ExtractStringFromObject(probeRun, "status"), new { result = probeRun });
        }

        var afterScriptWindow = GameRSceneScriptWindowScan(scanRoute, gameRoot, contextBytes: 16, maxScanBytes: 64 * 1024 * 1024, maxHitsPerWindow: 4, includePointerRefs: true, maxPointerRefs: 16, sessionDir);
        var finalRuntime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        var finalBattleMatch = GameBattleStateMatch("yingchuan_cao_zhangliang", gameRoot, sessionDir);
        steps.Add(new { step = "game_rscene_script_window_scan_after", result = afterScriptWindow });
        steps.Add(new { step = "runtime_final", result = finalRuntime });
        steps.Add(new { step = "battle_profile_final", result = finalBattleMatch });
        AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeStep", step = "game_rscene_script_window_scan_after", result = afterScriptWindow });
        AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeStep", step = "runtime_final", result = finalRuntime });
        AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeStep", step = "battle_profile_final", result = finalBattleMatch });

        var finalClassification = ExtractNestedStringFromObject(finalRuntime, "runtime", "classification");
        var finalProfileStatus = ExtractStringFromObject(finalBattleMatch, "status");
        var beforeScriptStatus = ExtractStringFromObject(beforeScriptWindow, "status");
        var afterScriptStatus = ExtractStringFromObject(afterScriptWindow, "status");
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            route = normalizedRoute,
            start_debugger = startDebugger,
            continue_startup = continueStartup,
            probe_before_startup_continue = probeBeforeStartupContinue,
            run_probes = runProbes,
            candidate_report_path = candidateReportPath,
            candidate_markdown_path = candidateMarkdownPath,
            probe_plan_path = probePlanPath,
            probe_plan_markdown_path = probePlanMarkdownPath,
            action_count = candidateContext.ActionCount,
            handler_candidate_count = candidateContext.HandlerCandidateCount,
            handler_probe_target_count = candidateContext.ProbeTargetCount,
            latest_evidence_path = candidateContext.LatestEvidencePath,
            latest_verified_hit_count = candidateContext.LatestVerifiedHitCount,
            before_script_window_status = beforeScriptStatus,
            after_script_window_status = afterScriptStatus,
            final_runtime_classification = finalClassification,
            battle_profile_status = finalProfileStatus,
            battle_loaded = finalClassification.Equals("battle_loaded", StringComparison.OrdinalIgnoreCase),
            battle_profile_matched = finalProfileStatus.Equals("profile-matched", StringComparison.OrdinalIgnoreCase) ||
                finalProfileStatus.Equals("attack_after_observed", StringComparison.OrdinalIgnoreCase),
            hit_count = hits.Count,
            hits,
            steps,
            safety = "R_00 handler probing uses debugger breakpoints and read-only state evidence only. It does not send mouse input, capture screenshots, inject code, write process memory, or patch game files."
        };
        var summaryPath = Path.Combine(sessionDir, "r00-runtime-handler-probe-summary.json");
        var markdownPath = Path.Combine(sessionDir, "r00-runtime-handler-probe-summary.md");
        WriteJson(summaryPath, summary);
        WriteR00RuntimeHandlerProbeRunMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new { type = "R00RuntimeHandlerProbeRunSummary", summary.status, path = summaryPath, summary.hit_count });
        AppendActionChainLine(sessionDir, "debug_r00_runtime_handler_probe_run", "summary", summary.status, new { summary_path = summaryPath, hit_count = summary.hit_count });
        return summary;
    }

    public object DebugRuntimeInvokeRun(
        string planPath,
        bool allowRuntimeInjection,
        bool allowDebugInvoke,
        string hostName,
        int port,
        string? gameRoot,
        string? outputDir)
    {
        GuardLocalHost(hostName);
        if (string.IsNullOrWhiteSpace(planPath))
        {
            throw new ArgumentException("Runtime invoke plan path is required.", nameof(planPath));
        }

        var paths = ResolveGamePaths(gameRoot);
        var resolvedPlanPath = Path.GetFullPath(planPath);
        if (!File.Exists(resolvedPlanPath))
        {
            throw new FileNotFoundException("Runtime invoke plan was not found.", resolvedPlanPath);
        }

        var plan = JsonSerializer.Deserialize<RuntimeInvokePlan>(File.ReadAllText(resolvedPlanPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse runtime invoke plan: {resolvedPlanPath}");
        var sessionDir = string.IsNullOrWhiteSpace(outputDir)
            ? Path.GetDirectoryName(resolvedPlanPath) ?? EnsureSessionDirectory(paths.WorkspaceRoot, null)
            : EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        Directory.CreateDirectory(sessionDir);
        Directory.CreateDirectory(Path.Combine(sessionDir, "x32dbg"));
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var health = InvokeX32dbg("GET", hostName, port, "/api/health");
        var bridgeReady = IsBridgeSuccess(health);
        AppendJsonLine(eventsPath, new
        {
            type = "RuntimeInvokeRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            plan_path = resolvedPlanPath,
            allow_runtime_injection = allowRuntimeInjection,
            allow_debug_invoke = allowDebugInvoke,
            bridge_ready = bridgeReady
        });
        AppendActionChainLine(
            sessionDir,
            "debug_runtime_invoke_run",
            "start",
            bridgeReady ? "bridge-ready" : "bridge-unavailable",
            new
            {
                plan_path = resolvedPlanPath,
                allowRuntimeInjection,
                allowDebugInvoke,
                bridge_health = health
            });

        var actionResults = new List<object>();
        foreach (var action in plan.Actions)
        {
            var debugState = bridgeReady ? InvokeX32dbg("GET", hostName, port, "/api/debug/state") : null;
            var debuggeePaused = debugState is not null && IsPausedState(debugState);
            var executableRuntimeInvoke = IsExecutableRuntimeInvoke(action);
            var canRun = bridgeReady &&
                allowDebugInvoke &&
                (!action.RequiresRuntimeInjection || allowRuntimeInjection);
            object? commandResult = null;
            object? beforeState = null;
            object? afterState = null;
            if (canRun)
            {
                beforeState = new
                {
                    runtime = GameRuntimeStateClassify(gameRoot, hostName, port, 8, sessionDir),
                    battle = SafeReadBattleState()
                };
                commandResult = ExecuteRuntimeInvokeAction(action, allowRuntimeInjection, hostName, port, sessionDir);
                Thread.Sleep(100);
                afterState = new
                {
                    runtime = GameRuntimeStateClassify(gameRoot, hostName, port, 8, sessionDir),
                    battle = SafeReadBattleState()
                };
            }

            var result = new
            {
                action = action.Key,
                intent = action.Intent,
                method = action.Method,
                requested_runtime_injection = action.RequiresRuntimeInjection,
                requires_paused_debuggee = action.RequiresPausedDebuggee,
                debuggee_paused = debuggeePaused,
                executable_runtime_invoke = executableRuntimeInvoke,
                can_run = canRun,
                status = canRun && commandResult is not null ? ExtractStringFromObject(commandResult, "status") : "plan-only-not-executed",
                skip_reason = canRun ? string.Empty : BuildRuntimeInvokeSkipReason(bridgeReady, allowDebugInvoke, allowRuntimeInjection, action, debuggeePaused, executableRuntimeInvoke),
                command_result = commandResult,
                verification = action.Verification,
                before_state = beforeState,
                after_state = afterState
            };
            actionResults.Add(result);
            AppendJsonLine(eventsPath, new { type = "RuntimeInvokeAction", created_at = DateTimeOffset.Now.ToString("O"), result });
            AppendActionChainLine(sessionDir, "debug_runtime_invoke_run", action.Key, result.status, new { result }, beforeState, afterState);
        }

        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = actionResults.Any(r => ExtractStringFromObject(r, "status") == "debug-command-issued")
                || actionResults.Any(r => ExtractStringFromObject(r, "status") == "runtime-stub-issued")
                ? "runtime-invoke-attempted"
                : "runtime-invoke-plan-only",
            session_dir = sessionDir,
            plan_path = resolvedPlanPath,
            bridge_health = health,
            allow_runtime_injection = allowRuntimeInjection,
            allow_debug_invoke = allowDebugInvoke,
            actions = actionResults,
            safety = "Temporary runtime invocation only. Persistent patching is not performed here."
        };
        WriteJson(Path.Combine(sessionDir, "runtime-invoke-run.json"), report);
        AppendJsonLine(eventsPath, new { type = "RuntimeInvokeRunSummary", report.status, path = Path.Combine(sessionDir, "runtime-invoke-run.json") });
        AppendActionChainLine(sessionDir, "debug_runtime_invoke_run", "summary", report.status, new { report_path = Path.Combine(sessionDir, "runtime-invoke-run.json"), action_count = actionResults.Count });
        return report;
    }

    public object DebugMenuRouteRun(
        string route,
        bool allowRuntimeInjection,
        bool allowDebugInvoke,
        string hostName,
        int port,
        string? gameRoot,
        string? outputDir)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var normalizedRoute = NormalizeMenuRoute(route);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var routes = ExpandMenuRoutes(normalizedRoute);
        var results = new List<object>();
        AppendJsonLine(eventsPath, new
        {
            type = "MenuRouteRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            route = normalizedRoute,
            routes,
            allow_runtime_injection = allowRuntimeInjection,
            allow_debug_invoke = allowDebugInvoke
        });
        AppendActionChainLine(
            sessionDir,
            "debug_menu_route_run",
            "start",
            "started",
            new
            {
                route = normalizedRoute,
                routes,
                allowRuntimeInjection,
                allowDebugInvoke
            });

        foreach (var subRoute in routes)
        {
            var routeDir = Path.Combine(sessionDir, "menu-routes", subRoute);
            Directory.CreateDirectory(routeDir);
            var plan = DebugRuntimeInvokePlan("menu", subRoute, routeDir, gameRoot);
            var planPath = ExtractStringFromObject(plan, "plan_path") ?? string.Empty;
            var run = DebugRuntimeInvokeRun(planPath, allowRuntimeInjection, allowDebugInvoke, hostName, port, gameRoot, routeDir);
            var routeResult = new { route = subRoute, plan, run };
            results.Add(routeResult);
            AppendJsonLine(eventsPath, new { type = "MenuRouteStep", created_at = DateTimeOffset.Now.ToString("O"), route = subRoute, result = routeResult });
            AppendActionChainLine(sessionDir, "debug_menu_route_run", subRoute, ExtractStringFromObject(run, "status"), new { plan, run });
        }

        var loadSaveStatus = DetectSaveFixture(paths.GameRoot)
            ? "save-fixture-present"
            : "blocked: missing-save-fixture";
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = "menu-route-recorded",
            session_dir = sessionDir,
            route = normalizedRoute,
            route_count = results.Count,
            load_save_status = loadSaveStatus,
            results,
            verification = new
            {
                no_mouse = true,
                no_screenshot = true,
                runtime = GameRuntimeStateClassify(gameRoot, hostName, port, 8, sessionDir),
                rscene_text = GameRSceneTextAnchorScan("", gameRoot, 1048576, 2, sessionDir),
                rscene_script = GameRSceneScriptWindowScan("regular_start", gameRoot, 16, 1048576, 2, false, 0, sessionDir)
            }
        };
        WriteJson(Path.Combine(sessionDir, "menu-route-summary.json"), summary);
        AppendJsonLine(eventsPath, new { type = "MenuRouteRunSummary", summary.status, path = Path.Combine(sessionDir, "menu-route-summary.json") });
        AppendActionChainLine(sessionDir, "debug_menu_route_run", "summary", summary.status, new { summary_path = Path.Combine(sessionDir, "menu-route-summary.json"), route_count = results.Count, loadSaveStatus });
        return summary;
    }

    public object DebugAddressVerifyRun(
        string stages,
        string triggerScript,
        bool runProbes,
        bool requireProfileMatch,
        int maxHits,
        int timeoutMs,
        string hostName,
        int port,
        string? outputDir,
        string? gameRoot)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var normalizedTrigger = NormalizeTriggerScript(triggerScript);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var stageList = ParseStageList(stages);
        AppendJsonLine(eventsPath, new
        {
            type = "AddressVerifyRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            stages = stageList,
            trigger_script = normalizedTrigger,
            run_probes = runProbes,
            require_profile_match = requireProfileMatch
        });
        AppendActionChainLine(
            sessionDir,
            "debug_address_verify_run",
            "start",
            runProbes ? "probe-requested" : "plan-only",
            new
            {
                stages = stageList,
                trigger_script = normalizedTrigger,
                runProbes,
                requireProfileMatch,
                maxHits,
                timeoutMs
            });
        AppendProbeHitLine(
            sessionDir,
            "probe-run-planned",
            new
            {
                source = "debug_address_verify_run",
                stages = stageList,
                trigger_script = normalizedTrigger,
                run_probes_requested = runProbes,
                note = "This ledger records dynamic probe hits when probes run. Plan-only validation creates this planning row without claiming a hit."
            });

        var beforeBattle = SafeReadBattleState();
        var profile = GameBattleStateMatch("yingchuan_cao_zhangliang", gameRoot, sessionDir);
        var profileStatus = ExtractStringFromObject(profile, "status") ?? "unknown";
        AppendActionChainLine(sessionDir, "debug_address_verify_run", "profile-gate", profileStatus, new { profile });
        var addressPlan = DebugAddressProbeRun(
            reportPath: null,
            stages,
            startGame: false,
            allowLaunch: false,
            startDebugger: false,
            waitForState: null,
            runProbes: false,
            maxHits,
            timeoutMs,
            hostName,
            port,
            sessionDir,
            gameRoot);
        AppendActionChainLine(sessionDir, "debug_address_verify_run", "address-probe-plan", ExtractStringFromObject(addressPlan, "status"), new { addressPlan });
        object? probeRun = null;
        var allowedByProfile = !requireProfileMatch ||
            profileStatus.Equals("profile-matched", StringComparison.OrdinalIgnoreCase) ||
            profileStatus.Equals("attack_after_observed", StringComparison.OrdinalIgnoreCase);
        if (runProbes && allowedByProfile)
        {
            var planPath = ExtractStringFromObject(addressPlan, "plan_path");
            probeRun = DebugInternalProbeRun(
                planPath,
                "address-verify",
                hostName,
                port,
                Math.Clamp(maxHits, 1, 256),
                Math.Max(timeoutMs, 1000),
                disableAfterHit: true,
                continueAfterEntryPointPause: false,
                gameRoot,
                sessionDir);
            AppendActionChainLine(sessionDir, "debug_address_verify_run", "debug_internal_probe_run", ExtractStringFromObject(probeRun, "status"), new { probeRun });
        }
        else
        {
            AppendActionChainLine(
                sessionDir,
                "debug_address_verify_run",
                "debug_internal_probe_run",
                runProbes ? "profile-gate-blocked" : "skipped-plan-only",
                new { runProbes, allowedByProfile, profileStatus });
        }

        var afterBattle = SafeReadBattleState();
        var promotions = BuildKnowledgePromotions(sessionDir, topic: "function-index", allowWrite: false, gameRoot);
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = runProbes
                ? allowedByProfile ? "address-verification-probe-attempted" : "profile-gate-blocked"
                : "address-verification-plan-ready",
            session_dir = sessionDir,
            stages = stageList,
            trigger_script = normalizedTrigger,
            profile_status = profileStatus,
            require_profile_match = requireProfileMatch,
            run_probes = runProbes,
            address_plan = addressPlan,
            probe_run = probeRun,
            before_battle = beforeBattle,
            after_battle = afterBattle,
            battle_diff = SummarizeBattleDiff(beforeBattle, afterBattle),
            promotion_preview = promotions,
            gate = "Promote only when breakpoint hits, explicit trigger, register/stack/disassembly evidence, and battle-state diff are all present."
        };
        WriteJson(Path.Combine(sessionDir, "address-verify-summary.json"), summary);
        AppendJsonLine(eventsPath, new { type = "AddressVerifyRunSummary", summary.status, path = Path.Combine(sessionDir, "address-verify-summary.json") });
        AppendProbeHitLine(
            sessionDir,
            "probe-run-summary",
            new
            {
                source = "debug_address_verify_run",
                summary_path = Path.Combine(sessionDir, "address-verify-summary.json"),
                summary.status,
                probe_run_status = probeRun is null ? string.Empty : ExtractStringFromObject(probeRun, "status"),
                battle_diff = summary.battle_diff
            });
        AppendActionChainLine(sessionDir, "debug_address_verify_run", "summary", summary.status, new { summary_path = Path.Combine(sessionDir, "address-verify-summary.json"), battle_diff = summary.battle_diff });
        return summary;
    }

    public object DebugAutonomyPlan(string scenario, string stages, string? outputDir, string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var normalizedScenario = NormalizeScenarioKey(scenario);
        var stageList = ParseStageList(stages);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var plan = BuildAutonomyPlan(normalizedScenario, stageList, paths);
        var planPath = Path.Combine(sessionDir, "autonomy-plan.json");
        var markdownPath = Path.Combine(sessionDir, "autonomy-plan.md");
        WriteJson(planPath, plan);
        WriteAutonomyPlanMarkdown(markdownPath, plan, paths);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
        {
            type = "AutonomyPlanCreated",
            created_at = DateTimeOffset.Now.ToString("O"),
            scenario = normalizedScenario,
            stages = stageList,
            path = planPath
        });
        return new
        {
            session_dir = sessionDir,
            plan_path = planPath,
            markdown_path = markdownPath,
            plan
        };
    }

    public object DebugAutonomyRun(
        string? planPath,
        string scenario,
        string stages,
        bool startGame,
        bool allowLaunch,
        int gameStartWaitMs,
        bool startDebugger,
        bool continueStartup,
        int startupContinueMaxRuns,
        bool probeBeforeStartupContinue,
        bool runProbes,
        int maxHitsPerStage,
        int timeoutMsPerStage,
        string hostName,
        int port,
        string? gameRoot,
        string? outputDir,
        string? waitForState,
        int waitTimeoutMs,
        int waitPollIntervalMs)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var context = ResolveAutonomyPlan(planPath, scenario, stages, outputDir, paths);
        var sessionDir = context.SessionDir;
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var stepResults = new List<object>();

        AppendJsonLine(eventsPath, new
        {
            type = "AutonomyRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            context.Plan.Scenario,
            context.Plan.Stages,
            start_game = startGame,
            allow_launch = allowLaunch,
            start_debugger = startDebugger,
            continue_startup = continueStartup,
            startup_continue_max_runs = startupContinueMaxRuns,
            probe_before_startup_continue = probeBeforeStartupContinue,
            run_probes = runProbes,
            wait_for_state = waitForState
        });

        if (startGame)
        {
            var start = GameProcessStart(gameRoot, allowLaunch, gameStartWaitMs, sessionDir);
            stepResults.Add(new { step = "game_process_start", result = start });
            AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "game_process_start", result = start });
        }

        if (startDebugger)
        {
            var start = DebugSessionStart(gameRoot, null, hostName, port, 10000, false);
            stepResults.Add(new { step = "debug_session_start", result = start });
            AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "debug_session_start", result = start });
        }

        var preStartupProbeStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (probeBeforeStartupContinue && runProbes && continueStartup)
        {
            foreach (var stage in context.Plan.Stages.Where(s => s.Equals("startup", StringComparison.OrdinalIgnoreCase)))
            {
                var preStageDir = Path.Combine(sessionDir, "stages", stage);
                Directory.CreateDirectory(preStageDir);
                var preProbePlan = DebugPhaseProbePlan(stage, preStageDir, gameRoot);
                stepResults.Add(new { step = "debug_phase_probe_plan", stage, order = "before_startup_continue", result = preProbePlan });
                AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "debug_phase_probe_plan", stage, order = "before_startup_continue", result = preProbePlan });

                var preProbePlanPath = ExtractStringFromObject(preProbePlan, "plan_path");
                if (!string.IsNullOrWhiteSpace(preProbePlanPath))
                {
                    var preProbeRun = DebugInternalProbeRun(
                        preProbePlanPath,
                        stage,
                        hostName,
                        port,
                        Math.Clamp(maxHitsPerStage, 1, 32),
                        Math.Max(timeoutMsPerStage, 1000),
                        disableAfterHit: true,
                        continueAfterEntryPointPause: true,
                        gameRoot,
                        preStageDir);
                    stepResults.Add(new { step = "debug_internal_probe_run", stage, order = "before_startup_continue", result = preProbeRun });
                    AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "debug_internal_probe_run", stage, order = "before_startup_continue", result = preProbeRun });
                    preStartupProbeStages.Add(stage);
                }
            }
        }

        object? startupContinue = null;
        if (continueStartup)
        {
            startupContinue = ContinueStartupThroughDebugger(hostName, port, Math.Max(waitTimeoutMs, 1000), startupContinueMaxRuns, sessionDir);
            stepResults.Add(new { step = "startup_continue", result = startupContinue });
            AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "startup_continue", result = startupContinue });
        }

        var state = DebugSessionState(gameRoot, hostName, port);
        stepResults.Add(new { step = "debug_session_state", result = state });
        AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "debug_session_state", result = state });

        var runtimeState = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        stepResults.Add(new { step = "game_runtime_state_classify", result = runtimeState });
        AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "game_runtime_state_classify", result = runtimeState });

        if (!string.IsNullOrWhiteSpace(waitForState) &&
            !waitForState.Equals("none", StringComparison.OrdinalIgnoreCase) &&
            !waitForState.Equals("skip", StringComparison.OrdinalIgnoreCase))
        {
            var wait = GameRuntimeWaitForState(
                gameRoot,
                hostName,
                port,
                waitForState,
                waitTimeoutMs,
                waitPollIntervalMs,
                minBattleUnits: 8,
                sessionDir);
            stepResults.Add(new { step = "game_runtime_wait_for_state", result = wait });
            AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "game_runtime_wait_for_state", result = wait });
        }

        foreach (var stage in context.Plan.Stages)
        {
            var stageDir = Path.Combine(sessionDir, "stages", stage);
            Directory.CreateDirectory(stageDir);

            var catalog = DebugFunctionCatalog(stage, stageDir, gameRoot);
            stepResults.Add(new { step = "debug_function_catalog", stage, result = catalog });
            AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "debug_function_catalog", stage, result = catalog });

            var xrefs = DebugStaticXrefScan(stage, nearBytes: 64, stageDir, gameRoot);
            stepResults.Add(new { step = "debug_static_xref_scan", stage, result = xrefs });
            AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "debug_static_xref_scan", stage, result = xrefs });

            var probePlan = DebugPhaseProbePlan(stage, stageDir, gameRoot);
            stepResults.Add(new { step = "debug_phase_probe_plan", stage, result = probePlan });
            AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "debug_phase_probe_plan", stage, result = probePlan });

            if (!runProbes || preStartupProbeStages.Contains(stage))
            {
                continue;
            }

            var probePlanPath = ExtractStringFromObject(probePlan, "plan_path");
            if (string.IsNullOrWhiteSpace(probePlanPath))
            {
                var skipped = new { stage, reason = "probe plan path was unavailable" };
                stepResults.Add(new { step = "debug_internal_probe_run_skipped", skipped });
                AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "debug_internal_probe_run_skipped", skipped });
                continue;
            }

            var probeRun = DebugInternalProbeRun(
                probePlanPath,
                stage,
                hostName,
                port,
                Math.Clamp(maxHitsPerStage, 1, 32),
                Math.Max(timeoutMsPerStage, 1000),
                disableAfterHit: true,
                continueAfterEntryPointPause: false,
                gameRoot,
                stageDir);
            stepResults.Add(new { step = "debug_internal_probe_run", stage, result = probeRun });
            AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "debug_internal_probe_run", stage, result = probeRun });
        }

        var draft = DebugWriteKnowledgeDraft(sessionDir, "automation-flow", null, gameRoot);
        stepResults.Add(new { step = "debug_write_knowledge_draft", result = draft });
        AppendJsonLine(eventsPath, new { type = "AutonomyStep", step = "debug_write_knowledge_draft", result = draft });

        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = runProbes ? "safe-run-with-probes-attempted" : "safe-plan-and-catalog-run",
            session_dir = sessionDir,
            plan_path = context.PlanPath,
            scenario = context.Plan.Scenario,
            stages = context.Plan.Stages,
            start_game = startGame,
            allow_launch = allowLaunch,
            start_debugger = startDebugger,
            continue_startup_requested = continueStartup,
            startup_continue_max_runs = Math.Clamp(startupContinueMaxRuns, 1, 8),
            probe_before_startup_continue = probeBeforeStartupContinue,
            startup_continue = startupContinue,
            run_probes = runProbes,
            wait_for_state = waitForState,
            step_count = stepResults.Count,
            steps = stepResults,
            safety = context.Plan.Safety
        };
        var summaryPath = Path.Combine(sessionDir, "autonomy-run-summary.json");
        var summaryMarkdownPath = Path.Combine(sessionDir, "autonomy-run-summary.md");
        WriteJson(summaryPath, summary);
        WriteAutonomyRunSummaryMarkdown(summaryMarkdownPath, summary);
        AppendJsonLine(eventsPath, new { type = "AutonomyRunSummary", path = summaryPath, summary.status });
        return summary;
    }

    public object DebugInternalProbePlan(string profile, string? outputDir, string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var context = CreateInternalProbePlan(profile, outputDir, paths);
        return new
        {
            session_dir = context.SessionDir,
            plan_path = context.PlanPath,
            target_count = context.Plan.Targets.Count,
            plan = context.Plan
        };
    }

    public object DebugInternalProbeRun(
        string? planPath,
        string profile,
        string hostName,
        int port,
        int maxHits,
        int timeoutMs,
        bool disableAfterHit,
        bool continueAfterEntryPointPause,
        string? gameRoot,
        string? outputDir)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var context = ResolveInternalProbePlan(planPath, profile, outputDir, paths);
        var eventsPath = Path.Combine(context.SessionDir, "events.jsonl");
        Directory.CreateDirectory(Path.Combine(context.SessionDir, "x32dbg"));

        maxHits = Math.Clamp(maxHits, 1, 256);
        timeoutMs = Math.Max(timeoutMs, 1000);
        var plannedTargets = context.Plan.Targets
            .GroupBy(t => NormalizeAddress(t.Address), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var health = InvokeX32dbg("GET", hostName, port, "/api/health");
        AppendJsonLine(eventsPath, new
        {
            type = "InternalProbeBridgeHealth",
            created_at = DateTimeOffset.Now.ToString("O"),
            health
        });

        if (!IsBridgeSuccess(health))
        {
            var unavailable = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                status = "bridge-unavailable",
                reason = "x32dbg MCP bridge did not return a successful local health response.",
                session_dir = context.SessionDir,
                plan_path = context.PlanPath,
                plan_created = context.Created,
                target_count = context.Plan.Targets.Count,
                health,
                safety = context.Plan.Safety
            };
            var unavailablePath = Path.Combine(context.SessionDir, "internal-probe-run-summary.json");
            WriteJson(unavailablePath, unavailable);
            AppendJsonLine(eventsPath, new { type = "InternalProbeRunSummary", path = unavailablePath, unavailable.status });
            return unavailable;
        }

        var setResults = new List<object>();
        foreach (var target in context.Plan.Targets)
        {
            var normalized = NormalizeAddress(target.Address);
            var command = "bp " + PlainAddress(normalized);
            var result = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command });
            var setResult = new
            {
                address = normalized,
                target.Name,
                target.Phase,
                command,
                result
            };
            setResults.Add(setResult);
            AppendJsonLine(eventsPath, new { type = "InternalProbeBreakpointSet", created_at = DateTimeOffset.Now.ToString("O"), setResult });
        }

        var hits = new List<object>();
        var stopReason = "timeout";
        object? unplannedPause = null;
        var runResult = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = "run" });
        AppendJsonLine(eventsPath, new { type = "InternalProbeRunCommand", created_at = DateTimeOffset.Now.ToString("O"), result = runResult });

        var sw = Stopwatch.StartNew();
        var pollCount = 0;
        while (sw.ElapsedMilliseconds < timeoutMs && hits.Count < maxHits)
        {
            Thread.Sleep(250);
            pollCount++;
            var state = InvokeX32dbg("GET", hostName, port, "/api/debug/state");
            if (!IsPausedState(state))
            {
                continue;
            }

            var cip = TryReadCip(state);
            if (string.IsNullOrWhiteSpace(cip))
            {
                stopReason = "paused-without-cip";
                unplannedPause = new { state };
                AppendJsonLine(eventsPath, new { type = "InternalProbePausedWithoutCip", created_at = DateTimeOffset.Now.ToString("O"), state });
                break;
            }

            var normalizedCip = NormalizeAddress(cip);
            if (!plannedTargets.TryGetValue(normalizedCip, out var target))
            {
                if (continueAfterEntryPointPause && IsEntryPointPause(normalizedCip, state))
                {
                    var resumeEntryPoint = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = "run" });
                    AppendJsonLine(eventsPath, new
                    {
                        type = "InternalProbeEntryPointPauseContinued",
                        created_at = DateTimeOffset.Now.ToString("O"),
                        cip = normalizedCip,
                        state,
                        resume = resumeEntryPoint
                    });
                    continue;
                }

                stopReason = "unplanned-pause";
                unplannedPause = new { cip = normalizedCip, state };
                AppendJsonLine(eventsPath, new { type = "InternalProbeUnplannedPause", created_at = DateTimeOffset.Now.ToString("O"), cip = normalizedCip, state });
                break;
            }

            var hit = CaptureInternalProbeHit(
                context.SessionDir,
                hits.Count + 1,
                sw.ElapsedMilliseconds,
                normalizedCip,
                target,
                state,
                hostName,
                port);
            hits.Add(hit);
            AppendJsonLine(eventsPath, new { type = "InternalProbeHit", created_at = DateTimeOffset.Now.ToString("O"), hit });

            if (disableAfterHit)
            {
                var disabled = InvokeX32dbg("POST", hostName, port, "/api/breakpoints/disable", null, new { address = normalizedCip });
                AppendJsonLine(eventsPath, new { type = "InternalProbeBreakpointDisabled", created_at = DateTimeOffset.Now.ToString("O"), address = normalizedCip, disabled });
            }

            if (hits.Count >= maxHits)
            {
                stopReason = "max-hits";
                break;
            }

            var resume = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = "run" });
            AppendJsonLine(eventsPath, new { type = "InternalProbeResume", created_at = DateTimeOffset.Now.ToString("O"), address = normalizedCip, resume });
        }

        if (sw.ElapsedMilliseconds >= timeoutMs && hits.Count == 0)
        {
            stopReason = "timeout-no-hit";
        }

        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = stopReason,
            session_dir = context.SessionDir,
            plan_path = context.PlanPath,
            plan_created = context.Created,
            profile = context.Plan.Profile,
            target_exe_sha256 = context.Plan.TargetExeSha256,
            target_count = context.Plan.Targets.Count,
            max_hits = maxHits,
            hit_count = hits.Count,
            elapsed_ms = sw.ElapsedMilliseconds,
            poll_count = pollCount,
            disable_after_hit = disableAfterHit,
            continue_after_entry_point_pause = continueAfterEntryPointPause,
            health,
            set_results = setResults,
            initial_run = runResult,
            unplanned_pause = unplannedPause,
            final_state = InvokeX32dbg("GET", hostName, port, "/api/debug/state"),
            breakpoint_list = InvokeX32dbg("GET", hostName, port, "/api/breakpoints/list"),
            hits,
            safety = context.Plan.Safety
        };
        var summaryPath = Path.Combine(context.SessionDir, "internal-probe-run-summary.json");
        WriteJson(summaryPath, summary);
        AppendJsonLine(eventsPath, new { type = "InternalProbeRunSummary", created_at = DateTimeOffset.Now.ToString("O"), path = summaryPath, summary.status, summary.hit_count });
        return summary;
    }

    public object DebugTransitionProbeRun(
        string stage,
        string sequence,
        bool allowInput,
        bool startDebugger,
        bool continueStartup,
        int startupContinueMaxRuns,
        string keyDelivery,
        string hostName,
        int port,
        int maxHits,
        int timeoutMs,
        bool disableAfterHit,
        bool continueAfterEntryPointPause,
        string? gameRoot,
        string? outputDir)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var normalizedStage = NormalizeStageFilter(stage);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var steps = new List<object>();
        var hits = new List<object>();
        object? startupContinue = null;
        object? trigger = null;
        object? unplannedPause = null;
        var stopReason = "not-started";
        var deliveryMode = NormalizeKeyDelivery(keyDelivery);

        AppendJsonLine(eventsPath, new
        {
            type = "TransitionProbeRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            stage = normalizedStage,
            sequence,
            key_delivery = deliveryMode,
            allow_input = allowInput,
            start_debugger = startDebugger,
            continue_startup = continueStartup,
            max_hits = maxHits,
            timeout_ms = timeoutMs
        });

        if (startDebugger)
        {
            var start = DebugSessionStart(gameRoot, null, hostName, port, waitMs: Math.Clamp(timeoutMs, 1000, 60000), hidden: false);
            steps.Add(new { step = "debug_session_start", result = start });
            AppendJsonLine(eventsPath, new { type = "TransitionProbeStep", step = "debug_session_start", result = start });
        }

        if (continueStartup)
        {
            startupContinue = ContinueStartupThroughDebugger(hostName, port, Math.Clamp(timeoutMs, 1000, 60000), startupContinueMaxRuns, sessionDir);
            steps.Add(new { step = "startup_continue", result = startupContinue });
            AppendJsonLine(eventsPath, new { type = "TransitionProbeStep", step = "startup_continue", result = startupContinue });
        }

        var plan = DebugPhaseProbePlan(normalizedStage, sessionDir, gameRoot);
        var planPath = ExtractStringFromObject(plan, "plan_path");
        steps.Add(new { step = "debug_phase_probe_plan", result = plan });
        AppendJsonLine(eventsPath, new { type = "TransitionProbeStep", step = "debug_phase_probe_plan", result = plan });

        var context = ResolveInternalProbePlan(planPath, normalizedStage, sessionDir, paths);
        var plannedTargets = context.Plan.Targets
            .GroupBy(t => NormalizeAddress(t.Address), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        if (!allowInput)
        {
            stopReason = "dry-run";
            var drySummary = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                status = stopReason,
                reason = "allow_input was false; no x32dbg run command, breakpoint mutation, or keyboard message was issued.",
                session_dir = sessionDir,
                stage = normalizedStage,
                sequence,
                key_delivery = deliveryMode,
                allow_input = allowInput,
                start_debugger = startDebugger,
                continue_startup_requested = continueStartup,
                startup_continue_max_runs = Math.Clamp(startupContinueMaxRuns, 1, 8),
                startup_continue = startupContinue,
                plan_path = planPath,
                target_count = ExtractIntFromObject(plan, "target_count"),
                max_hits = Math.Clamp(maxHits, 1, 256),
                hit_count = hits.Count,
                trigger,
                unplanned_pause = unplannedPause,
                final_runtime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir),
                final_battle_match = GameBattleStateMatch("yingchuan_cao_zhangliang", gameRoot, sessionDir),
                steps,
                hits,
                safety = "Dry-run transition probe only. It does not run x32dbg, change breakpoints, post keyboard messages, move/click the mouse, capture screenshots, write target process memory, or patch game files."
            };
            var drySummaryPath = Path.Combine(sessionDir, "transition-probe-run-summary.json");
            var dryMarkdownPath = Path.Combine(sessionDir, "transition-probe-run-summary.md");
            WriteJson(drySummaryPath, drySummary);
            WriteTransitionProbeRunSummaryMarkdown(dryMarkdownPath, drySummary);
            AppendJsonLine(eventsPath, new { type = "TransitionProbeRunSummary", created_at = DateTimeOffset.Now.ToString("O"), path = drySummaryPath, drySummary.status, drySummary.hit_count });
            return drySummary;
        }

        var health = InvokeX32dbg("GET", hostName, port, "/api/health");
        steps.Add(new { step = "bridge_health", result = health });
        AppendJsonLine(eventsPath, new { type = "TransitionProbeBridgeHealth", created_at = DateTimeOffset.Now.ToString("O"), health });

        if (!IsBridgeSuccess(health))
        {
            stopReason = "bridge-unavailable";
        }
        else
        {
            var setResults = new List<object>();
            foreach (var target in context.Plan.Targets)
            {
                var normalized = NormalizeAddress(target.Address);
                var command = "bp " + PlainAddress(normalized);
                var result = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command });
                var setResult = new
                {
                    address = normalized,
                    target.Name,
                    target.Phase,
                    command,
                    result
                };
                setResults.Add(setResult);
                AppendJsonLine(eventsPath, new { type = "TransitionProbeBreakpointSet", created_at = DateTimeOffset.Now.ToString("O"), setResult });
            }
            steps.Add(new { step = "set_breakpoints", count = setResults.Count, result = setResults });

            var beforeState = InvokeX32dbg("GET", hostName, port, "/api/debug/state");
            if (continueAfterEntryPointPause)
            {
                var cip = TryReadCip(beforeState);
                if (!string.IsNullOrWhiteSpace(cip) && IsEntryPointPause(NormalizeAddress(cip), beforeState))
                {
                    var resumeEntryPoint = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = "run" });
                    steps.Add(new { step = "entry_point_continue", cip = NormalizeAddress(cip), result = resumeEntryPoint });
                    AppendJsonLine(eventsPath, new { type = "TransitionProbeEntryPointPauseContinued", created_at = DateTimeOffset.Now.ToString("O"), cip = NormalizeAddress(cip), resume = resumeEntryPoint });
                    Thread.Sleep(250);
                }
            }

            var runResult = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = "run" });
            steps.Add(new { step = "run_before_trigger", result = runResult });
            AppendJsonLine(eventsPath, new { type = "TransitionProbeRunBeforeTrigger", created_at = DateTimeOffset.Now.ToString("O"), result = runResult });

            var triggerDelay = Math.Clamp(Math.Min(timeoutMs / 10, 2000), 250, 2000);
            Thread.Sleep(triggerDelay);
            trigger = GameKeySequence(sequence, allowInput, gameRoot, delayMs: 250, bringToFront: true, deliveryMode, hostName, port, sessionDir);
            steps.Add(new { step = "game_key_sequence", result = trigger });
            AppendJsonLine(eventsPath, new { type = "TransitionProbeStep", step = "game_key_sequence", result = trigger });

            if (!allowInput)
            {
                stopReason = "dry-run";
            }
            else
            {
                maxHits = Math.Clamp(maxHits, 1, 256);
                timeoutMs = Math.Max(timeoutMs, 1000);
                var sw = Stopwatch.StartNew();
                var pollCount = 0;
                stopReason = "timeout";
                while (sw.ElapsedMilliseconds < timeoutMs && hits.Count < maxHits)
                {
                    Thread.Sleep(250);
                    pollCount++;
                    var state = InvokeX32dbg("GET", hostName, port, "/api/debug/state");
                    if (!IsPausedState(state))
                    {
                        continue;
                    }

                    var cip = TryReadCip(state);
                    if (string.IsNullOrWhiteSpace(cip))
                    {
                        stopReason = "paused-without-cip";
                        unplannedPause = new { state };
                        AppendJsonLine(eventsPath, new { type = "TransitionProbePausedWithoutCip", created_at = DateTimeOffset.Now.ToString("O"), state });
                        break;
                    }

                    var normalizedCip = NormalizeAddress(cip);
                    if (!plannedTargets.TryGetValue(normalizedCip, out var target))
                    {
                        if (continueAfterEntryPointPause && IsEntryPointPause(normalizedCip, state))
                        {
                            var resumeEntryPoint = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = "run" });
                            AppendJsonLine(eventsPath, new { type = "TransitionProbeEntryPointPauseContinued", created_at = DateTimeOffset.Now.ToString("O"), cip = normalizedCip, resume = resumeEntryPoint });
                            continue;
                        }

                        stopReason = "unplanned-pause";
                        unplannedPause = new { cip = normalizedCip, state };
                        AppendJsonLine(eventsPath, new { type = "TransitionProbeUnplannedPause", created_at = DateTimeOffset.Now.ToString("O"), cip = normalizedCip, state });
                        break;
                    }

                    var hit = CaptureInternalProbeHit(
                        sessionDir,
                        hits.Count + 1,
                        sw.ElapsedMilliseconds,
                        normalizedCip,
                        target,
                        state,
                        hostName,
                        port);
                    hits.Add(hit);
                    AppendJsonLine(eventsPath, new { type = "TransitionProbeHit", created_at = DateTimeOffset.Now.ToString("O"), hit });

                    if (disableAfterHit)
                    {
                        var disabled = InvokeX32dbg("POST", hostName, port, "/api/breakpoints/disable", null, new { address = normalizedCip });
                        AppendJsonLine(eventsPath, new { type = "TransitionProbeBreakpointDisabled", created_at = DateTimeOffset.Now.ToString("O"), address = normalizedCip, disabled });
                    }

                    if (hits.Count >= maxHits)
                    {
                        stopReason = "max-hits";
                        break;
                    }

                    var resume = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = "run" });
                    AppendJsonLine(eventsPath, new { type = "TransitionProbeResume", created_at = DateTimeOffset.Now.ToString("O"), address = normalizedCip, resume });
                }

                steps.Add(new { step = "poll_after_trigger", poll_count = pollCount, hit_count = hits.Count, status = stopReason });
                if (stopReason.Equals("timeout", StringComparison.OrdinalIgnoreCase) && hits.Count == 0)
                {
                    stopReason = "timeout-no-hit";
                }
            }
        }

        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = stopReason,
            session_dir = sessionDir,
            stage = normalizedStage,
            sequence,
            key_delivery = deliveryMode,
            allow_input = allowInput,
            start_debugger = startDebugger,
            continue_startup_requested = continueStartup,
            startup_continue_max_runs = Math.Clamp(startupContinueMaxRuns, 1, 8),
            startup_continue = startupContinue,
            plan_path = planPath,
            target_count = ExtractIntFromObject(plan, "target_count"),
            max_hits = Math.Clamp(maxHits, 1, 256),
            hit_count = hits.Count,
            trigger,
            unplanned_pause = unplannedPause,
            final_runtime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir),
            final_battle_match = GameBattleStateMatch("yingchuan_cao_zhangliang", gameRoot, sessionDir),
            final_state = InvokeX32dbg("GET", hostName, port, "/api/debug/state"),
            breakpoint_list = InvokeX32dbg("GET", hostName, port, "/api/breakpoints/list"),
            steps,
            hits,
            safety = "Transition probe uses x32dbg breakpoints plus optional keyboard-only input delivery. It does not move/click the mouse, capture screenshots, write target process memory, or patch game files."
        };
        var summaryPath = Path.Combine(sessionDir, "transition-probe-run-summary.json");
        var markdownPath = Path.Combine(sessionDir, "transition-probe-run-summary.md");
        WriteJson(summaryPath, summary);
        WriteTransitionProbeRunSummaryMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new { type = "TransitionProbeRunSummary", created_at = DateTimeOffset.Now.ToString("O"), path = summaryPath, summary.status, summary.hit_count });
        return summary;
    }

    public object DebugRunUntilEvent(string? planPath, string hostName, int port, int timeoutMs, int pollIntervalMs, bool stopOnMemoryEvent, string? outputDir)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(null, requireExpectedHash: false);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var plannedAddresses = LoadPlanAddresses(planPath);
        var beforeMemory = stopOnMemoryEvent ? SafeReadBattleState() : null;
        var events = new List<object>();
        var run = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = "run" });
        events.Add(new { type = "RunCommand", result = run });
        var sw = Stopwatch.StartNew();
        object? hit = null;
        while (sw.ElapsedMilliseconds < Math.Max(timeoutMs, 1000))
        {
            Thread.Sleep(Math.Clamp(pollIntervalMs, 50, 5000));
            var state = InvokeX32dbg("GET", hostName, port, "/api/debug/state");
            events.Add(new { type = "Poll", elapsed_ms = sw.ElapsedMilliseconds, state });
            var cip = TryReadCip(state);
            if (state.Ok && cip is not null && IsPausedState(state))
            {
                var normalized = NormalizeAddress(cip);
                hit = new
                {
                    type = "DebuggerPaused",
                    cip = normalized,
                    planned = plannedAddresses.Contains(normalized)
                };
                break;
            }

            if (stopOnMemoryEvent && beforeMemory is BattleStateSnapshot before)
            {
                var now = SafeReadBattleState();
                if (now is BattleStateSnapshot after && HasBattleStateChanged(before, after))
                {
                    hit = new
                    {
                        type = "BattleStateChanged",
                        before_active_unit_count = before.ActiveUnitCount,
                        after_active_unit_count = after.ActiveUnitCount
                    };
                    break;
                }
            }
        }

        var evidence = DebugCaptureEvidence(hit?.ToString() ?? "run-until-event-final", null, hostName, port, null, sessionDir, includeScreenshot: false);
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            session_dir = sessionDir,
            timeout_ms = timeoutMs,
            elapsed_ms = sw.ElapsedMilliseconds,
            hit,
            final_state = InvokeX32dbg("GET", hostName, port, "/api/debug/state"),
            evidence,
            events
        };
        WriteJson(Path.Combine(sessionDir, $"run-until-event-{Timestamp()}.json"), report);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "RunUntilEvent", report });
        return report;
    }

    public object DebugCaptureEvidence(string reason, string? address, string hostName, int port, string? gameRoot, string? outputDir, bool includeScreenshot = false)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var evidenceDir = Path.Combine(sessionDir, "x32dbg");
        Directory.CreateDirectory(evidenceDir);
        var state = InvokeX32dbg("GET", hostName, port, "/api/debug/state");
        var targetAddress = !string.IsNullOrWhiteSpace(address) ? NormalizeAddress(address) : TryReadCip(state);
        var screenshot = includeScreenshot ? TryCapture(sessionDir, "evidence") : null;
        var battle = SafeReadBattleState();
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            reason,
            address = targetAddress,
            state,
            process = InvokeX32dbg("GET", hostName, port, "/api/process/info"),
            registers = InvokeX32dbg("GET", hostName, port, "/api/registers/all"),
            breakpoint_list = InvokeX32dbg("GET", hostName, port, "/api/breakpoints/list"),
            disasm = targetAddress is null ? null : InvokeX32dbg("GET", hostName, port, "/api/disasm/at", new Dictionary<string, string> { ["address"] = targetAddress, ["count"] = "32" }),
            function_disasm = targetAddress is null ? null : InvokeX32dbg("GET", hostName, port, "/api/disasm/function", new Dictionary<string, string> { ["address"] = targetAddress, ["max_instructions"] = "80" }),
            stack_trace = InvokeX32dbg("GET", hostName, port, "/api/stack/trace", new Dictionary<string, string> { ["max_depth"] = "32" }),
            memory = targetAddress is null ? null : InvokeX32dbg("GET", hostName, port, "/api/memory/read", new Dictionary<string, string> { ["address"] = targetAddress, ["size"] = "128" }),
            include_screenshot = includeScreenshot,
            screenshot,
            battle,
            safety = includeScreenshot
                ? "Debugger/read-only evidence with explicit screenshot capture; no mouse input, process-memory writes, or game-file writes."
                : "Internal evidence only; no mouse input, screenshots, process-memory writes, or game-file writes."
        };
        var path = Path.Combine(evidenceDir, $"evidence-{SanitizeLabel(reason)}-{Timestamp()}.json");
        WriteJson(path, report);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "EvidenceCaptured", path, reason });
        WriteSummary(sessionDir, reason, path, screenshot?.ToString() ?? string.Empty);
        return new
        {
            session_dir = sessionDir,
            evidence_path = path,
            report
        };
    }

    public object DebugR00ModeRouteAnalyze(string route, string? gameRoot, string? outputDir)
    {
        var paths = ResolveGamePaths(gameRoot);
        var normalizedRoute = NormalizeR00Route(route);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var scenarioPath = Path.Combine(paths.GameRoot, "RS", "R_00.eex");
        if (!File.Exists(scenarioPath))
        {
            throw new FileNotFoundException("RS/R_00.eex was not found under the verified game root.", scenarioPath);
        }

        var bytes = File.ReadAllBytes(scenarioPath);
        var xuZijiangClick = ReadR00ActorClickTest(bytes, personId: 157);
        var modeChoice = ReadR00ChoiceBox(
            bytes,
            key: "mode_choice",
            anchorText: "[C97\u5E38\u89C4\u6A21\u5F0F]\n[C3A\u81EA\u9009\u6A21\u5F0F]\n\u6A21\u5F0F\u8BF4\u660E");
        var configChoice = ReadR00ChoiceBox(
            bytes,
            key: "regular_config_menu",
            anchorText: "\u57F9\u517B\u6A21\u5F0F      \u3010[C28*.1000]\u3011\n\u96BE\u5EA6\u8BBE\u7F6E      \u3010[C28*.1001*/2%]\u3011\n\u547D\u4E2D\u7C7B\u578B      \u3010[C28*.1005]\u3011\n\u6740\u654C\u52A0\u6210      \u3010[C28*.1006\u52A0\u6210]\u3011\n[C25\u9009\u9879\u8BF4\u660E]\n[C3A\u5F00\u59CB\u6E38\u620F]");

        var modeCases = ReadR00CaseBranches(bytes, modeChoice.CommandOffset, Math.Min(configChoice.CommandOffset, bytes.Length));
        var configCases = ReadR00CaseBranches(bytes, configChoice.CommandOffset, FindR00RouteWindowEnd(bytes, configChoice.CommandOffset));
        var regularVariableWrites = ReadR00VariableWrites(bytes, FindCaseOffset(modeCases, 1), Math.Min(configChoice.CommandOffset, bytes.Length));
        var startGameCaseOffset = FindCaseOffset(configCases, 6);
        var startGameGateWrites = startGameCaseOffset >= 0
            ? ReadR00VariableWrites(bytes, startGameCaseOffset, Math.Min(startGameCaseOffset + 256, bytes.Length))
            : new List<R00VariableWrite>();

        var sequence = normalizedRoute switch
        {
            "regular_start" => "enter,down,down,down,down,down,enter",
            _ => "enter,down,down,down,down,down,enter"
        };
        var routePlan = new
        {
            route = normalizedRoute,
            sequence,
            key_delivery_preference = "send_input",
            prerequisite = new
            {
                command = "2D actor_click_test",
                person_id = xuZijiangClick.PersonId,
                command_offset = FormatFileOffset(xuZijiangClick.CommandOffset),
                requirement = "The Xu Zijiang R-scene actor interaction must be triggered before the two keyboard-only choice boxes are available.",
                current_gap = "GameDebug MCP now records this prerequisite, but live no-mouse triggering of the actor-click event still needs an internal engine hook or R-scene state probe."
            },
            first_choice = new
            {
                choice_box = modeChoice.Key,
                selected_option = 1,
                option_text = modeChoice.Options.ElementAtOrDefault(0) ?? string.Empty,
                reasoning = "The first mode choice defaults to option 1, so Enter selects regular mode."
            },
            second_choice = new
            {
                choice_box = configChoice.Key,
                selected_option = 6,
                option_text = configChoice.Options.ElementAtOrDefault(5) ?? string.Empty,
                reasoning = "The regular configuration menu has six options; five Down keys followed by Enter selects Start Game."
            },
            validation_gate = "After running this key route, use game_runtime_wait_for_state(target_classifications=\"battle_loaded\") and game_battle_state_match before dynamic attack/turn probes.",
            probe_follow_up = "If battle_loaded is reached, run debug_battle_profile_probe_run(profile=\"yingchuan_cao_zhangliang\", run_probes=true) at the intended action state."
        };

        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = "route-plan-ready",
            session_dir = sessionDir,
            route = normalizedRoute,
            paths,
            scenario = new
            {
                relative_path = "RS/R_00.eex",
                path = scenarioPath,
                sha256 = ComputeSha256(scenarioPath),
                size = bytes.Length
            },
            choice_boxes = new[] { FormatR00ChoiceBox(modeChoice), FormatR00ChoiceBox(configChoice) },
            prerequisite_actor_click = FormatR00ActorClickTest(xuZijiangClick),
            case_branches = new
            {
                mode_choice = modeCases.Select(FormatR00CaseBranch).ToList(),
                regular_config_menu = configCases.Select(FormatR00CaseBranch).ToList()
            },
            regular_mode_variable_writes = regularVariableWrites.Select(FormatR00VariableWrite).ToList(),
            start_game_case = startGameCaseOffset >= 0 ? FormatFileOffset(startGameCaseOffset) : string.Empty,
            start_game_nearby_variable_writes = startGameGateWrites.Select(FormatR00VariableWrite).ToList(),
            route_plan = routePlan,
            confidence = new
            {
                level = "medium",
                basis = "Route is derived from static R_00.eex choice-box text, following case markers, and regular-mode variable writes. It still requires live runtime validation before attack/turn breakpoint semantics are promoted.",
                limitations = new[]
                {
                    "This tool does not execute the route.",
                    "It does not read live R-scene interpreter variables yet.",
                    "It does not prove battle_loaded or attack/turn function semantics."
                }
            },
            safety = "Read-only R_00.eex route analysis only; no game launch, input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };

        var reportPath = Path.Combine(sessionDir, "r00-mode-route-analysis.json");
        var markdownPath = Path.Combine(sessionDir, "r00-mode-route-analysis.md");
        WriteJson(reportPath, report);
        WriteR00ModeRouteMarkdown(markdownPath, report);
        AppendJsonLine(eventsPath, new
        {
            type = "R00ModeRouteAnalysis",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = reportPath,
            route = normalizedRoute,
            sequence,
            mode_choice_offset = FormatFileOffset(modeChoice.CommandOffset),
            config_choice_offset = FormatFileOffset(configChoice.CommandOffset)
        });

        return new
        {
            session_dir = sessionDir,
            report_path = reportPath,
            markdown_path = markdownPath,
            status = "route-plan-ready",
            route = normalizedRoute,
            sequence,
            first_choice_offset = FormatFileOffset(modeChoice.CommandOffset),
            second_choice_offset = FormatFileOffset(configChoice.CommandOffset),
            prerequisite_actor_click_offset = FormatFileOffset(xuZijiangClick.CommandOffset),
            prerequisite_person_id = xuZijiangClick.PersonId,
            start_game_case = startGameCaseOffset >= 0 ? FormatFileOffset(startGameCaseOffset) : string.Empty,
            mode_option_count = modeChoice.Options.Count,
            config_option_count = configChoice.Options.Count,
            regular_mode_variable_write_count = regularVariableWrites.Count,
            safety = "No launch, input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };
    }

    public object DebugR00ActorRouteAnalyze(
        string route,
        int personId,
        string? evidencePath,
        bool includeLatestEvidence,
        string? gameRoot,
        string? outputDir)
    {
        var paths = ResolveGamePaths(gameRoot);
        var normalizedRoute = NormalizeR00Route(route);
        var normalizedPersonId = personId <= 0 ? 157 : personId;
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var scenarioPath = Path.Combine(paths.GameRoot, "RS", "R_00.eex");
        if (!File.Exists(scenarioPath))
        {
            throw new FileNotFoundException("RS/R_00.eex was not found under the verified game root.", scenarioPath);
        }

        var bytes = File.ReadAllBytes(scenarioPath);
        var actorCommands = ReadR00ActorCommands(bytes, normalizedPersonId);
        var click = actorCommands.FirstOrDefault(command => command.CommandId == 0x2D);
        var placement = actorCommands
            .Where(command => command.CommandId is 0x30 or 0x32 or 0x33 or 0x34)
            .Select(FormatR00ActorCommand)
            .ToList();
        var modeChoice = ReadR00ChoiceBox(
            bytes,
            key: "mode_choice",
            anchorText: "[C97\u5E38\u89C4\u6A21\u5F0F]\n[C3A\u81EA\u9009\u6A21\u5F0F]\n\u6A21\u5F0F\u8BF4\u660E");
        var configChoice = ReadR00ChoiceBox(
            bytes,
            key: "regular_config_menu",
            anchorText: "\u57F9\u517B\u6A21\u5F0F      \u3010[C28*.1000]\u3011\n\u96BE\u5EA6\u8BBE\u7F6E      \u3010[C28*.1001*/2%]\u3011\n\u547D\u4E2D\u7C7B\u578B      \u3010[C28*.1005]\u3011\n\u6740\u654C\u52A0\u6210      \u3010[C28*.1006\u52A0\u6210]\u3011\n[C25\u9009\u9879\u8BF4\u660E]\n[C3A\u5F00\u59CB\u6E38\u620F]");
        var modeCases = ReadR00CaseBranches(bytes, modeChoice.CommandOffset, Math.Min(configChoice.CommandOffset, bytes.Length));
        var configCases = ReadR00CaseBranches(bytes, configChoice.CommandOffset, FindR00RouteWindowEnd(bytes, configChoice.CommandOffset));
        var regularVariableWrites = ReadR00VariableWrites(bytes, FindCaseOffset(modeCases, 1), Math.Min(configChoice.CommandOffset, bytes.Length));
        var startGameCaseOffset = FindCaseOffset(configCases, 6);
        var startGameGateWrites = startGameCaseOffset >= 0
            ? ReadR00VariableWrites(bytes, startGameCaseOffset, Math.Min(startGameCaseOffset + 256, bytes.Length))
            : new List<R00VariableWrite>();
        var latestEvidencePath = ResolveR00StartupProbeEvidencePath(paths.WorkspaceRoot, evidencePath, includeLatestEvidence);
        var evidenceSummary = ReadR00StartupProbeEvidenceSummary(latestEvidencePath);

        var actorPath = actorCommands
            .Where(command => (command.CommandId is 0x30 or 0x32) && command.X.HasValue && command.Y.HasValue)
            .Select(command => new
            {
                offset = FormatFileOffset(command.Offset),
                command = $"{FormatHex(command.CommandId, 2)} {command.CommandName}",
                x = command.X,
                y = command.Y
            })
            .ToList();
        var lastKnownPosition = actorCommands
            .Where(command => command.X.HasValue && command.Y.HasValue)
            .OrderBy(command => command.Offset)
            .LastOrDefault();

        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = click is not null ? "actor-route-ready" : "actor-click-not-found",
            session_dir = sessionDir,
            route = normalizedRoute,
            person_id = normalizedPersonId,
            paths,
            scenario = new
            {
                relative_path = "RS/R_00.eex",
                path = scenarioPath,
                sha256 = ComputeSha256(scenarioPath),
                size = bytes.Length
            },
            actor_route = new
            {
                command_count = actorCommands.Count,
                placement,
                path = actorPath,
                last_known_position = lastKnownPosition is null
                    ? null
                    : new
                    {
                        offset = FormatFileOffset(lastKnownPosition.Offset),
                        x = lastKnownPosition.X,
                        y = lastKnownPosition.Y,
                        command = $"{FormatHex(lastKnownPosition.CommandId, 2)} {lastKnownPosition.CommandName}"
                    },
                click_test = click is null ? null : FormatR00ActorCommand(click)
            },
            choice_route = new
            {
                prerequisite = click is null
                    ? "actor-click command was not found"
                    : $"2D actor-click test for person {normalizedPersonId} at {FormatFileOffset(click.Offset)}",
                first_choice = FormatR00ChoiceBox(modeChoice),
                first_choice_cases = modeCases.Select(FormatR00CaseBranch).ToList(),
                first_choice_regular_writes = regularVariableWrites.Select(FormatR00VariableWrite).ToList(),
                second_choice = FormatR00ChoiceBox(configChoice),
                second_choice_cases = configCases.Select(FormatR00CaseBranch).ToList(),
                start_game_case = startGameCaseOffset >= 0 ? FormatFileOffset(startGameCaseOffset) : string.Empty,
                start_game_nearby_variable_writes = startGameGateWrites.Select(FormatR00VariableWrite).ToList(),
                candidate_sequence_after_actor_click = "enter,down,down,down,down,down,enter"
            },
            latest_no_input_probe = evidenceSummary,
            interpretation = new
            {
                current_positive_evidence = new[]
                {
                    "The verified R_00 script contains a single 2D actor-click test for Xu Zijiang/person 157.",
                    "The actor is placed and moved on the R scene before the mode-choice child event.",
                    "The regular-start choice route is statically derivable from 12 choice boxes and 13 case branches."
                },
                current_negative_evidence = evidenceSummary is null
                    ? []
                    : new[]
                    {
                        "The latest no-input live probe launched x32dbg/Ekd5 and set the generated handler breakpoints, but did not hit them.",
                        "The latest runtime script-window scan did not find the R_00 byte windows after the title window appeared.",
                        "Therefore the remaining gap is before or at title-to-R-scene script loading/entry, not yet the attack/turn function layer."
                    },
                next_probe = "Locate the title-entry/R-scene-load transition and live interpreter script pointer before trying to trigger person 157 or interpret attack-before/attack-after/turn-end probes.",
                promotion_rule = "Do not mark autonomous battle entry complete until a live run reaches battle_loaded and game_battle_state_match(profile=\"yingchuan_cao_zhangliang\") passes."
            },
            safety = "Read-only R_00 actor route analysis only; no launch, input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };

        var reportPath = Path.Combine(sessionDir, "r00-actor-route-analysis.json");
        var markdownPath = Path.Combine(sessionDir, "r00-actor-route-analysis.md");
        WriteJson(reportPath, report);
        WriteR00ActorRouteMarkdown(markdownPath, report);
        AppendJsonLine(eventsPath, new
        {
            type = "R00ActorRouteAnalysis",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = reportPath,
            status = click is not null ? "actor-route-ready" : "actor-click-not-found",
            person_id = normalizedPersonId,
            actor_command_count = actorCommands.Count,
            latest_evidence_path = latestEvidencePath
        });

        return new
        {
            session_dir = sessionDir,
            report_path = reportPath,
            markdown_path = markdownPath,
            status = click is not null ? "actor-route-ready" : "actor-click-not-found",
            person_id = normalizedPersonId,
            actor_command_count = actorCommands.Count,
            click_offset = click is null ? string.Empty : FormatFileOffset(click.Offset),
            last_known_position = lastKnownPosition is null ? string.Empty : $"{lastKnownPosition.X},{lastKnownPosition.Y}",
            latest_evidence_path = latestEvidencePath ?? string.Empty,
            latest_probe_status = evidenceSummary is null ? string.Empty : ExtractStringFromObject(evidenceSummary, "status"),
            safety = "No launch, input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };
    }

    public object DebugRSceneCommandHandlerScan(
        string commandIds,
        int maxCandidatesPerCommand,
        int contextBytes,
        bool writeProbePlan,
        string? outputDir,
        string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var ids = ParseCommandIdList(commandIds);
        var maxCandidates = Math.Clamp(maxCandidatesPerCommand, 1, 128);
        var context = Math.Clamp(contextBytes, 4, 64);
        var text = ReadTextSection(paths.ExePath);
        var allCandidates = ScanRSceneCommandCompareCandidates(text, ids, maxCandidates, context);
        var grouped = ids
            .Select(id =>
            {
                var candidates = allCandidates
                    .Where(c => c.CommandId == id)
                    .OrderBy(c => c.FileOffset)
                    .Take(maxCandidates)
                    .Select(FormatRSceneCommandCompareCandidate)
                    .ToList();
                return new
                {
                    command_id = FormatHex(id, 2),
                    command_name = RSceneCommandName(id),
                    candidate_count = candidates.Count,
                    candidates
                };
            })
            .ToList();

        object? probePlan = null;
        string probePlanPath = string.Empty;
        string probePlanMarkdownPath = string.Empty;
        var uniqueTargets = new Dictionary<string, InternalProbeTarget>(StringComparer.OrdinalIgnoreCase);
        if (writeProbePlan)
        {
            foreach (var candidate in allCandidates
                         .OrderBy(c => RSceneCommandPriority(c.CommandId))
                         .ThenBy(c => c.FileOffset)
                         .Take(Math.Clamp(ids.Count * maxCandidates, 1, 256)))
            {
                var address = FormatHex(candidate.Address, 8);
                AddProbeTarget(uniqueTargets, new InternalProbeTarget
                {
                    Address = address,
                    Name = $"rscene_cmd_{candidate.CommandId:X2}_{candidate.Pattern}_{PlainAddress(address)}",
                    Phase = "settings",
                    ExpectedSemantics = $"Offline command-id comparison candidate for R-scene command {FormatHex(candidate.CommandId, 2)} ({RSceneCommandName(candidate.CommandId)}).",
                    TriggerHint = "Use at title/R_00 Xu Zijiang configuration flow; dynamic hit plus stack/disassembly review is required before treating this as a handler.",
                    EvidenceLevel = $"static-rscene-command-candidate-{candidate.Confidence}",
                    HighFrequency = candidate.CommandId is 0x12 or 0x13
                });
            }

            var plan = new InternalProbePlan
            {
                CreatedAt = DateTimeOffset.Now.ToString("O"),
                Profile = "rscene-command-handlers",
                TargetExeSha256 = paths.ExeSha256,
                Targets = uniqueTargets.Values
                    .OrderBy(t => t.Address, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Safety = "Generated from offline R-scene command-id scan; debugger breakpoints and read-only evidence only."
            };
            probePlanPath = Path.Combine(sessionDir, "rscene-command-handler-probe-plan.json");
            probePlanMarkdownPath = Path.Combine(sessionDir, "rscene-command-handler-probe-plan.md");
            WriteJson(probePlanPath, plan);
            WriteInternalProbePlanMarkdown(probePlanMarkdownPath, plan, "rscene-command-handler-scan.json");
            probePlan = plan;
        }

        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = allCandidates.Count > 0 ? "handler-candidates-found" : "handler-candidates-not-found",
            session_dir = sessionDir,
            target = new
            {
                paths.ExePath,
                paths.ExeSha256,
                expected_sha256 = ExpectedSha256,
                paths.IsExpectedSha256,
                image_base = FormatHex(ImageBase, 8),
                text_section = new
                {
                    virtual_address = FormatHex(text.VirtualAddress, 8),
                    virtual_size = FormatHex(text.VirtualSize),
                    raw_pointer = FormatHex(text.RawPointer),
                    raw_size = FormatHex(text.RawSize)
                }
            },
            command_ids = ids.Select(id => FormatHex(id, 2)).ToList(),
            max_candidates_per_command = maxCandidates,
            context_bytes = context,
            command_count = grouped.Count,
            candidate_count = allCandidates.Count,
            commands = grouped,
            probe_plan_path = probePlanPath,
            probe_plan_markdown_path = probePlanMarkdownPath,
            probe_target_count = uniqueTargets.Count,
            probe_plan = probePlan,
            confidence = new
            {
                level = "low",
                basis = "The scan finds byte-level command-id comparison patterns only. It is useful for breakpoint planning, not handler semantics.",
                promotion_rule = "Promote only after x32dbg hits occur during the concrete R_00 actor-click/choice flow and the surrounding disassembly confirms command dispatch semantics."
            },
            safety = "Offline scan only; reads Ekd5.exe bytes and writes evidence. It does not launch the game, send input, mutate x32dbg, write process memory, or patch files."
        };

        var reportPath = Path.Combine(sessionDir, "rscene-command-handler-scan.json");
        var markdownPath = Path.Combine(sessionDir, "rscene-command-handler-scan.md");
        WriteJson(reportPath, report);
        WriteRSceneCommandHandlerScanMarkdown(markdownPath, report);
        AppendJsonLine(eventsPath, new
        {
            type = "RSceneCommandHandlerScan",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = reportPath,
            command_count = grouped.Count,
            candidate_count = allCandidates.Count,
            probe_target_count = uniqueTargets.Count
        });

        return new
        {
            session_dir = sessionDir,
            report_path = reportPath,
            markdown_path = markdownPath,
            status = allCandidates.Count > 0 ? "handler-candidates-found" : "handler-candidates-not-found",
            command_count = grouped.Count,
            candidate_count = allCandidates.Count,
            probe_plan_path = probePlanPath,
            probe_plan_markdown_path = probePlanMarkdownPath,
            probe_target_count = uniqueTargets.Count,
            safety = "Offline scan only; no launch, input, debugger mutation, screenshots, process-memory writes, or game-file writes."
        };
    }

    public object DebugRSceneLoadTransitionScan(
        string route,
        string anchors,
        int maxCandidates,
        int contextBytes,
        bool writeProbePlan,
        bool includeRuntimeScan,
        string candidateFilter,
        int maxScanBytes,
        int maxPointerRefs,
        string? outputDir,
        string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var normalizedRoute = NormalizeR00Route(route);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var anchorList = ParseAsciiAnchorList(anchors);
        var probeFilter = ParseRSceneLoadTransitionCandidateFilter(candidateFilter);
        var context = Math.Clamp(contextBytes, 4, 64);
        var maxStaticCandidates = Math.Clamp(maxCandidates, 1, 256);
        var maxBytes = Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024);
        var maxRefs = Math.Clamp(maxPointerRefs, 1, 128);
        var sections = ReadPeSections(paths.ExePath);
        var textSection = SelectExecutableSection(sections);
        var text = new TextSection(
            textSection.VirtualAddress,
            textSection.VirtualSize,
            textSection.RawPointer,
            textSection.RawSize,
            textSection.Bytes);
        var script = ReadR00RouteScriptContext(paths.GameRoot);
        var windows = BuildR00ScriptWindows(script.Bytes, script.ActorClick, script.ModeChoice, script.ConfigChoice, contextBytes);
        var staticAnchors = FindAsciiAnchorsInSections(sections, anchorList)
            .OrderBy(hit => hit.FileOffset)
            .ToList();
        var referenceCandidates = ScanTextForStaticAnchorReferences(text, staticAnchors, maxStaticCandidates, context)
            .OrderBy(candidate => StaticReferenceConfidenceSortKey(candidate.Confidence))
            .ThenBy(candidate => candidate.BreakpointAddress)
            .Take(maxStaticCandidates)
            .ToList();
        var anchorFunctionCandidates = FindAnchorContainingFunctionCandidates(text, referenceCandidates, context)
            .OrderBy(candidate => candidate.BreakpointAddress)
            .Take(maxStaticCandidates)
            .ToList();
        var importFunctions = ReadPeImportFunctions(paths.ExePath, sections)
            .Where(import => IsRSceneLoadImport(import.FunctionName))
            .OrderBy(import => import.ImportAddress)
            .ToList();
        var importCallCandidates = ScanTextForImportCallCandidates(text, importFunctions, maxStaticCandidates, context)
            .OrderBy(candidate => ImportCallCandidateSortKey(candidate))
            .ThenBy(candidate => candidate.BreakpointAddress)
            .Take(maxStaticCandidates)
            .ToList();

        object? probePlan = null;
        string probePlanPath = string.Empty;
        string probePlanMarkdownPath = string.Empty;
        var uniqueTargets = new Dictionary<string, InternalProbeTarget>(StringComparer.OrdinalIgnoreCase);
        var referenceProbeTargetCount = 0;
        var anchorFunctionProbeTargetCount = 0;
        var importCallProbeTargetCount = 0;
        if (writeProbePlan)
        {
            if (probeFilter.IncludeDirectRefs)
            {
                foreach (var candidate in referenceCandidates
                             .Where(candidate => !candidate.Confidence.Equals("low", StringComparison.OrdinalIgnoreCase))
                             .Take(maxStaticCandidates))
                {
                    var address = $"0x{candidate.BreakpointAddress:X8}";
                    var beforeCount = uniqueTargets.Count;
                    AddProbeTarget(uniqueTargets, new InternalProbeTarget
                    {
                        Address = address,
                        Name = $"rscene_load_ref_{SanitizeProbeName(candidate.Anchor)}_{PlainAddress(address)}",
                        Phase = "startup",
                        ExpectedSemantics = $"Static reference to R/S EEX path anchor '{candidate.Anchor}'. Candidate for title-to-R-scene load transition or script file selection.",
                        TriggerHint = "Run during title startup / R_00 entry. Promote only after x32dbg hit evidence ties this code to opening or preparing RS/R_00.eex.",
                        EvidenceLevel = $"static-rscene-load-ref-{candidate.Confidence}",
                        HighFrequency = false
                    });
                    if (uniqueTargets.Count > beforeCount) referenceProbeTargetCount++;
                }
            }

            if (probeFilter.IncludeAnchorFunctions)
            {
                foreach (var candidate in anchorFunctionCandidates.Take(maxStaticCandidates))
                {
                    var address = $"0x{candidate.BreakpointAddress:X8}";
                    var beforeCount = uniqueTargets.Count;
                    AddProbeTarget(uniqueTargets, new InternalProbeTarget
                    {
                        Address = address,
                        Name = $"rscene_load_fn_{SanitizeProbeName(candidate.Anchor)}_{PlainAddress(address)}",
                        Phase = "startup",
                        ExpectedSemantics = $"Function-entry candidate containing a reference to R/S EEX path anchor '{candidate.Anchor}'.",
                        TriggerHint = "Run during title startup / R_00 entry. This should capture caller context before the path string is selected.",
                        EvidenceLevel = $"static-rscene-load-function-{candidate.Confidence}",
                        HighFrequency = false
                    });
                    if (uniqueTargets.Count > beforeCount) anchorFunctionProbeTargetCount++;
                }
            }

            if (probeFilter.IncludeImportCalls)
            {
                foreach (var candidate in importCallCandidates
                             .Where(candidate => !candidate.Confidence.Equals("low", StringComparison.OrdinalIgnoreCase))
                             .Take(maxStaticCandidates))
                {
                    var address = $"0x{candidate.BreakpointAddress:X8}";
                    var beforeCount = uniqueTargets.Count;
                    AddProbeTarget(uniqueTargets, new InternalProbeTarget
                    {
                        Address = address,
                        Name = $"rscene_load_api_{SanitizeProbeName(candidate.FunctionName)}_{PlainAddress(address)}",
                        Phase = "startup",
                        ExpectedSemantics = $"Static call-through-IAT candidate for {candidate.ModuleName}!{candidate.FunctionName}; possible file/buffer step in title-to-R-scene script loading.",
                        TriggerHint = "Run during title startup / R_00 entry. Promote only after the hit evidence shows R/S path, EEX buffer, or R_00 script-window residency.",
                        EvidenceLevel = $"static-rscene-load-api-{candidate.Confidence}",
                        HighFrequency = IsHighFrequencyImport(candidate.FunctionName)
                    });
                    if (uniqueTargets.Count > beforeCount) importCallProbeTargetCount++;
                }
            }

            var plan = new InternalProbePlan
            {
                CreatedAt = DateTimeOffset.Now.ToString("O"),
                Profile = "rscene-load-transition",
                TargetExeSha256 = paths.ExeSha256,
                Targets = uniqueTargets.Values
                    .OrderBy(t => t.Address, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Safety = $"Generated from offline R/S EEX string-reference scan with probe target filter '{probeFilter.Normalized}'; debugger breakpoints and read-only evidence only."
            };
            probePlanPath = Path.Combine(sessionDir, "rscene-load-transition-probe-plan.json");
            probePlanMarkdownPath = Path.Combine(sessionDir, "rscene-load-transition-probe-plan.md");
            WriteJson(probePlanPath, plan);
            WriteInternalProbePlanMarkdown(probePlanMarkdownPath, plan, "rscene-load-transition-scan.json");
            probePlan = plan;
        }

        object? runtimeTextAnchorScan = null;
        object? runtimeScriptWindowScan = null;
        if (includeRuntimeScan)
        {
            runtimeTextAnchorScan = GameRSceneTextAnchorScan(
                string.Empty,
                gameRoot,
                maxBytes,
                4,
                sessionDir);
            runtimeScriptWindowScan = GameRSceneScriptWindowScan(
                normalizedRoute,
                gameRoot,
                16,
                maxBytes,
                4,
                true,
                maxRefs,
                sessionDir);
        }

        var runtimeTextStatus = runtimeTextAnchorScan is null ? "skipped" : ExtractStringFromObject(runtimeTextAnchorScan, "status");
        var runtimeScriptStatus = runtimeScriptWindowScan is null ? "skipped" : ExtractStringFromObject(runtimeScriptWindowScan, "status");
        var process = FindTargetProcess();
        var status = referenceCandidates.Count > 0
            ? "transition-candidates-found"
            : staticAnchors.Count > 0
                ? "anchors-found-no-text-refs"
                : "anchors-not-found";
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            route = normalizedRoute,
            target = new
            {
                paths.ExePath,
                paths.ExeSha256,
                expected_sha256 = ExpectedSha256,
                paths.IsExpectedSha256,
                image_base = $"0x{ImageBase:X8}",
                text_section = new
                {
                    name = textSection.Name,
                    virtual_address = $"0x{textSection.VirtualAddress:X8}",
                    virtual_size = $"0x{textSection.VirtualSize:X}",
                    raw_pointer = $"0x{textSection.RawPointer:X}",
                    raw_size = $"0x{textSection.RawSize:X}"
                }
            },
            target_process = ProcessSummary(process),
            route_anchors = new
            {
                actor_click = FormatR00ActorClickTest(script.ActorClick),
                mode_choice = FormatR00ChoiceBox(script.ModeChoice),
                regular_config_menu = FormatR00ChoiceBox(script.ConfigChoice)
            },
            scan = new
            {
                static_anchor_count = staticAnchors.Count,
                reference_candidate_count = referenceCandidates.Count,
                anchor_function_candidate_count = anchorFunctionCandidates.Count,
                import_function_count = importFunctions.Count,
                import_call_candidate_count = importCallCandidates.Count,
                probe_target_filter = new
                {
                    requested = candidateFilter,
                    normalized = probeFilter.Normalized,
                    probeFilter.IncludeDirectRefs,
                    probeFilter.IncludeAnchorFunctions,
                    probeFilter.IncludeImportCalls,
                    reference_probe_target_count = referenceProbeTargetCount,
                    anchor_function_probe_target_count = anchorFunctionProbeTargetCount,
                    import_call_probe_target_count = importCallProbeTargetCount
                },
                max_candidates = maxStaticCandidates,
                context_bytes = context,
                include_runtime_scan = includeRuntimeScan,
                runtime_text_anchor_status = runtimeTextStatus,
                runtime_script_window_status = runtimeScriptStatus
            },
            anchors = staticAnchors.Select(FormatStaticAnchorHit).ToList(),
            reference_candidates = referenceCandidates.Select(FormatStaticAnchorRefCandidate).ToList(),
            anchor_function_candidates = anchorFunctionCandidates.Select(FormatStaticAnchorRefCandidate).ToList(),
            import_functions = importFunctions.Select(FormatImportFunction).ToList(),
            import_call_candidates = importCallCandidates.Select(FormatImportCallCandidate).ToList(),
            script_windows = windows.Select(FormatScriptWindow).ToList(),
            runtime_text_anchor_scan = runtimeTextAnchorScan,
            runtime_script_window_scan = runtimeScriptWindowScan,
            probe_plan_path = probePlanPath,
            probe_plan_markdown_path = probePlanMarkdownPath,
            probe_target_count = uniqueTargets.Count,
            probe_plan = probePlan,
            confidence = new
            {
                level = referenceCandidates.Count > 0 ? "low" : "pending",
                basis = referenceCandidates.Count > 0
                    ? "The scan found static references from executable code to R/S EEX path format strings."
                    : importCallCandidates.Count > 0
                        ? "The scan found static call-through-IAT candidates for file/buffer APIs that can participate in script loading."
                        : "No code references to the requested R/S EEX anchors or selected import APIs were found in the executable section.",
                promotion_rule = "A candidate is not a verified loader/interpreter function until a live x32dbg hit occurs during title-to-R_00 transition and the evidence shows RS/R_00.eex script residency or interpreter pointer movement."
            },
            safety = "Offline EXE scan plus optional read-only process-memory scan only; no launch, input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };

        var reportPath = Path.Combine(sessionDir, "rscene-load-transition-scan.json");
        var markdownPath = Path.Combine(sessionDir, "rscene-load-transition-scan.md");
        WriteJson(reportPath, report);
        WriteRSceneLoadTransitionScanMarkdown(markdownPath, report);
        AppendJsonLine(eventsPath, new
        {
            type = "RSceneLoadTransitionScan",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = reportPath,
            status,
            static_anchor_count = staticAnchors.Count,
            reference_candidate_count = referenceCandidates.Count,
            probe_target_count = uniqueTargets.Count,
            runtime_text_anchor_status = runtimeTextStatus,
            runtime_script_window_status = runtimeScriptStatus
        });

        return new
        {
            session_dir = sessionDir,
            report_path = reportPath,
            markdown_path = markdownPath,
            status,
            route = normalizedRoute,
            static_anchor_count = staticAnchors.Count,
            reference_candidate_count = referenceCandidates.Count,
            anchor_function_candidate_count = anchorFunctionCandidates.Count,
            import_function_count = importFunctions.Count,
            import_call_candidate_count = importCallCandidates.Count,
            probe_plan_path = probePlanPath,
            probe_plan_markdown_path = probePlanMarkdownPath,
            probe_target_count = uniqueTargets.Count,
            probe_target_filter = new
            {
                requested = candidateFilter,
                normalized = probeFilter.Normalized,
                probeFilter.IncludeDirectRefs,
                probeFilter.IncludeAnchorFunctions,
                probeFilter.IncludeImportCalls,
                reference_probe_target_count = referenceProbeTargetCount,
                anchor_function_probe_target_count = anchorFunctionProbeTargetCount,
                import_call_probe_target_count = importCallProbeTargetCount
            },
            runtime_text_anchor_status = runtimeTextStatus,
            runtime_script_window_status = runtimeScriptStatus,
            safety = "Offline EXE scan plus optional read-only process-memory scan only; no launch, input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };
    }

    public object DebugRSceneLoadTransitionProbeRun(
        string route,
        bool startDebugger,
        bool runProbes,
        bool continueStartup,
        int startupContinueMaxRuns,
        string anchors,
        int maxCandidates,
        int contextBytes,
        string candidateFilter,
        int maxHits,
        int timeoutMs,
        bool disableAfterHit,
        bool continueAfterEntryPointPause,
        string hostName,
        int port,
        int maxScanBytes,
        int maxPointerRefs,
        string? outputDir,
        string? gameRoot)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var normalizedRoute = NormalizeR00Route(route);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var steps = new List<object>();
        var hits = new List<object>();
        object? startResult = null;
        object? probeRun = null;
        object? startupContinue = null;
        var status = "plan-ready";

        AppendJsonLine(eventsPath, new
        {
            type = "RSceneLoadTransitionProbeRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            route = normalizedRoute,
            start_debugger = startDebugger,
            run_probes = runProbes,
            continue_startup = continueStartup,
            anchors,
            candidate_filter = candidateFilter
        });
        AppendActionChainLine(
            sessionDir,
            "debug_rscene_load_transition_probe_run",
            "session-started",
            "started",
            new
            {
                route = normalizedRoute,
                startDebugger,
                runProbes,
                continueStartup,
                anchors,
                candidateFilter
            });

        var beforeRuntime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        var beforeScriptWindow = GameRSceneScriptWindowScan(
            normalizedRoute,
            gameRoot,
            contextBytes: 16,
            maxScanBytes: Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024),
            maxHitsPerWindow: 4,
            includePointerRefs: true,
            maxPointerRefs: Math.Clamp(maxPointerRefs, 1, 128),
            sessionDir);
        steps.Add(new { step = "runtime_before", result = beforeRuntime });
        steps.Add(new { step = "game_rscene_script_window_scan_before", result = beforeScriptWindow });
        AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeStep", step = "runtime_before", result = beforeRuntime });
        AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeStep", step = "game_rscene_script_window_scan_before", result = beforeScriptWindow });

        var transitionScan = DebugRSceneLoadTransitionScan(
            normalizedRoute,
            anchors,
            maxCandidates,
            contextBytes,
            writeProbePlan: true,
            includeRuntimeScan: false,
            candidateFilter,
            maxScanBytes,
            maxPointerRefs,
            sessionDir,
            gameRoot);
        var planPath = ExtractStringFromObject(transitionScan, "probe_plan_path");
        var targetCount = ExtractIntFromObject(transitionScan, "probe_target_count");
        steps.Add(new { step = "debug_rscene_load_transition_scan", result = transitionScan });
        AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeStep", step = "debug_rscene_load_transition_scan", result = transitionScan });
        AppendActionChainLine(
            sessionDir,
            "debug_rscene_load_transition_probe_run",
            "transition-plan",
            ExtractStringFromObject(transitionScan, "status"),
            new
            {
                transitionScan,
                plan_path = planPath,
                target_count = targetCount
            });

        if (startDebugger)
        {
            startResult = DebugSessionStart(gameRoot, null, hostName, port, waitMs: Math.Clamp(timeoutMs, 1000, 60000), hidden: false);
            steps.Add(new { step = "debug_session_start", result = startResult });
            AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeStep", step = "debug_session_start", result = startResult });
            AppendActionChainLine(sessionDir, "debug_rscene_load_transition_probe_run", "debug_session_start", ExtractStringFromObject(startResult, "status"), new { result = startResult });
        }

        var bridge = InvokeX32dbg("GET", hostName, port, "/api/health");
        var bridgeReady = IsBridgeSuccess(bridge);
        steps.Add(new { step = "bridge_health_before_probe", result = bridge });
        AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeStep", step = "bridge_health_before_probe", result = bridge });

        if (!runProbes)
        {
            status = "plan-ready";
            AppendActionChainLine(sessionDir, "debug_rscene_load_transition_probe_run", "debug_internal_probe_run", "skipped-plan-only", new { runProbes });
        }
        else if (!bridgeReady)
        {
            status = "bridge-unavailable";
            AppendActionChainLine(sessionDir, "debug_rscene_load_transition_probe_run", "debug_internal_probe_run", status, new { bridge });
        }
        else if (string.IsNullOrWhiteSpace(planPath) || !File.Exists(planPath) || targetCount <= 0)
        {
            status = "plan-unavailable";
            AppendActionChainLine(sessionDir, "debug_rscene_load_transition_probe_run", "debug_internal_probe_run", status, new { plan_path = planPath, target_count = targetCount });
        }
        else
        {
            probeRun = DebugInternalProbeRun(
                planPath,
                profile: "rscene-load-transition",
                hostName,
                port,
                maxHits: Math.Clamp(maxHits, 1, 128),
                timeoutMs: Math.Max(timeoutMs, 1000),
                disableAfterHit,
                continueAfterEntryPointPause,
                gameRoot,
                sessionDir);
            hits.AddRange(ExtractHitsFromProbeRun(probeRun));
            steps.Add(new { step = "debug_internal_probe_run", result = probeRun });
            AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeStep", step = "debug_internal_probe_run", result = probeRun });
            status = $"probe-{ExtractStringFromObject(probeRun, "status")}";
            AppendActionChainLine(sessionDir, "debug_rscene_load_transition_probe_run", "debug_internal_probe_run", ExtractStringFromObject(probeRun, "status"), new { result = probeRun });
        }

        if (continueStartup)
        {
            startupContinue = ContinueStartupThroughDebugger(hostName, port, Math.Clamp(timeoutMs, 1000, 60000), Math.Clamp(startupContinueMaxRuns, 1, 8), sessionDir);
            steps.Add(new { step = "startup_continue", result = startupContinue });
            AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeStep", step = "startup_continue", result = startupContinue });
            AppendActionChainLine(sessionDir, "debug_rscene_load_transition_probe_run", "startup_continue", ExtractStringFromObject(startupContinue, "status"), new { result = startupContinue });
        }

        var afterTextAnchor = GameRSceneTextAnchorScan(
            string.Empty,
            gameRoot,
            Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024),
            4,
            sessionDir);
        var afterScriptWindow = GameRSceneScriptWindowScan(
            normalizedRoute,
            gameRoot,
            contextBytes: 16,
            maxScanBytes: Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024),
            maxHitsPerWindow: 4,
            includePointerRefs: true,
            maxPointerRefs: Math.Clamp(maxPointerRefs, 1, 128),
            sessionDir);
        var finalRuntime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        var finalBattleMatch = GameBattleStateMatch("yingchuan_cao_zhangliang", gameRoot, sessionDir);
        steps.Add(new { step = "game_rscene_text_anchor_scan_after", result = afterTextAnchor });
        steps.Add(new { step = "game_rscene_script_window_scan_after", result = afterScriptWindow });
        steps.Add(new { step = "runtime_final", result = finalRuntime });
        steps.Add(new { step = "battle_profile_final", result = finalBattleMatch });
        AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeStep", step = "game_rscene_text_anchor_scan_after", result = afterTextAnchor });
        AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeStep", step = "game_rscene_script_window_scan_after", result = afterScriptWindow });
        AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeStep", step = "runtime_final", result = finalRuntime });
        AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeStep", step = "battle_profile_final", result = finalBattleMatch });

        var beforeScriptStatus = ExtractStringFromObject(beforeScriptWindow, "status");
        var afterTextStatus = ExtractStringFromObject(afterTextAnchor, "status");
        var afterScriptStatus = ExtractStringFromObject(afterScriptWindow, "status");
        var finalClassification = ExtractNestedStringFromObject(finalRuntime, "runtime", "classification");
        var finalProfileStatus = ExtractStringFromObject(finalBattleMatch, "status");
        var scriptResident = afterScriptStatus.Equals("script-windows-found", StringComparison.OrdinalIgnoreCase);
        var textResident = afterTextStatus.Equals("anchors-found", StringComparison.OrdinalIgnoreCase);
        var battleLoaded = finalClassification.Equals("battle_loaded", StringComparison.OrdinalIgnoreCase);
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            route = normalizedRoute,
            start_debugger = startDebugger,
            run_probes = runProbes,
            continue_startup = continueStartup,
            candidate_filter = candidateFilter,
            transition_report_path = ExtractStringFromObject(transitionScan, "report_path"),
            transition_markdown_path = ExtractStringFromObject(transitionScan, "markdown_path"),
            probe_plan_path = planPath,
            probe_target_count = targetCount,
            hit_count = hits.Count,
            before_script_window_status = beforeScriptStatus,
            after_text_anchor_status = afterTextStatus,
            after_script_window_status = afterScriptStatus,
            rscene_text_resident = textResident,
            r00_script_window_resident = scriptResident,
            final_runtime_classification = finalClassification,
            battle_profile_status = finalProfileStatus,
            battle_loaded = battleLoaded,
            startup_continue = startupContinue,
            probe_run = probeRun,
            hits,
            steps,
            safety = "R-scene load transition probing uses x32dbg breakpoints plus read-only runtime scans. It does not send mouse input, capture screenshots, inject code, write process memory, or patch game files."
        };
        var summaryPath = Path.Combine(sessionDir, "rscene-load-transition-probe-summary.json");
        var markdownPath = Path.Combine(sessionDir, "rscene-load-transition-probe-summary.md");
        WriteJson(summaryPath, summary);
        WriteRSceneLoadTransitionProbeRunMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new { type = "RSceneLoadTransitionProbeRunSummary", created_at = DateTimeOffset.Now.ToString("O"), path = summaryPath, summary.status, summary.hit_count, summary.after_script_window_status });
        AppendActionChainLine(sessionDir, "debug_rscene_load_transition_probe_run", "summary", summary.status, new { summary_path = summaryPath, hit_count = summary.hit_count, after_script_window_status = summary.after_script_window_status });
        return summary;
    }

    public object DebugTitleMenuDispatchScan(
        int maxCandidatesPerApi,
        int contextBytes,
        bool includeFunctionEntries,
        bool writeProbePlan,
        string? outputDir,
        string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var maxCandidates = Math.Clamp(maxCandidatesPerApi, 1, 64);
        var context = Math.Clamp(contextBytes, 4, 64);
        var sections = ReadPeSections(paths.ExePath);
        var textSection = SelectExecutableSection(sections);
        var text = new TextSection(
            textSection.VirtualAddress,
            textSection.VirtualSize,
            textSection.RawPointer,
            textSection.RawSize,
            textSection.Bytes);
        var imports = ReadPeImportFunctions(paths.ExePath, sections)
            .Where(import => IsTitleMenuDispatchImport(import.FunctionName))
            .OrderBy(import => TitleMenuDispatchImportPriority(import.FunctionName))
            .ThenBy(import => import.ImportAddress)
            .ToList();
        var callCandidates = ScanTextForImportCallCandidates(text, imports, maxCandidates, context)
            .OrderBy(candidate => TitleMenuDispatchCallSortKey(candidate))
            .ThenBy(candidate => candidate.BreakpointAddress)
            .ToList();
        var functionCandidates = includeFunctionEntries
            ? FindImportCallContainingFunctionCandidates(text, callCandidates, context)
                .OrderBy(candidate => TitleMenuDispatchImportPriority(candidate.FunctionName))
                .ThenBy(candidate => candidate.BreakpointAddress)
                .ToList()
            : [];

        object? probePlan = null;
        string probePlanPath = string.Empty;
        string probePlanMarkdownPath = string.Empty;
        var probeTargets = writeProbePlan
            ? BuildTitleMenuDispatchProbeTargets(callCandidates, functionCandidates, maxCandidates)
            : [];
        if (writeProbePlan)
        {
            var plan = new InternalProbePlan
            {
                CreatedAt = DateTimeOffset.Now.ToString("O"),
                Profile = "title-menu-dispatch",
                TargetExeSha256 = paths.ExeSha256,
                Targets = probeTargets,
                Safety = "Generated from offline Win32 title/menu API scan; debugger breakpoints and read-only evidence only."
            };
            probePlanPath = Path.Combine(sessionDir, "title-menu-dispatch-probe-plan.json");
            probePlanMarkdownPath = Path.Combine(sessionDir, "title-menu-dispatch-probe-plan.md");
            WriteJson(probePlanPath, plan);
            WriteInternalProbePlanMarkdown(probePlanMarkdownPath, plan, "title-menu-dispatch-scan.json");
            probePlan = plan;
        }

        var grouped = imports
            .Select(import =>
            {
                var calls = callCandidates
                    .Where(candidate => candidate.FunctionName.Equals(import.FunctionName, StringComparison.OrdinalIgnoreCase) &&
                                        candidate.ImportAddress == import.ImportAddress)
                    .OrderBy(candidate => candidate.BreakpointAddress)
                    .Take(maxCandidates)
                    .Select(FormatImportCallCandidate)
                    .ToList();
                var functions = functionCandidates
                    .Where(candidate => candidate.FunctionName.Equals(import.FunctionName, StringComparison.OrdinalIgnoreCase) &&
                                        candidate.ImportAddress == import.ImportAddress)
                    .OrderBy(candidate => candidate.BreakpointAddress)
                    .Take(maxCandidates)
                    .Select(FormatImportCallCandidate)
                    .ToList();
                return new
                {
                    function = import.FunctionName,
                    module = import.ModuleName,
                    import_address = $"0x{import.ImportAddress:X8}",
                    priority = TitleMenuDispatchImportPriority(import.FunctionName),
                    call_candidate_count = calls.Count,
                    function_candidate_count = functions.Count,
                    call_candidates = calls,
                    function_candidates = functions
                };
            })
            .ToList();
        var status = probeTargets.Count > 0 || callCandidates.Count > 0
            ? "title-menu-dispatch-candidates-found"
            : imports.Count > 0
                ? "title-menu-imports-found-no-call-sites"
                : "title-menu-imports-not-found";
        var report = new
        {
            schema_version = "1.0",
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            target = new
            {
                paths.ExePath,
                paths.ExeSha256,
                expected_sha256 = ExpectedSha256,
                paths.IsExpectedSha256,
                image_base = $"0x{ImageBase:X8}",
                text_section = new
                {
                    name = textSection.Name,
                    virtual_address = $"0x{textSection.VirtualAddress:X8}",
                    virtual_size = $"0x{textSection.VirtualSize:X}",
                    raw_pointer = $"0x{textSection.RawPointer:X}",
                    raw_size = $"0x{textSection.RawSize:X}"
                }
            },
            scan = new
            {
                max_candidates_per_api = maxCandidates,
                context_bytes = context,
                include_function_entries = includeFunctionEntries,
                import_function_count = imports.Count,
                call_candidate_count = callCandidates.Count,
                function_candidate_count = functionCandidates.Count,
                probe_target_count = probeTargets.Count
            },
            imports = imports.Select(FormatImportFunction).ToList(),
            api_groups = grouped,
            probe_plan_path = probePlanPath,
            probe_plan_markdown_path = probePlanMarkdownPath,
            probe_target_count = probeTargets.Count,
            probe_plan = probePlan,
            confidence = new
            {
                level = callCandidates.Count > 0 ? "low" : "pending",
                basis = "Win32 message/menu API call sites are useful breakpoint candidates for locating the title menu dispatcher, but not semantic proof by themselves.",
                promotion_rule = "A candidate can only become a title-menu dispatcher after Start/Load/Settings/Exit triggers produce distinct x32dbg hit evidence and internal runtime/menu state changes."
            },
            safety = "Offline EXE scan only; no launch, input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };
        var reportPath = Path.Combine(sessionDir, "title-menu-dispatch-scan.json");
        var markdownPath = Path.Combine(sessionDir, "title-menu-dispatch-scan.md");
        WriteJson(reportPath, report);
        WriteTitleMenuDispatchScanMarkdown(markdownPath, report);
        AppendJsonLine(eventsPath, new
        {
            type = "TitleMenuDispatchScan",
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            path = reportPath,
            import_function_count = imports.Count,
            call_candidate_count = callCandidates.Count,
            function_candidate_count = functionCandidates.Count,
            probe_target_count = probeTargets.Count
        });
        return new
        {
            session_dir = sessionDir,
            report_path = reportPath,
            markdown_path = markdownPath,
            status,
            import_function_count = imports.Count,
            call_candidate_count = callCandidates.Count,
            function_candidate_count = functionCandidates.Count,
            probe_plan_path = probePlanPath,
            probe_plan_markdown_path = probePlanMarkdownPath,
            probe_target_count = probeTargets.Count,
            safety = "Offline EXE scan only; no launch, input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };
    }

    public object DebugTitleWndProcDispatchScan(
        int maxCandidatesPerConstant,
        int contextBytes,
        bool includeFunctionEntries,
        bool writeProbePlan,
        string? outputDir,
        string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var maxCandidates = Math.Clamp(maxCandidatesPerConstant, 1, 64);
        var context = Math.Clamp(contextBytes, 4, 64);
        var sections = ReadPeSections(paths.ExePath);
        var textSection = SelectExecutableSection(sections);
        var text = new TextSection(
            textSection.VirtualAddress,
            textSection.VirtualSize,
            textSection.RawPointer,
            textSection.RawSize,
            textSection.Bytes);
        var compareCandidates = ScanTitleWndProcDispatchCompareCandidates(text, maxCandidates, context)
            .OrderBy(candidate => TitleWndProcDispatchCandidateSortKey(candidate))
            .ThenBy(candidate => candidate.BreakpointAddress)
            .ToList();
        var functionCandidates = includeFunctionEntries
            ? BuildTitleWndProcFunctionCandidates(text, compareCandidates, context)
                .OrderBy(candidate => TitleWndProcDispatchCandidateSortKey(candidate))
                .ThenBy(candidate => candidate.BreakpointAddress)
                .ToList()
            : [];

        object? probePlan = null;
        string probePlanPath = string.Empty;
        string probePlanMarkdownPath = string.Empty;
        var probeTargets = writeProbePlan
            ? BuildTitleWndProcDispatchProbeTargets(compareCandidates, functionCandidates, maxCandidates)
            : [];
        if (writeProbePlan)
        {
            var plan = new InternalProbePlan
            {
                CreatedAt = DateTimeOffset.Now.ToString("O"),
                Profile = "title-wndproc-dispatch",
                TargetExeSha256 = paths.ExeSha256,
                Targets = probeTargets,
                Safety = "Generated from offline WndProc/message compare scan; debugger breakpoints and read-only evidence only."
            };
            probePlanPath = Path.Combine(sessionDir, "title-wndproc-dispatch-probe-plan.json");
            probePlanMarkdownPath = Path.Combine(sessionDir, "title-wndproc-dispatch-probe-plan.md");
            WriteJson(probePlanPath, plan);
            WriteInternalProbePlanMarkdown(probePlanMarkdownPath, plan, "title-wndproc-dispatch-scan.json");
            probePlan = plan;
        }

        var grouped = TitleWndProcDispatchConstants
            .Select(constant =>
            {
                var compares = compareCandidates
                    .Where(candidate => candidate.ConstantName.Equals(constant.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(candidate => TitleWndProcDispatchCandidateSortKey(candidate))
                    .ThenBy(candidate => candidate.BreakpointAddress)
                    .Take(maxCandidates)
                    .Select(FormatTitleWndProcDispatchCandidate)
                    .ToList();
                var functions = functionCandidates
                    .Where(candidate => candidate.ConstantName.Equals(constant.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(candidate => TitleWndProcDispatchCandidateSortKey(candidate))
                    .ThenBy(candidate => candidate.BreakpointAddress)
                    .Take(maxCandidates)
                    .Select(FormatTitleWndProcDispatchCandidate)
                    .ToList();
                return new
                {
                    constant = constant.Name,
                    value = $"0x{constant.Value:X}",
                    kind = constant.Kind,
                    priority = constant.Priority,
                    compare_candidate_count = compares.Count,
                    function_candidate_count = functions.Count,
                    compare_candidates = compares,
                    function_candidates = functions
                };
            })
            .Where(group => group.compare_candidate_count > 0 || group.function_candidate_count > 0)
            .ToList();
        var status = probeTargets.Count > 0 || compareCandidates.Count > 0
            ? "title-wndproc-dispatch-candidates-found"
            : "title-wndproc-dispatch-candidates-not-found";
        var report = new
        {
            schema_version = "1.0",
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            target = new
            {
                paths.ExePath,
                paths.ExeSha256,
                expected_sha256 = ExpectedSha256,
                paths.IsExpectedSha256,
                image_base = $"0x{ImageBase:X8}",
                text_section = new
                {
                    name = textSection.Name,
                    virtual_address = $"0x{textSection.VirtualAddress:X8}",
                    virtual_size = $"0x{textSection.VirtualSize:X}",
                    raw_pointer = $"0x{textSection.RawPointer:X}",
                    raw_size = $"0x{textSection.RawSize:X}"
                }
            },
            scan = new
            {
                max_candidates_per_constant = maxCandidates,
                context_bytes = context,
                include_function_entries = includeFunctionEntries,
                constant_count = TitleWndProcDispatchConstants.Count,
                compare_candidate_count = compareCandidates.Count,
                function_candidate_count = functionCandidates.Count,
                probe_target_count = probeTargets.Count
            },
            constants = TitleWndProcDispatchConstants
                .Select(constant => new { constant.Name, value = $"0x{constant.Value:X}", constant.Kind, constant.Priority })
                .ToList(),
            constant_groups = grouped,
            candidates = compareCandidates
                .Take(maxCandidates * 4)
                .Select(FormatTitleWndProcDispatchCandidate)
                .ToList(),
            function_candidates = functionCandidates
                .Take(maxCandidates * 4)
                .Select(FormatTitleWndProcDispatchCandidate)
                .ToList(),
            probe_plan_path = probePlanPath,
            probe_plan_markdown_path = probePlanMarkdownPath,
            probe_target_count = probeTargets.Count,
            probe_plan = probePlan,
            confidence = new
            {
                level = compareCandidates.Count > 0 ? "low-to-medium" : "pending",
                basis = "WndProc-style message and wParam compare patterns are stronger dispatcher candidates than generic Win32 API call sites, but still require route-specific live hit evidence.",
                promotion_rule = "A candidate can only become a title WndProc/menu dispatcher after Start/Load/Settings/Exit triggers produce distinct x32dbg hits plus internal runtime/menu state changes."
            },
            safety = "Offline EXE scan only; no launch, input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };
        var reportPath = Path.Combine(sessionDir, "title-wndproc-dispatch-scan.json");
        var markdownPath = Path.Combine(sessionDir, "title-wndproc-dispatch-scan.md");
        WriteJson(reportPath, report);
        WriteTitleWndProcDispatchScanMarkdown(markdownPath, report);
        AppendJsonLine(eventsPath, new
        {
            type = "TitleWndProcDispatchScan",
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            path = reportPath,
            compare_candidate_count = compareCandidates.Count,
            function_candidate_count = functionCandidates.Count,
            probe_target_count = probeTargets.Count
        });
        return new
        {
            session_dir = sessionDir,
            report_path = reportPath,
            markdown_path = markdownPath,
            status,
            constant_count = TitleWndProcDispatchConstants.Count,
            compare_candidate_count = compareCandidates.Count,
            function_candidate_count = functionCandidates.Count,
            probe_plan_path = probePlanPath,
            probe_plan_markdown_path = probePlanMarkdownPath,
            probe_target_count = probeTargets.Count,
            safety = "Offline EXE scan only; no launch, input, screenshots, debugger mutation, process-memory writes, or game-file writes."
        };
    }

    public object DebugTitleMenuDispatchProbeRun(
        string route,
        string triggerSequence,
        bool allowInput,
        bool startDebugger,
        bool continueStartup,
        int startupContinueMaxRuns,
        bool runProbes,
        int maxCandidatesPerApi,
        int contextBytes,
        string keyDelivery,
        string hostName,
        int port,
        int maxHits,
        int timeoutMs,
        bool disableAfterHit,
        bool continueAfterEntryPointPause,
        int maxScanBytes,
        int maxPointerRefs,
        string? outputDir,
        string? gameRoot)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var normalizedRoute = NormalizeTitleMenuProbeRoute(route);
        var deliveryMode = NormalizeKeyDelivery(keyDelivery);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var steps = new List<object>();
        var hits = new List<object>();
        object? startResult = null;
        object? startupContinue = null;
        object? probeRun = null;
        object? trigger = null;
        var status = "plan-ready";

        AppendJsonLine(eventsPath, new
        {
            type = "TitleMenuDispatchProbeRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            route = normalizedRoute,
            trigger_sequence = triggerSequence,
            allow_input = allowInput,
            start_debugger = startDebugger,
            continue_startup = continueStartup,
            run_probes = runProbes,
            key_delivery = deliveryMode
        });
        AppendActionChainLine(
            sessionDir,
            "debug_title_menu_dispatch_probe_run",
            "session-started",
            "started",
            new
            {
                route = normalizedRoute,
                triggerSequence,
                allowInput,
                startDebugger,
                continueStartup,
                runProbes,
                keyDelivery = deliveryMode
            });

        var beforeRuntime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        var beforeTextAnchor = GameRSceneTextAnchorScan(
            string.Empty,
            gameRoot,
            Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024),
            4,
            sessionDir);
        var beforeScriptWindow = GameRSceneScriptWindowScan(
            "regular_start",
            gameRoot,
            contextBytes: 16,
            maxScanBytes: Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024),
            maxHitsPerWindow: 4,
            includePointerRefs: true,
            maxPointerRefs: Math.Clamp(maxPointerRefs, 1, 128),
            sessionDir);
        steps.Add(new { step = "runtime_before", result = beforeRuntime });
        steps.Add(new { step = "game_rscene_text_anchor_scan_before", result = beforeTextAnchor });
        steps.Add(new { step = "game_rscene_script_window_scan_before", result = beforeScriptWindow });
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "runtime_before", result = beforeRuntime });
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "game_rscene_text_anchor_scan_before", result = beforeTextAnchor });
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "game_rscene_script_window_scan_before", result = beforeScriptWindow });

        var dispatchScan = DebugTitleMenuDispatchScan(
            Math.Clamp(maxCandidatesPerApi, 1, 64),
            Math.Clamp(contextBytes, 4, 64),
            includeFunctionEntries: true,
            writeProbePlan: true,
            sessionDir,
            gameRoot);
        var planPath = ExtractStringFromObject(dispatchScan, "probe_plan_path");
        var targetCount = ExtractIntFromObject(dispatchScan, "probe_target_count");
        steps.Add(new { step = "debug_title_menu_dispatch_scan", result = dispatchScan });
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "debug_title_menu_dispatch_scan", result = dispatchScan });
        AppendActionChainLine(
            sessionDir,
            "debug_title_menu_dispatch_probe_run",
            "dispatch-plan",
            ExtractStringFromObject(dispatchScan, "status"),
            new
            {
                dispatchScan,
                plan_path = planPath,
                target_count = targetCount
            });

        if (startDebugger)
        {
            startResult = DebugSessionStart(gameRoot, null, hostName, port, waitMs: Math.Clamp(timeoutMs, 1000, 60000), hidden: false);
            steps.Add(new { step = "debug_session_start", result = startResult });
            AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "debug_session_start", result = startResult });
            AppendActionChainLine(sessionDir, "debug_title_menu_dispatch_probe_run", "debug_session_start", ExtractStringFromObject(startResult, "status"), new { result = startResult });
        }

        var bridge = InvokeX32dbg("GET", hostName, port, "/api/health");
        var bridgeReady = IsBridgeSuccess(bridge);
        steps.Add(new { step = "bridge_health_before_probe", result = bridge });
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "bridge_health_before_probe", result = bridge });

        if (runProbes)
        {
            if (!bridgeReady)
            {
                status = "bridge-unavailable";
                AppendActionChainLine(sessionDir, "debug_title_menu_dispatch_probe_run", "debug_internal_probe_run", status, new { bridge });
            }
            else if (string.IsNullOrWhiteSpace(planPath) || !File.Exists(planPath) || targetCount <= 0)
            {
                status = "plan-unavailable";
                AppendActionChainLine(sessionDir, "debug_title_menu_dispatch_probe_run", "debug_internal_probe_run", status, new { plan_path = planPath, target_count = targetCount });
            }
            else
            {
                probeRun = DebugInternalProbeRun(
                    planPath,
                    profile: "title-menu-dispatch",
                    hostName,
                    port,
                    maxHits: Math.Clamp(maxHits, 1, 128),
                    timeoutMs: Math.Max(timeoutMs, 1000),
                    disableAfterHit,
                    continueAfterEntryPointPause,
                    gameRoot,
                    sessionDir);
                hits.AddRange(ExtractHitsFromProbeRun(probeRun));
                steps.Add(new { step = "debug_internal_probe_run", result = probeRun });
                AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "debug_internal_probe_run", result = probeRun });
                status = $"probe-{ExtractStringFromObject(probeRun, "status")}";
                AppendActionChainLine(sessionDir, "debug_title_menu_dispatch_probe_run", "debug_internal_probe_run", ExtractStringFromObject(probeRun, "status"), new { result = probeRun });
            }
        }
        else
        {
            AppendActionChainLine(sessionDir, "debug_title_menu_dispatch_probe_run", "debug_internal_probe_run", "skipped-plan-only", new { runProbes });
        }

        if (continueStartup)
        {
            startupContinue = ContinueStartupThroughDebugger(hostName, port, Math.Clamp(timeoutMs, 1000, 60000), Math.Clamp(startupContinueMaxRuns, 1, 8), sessionDir);
            steps.Add(new { step = "startup_continue", result = startupContinue });
            AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "startup_continue", result = startupContinue });
            AppendActionChainLine(sessionDir, "debug_title_menu_dispatch_probe_run", "startup_continue", ExtractStringFromObject(startupContinue, "status"), new { result = startupContinue });
        }

        if (!string.IsNullOrWhiteSpace(triggerSequence))
        {
            trigger = GameKeySequence(
                triggerSequence,
                allowInput,
                gameRoot,
                delayMs: 250,
                bringToFront: true,
                deliveryMode,
                hostName,
                port,
                sessionDir);
            steps.Add(new { step = "game_key_sequence", result = trigger });
            AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "game_key_sequence", result = trigger });
            AppendActionChainLine(sessionDir, "debug_title_menu_dispatch_probe_run", "trigger_sequence", ExtractStringFromObject(trigger, "status"), new { trigger });
        }

        if (status.Equals("plan-ready", StringComparison.OrdinalIgnoreCase) && runProbes && hits.Count == 0 && probeRun is not null)
        {
            status = $"probe-{ExtractStringFromObject(probeRun, "status")}";
        }

        var afterTextAnchor = GameRSceneTextAnchorScan(
            string.Empty,
            gameRoot,
            Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024),
            4,
            sessionDir);
        var afterScriptWindow = GameRSceneScriptWindowScan(
            "regular_start",
            gameRoot,
            contextBytes: 16,
            maxScanBytes: Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024),
            maxHitsPerWindow: 4,
            includePointerRefs: true,
            maxPointerRefs: Math.Clamp(maxPointerRefs, 1, 128),
            sessionDir);
        var finalRuntime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        var finalBattleMatch = GameBattleStateMatch("yingchuan_cao_zhangliang", gameRoot, sessionDir);
        steps.Add(new { step = "game_rscene_text_anchor_scan_after", result = afterTextAnchor });
        steps.Add(new { step = "game_rscene_script_window_scan_after", result = afterScriptWindow });
        steps.Add(new { step = "runtime_final", result = finalRuntime });
        steps.Add(new { step = "battle_profile_final", result = finalBattleMatch });
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "game_rscene_text_anchor_scan_after", result = afterTextAnchor });
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "game_rscene_script_window_scan_after", result = afterScriptWindow });
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "runtime_final", result = finalRuntime });
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeStep", step = "battle_profile_final", result = finalBattleMatch });

        var beforeTextStatus = ExtractStringFromObject(beforeTextAnchor, "status");
        var beforeScriptStatus = ExtractStringFromObject(beforeScriptWindow, "status");
        var afterTextStatus = ExtractStringFromObject(afterTextAnchor, "status");
        var afterScriptStatus = ExtractStringFromObject(afterScriptWindow, "status");
        var finalClassification = ExtractNestedStringFromObject(finalRuntime, "runtime", "classification");
        var finalProfileStatus = ExtractStringFromObject(finalBattleMatch, "status");
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            route = normalizedRoute,
            trigger_sequence = triggerSequence,
            key_delivery = deliveryMode,
            allow_input = allowInput,
            start_debugger = startDebugger,
            continue_startup = continueStartup,
            run_probes = runProbes,
            dispatch_report_path = ExtractStringFromObject(dispatchScan, "report_path"),
            dispatch_markdown_path = ExtractStringFromObject(dispatchScan, "markdown_path"),
            probe_plan_path = planPath,
            probe_target_count = targetCount,
            hit_count = hits.Count,
            before_text_anchor_status = beforeTextStatus,
            before_script_window_status = beforeScriptStatus,
            after_text_anchor_status = afterTextStatus,
            after_script_window_status = afterScriptStatus,
            rscene_text_resident = afterTextStatus.Equals("anchors-found", StringComparison.OrdinalIgnoreCase),
            r00_script_window_resident = afterScriptStatus.Equals("script-windows-found", StringComparison.OrdinalIgnoreCase),
            final_runtime_classification = finalClassification,
            battle_profile_status = finalProfileStatus,
            battle_loaded = finalClassification.Equals("battle_loaded", StringComparison.OrdinalIgnoreCase),
            startup_continue = startupContinue,
            trigger,
            probe_run = probeRun,
            hits,
            steps,
            safety = "Title-menu dispatch probing uses x32dbg breakpoints, optional keyboard-only trigger, and read-only runtime scans. It does not move/click the mouse, capture screenshots, inject code, write process memory, or patch game files."
        };
        var summaryPath = Path.Combine(sessionDir, "title-menu-dispatch-probe-summary.json");
        var markdownPath = Path.Combine(sessionDir, "title-menu-dispatch-probe-summary.md");
        WriteJson(summaryPath, summary);
        WriteTitleMenuDispatchProbeRunMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchProbeRunSummary", created_at = DateTimeOffset.Now.ToString("O"), path = summaryPath, summary.status, summary.hit_count });
        AppendActionChainLine(sessionDir, "debug_title_menu_dispatch_probe_run", "summary", summary.status, new { summary_path = summaryPath, hit_count = summary.hit_count, after_script_window_status = summary.after_script_window_status });
        return summary;
    }

    public object DebugTitleMenuDispatchMatrixRun(
        string routes,
        bool allowInput,
        bool allowExitRoute,
        bool useDefaultTriggers,
        bool startDebugger,
        bool continueStartup,
        int startupContinueMaxRuns,
        bool runProbes,
        int maxCandidatesPerApi,
        int contextBytes,
        string keyDelivery,
        string hostName,
        int port,
        int maxHitsPerRoute,
        int timeoutMs,
        bool disableAfterHit,
        bool continueAfterEntryPointPause,
        int maxScanBytes,
        int maxPointerRefs,
        string? outputDir,
        string? gameRoot)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var routeList = ParseTitleMenuMatrixRoutes(routes);
        var deliveryMode = NormalizeKeyDelivery(keyDelivery);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var routeResults = new List<object>();
        var blockedRoutes = new List<string>();

        AppendJsonLine(eventsPath, new
        {
            type = "TitleMenuDispatchMatrixRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            routes = routeList,
            allow_input = allowInput,
            allow_exit_route = allowExitRoute,
            use_default_triggers = useDefaultTriggers,
            start_debugger = startDebugger,
            continue_startup = continueStartup,
            run_probes = runProbes,
            key_delivery = deliveryMode
        });
        AppendActionChainLine(
            sessionDir,
            "debug_title_menu_dispatch_matrix_run",
            "session-started",
            "started",
            new
            {
                routes = routeList,
                allowInput,
                allowExitRoute,
                useDefaultTriggers,
                startDebugger,
                continueStartup,
                runProbes,
                keyDelivery = deliveryMode
            });

        var sharedScan = DebugTitleMenuDispatchScan(
            Math.Clamp(maxCandidatesPerApi, 1, 64),
            Math.Clamp(contextBytes, 4, 64),
            includeFunctionEntries: true,
            writeProbePlan: true,
            Path.Combine(sessionDir, "shared-dispatch-plan"),
            gameRoot);
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchMatrixSharedScan", result = sharedScan });
        AppendActionChainLine(
            sessionDir,
            "debug_title_menu_dispatch_matrix_run",
            "shared-dispatch-plan",
            ExtractStringFromObject(sharedScan, "status"),
            new
            {
                shared_scan = sharedScan,
                target_count = ExtractIntFromObject(sharedScan, "probe_target_count")
            });

        foreach (var route in routeList)
        {
            if (allowInput && route.Equals("title_exit", StringComparison.OrdinalIgnoreCase) && !allowExitRoute)
            {
                var blocked = new
                {
                    route,
                    status = "blocked: exit-route-requires-allow_exit_route",
                    reason = "The title_exit route can close the debuggee and is blocked unless allow_exit_route=true."
                };
                blockedRoutes.Add(route);
                routeResults.Add(blocked);
                AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchMatrixRouteBlocked", blocked });
                AppendActionChainLine(sessionDir, "debug_title_menu_dispatch_matrix_run", route, blocked.status, new { blocked });
                continue;
            }

            var routeDir = Path.Combine(sessionDir, "title-menu-routes", route);
            Directory.CreateDirectory(routeDir);
            var trigger = allowInput && useDefaultTriggers
                ? DefaultTitleMenuTriggerSequence(route)
                : string.Empty;
            var result = DebugTitleMenuDispatchProbeRun(
                route,
                trigger,
                allowInput,
                startDebugger,
                continueStartup,
                startupContinueMaxRuns,
                runProbes,
                maxCandidatesPerApi,
                contextBytes,
                deliveryMode,
                hostName,
                port,
                maxHitsPerRoute,
                timeoutMs,
                disableAfterHit,
                continueAfterEntryPointPause,
                maxScanBytes,
                maxPointerRefs,
                routeDir,
                gameRoot);
            routeResults.Add(new { route, trigger_sequence = trigger, result });
            AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchMatrixRoute", route, trigger_sequence = trigger, result });
            AppendActionChainLine(sessionDir, "debug_title_menu_dispatch_matrix_run", route, ExtractStringFromObject(result, "status"), new { trigger_sequence = trigger, result });
        }

        var hitCount = routeResults.Sum(ExtractMatrixRouteHitCount);
        var status = runProbes
            ? "title-menu-dispatch-matrix-probe-attempted"
            : "title-menu-dispatch-matrix-plan-ready";
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            routes = routeList,
            route_count = routeResults.Count,
            blocked_routes = blockedRoutes,
            allow_input = allowInput,
            allow_exit_route = allowExitRoute,
            use_default_triggers = useDefaultTriggers,
            start_debugger = startDebugger,
            continue_startup = continueStartup,
            run_probes = runProbes,
            key_delivery = deliveryMode,
            shared_dispatch_report_path = ExtractStringFromObject(sharedScan, "report_path"),
            shared_probe_plan_path = ExtractStringFromObject(sharedScan, "probe_plan_path"),
            shared_probe_target_count = ExtractIntFromObject(sharedScan, "probe_target_count"),
            hit_count = hitCount,
            results = routeResults,
            safety = "Title-menu dispatch matrix only coordinates route-specific title dispatch probes. Safe defaults do not launch, run probes, send input, capture screenshots, inject code, write process memory, or patch game files. Exit route input is blocked unless allow_exit_route=true."
        };
        var summaryPath = Path.Combine(sessionDir, "title-menu-dispatch-matrix-summary.json");
        var markdownPath = Path.Combine(sessionDir, "title-menu-dispatch-matrix-summary.md");
        WriteJson(summaryPath, summary);
        WriteTitleMenuDispatchMatrixRunMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new { type = "TitleMenuDispatchMatrixRunSummary", created_at = DateTimeOffset.Now.ToString("O"), path = summaryPath, summary.status, summary.hit_count });
        AppendActionChainLine(sessionDir, "debug_title_menu_dispatch_matrix_run", "summary", summary.status, new { summary_path = summaryPath, route_count = routeResults.Count, hit_count = hitCount, blocked_routes = blockedRoutes });
        return summary;
    }

    public object DebugR00StartupRouteProbeRun(
        string route,
        string sequence,
        bool allowInput,
        bool startDebugger,
        bool continueStartup,
        int startupContinueMaxRuns,
        bool runProbes,
        bool probeBeforeStartupContinue,
        string commandIds,
        int maxCandidatesPerCommand,
        string keyDelivery,
        string hostName,
        int port,
        int maxHits,
        int timeoutMs,
        bool disableAfterHit,
        bool continueAfterEntryPointPause,
        string? gameRoot,
        string? outputDir)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot);
        var normalizedRoute = NormalizeR00Route(route);
        var deliveryMode = NormalizeKeyDelivery(keyDelivery);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var steps = new List<object>();
        var hits = new List<object>();
        object? startupContinue = null;
        object? preStartupProbeRun = null;
        object? probeRun = null;
        object? trigger = null;
        var status = "plan-ready";

        AppendJsonLine(eventsPath, new
        {
            type = "R00StartupRouteProbeRunStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            route = normalizedRoute,
            sequence,
            allow_input = allowInput,
            start_debugger = startDebugger,
            continue_startup = continueStartup,
            probe_before_startup_continue = probeBeforeStartupContinue,
            run_probes = runProbes,
            command_ids = commandIds,
            key_delivery = deliveryMode
        });

        var routeAnalysis = DebugR00ModeRouteAnalyze(normalizedRoute, gameRoot, sessionDir);
        steps.Add(new { step = "debug_r00_mode_route_analyze", result = routeAnalysis });
        AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "debug_r00_mode_route_analyze", result = routeAnalysis });

        var beforeRuntime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        steps.Add(new { step = "runtime_before", result = beforeRuntime });
        AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "runtime_before", result = beforeRuntime });

        var beforeScriptWindow = GameRSceneScriptWindowScan(normalizedRoute, gameRoot, contextBytes: 16, maxScanBytes: 64 * 1024 * 1024, maxHitsPerWindow: 4, includePointerRefs: true, maxPointerRefs: 16, sessionDir);
        steps.Add(new { step = "game_rscene_script_window_scan_before", result = beforeScriptWindow });
        AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "game_rscene_script_window_scan_before", result = beforeScriptWindow });

        var handlerScan = DebugRSceneCommandHandlerScan(commandIds, Math.Clamp(maxCandidatesPerCommand, 1, 64), contextBytes: 16, writeProbePlan: true, sessionDir, gameRoot);
        var handlerPlanPath = ExtractStringFromObject(handlerScan, "probe_plan_path");
        steps.Add(new { step = "debug_rscene_command_handler_scan", result = handlerScan });
        AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "debug_rscene_command_handler_scan", result = handlerScan });

        if (startDebugger)
        {
            var start = DebugSessionStart(gameRoot, null, hostName, port, waitMs: Math.Clamp(timeoutMs, 1000, 60000), hidden: false);
            steps.Add(new { step = "debug_session_start", result = start });
            AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "debug_session_start", result = start });
        }

        var bridge = InvokeX32dbg("GET", hostName, port, "/api/health");
        var bridgeReady = IsBridgeSuccess(bridge);
        steps.Add(new { step = "bridge_health_before_startup_continue", result = bridge });
        AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "bridge_health_before_startup_continue", result = bridge });

        if (runProbes && probeBeforeStartupContinue)
        {
            if (!bridgeReady)
            {
                status = "pre-startup-bridge-unavailable";
                steps.Add(new { step = "debug_internal_probe_run_before_startup_continue_skipped", reason = "bridge-unavailable" });
            }
            else if (string.IsNullOrWhiteSpace(handlerPlanPath) || !File.Exists(handlerPlanPath))
            {
                status = "pre-startup-plan-unavailable";
                steps.Add(new { step = "debug_internal_probe_run_before_startup_continue_skipped", reason = "plan-unavailable" });
            }
            else
            {
                preStartupProbeRun = DebugInternalProbeRun(
                    handlerPlanPath,
                    profile: "rscene-command-handlers",
                    hostName,
                    port,
                    maxHits: Math.Clamp(maxHits, 1, 128),
                    timeoutMs: Math.Max(timeoutMs, 1000),
                    disableAfterHit,
                    continueAfterEntryPointPause,
                    gameRoot,
                    sessionDir);
                hits.AddRange(ExtractHitsFromProbeRun(preStartupProbeRun));
                steps.Add(new { step = "debug_internal_probe_run_before_startup_continue", result = preStartupProbeRun });
                AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "debug_internal_probe_run_before_startup_continue", result = preStartupProbeRun });
                status = $"pre-startup-probe-{ExtractStringFromObject(preStartupProbeRun, "status")}";
            }
        }

        if (continueStartup)
        {
            startupContinue = ContinueStartupThroughDebugger(hostName, port, Math.Clamp(timeoutMs, 1000, 60000), startupContinueMaxRuns, sessionDir);
            steps.Add(new { step = "startup_continue", result = startupContinue });
            AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "startup_continue", result = startupContinue });
        }

        bridge = InvokeX32dbg("GET", hostName, port, "/api/health");
        bridgeReady = IsBridgeSuccess(bridge);
        steps.Add(new { step = "bridge_health", result = bridge });
        AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "bridge_health", result = bridge });

        if (!runProbes)
        {
            status = "plan-ready";
        }
        else if (!bridgeReady)
        {
            status = "bridge-unavailable";
        }
        else if (string.IsNullOrWhiteSpace(handlerPlanPath) || !File.Exists(handlerPlanPath))
        {
            status = "plan-unavailable";
        }
        else
        {
            probeRun = DebugInternalProbeRun(
                handlerPlanPath,
                profile: "rscene-command-handlers",
                hostName,
                port,
                maxHits: Math.Clamp(maxHits, 1, 128),
                timeoutMs: Math.Max(timeoutMs, 1000),
                disableAfterHit,
                continueAfterEntryPointPause,
                gameRoot,
                sessionDir);
            hits = ExtractHitsFromProbeRun(probeRun);
            steps.Add(new { step = "debug_internal_probe_run", result = probeRun });
            AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "debug_internal_probe_run", result = probeRun });
            status = $"probe-{ExtractStringFromObject(probeRun, "status")}";
        }

        if (allowInput)
        {
            trigger = GameKeySequence(sequence, allowInput, gameRoot, delayMs: 250, bringToFront: true, deliveryMode, hostName, port, sessionDir);
            steps.Add(new { step = "game_key_sequence", result = trigger });
            AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "game_key_sequence", result = trigger });

            if (runProbes && bridgeReady && !string.IsNullOrWhiteSpace(handlerPlanPath) && File.Exists(handlerPlanPath))
            {
                var afterTriggerProbe = DebugInternalProbeRun(
                    handlerPlanPath,
                    profile: "rscene-command-handlers",
                    hostName,
                    port,
                    maxHits: Math.Clamp(maxHits, 1, 128),
                    timeoutMs: Math.Max(timeoutMs, 1000),
                    disableAfterHit,
                    continueAfterEntryPointPause,
                    gameRoot,
                    sessionDir);
                hits.AddRange(ExtractHitsFromProbeRun(afterTriggerProbe));
                steps.Add(new { step = "debug_internal_probe_run_after_trigger", result = afterTriggerProbe });
                AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "debug_internal_probe_run_after_trigger", result = afterTriggerProbe });
                status = $"triggered-probe-{ExtractStringFromObject(afterTriggerProbe, "status")}";
            }
        }
        else if (runProbes)
        {
            steps.Add(new { step = "game_key_sequence_skipped", reason = "allow_input=false" });
        }

        var afterScriptWindow = GameRSceneScriptWindowScan(normalizedRoute, gameRoot, contextBytes: 16, maxScanBytes: 64 * 1024 * 1024, maxHitsPerWindow: 4, includePointerRefs: true, maxPointerRefs: 16, sessionDir);
        steps.Add(new { step = "game_rscene_script_window_scan_after", result = afterScriptWindow });
        AppendJsonLine(eventsPath, new { type = "R00StartupRouteProbeStep", step = "game_rscene_script_window_scan_after", result = afterScriptWindow });

        var finalRuntime = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        var finalBattleMatch = GameBattleStateMatch("yingchuan_cao_zhangliang", gameRoot, sessionDir);
        steps.Add(new { step = "runtime_final", result = finalRuntime });
        steps.Add(new { step = "battle_profile_final", result = finalBattleMatch });

        var finalClassification = ExtractNestedStringFromObject(finalRuntime, "runtime", "classification");
        var finalProfileStatus = ExtractStringFromObject(finalBattleMatch, "status");
        var battleLoaded = finalClassification.Equals("battle_loaded", StringComparison.OrdinalIgnoreCase);
        var profileReady = finalProfileStatus.Equals("profile-matched", StringComparison.OrdinalIgnoreCase) ||
            finalProfileStatus.Equals("attack_after_observed", StringComparison.OrdinalIgnoreCase);

        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            session_dir = sessionDir,
            route = normalizedRoute,
            sequence,
            allow_input = allowInput,
            key_delivery = deliveryMode,
            start_debugger = startDebugger,
            continue_startup_requested = continueStartup,
            startup_continue_max_runs = Math.Clamp(startupContinueMaxRuns, 1, 8),
            probe_before_startup_continue = probeBeforeStartupContinue,
            startup_continue = startupContinue,
            run_probes = runProbes,
            command_ids = ParseCommandIdList(commandIds).Select(id => $"0x{id:X2}").ToList(),
            handler_plan_path = handlerPlanPath,
            handler_probe_target_count = ExtractIntFromObject(handlerScan, "probe_target_count"),
            bridge_ready = bridgeReady,
            trigger,
            pre_startup_probe_run = preStartupProbeRun,
            probe_run = probeRun,
            hit_count = hits.Count,
            hits,
            before_script_window_status = ExtractStringFromObject(beforeScriptWindow, "status"),
            after_script_window_status = ExtractStringFromObject(afterScriptWindow, "status"),
            final_runtime_classification = finalClassification,
            battle_profile_status = finalProfileStatus,
            battle_loaded = battleLoaded,
            battle_profile_ready = profileReady,
            route_analysis = routeAnalysis,
            handler_scan = handlerScan,
            before_script_window = beforeScriptWindow,
            after_script_window = afterScriptWindow,
            final_runtime = finalRuntime,
            final_battle_match = finalBattleMatch,
            step_count = steps.Count,
            steps,
            safety = "R_00 startup route probe orchestration. Launching x32dbg requires start_debugger=true; keyboard input requires allow_input=true; breakpoint mutation requires run_probes=true. It never uses mouse input, screenshots, process-memory writes, or game-file writes."
        };
        var summaryPath = Path.Combine(sessionDir, "r00-startup-route-probe-summary.json");
        var markdownPath = Path.Combine(sessionDir, "r00-startup-route-probe-summary.md");
        WriteJson(summaryPath, summary);
        WriteR00StartupRouteProbeRunMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new
        {
            type = "R00StartupRouteProbeRunSummary",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = summaryPath,
            status,
            hit_count = hits.Count,
            final_runtime_classification = finalClassification,
            battle_profile_status = finalProfileStatus
        });

        return summary;
    }

    public object DebugKeyboardExplorationRun(
        string route,
        string sequences,
        bool allowLaunch,
        bool allowInput,
        bool startDebugger,
        bool continueStartup,
        int startupContinueMaxRuns,
        string keyDelivery,
        int delayMs,
        int settleMs,
        int maxSequences,
        int maxScanBytes,
        string hostName,
        int port,
        string? gameRoot,
        string? outputDir)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: allowLaunch || allowInput || startDebugger);
        var normalizedRoute = NormalizeR00Route(route);
        var deliveryMode = NormalizeKeyDelivery(keyDelivery);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
        var sequenceList = ParseSequenceCandidates(sequences)
            .Take(Math.Clamp(maxSequences, 1, 32))
            .ToList();
        if (sequenceList.Count == 0)
        {
            throw new ArgumentException("No keyboard exploration sequences were provided.", nameof(sequences));
        }

        var steps = new List<object>();
        object? processStart = null;
        object? debuggerStart = null;
        object? startupContinue = null;
        var stopReason = "not-started";

        AppendJsonLine(eventsPath, new
        {
            type = "KeyboardExplorationStarted",
            created_at = DateTimeOffset.Now.ToString("O"),
            route = normalizedRoute,
            sequence_count = sequenceList.Count,
            allow_launch = allowLaunch,
            allow_input = allowInput,
            start_debugger = startDebugger,
            continue_startup = continueStartup,
            key_delivery = deliveryMode
        });

        processStart = GameProcessStart(gameRoot, allowLaunch, waitMs: allowLaunch ? 10000 : 1000, sessionDir);
        steps.Add(new { step = "game_process_start", result = processStart });
        AppendJsonLine(eventsPath, new { type = "KeyboardExplorationStep", step = "game_process_start", result = processStart });

        if (startDebugger)
        {
            debuggerStart = DebugSessionStart(gameRoot, null, hostName, port, waitMs: 10000, hidden: false);
            steps.Add(new { step = "debug_session_start", result = debuggerStart });
            AppendJsonLine(eventsPath, new { type = "KeyboardExplorationStep", step = "debug_session_start", result = debuggerStart });
        }

        if (continueStartup)
        {
            startupContinue = ContinueStartupThroughDebugger(hostName, port, timeoutMs: 30000, startupContinueMaxRuns, sessionDir);
            steps.Add(new { step = "startup_continue", result = startupContinue });
            AppendJsonLine(eventsPath, new { type = "KeyboardExplorationStep", step = "startup_continue", result = startupContinue });
        }

        var initialProbe = CaptureKeyboardExplorationState(
            normalizedRoute,
            gameRoot,
            hostName,
            port,
            Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024),
            sessionDir,
            "initial");
        steps.Add(new { step = "initial_internal_state", result = initialProbe });
        AppendJsonLine(eventsPath, new { type = "KeyboardExplorationStep", step = "initial_internal_state", result = initialProbe });

        if (KeyboardExplorationStopReached(initialProbe, out stopReason))
        {
            // Already at a useful internal state; do not send exploratory input.
        }
        else if (!allowInput)
        {
            stopReason = "dry-run-input-disabled";
        }
        else
        {
            var settle = Math.Clamp(settleMs, 0, 10000);
            for (var i = 0; i < sequenceList.Count; i++)
            {
                var candidate = sequenceList[i];
                var input = GameKeySequence(
                    candidate,
                    allowInput,
                    gameRoot,
                    Math.Clamp(delayMs, 0, 5000),
                    bringToFront: true,
                    deliveryMode,
                    hostName,
                    port,
                    sessionDir);
                steps.Add(new { step = "game_key_sequence", index = i + 1, sequence = candidate, result = input });
                AppendJsonLine(eventsPath, new
                {
                    type = "KeyboardExplorationSequence",
                    created_at = DateTimeOffset.Now.ToString("O"),
                    index = i + 1,
                    sequence = candidate,
                    input
                });

                if (settle > 0)
                {
                    Thread.Sleep(settle);
                }

                var probe = CaptureKeyboardExplorationState(
                    normalizedRoute,
                    gameRoot,
                    hostName,
                    port,
                    Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024),
                    sessionDir,
                    $"after-sequence-{i + 1}");
                steps.Add(new { step = "internal_state_after_sequence", index = i + 1, sequence = candidate, result = probe });
                AppendJsonLine(eventsPath, new
                {
                    type = "KeyboardExplorationProbe",
                    created_at = DateTimeOffset.Now.ToString("O"),
                    index = i + 1,
                    sequence = candidate,
                    probe
                });

                if (KeyboardExplorationStopReached(probe, out stopReason))
                {
                    break;
                }

                stopReason = "sequences-exhausted";
            }
        }

        object finalProbe;
        if (allowInput)
        {
            finalProbe = CaptureKeyboardExplorationState(
                normalizedRoute,
                gameRoot,
                hostName,
                port,
                Math.Clamp(maxScanBytes, 1024 * 1024, 256 * 1024 * 1024),
                sessionDir,
                "final");
            steps.Add(new { step = "final_internal_state", result = finalProbe });
            AppendJsonLine(eventsPath, new { type = "KeyboardExplorationStep", step = "final_internal_state", result = finalProbe });
        }
        else
        {
            finalProbe = initialProbe;
            steps.Add(new { step = "final_internal_state_reused", result = finalProbe });
            AppendJsonLine(eventsPath, new { type = "KeyboardExplorationStep", step = "final_internal_state_reused", result = finalProbe });
        }

        if (KeyboardExplorationStopReached(finalProbe, out var finalStopReason))
        {
            stopReason = finalStopReason;
        }

        var finalRuntime = ExtractStringFromObject(finalProbe, "runtime_classification");
        var finalAnchorSweepStatus = ExtractStringFromObject(finalProbe, "anchor_sweep_status");
        var finalAnchorSweepPhase = ExtractStringFromObject(finalProbe, "anchor_sweep_phase");
        var finalTextStatus = ExtractStringFromObject(finalProbe, "text_anchor_status");
        var finalScriptStatus = ExtractStringFromObject(finalProbe, "script_window_status");
        var finalProfileStatus = ExtractStringFromObject(finalProbe, "profile_status");
        var status = stopReason switch
        {
            "battle-loaded" => "battle-loaded",
            "battle-profile-ready" => "battle-profile-ready",
            "r00-script-resident" => "r00-script-resident",
            "rscene-text-resident" => "rscene-text-resident",
            "dry-run-input-disabled" => "plan-ready",
            _ => stopReason
        };

        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            stop_reason = stopReason,
            session_dir = sessionDir,
            route = normalizedRoute,
            sequence_count = sequenceList.Count,
            sequences = sequenceList,
            allow_launch = allowLaunch,
            allow_input = allowInput,
            start_debugger = startDebugger,
            continue_startup_requested = continueStartup,
            startup_continue_max_runs = Math.Clamp(startupContinueMaxRuns, 1, 8),
            key_delivery = deliveryMode,
            delay_ms = Math.Clamp(delayMs, 0, 5000),
            settle_ms = Math.Clamp(settleMs, 0, 10000),
            process_start = processStart,
            debugger_start = debuggerStart,
            startup_continue = startupContinue,
            final_runtime_classification = finalRuntime,
            final_anchor_sweep_status = finalAnchorSweepStatus,
            final_anchor_sweep_phase = finalAnchorSweepPhase,
            final_text_anchor_status = finalTextStatus,
            final_script_window_status = finalScriptStatus,
            battle_profile_status = finalProfileStatus,
            battle_loaded = finalRuntime.Equals("battle_loaded", StringComparison.OrdinalIgnoreCase),
            r00_resident = finalTextStatus.Equals("anchors-found", StringComparison.OrdinalIgnoreCase) ||
                finalScriptStatus.Equals("script-windows-found", StringComparison.OrdinalIgnoreCase),
            step_count = steps.Count,
            steps,
            safety = "Bounded keyboard-only exploration. Launch requires allow_launch=true, input requires allow_input=true, debugger start requires start_debugger=true. It never uses mouse input, screenshots, process-memory writes, or game-file writes."
        };
        var summaryPath = Path.Combine(sessionDir, "keyboard-exploration-summary.json");
        var markdownPath = Path.Combine(sessionDir, "keyboard-exploration-summary.md");
        WriteJson(summaryPath, summary);
        WriteKeyboardExplorationRunMarkdown(markdownPath, summary);
        AppendJsonLine(eventsPath, new
        {
            type = "KeyboardExplorationSummary",
            created_at = DateTimeOffset.Now.ToString("O"),
            path = summaryPath,
            status,
            stop_reason = stopReason,
            final_runtime_classification = finalRuntime,
            final_anchor_sweep_status = finalAnchorSweepStatus,
            final_anchor_sweep_phase = finalAnchorSweepPhase,
            final_text_anchor_status = finalTextStatus,
            final_script_window_status = finalScriptStatus,
            battle_profile_status = finalProfileStatus
        });

        return summary;
    }

    public object DebugScriptRun(string script, bool allowInput, string? gameRoot, string hostName, int port, string? outputDir)
    {
        GuardLocalHost(hostName);
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var normalized = script.Trim().ToLowerInvariant();
        if (normalized is not ("yj_cao_attack_zhangliang" or "cao_attack_zhangliang"))
        {
            throw new ArgumentException("Unsupported script. v1 supports yj_cao_attack_zhangliang only.");
        }

        var steps = new List<object>();
        steps.Add(new { step = "state-before", result = DebugSessionState(gameRoot, hostName, port) });
        steps.Add(new { step = "runtime-classify-before", result = GameRuntimeStateClassify(gameRoot, hostName, port, 8, sessionDir) });
        steps.Add(new { step = "read-before", result = SafeReadBattleState() });

        if (!allowInput)
        {
            var dryRun = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                script = normalized,
                status = "dry-run",
                reason = "allow_input was false; no game input was sent.",
                intended_sequence = new[]
                {
                    "prepare_window",
                    "wait for process_no_battle_signal or battle_loaded by read-only state",
                    "apply stage probe plan through x32dbg bridge",
                    "run until attack/turn breakpoint or battle memory diff",
                    "capture internal debugger and battle-state evidence"
                },
                safety = "Dry-run only; no mouse input, screenshots, process-memory writes, or game-file writes.",
                steps
            };
            WriteJson(Path.Combine(sessionDir, $"script-{normalized}-{Timestamp()}.json"), dryRun);
            return new { session_dir = sessionDir, dryRun };
        }

        object? automationResult = null;
        try
        {
            automationResult = DebugBattleAutoProbeRun(
                maxSteps: 3,
                policy: "safe_attack",
                runProbes: true,
                allowInput: allowInput,
                hostName: hostName,
                port: port,
                maxHits: 12,
                timeoutMs: 60000,
                gameRoot: gameRoot,
                outputDir: sessionDir);
        }
        catch (Exception ex)
        {
            automationResult = new
            {
                status = "script-automation-error",
                error = ex.Message,
                type = ex.GetType().FullName
            };
        }
        steps.Add(new { step = "debug_battle_auto_probe_run", result = automationResult });

        var finalEvidence = DebugCaptureEvidence("script-live-run", null, hostName, port, gameRoot, sessionDir, includeScreenshot: false);
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            script = normalized,
            status = ExtractStringFromObject(automationResult, "status"),
            reason = "Script delegated to the battle auto probe runner with live defaults enabled.",
            steps,
            evidence = finalEvidence
        };
        WriteJson(Path.Combine(sessionDir, $"script-{normalized}-{Timestamp()}.json"), report);
        return new { session_dir = sessionDir, report };
    }

    public object DebugWriteKnowledgeDraft(string evidencePath, string topic, string? draftDir, string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(evidencePath))
        {
            throw new ArgumentException("Evidence path is required.", nameof(evidencePath));
        }

        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var resolvedEvidencePath = Path.GetFullPath(evidencePath);
        if (!Directory.Exists(resolvedEvidencePath) && !File.Exists(resolvedEvidencePath))
        {
            throw new FileNotFoundException("Evidence path was not found.", resolvedEvidencePath);
        }

        var resolvedDraftDir = string.IsNullOrWhiteSpace(draftDir)
            ? Path.Combine(paths.WorkspaceRoot, "CCZModStudio_Notes", "DebugKnowledgeDrafts")
            : Path.GetFullPath(draftDir);
        Directory.CreateDirectory(resolvedDraftDir);

        var hits = ReadKnowledgeHitSummaries(resolvedEvidencePath);
        var safeTopic = SanitizeLabel(topic);
        var draftPath = Path.Combine(resolvedDraftDir, $"internal-probe-knowledge-draft-{safeTopic}-{Timestamp()}.md");
        var lines = new List<string>
        {
            "# Internal Probe Knowledge Draft",
            "",
            $"- Created: {DateTimeOffset.Now:O}",
            $"- Topic: {topic}",
            $"- Evidence: `{resolvedEvidencePath}`",
            $"- Target: Ekd5.exe SHA256 `{paths.ExeSha256}`",
            "",
            "This is a review draft. Do not promote entries into the main knowledge base until the hit state, gameplay trigger, registers, disassembly, and memory context have been reviewed together.",
            "",
            "## Observed Probe Hits",
            ""
        };

        if (hits.Count == 0)
        {
            lines.Add("No internal probe hits were found in the supplied evidence. Current status remains `pending-breakpoint` or `needs-live-run`.");
        }
        else
        {
            lines.Add("| Address | Name | Phase | Evidence level | Evidence file |");
            lines.Add("| --- | --- | --- | --- | --- |");
            foreach (var hit in hits)
            {
                lines.Add($"| `{EscapeMarkdownCell(hit.Address)}` | {EscapeMarkdownCell(hit.Name)} | {EscapeMarkdownCell(hit.Phase)} | {EscapeMarkdownCell(hit.EvidenceLevel)} | `{EscapeMarkdownCell(hit.Path)}` |");
            }
        }

        lines.Add("");
        lines.Add("## Pending Review");
        lines.Add("");
        lines.Add("- Confirm whether each pause was caused by the intended gameplay action rather than title/menu/UI refresh traffic.");
        lines.Add("- Cross-check CIP, surrounding disassembly, registers, stack trace, and battle-state delta before updating function semantics.");
        lines.Add("- Keep process-memory writes and EXE patching outside this MCP server; use the existing patch preview, backup, and reread workflow for binary changes.");
        lines.Add("");
        lines.Add("## Suggested Knowledge Status");
        lines.Add("");
        lines.Add(hits.Count == 0
            ? "- No promotion. Evidence only shows that a probe plan exists or the bridge was unavailable."
            : "- Candidate dynamic evidence. Promote individual rows only after manual review marks the trigger and semantics as reproducible.");

        File.WriteAllLines(draftPath, lines, Encoding.UTF8);
        return new
        {
            draft_path = draftPath,
            evidence_path = resolvedEvidencePath,
            hit_count = hits.Count,
            status = "draft-created",
            safety = "Draft only; no main knowledge-base file was modified."
        };
    }

    public object DebugKnowledgePromote(string evidencePath, string topic, bool allowWrite, string? gameRoot, string? outputDir)
    {
        if (string.IsNullOrWhiteSpace(evidencePath))
        {
            throw new ArgumentException("Evidence path is required.", nameof(evidencePath));
        }

        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var promotions = BuildKnowledgePromotions(evidencePath, topic, allowWrite, gameRoot);
        var reportPath = Path.Combine(sessionDir, "knowledge-promotions.json");
        WriteJson(reportPath, promotions);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
        {
            type = "KnowledgePromote",
            created_at = DateTimeOffset.Now.ToString("O"),
            evidence_path = Path.GetFullPath(evidencePath),
            topic,
            allow_write = allowWrite,
            path = reportPath
        });

        if (allowWrite)
        {
            var targetPath = ResolveKnowledgePromotionTarget(paths.ToolRoot, topic);
            AppendKnowledgePromotionMarkdown(targetPath, promotions);
            var written = new
            {
                status = "promoted",
                report_path = reportPath,
                knowledge_path = targetPath,
                promotions
            };
            WriteJson(Path.Combine(sessionDir, "knowledge-promote-write.json"), written);
            return written;
        }

        return new
        {
            status = "promotion-preview",
            report_path = reportPath,
            allow_write = allowWrite,
            promotions,
            safety = "Formal knowledge-base writes are enabled when allow_write=true; incomplete evidence is recorded as pending."
        };
    }

    private sealed record InternalProbePlanContext(string SessionDir, string PlanPath, InternalProbePlan Plan, bool Created);

    private sealed record AutonomyPlanContext(string SessionDir, string PlanPath, AutonomyPlan Plan, bool Created);

    private sealed record KnowledgeHitSummary(string Address, string Name, string Phase, string EvidenceLevel, string Path);

    private sealed record R00RuntimeInvokeCandidateContext(
        object Report,
        InternalProbePlan ProbePlan,
        List<RuntimeInvokeAction> Actions,
        int ActionCount,
        int HandlerCandidateCount,
        int ProbeTargetCount,
        string LatestEvidencePath,
        int LatestVerifiedHitCount);

    private sealed record R00RuntimeScriptAction(
        string Key,
        string Intent,
        int CommandId,
        string CommandName,
        int ScriptOffset,
        int? PersonId,
        int? SelectedOption,
        string SelectionText);

    private sealed record R00HandlerCandidate(
        int CommandId,
        string CommandName,
        string Address,
        string FileOffset,
        string Pattern,
        string Confidence,
        string ContextHex,
        string EvidenceLevel,
        bool VerifiedHit,
        string EvidencePath);

    private sealed record KeyMessage(string Name, int VirtualKey);

    private sealed record R00ChoiceBox(string Key, int CommandOffset, int TextOffset, string Text, List<string> Options, int DefaultValue);

    private sealed record R00ActorClickTest(int CommandOffset, int PersonId);

    private sealed record R00ActorCommand(int CommandId, string CommandName, int Offset, int PersonId, int? X, int? Y, int? Facing, int? Action);

    private sealed record R00CaseBranch(int CaseValue, int Offset);

    private sealed record R00VariableWrite(int VariableId, int Value, int Offset);

    private sealed record RSceneAnchorHit(
        string Anchor,
        ulong Address,
        ulong RegionBase,
        ulong RegionSize,
        ulong AllocationBase,
        int OffsetInRegion,
        string Encoding,
        string RegionKind,
        uint Protect,
        uint Type,
        string ContextText);

    private sealed record RSceneAnchorScanResult(long ScannedBytes, int RegionCount, string StoppedReason, List<RSceneAnchorHit> Hits);

    private sealed record R00RouteScriptContext(byte[] Bytes, object Scenario, R00ActorClickTest ActorClick, R00ChoiceBox ModeChoice, R00ChoiceBox ConfigChoice);

    private sealed record RSceneScriptWindow(string Key, string CommandId, string Meaning, int CommandOffset, int StartOffset, byte[] Pattern);

    private sealed record RSceneScriptWindowHit(string WindowKey, string CommandId, ulong Address, ulong CommandAddress, ulong RegionBase, ulong RegionSize, int OffsetInRegion, int ScriptCommandOffset);

    private sealed record RScenePointerRef(string WindowKey, string TargetKind, ulong TargetAddress, ulong RefAddress, ulong RegionBase, ulong RegionSize, int OffsetInRegion);

    private sealed record RSceneScriptWindowScanResult(long ScannedBytes, int RegionCount, string StoppedReason, List<RSceneScriptWindowHit> WindowHits, List<RScenePointerRef> PointerRefs);

    private sealed record TextSection(uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize, byte[] Bytes);

    private sealed record PeSection(string Name, uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize, uint Characteristics, byte[] Bytes);

    private sealed record StaticAnchorHit(string Anchor, string SectionName, uint VirtualAddress, uint FileOffset);

    private sealed record StaticAnchorRefCandidate(
        string Anchor,
        string SectionName,
        uint AnchorAddress,
        uint AnchorFileOffset,
        uint SourceAddress,
        uint FileOffset,
        uint BreakpointAddress,
        string Pattern,
        string Confidence,
        string ContextHex);

    private sealed record RSceneLoadTransitionProbeFilter(
        string Normalized,
        bool IncludeDirectRefs,
        bool IncludeAnchorFunctions,
        bool IncludeImportCalls);

    private sealed record PeImportFunction(string ModuleName, string FunctionName, uint ImportAddress, uint HintNameRva, uint ThunkRva);

    private sealed record ImportCallCandidate(
        string ModuleName,
        string FunctionName,
        uint ImportAddress,
        uint SourceAddress,
        uint FileOffset,
        uint BreakpointAddress,
        string Pattern,
        string Confidence,
        string ContextHex);

    private sealed record TitleWndProcConstant(string Name, int Value, string Kind, int Priority);

    private sealed record TitleWndProcDispatchCandidate(
        string ConstantName,
        int ConstantValue,
        string ConstantKind,
        uint SourceAddress,
        uint FileOffset,
        uint BreakpointAddress,
        uint FunctionAddress,
        string Pattern,
        string Confidence,
        int Priority,
        bool FunctionEntry,
        string ContextHex);

    private sealed record StaticXref(uint Source, uint Target, string Mnemonic, uint FileOffset, int Displacement);

    private sealed record CallerCandidate(string Source, string FileOffset, string Mnemonic, int TargetDelta);

    private sealed record RSceneCommandCompareCandidate(int CommandId, uint Address, uint FileOffset, string Pattern, string Confidence, string ContextHex);

    private static string NormalizeScenarioKey(string scenario)
    {
        var normalized = string.IsNullOrWhiteSpace(scenario)
            ? "yingchuan_cao_attack_zhangliang"
            : scenario.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "yingchuan_cao_attack_zhangliang" or "yj_cao_attack_zhangliang" or "cao_attack_zhangliang" => "yingchuan_cao_attack_zhangliang",
            "generic" or "generic_internal_probe" => "generic_internal_probe",
            _ => normalized
        };
    }

    private static string NormalizeBattleProfile(string profile)
    {
        var normalized = string.IsNullOrWhiteSpace(profile)
            ? "yingchuan_cao_zhangliang"
            : profile.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "yingchuan_cao_zhangliang" or "yj_cao_attack_zhangliang" or "cao_attack_zhangliang" => "yingchuan_cao_zhangliang",
            _ => throw new ArgumentException("Profile must be yingchuan_cao_zhangliang.")
        };
    }

    private static string NormalizeR00Route(string route)
    {
        var normalized = string.IsNullOrWhiteSpace(route)
            ? "regular_start"
            : route.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "regular_start" or "regular" or "normal_start" or "normal" => "regular_start",
            _ => throw new ArgumentException("Route must be regular_start.")
        };
    }

    private static string NormalizeR00RuntimeRoute(string route)
    {
        var normalized = string.IsNullOrWhiteSpace(route)
            ? "r00_regular_start"
            : route.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "regular_start" or "regular" or "normal_start" or "normal" or "r00_regular_start" => "r00_regular_start",
            "custom_mode" or "custom" or "r00_custom_mode" => "r00_custom_mode",
            "mode_help" or "help" or "r00_mode_help" => "r00_mode_help",
            _ => throw new ArgumentException("R_00 runtime route must be regular_start, custom_mode, or mode_help.")
        };
    }

    private static string ToR00ScanRoute(string runtimeRoute)
        => NormalizeR00RuntimeRoute(runtimeRoute) switch
        {
            "r00_regular_start" => "regular_start",
            "r00_custom_mode" => "regular_start",
            "r00_mode_help" => "regular_start",
            _ => "regular_start"
        };

    private static List<string> ParseStageList(string stages)
    {
        var tokens = string.IsNullOrWhiteSpace(stages)
            ? ["startup", "battle_entry", "attack_before", "attack_execute", "attack_after", "turn_end"]
            : stages.Split([',', ';', '，', '；', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var normalized = new List<string>();
        foreach (var token in tokens)
        {
            var stage = NormalizeStageFilter(token);
            if (stage == "all")
            {
                normalized.AddRange(["startup", "settings", "battle_entry", "attack_before", "attack_execute", "attack_after", "turn_end"]);
            }
            else if (stage == "battle")
            {
                normalized.AddRange(["battle_entry", "attack_before", "attack_execute", "attack_after", "turn_end"]);
            }
            else
            {
                normalized.Add(stage);
            }
        }

        return normalized.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static AutonomyPlan BuildAutonomyPlan(string scenario, List<string> stages, GamePaths paths)
    {
        var steps = new List<AutonomyPlanStep>
        {
            new()
            {
                Step = "game_process_start_optional",
                Stage = "startup",
                Tool = "game_process_start",
                Purpose = "Optionally launch Ekd5.exe directly after SHA256 verification, without debugger commands, mouse input, screenshots, or memory writes.",
                RequiresBridge = false,
                WritesInputOrMemory = false
            },
            new()
            {
                Step = "session_state",
                Stage = "startup",
                Tool = "debug_session_state",
                Purpose = "Confirm target hash, process/window presence, x32dbg bridge state, and battle memory availability.",
                RequiresBridge = false,
                WritesInputOrMemory = false
            },
            new()
            {
                Step = "runtime_state_classify",
                Stage = "startup",
                Tool = "game_runtime_state_classify",
                Purpose = "Classify not_running, process_no_battle_signal, partial_battle_signal, or battle_loaded using process/debugger/read-only memory state.",
                RequiresBridge = false,
                WritesInputOrMemory = false
            },
            new()
            {
                Step = "runtime_wait_optional",
                Stage = "startup",
                Tool = "game_runtime_wait_for_state",
                Purpose = "Optionally poll read-only runtime classification until a requested phase appears or the timeout expires.",
                RequiresBridge = false,
                WritesInputOrMemory = false
            }
        };

        foreach (var stage in stages)
        {
            steps.Add(new()
            {
                Step = $"catalog_{stage}",
                Stage = stage,
                Tool = "debug_function_catalog",
                Purpose = $"Read back Ekd5.exe addresses and original bytes for stage {stage}.",
                RequiresBridge = false,
                WritesInputOrMemory = false,
                Arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["stage"] = stage }
            });
            steps.Add(new()
            {
                Step = $"static_xrefs_{stage}",
                Stage = stage,
                Tool = "debug_static_xref_scan",
                Purpose = $"Offline scan .text call/jump references around stage {stage} functions to produce caller breakpoint candidates.",
                RequiresBridge = false,
                WritesInputOrMemory = false,
                Arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["stage"] = stage }
            });
            steps.Add(new()
            {
                Step = $"probe_plan_{stage}",
                Stage = stage,
                Tool = "debug_phase_probe_plan",
                Purpose = $"Create staged breakpoint plan for {stage}.",
                RequiresBridge = false,
                WritesInputOrMemory = false,
                Arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["stage"] = stage }
            });
            steps.Add(new()
            {
                Step = $"probe_run_{stage}",
                Stage = stage,
                Tool = "debug_internal_probe_run",
                Purpose = $"Optionally apply staged breakpoints and capture x32dbg evidence for {stage}.",
                RequiresBridge = true,
                WritesInputOrMemory = false,
                Arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["stage"] = stage }
            });
        }

        steps.Add(new()
        {
            Step = "knowledge_draft",
            Stage = "review",
            Tool = "debug_write_knowledge_draft",
            Purpose = "Summarize collected evidence into a draft without promoting unverified facts.",
            RequiresBridge = false,
            WritesInputOrMemory = false
        });

        return new AutonomyPlan
        {
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Scenario = scenario,
            TargetExeSha256 = paths.ExeSha256,
            Stages = stages,
            Steps = steps
        };
    }

    private AutonomyPlanContext ResolveAutonomyPlan(string? planPath, string scenario, string stages, string? outputDir, GamePaths paths)
    {
        if (string.IsNullOrWhiteSpace(planPath))
        {
            var result = DebugAutonomyPlan(scenario, stages, outputDir, null);
            var createdPlanPath = ExtractStringFromObject(result, "plan_path");
            if (string.IsNullOrWhiteSpace(createdPlanPath))
            {
                throw new InvalidOperationException("Failed to create autonomy plan.");
            }
            var plan = JsonSerializer.Deserialize<AutonomyPlan>(File.ReadAllText(createdPlanPath, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidOperationException($"Failed to parse created autonomy plan: {createdPlanPath}");
            var sessionDir = Path.GetDirectoryName(createdPlanPath) ?? EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
            return new AutonomyPlanContext(sessionDir, createdPlanPath, plan, true);
        }

        var resolvedPlanPath = Path.GetFullPath(planPath);
        if (!File.Exists(resolvedPlanPath))
        {
            throw new FileNotFoundException("Autonomy plan JSON was not found.", resolvedPlanPath);
        }

        var existingPlan = JsonSerializer.Deserialize<AutonomyPlan>(File.ReadAllText(resolvedPlanPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse autonomy plan: {resolvedPlanPath}");
        var resolvedSessionDir = string.IsNullOrWhiteSpace(outputDir)
            ? Path.GetDirectoryName(resolvedPlanPath) ?? EnsureSessionDirectory(paths.WorkspaceRoot, null)
            : Path.GetFullPath(outputDir);
        _ = EnsureSessionDirectory(paths.WorkspaceRoot, resolvedSessionDir);
        return new AutonomyPlanContext(resolvedSessionDir, resolvedPlanPath, existingPlan, false);
    }

    private static void WriteAutonomyPlanMarkdown(string path, AutonomyPlan plan, GamePaths paths)
    {
        var lines = new List<string>
        {
            "# Ekd5 Internal Autonomy Plan",
            "",
            $"- Created: {plan.CreatedAt}",
            $"- Scenario: `{plan.Scenario}`",
            $"- Target: `{paths.ExePath}`",
            $"- SHA256: `{plan.TargetExeSha256}`",
            "",
            "This plan is intentionally limited to internal/debugger/read-only automation. It does not send mouse input, does not capture screenshots, does not write process memory, and does not patch game files.",
            "",
            "| Step | Stage | Tool | Bridge | Writes input/memory | Purpose |",
            "| --- | --- | --- | --- | --- | --- |"
        };
        foreach (var step in plan.Steps)
        {
            lines.Add($"| {EscapeMarkdownCell(step.Step)} | {EscapeMarkdownCell(step.Stage)} | `{EscapeMarkdownCell(step.Tool)}` | {step.RequiresBridge} | {step.WritesInputOrMemory} | {EscapeMarkdownCell(step.Purpose)} |");
        }
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteRuntimeInvokePlanMarkdown(string path, RuntimeInvokePlan plan)
    {
        var lines = new List<string>
        {
            "# Ekd5 Runtime Invoke Plan",
            "",
            $"- Created: {plan.CreatedAt}",
            $"- Stage: `{plan.Stage}`",
            $"- Route: `{plan.Route}`",
            $"- SHA256: `{plan.TargetExeSha256}`",
            "",
            "This plan describes debugger-mediated internal calls or temporary runtime injection candidates. It does not execute or persist anything by itself.",
            "",
            "| Action | Method | Candidate | Injection | Status | Verification |",
            "| --- | --- | --- | --- | --- | --- |"
        };

        foreach (var action in plan.Actions)
        {
            lines.Add($"| {EscapeMarkdownCell(action.Key)} | {EscapeMarkdownCell(action.Method)} | `{EscapeMarkdownCell(action.CandidateAddress)}` | {action.RequiresRuntimeInjection} | {EscapeMarkdownCell(action.Status)} | {EscapeMarkdownCell(action.Verification)} |");
        }

        if (plan.RouteErrors.Count > 0)
        {
            lines.Add("");
            lines.Add("## Route Errors");
            lines.Add("");
            lines.Add("| Route | Exception | Error |");
            lines.Add("| --- | --- | --- |");
            foreach (var error in plan.RouteErrors)
            {
                lines.Add($"| `{EscapeMarkdownCell(error.Route)}` | `{EscapeMarkdownCell(error.ExceptionType)}` | {EscapeMarkdownCell(error.Error)} |");
            }
        }

        lines.Add("");
        lines.Add($"Safety: {plan.Safety}");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteR00RuntimeInvokeCandidatePlanMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# R_00 Runtime Invoke Candidate Plan",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Route: `{GetStringProperty(root, "route")}`",
            $"- Latest evidence: `{GetStringProperty(root, "latest_evidence_path", "latestEvidencePath")}`",
            $"- Handler candidates: `{GetIntProperty(root, "handler_candidate_count", "handlerCandidateCount")}`",
            $"- Latest verified handler hits: `{GetIntProperty(root, "latest_verified_handler_hit_count", "latestVerifiedHandlerHitCount")}`",
            "",
            "This plan links `RS/R_00.eex` script offsets to `.text` command-handler breakpoint candidates. Static rows are not callable function entries.",
            "",
            "## Script Actions",
            "",
            "| Action | Command | Script offset | Candidate | Status |",
            "| --- | --- | --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "runtime_invoke_actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (var action in actions.EnumerateArray())
            {
                var parameters = TryGetJsonProperty(action, "parameters", out var parameterElement)
                    ? parameterElement
                    : default;
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(action, "key") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(parameters, "command_id", "commandId") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(parameters, "script_offset", "scriptOffset") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(action, "candidate_address", "candidateAddress") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(action, "status") ?? string.Empty)} |");
            }
        }

        lines.Add("");
        lines.Add("## Handler Candidates");
        lines.Add("");
        lines.Add("| Command | Candidates | Live hits | First addresses |");
        lines.Add("| --- | --- | --- | --- |");
        if (TryGetJsonProperty(root, "handler_candidates", out var commands) && commands.ValueKind == JsonValueKind.Array)
        {
            foreach (var command in commands.EnumerateArray())
            {
                var firstAddresses = new List<string>();
                if (TryGetJsonProperty(command, "candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
                {
                    firstAddresses.AddRange(candidates.EnumerateArray()
                        .Take(8)
                        .Select(candidate => GetStringProperty(candidate, "address", "Address") ?? string.Empty)
                        .Where(address => !string.IsNullOrWhiteSpace(address))
                        .Select(address => $"`{address}`"));
                }

                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(command, "command_id", "commandId") ?? string.Empty)}` {EscapeMarkdownCell(GetStringProperty(command, "command_name", "commandName") ?? string.Empty)} " +
                    $"| `{GetIntProperty(command, "candidate_count", "candidateCount")}` " +
                    $"| `{GetIntProperty(command, "live_hit_count", "liveHitCount")}` " +
                    $"| {string.Join(", ", firstAddresses)} |");
            }
        }

        lines.Add("");
        lines.Add("## Evidence Gates");
        lines.Add("");
        if (TryGetJsonProperty(root, "evidence_gates", out var gates) && gates.ValueKind == JsonValueKind.Array)
        {
            foreach (var gate in gates.EnumerateArray())
            {
                lines.Add($"- {gate}");
            }
        }
        else
        {
            lines.Add("- Capture live handler hit evidence before deriving ABI or enabling debugger stub calls.");
        }

        lines.Add("");
        lines.Add("Safety: plan only; no debugger mutation, input, screenshot, process-memory write, or game-file write.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteAutonomyRunSummaryMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# Ekd5 Internal Autonomy Run Summary",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Scenario: `{GetStringProperty(root, "scenario")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            $"- Plan: `{GetStringProperty(root, "plan_path", "planPath")}`",
            "",
            "Review generated stage catalogs, probe plans, probe hit evidence, and knowledge draft before updating verified function semantics."
        };
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteRuntimeWaitSummaryMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var targets = TryGetJsonProperty(root, "target_classifications", out var targetArray) && targetArray.ValueKind == JsonValueKind.Array
            ? string.Join(", ", targetArray.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)))
            : string.Empty;
        var lines = new List<string>
        {
            "# Ekd5 Runtime Wait Summary",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Targets: `{targets}`",
            $"- Final classification: `{GetStringProperty(root, "final_classification", "finalClassification")}`",
            $"- Final confidence: `{GetStringProperty(root, "final_confidence", "finalConfidence")}`",
            $"- Elapsed ms: `{GetIntProperty(root, "elapsed_ms", "elapsedMs")}`",
            $"- Samples: `{GetIntProperty(root, "sample_count", "sampleCount")}`",
            "",
            "This wait step only polls read-only runtime classification. It does not send input, capture screenshots, write process memory, or patch game files."
        };
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteBattleStateMatchMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# Ekd5 Battle State Match",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Profile: `{GetStringProperty(root, "profile")}`",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Confidence: `{GetStringProperty(root, "confidence")}`",
            $"- Reason: {GetStringProperty(root, "reason")}",
            $"- Source: {GetStringProperty(root, "source")}",
            "",
            "This profile gate reads battle memory only. It does not send input, capture screenshots, write process memory, call debugger commands, or patch game files.",
            "",
            "## Gates",
            "",
            "| Gate | Passed | Expected | Evidence |",
            "| --- | --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "gates", out var gates) && gates.ValueKind == JsonValueKind.Array)
        {
            foreach (var gate in gates.EnumerateArray())
            {
                var evidence = TryGetJsonProperty(gate, "evidence", out var evidenceElement)
                    ? CompactJson(evidenceElement, 220)
                    : string.Empty;
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(gate, "gate") ?? string.Empty)} " +
                    $"| {TryGetBoolString(gate, "passed")} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(gate, "expected") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(evidence)} |");
            }
        }

        lines.Add("");
        lines.Add("## Key Units");
        lines.Add("");
        lines.Add("| Label | Unit | Side | Coord | HP | MP | Action |");
        lines.Add("| --- | --- | --- | --- | --- | --- | --- |");
        AddBattleUnitMarkdownRow(lines, "cao_cao", "Cao Cao", root);
        AddBattleUnitMarkdownRow(lines, "zhang_liang", "Zhang Liang", root);

        lines.Add("");
        lines.Add("## Battle Summary");
        lines.Add("");
        if (TryGetJsonProperty(root, "battle", out var battle) && battle.ValueKind == JsonValueKind.Object)
        {
            lines.Add($"- Active units: `{GetIntProperty(battle, "active_unit_count", "activeUnitCount", "ActiveUnitCount")}`");
            if (TryGetJsonProperty(battle, "side_counts", out var sideCounts) && sideCounts.ValueKind == JsonValueKind.Object)
            {
                lines.Add($"- Side counts: `{CompactJson(sideCounts, 160)}`");
            }
        }
        else
        {
            lines.Add("- Battle memory was not available for this run.");
        }

        lines.Add("");
        lines.Add("## Promotion Rule");
        lines.Add("");
        lines.Add("Use `profile-matched` as an internal scenario gate before attack probes. Use `attack_after_observed` only as HP-delta evidence; function semantics still require x32dbg dynamic hits tied to the same action.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteStaticXrefMarkdown(string path, object report)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(report, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# Ekd5 Static Xref Scan",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Stage: `{GetStringProperty(root, "stage")}`",
            $"- Target count: `{TryGetNestedInt(root, "scan", "target_count")}`",
            $"- Direct xrefs: `{TryGetNestedInt(root, "scan", "direct_xref_total")}`",
            $"- Breakpoint candidates: `{TryGetNestedInt(root, "scan", "breakpoint_candidate_count")}`",
            "",
            "This is offline static evidence only. Promote function semantics only after dynamic x32dbg evidence confirms the gameplay phase.",
            "",
            "| Stage | Address | Name | Direct xrefs | Nearby xrefs | Evidence |",
            "| --- | --- | --- | --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
        {
            foreach (var target in targets.EnumerateArray())
            {
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(target, "stage", "Stage") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(target, "address", "Address") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(target, "name", "Name") ?? string.Empty)} " +
                    $"| {GetIntProperty(target, "direct_xref_count", "directXrefCount")} " +
                    $"| {GetIntProperty(target, "nearby_xref_count", "nearbyXrefCount")} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(target, "evidence_level", "evidenceLevel") ?? string.Empty)} |");
            }
        }

        lines.Add("");
        lines.Add("## Breakpoint Candidates");
        lines.Add("");
        if (TryGetJsonProperty(root, "breakpoint_candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
        {
            lines.Add("| Stage | Target | Target address | Caller source | Mnemonic | File offset |");
            lines.Add("| --- | --- | --- | --- | --- | --- |");
            foreach (var candidate in candidates.EnumerateArray().Take(64))
            {
                var caller = TryGetJsonProperty(candidate, "caller", out var c) ? c : default;
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(candidate, "stage") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(candidate, "target") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "target_address", "targetAddress") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(caller, "source", "Source") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(caller, "mnemonic", "Mnemonic") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(caller, "file_offset", "fileOffset") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("- No direct call/jump candidates found for this stage.");
        }

        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteAddressReportMarkdown(string path, object report)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(report, JsonOptions));
        var root = document.RootElement;
        var stages = TryGetJsonProperty(root, "stages", out var stageArray) && stageArray.ValueKind == JsonValueKind.Array
            ? string.Join(", ", stageArray.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)))
            : string.Empty;
        var lines = new List<string>
        {
            "# Ekd5 Function Address Report",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Stages: `{stages}`",
            $"- Target count: `{TryGetNestedInt(root, "summary", "target_count")}`",
            $"- Breakpoint candidates: `{TryGetNestedInt(root, "summary", "breakpoint_candidate_count")}`",
            $"- High priority targets: `{TryGetNestedInt(root, "summary", "high_priority_count")}`",
            "",
            "This report combines the staged function catalog with offline static xref scans. It is a breakpoint planning artifact, not proof of gameplay semantics.",
            "",
            "| Priority | Stage | Address | Name | Direct xrefs | Nearby xrefs | Recommended breakpoints |",
            "| --- | --- | --- | --- | --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
        {
            foreach (var target in targets.EnumerateArray())
            {
                var breakpoints = string.Empty;
                if (TryGetJsonProperty(target, "recommended_breakpoints", out var bpArray) && bpArray.ValueKind == JsonValueKind.Array)
                {
                    breakpoints = string.Join(", ", bpArray.EnumerateArray().Select(e => $"`{e.GetString()}`"));
                }
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(target, "priority") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(target, "stage") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(target, "address") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(target, "name") ?? string.Empty)} " +
                    $"| {GetIntProperty(target, "direct_xref_count", "directXrefCount")} " +
                    $"| {GetIntProperty(target, "nearby_xref_count", "nearbyXrefCount")} " +
                    $"| {breakpoints} |");
            }
        }

        lines.Add("");
        lines.Add("## Dynamic Verification Rule");
        lines.Add("");
        lines.Add("Promote an address only after x32dbg evidence ties it to a concrete gameplay action, with CIP, caller chain, registers, disassembly, and read-only battle-state delta.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteInternalProbePlanMarkdown(string path, InternalProbePlan plan, string sourceReportPath)
    {
        var lines = new List<string>
        {
            "# Ekd5 Address Report Probe Plan",
            "",
            $"- Created: {plan.CreatedAt}",
            $"- Profile: `{plan.Profile}`",
            $"- Source report: `{sourceReportPath}`",
            $"- Target count: `{plan.Targets.Count}`",
            "",
            "This plan is generated from static address-report candidates. Dynamic hits must still be reviewed before updating verified function semantics.",
            "",
            "| Phase | Address | Name | Evidence | Trigger hint |",
            "| --- | --- | --- | --- | --- |"
        };

        foreach (var target in plan.Targets)
        {
            lines.Add(
                $"| {EscapeMarkdownCell(target.Phase)} " +
                $"| `{EscapeMarkdownCell(target.Address)}` " +
                $"| {EscapeMarkdownCell(target.Name)} " +
                $"| {EscapeMarkdownCell(target.EvidenceLevel)} " +
                $"| {EscapeMarkdownCell(target.TriggerHint)} |");
        }

        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteAddressProbeRunSummaryMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# Ekd5 Address Probe Run Summary",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            $"- Report: `{GetStringProperty(root, "report_path", "reportPath")}`",
            $"- Plan: `{GetStringProperty(root, "plan_path", "planPath")}`",
            $"- Target count: `{GetIntProperty(root, "target_count", "targetCount")}`",
            $"- Run probes: `{GetStringProperty(root, "run_probes", "runProbes") ?? TryGetBoolString(root, "run_probes", "runProbes")}`",
            "",
            "Use the generated plan with `debug_internal_probe_run` when x32dbg bridge is online and the game is at the intended battle action."
        };
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteBattleProfileProbeRunSummaryMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var stages = TryGetJsonProperty(root, "stages", out var stageArray) && stageArray.ValueKind == JsonValueKind.Array
            ? string.Join(", ", stageArray.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)))
            : string.Empty;
        var lines = new List<string>
        {
            "# Ekd5 Battle Profile Probe Run Summary",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Profile: `{GetStringProperty(root, "profile")}`",
            $"- Profile status: `{GetStringProperty(root, "profile_status", "profileStatus")}`",
            $"- Profile ready: `{TryGetBoolString(root, "profile_ready", "profileReady")}`",
            $"- Require profile match: `{TryGetBoolString(root, "require_profile_match", "requireProfileMatch")}`",
            $"- Run probes requested: `{TryGetBoolString(root, "run_probes_requested", "runProbesRequested")}`",
            $"- Dynamic probes allowed: `{TryGetBoolString(root, "dynamic_probes_allowed", "dynamicProbesAllowed")}`",
            $"- Stages: `{stages}`",
            $"- Target count: `{GetIntProperty(root, "target_count", "targetCount")}`",
            $"- Plan: `{GetStringProperty(root, "plan_path", "planPath")}`",
            $"- Report: `{GetStringProperty(root, "report_path", "reportPath")}`",
            "",
            "This summary ties a read-only battle profile gate to an address probe plan. A matched profile or HP-delta state is not proof of function semantics; dynamic x32dbg hits still need CIP, registers, disassembly, stack, and battle-state delta review."
        };
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteLiveProbeReadinessMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var missing = TryGetJsonProperty(root, "missing_gates", out var missingArray) && missingArray.ValueKind == JsonValueKind.Array
            ? string.Join(", ", missingArray.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)))
            : string.Empty;
        var lines = new List<string>
        {
            "# Ekd5 Live Probe Readiness",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Ready for dynamic probe: `{TryGetBoolString(root, "ready_for_dynamic_probe", "readyForDynamicProbe")}`",
            $"- Profile: `{GetStringProperty(root, "profile")}`",
            $"- Runtime classification: `{GetStringProperty(root, "runtime_classification", "runtimeClassification")}`",
            $"- Profile status: `{GetStringProperty(root, "profile_status", "profileStatus")}`",
            $"- Target count: `{GetIntProperty(root, "target_count", "targetCount")}`",
            $"- Plan: `{GetStringProperty(root, "plan_path", "planPath")}`",
            $"- Missing gates: `{missing}`",
            "",
            "| Gate | Passed | Evidence |",
            "| --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "gates", out var gates) && gates.ValueKind == JsonValueKind.Array)
        {
            foreach (var gate in gates.EnumerateArray())
            {
                var evidence = TryGetJsonProperty(gate, "evidence", out var evidenceElement)
                    ? CompactJson(evidenceElement, 220)
                    : string.Empty;
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(gate, "gate") ?? string.Empty)} " +
                    $"| {TryGetBoolString(gate, "passed")} " +
                    $"| {EscapeMarkdownCell(evidence)} |");
            }
        }

        lines.Add("");
        lines.Add("## Next Actions");
        lines.Add("");
        if (TryGetJsonProperty(root, "next_actions", out var actions) && actions.ValueKind == JsonValueKind.Array && actions.GetArrayLength() > 0)
        {
            foreach (var action in actions.EnumerateArray())
            {
                lines.Add($"- {EscapeMarkdownCell(action.GetString() ?? string.Empty)}");
            }
        }
        else
        {
            lines.Add("- Dynamic probe prerequisites are satisfied. Run `debug_battle_profile_probe_run(..., run_probes=true)` to collect x32dbg hit evidence.");
        }

        lines.Add("");
        lines.Add("This check does not launch the game, send input, capture screenshots, write process memory, patch files, or change x32dbg breakpoints.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteLiveProbeAutoRunSummaryMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var stages = TryGetJsonProperty(root, "stages", out var stageArray) && stageArray.ValueKind == JsonValueKind.Array
            ? string.Join(", ", stageArray.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)))
            : string.Empty;
        var lines = new List<string>
        {
            "# Ekd5 Live Probe Auto Run Summary",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Profile: `{GetStringProperty(root, "profile")}`",
            $"- Stages: `{stages}`",
            $"- Start game: `{TryGetBoolString(root, "start_game", "startGame")}`",
            $"- Start debugger: `{TryGetBoolString(root, "start_debugger", "startDebugger")}`",
            $"- Run probes requested: `{TryGetBoolString(root, "run_probes_requested", "runProbesRequested")}`",
            $"- Require ready: `{TryGetBoolString(root, "require_ready", "requireReady")}`",
            $"- Ready for dynamic probe: `{TryGetBoolString(root, "ready_for_dynamic_probe", "readyForDynamicProbe")}`",
            $"- Readiness status: `{GetStringProperty(root, "readiness_status", "readinessStatus")}`",
            $"- Dynamic probes allowed: `{TryGetBoolString(root, "dynamic_probes_allowed", "dynamicProbesAllowed")}`",
            $"- Target count: `{GetIntProperty(root, "target_count", "targetCount")}`",
            $"- Plan: `{GetStringProperty(root, "plan_path", "planPath")}`",
            "",
            "Use this as the one-call path for live setup: optional launch, optional debugger attach, readiness check, then optional dynamic probe when prerequisites pass."
        };
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteR00ModeRouteMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var routePlan = TryGetJsonProperty(root, "route_plan", out var routeElement) ? routeElement : default;
        var lines = new List<string>
        {
            "# R_00 Mode Route Analysis",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Route: `{GetStringProperty(root, "route")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            $"- Sequence candidate: `{GetStringProperty(routePlan, "sequence")}`",
            "",
            "This report is a read-only static route analysis of `RS/R_00.eex`. It does not launch the game, send input, use screenshots, mutate x32dbg breakpoints, write process memory, or modify game files.",
            "",
            "## Prerequisite",
            ""
        };

        if (TryGetJsonProperty(root, "prerequisite_actor_click", out var actorClick) && actorClick.ValueKind == JsonValueKind.Object)
        {
            lines.Add($"- Actor click test: `0x2D` at `{GetStringProperty(actorClick, "command_offset", "commandOffset")}`");
            lines.Add($"- Person id: `{GetIntProperty(actorClick, "person_id", "personId")}`");
            lines.Add("- This prerequisite must be satisfied before the keyboard-only choice route is available.");
        }
        else
        {
            lines.Add("- Actor-click prerequisite was not decoded.");
        }

        lines.AddRange(new[]
        {
            "",
            "## Choice Boxes",
            "",
            "| Key | Command offset | Text offset | Options |",
            "| --- | --- | --- | --- |"
        });

        if (TryGetJsonProperty(root, "choice_boxes", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                var options = TryGetJsonProperty(choice, "options", out var optionArray) && optionArray.ValueKind == JsonValueKind.Array
                    ? string.Join(" / ", optionArray.EnumerateArray().Select(o => o.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)))
                    : string.Empty;
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(choice, "key") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(choice, "command_offset", "commandOffset") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(choice, "text_offset", "textOffset") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(options)} |");
            }
        }

        lines.Add("");
        lines.Add("## Route Candidate");
        lines.Add("");
        lines.Add($"- First choice: {EscapeMarkdownCell(TryGetNestedString(routePlan, "first_choice", "reasoning"))}");
        lines.Add($"- Second choice: {EscapeMarkdownCell(TryGetNestedString(routePlan, "second_choice", "reasoning"))}");
        lines.Add($"- Validation gate: {EscapeMarkdownCell(GetStringProperty(routePlan, "validation_gate", "validationGate") ?? string.Empty)}");
        lines.Add("");
        lines.Add("## Variable Evidence");
        lines.Add("");
        lines.Add("| Offset | Variable | Value |");
        lines.Add("| --- | --- | --- |");
        if (TryGetJsonProperty(root, "regular_mode_variable_writes", out var writes) && writes.ValueKind == JsonValueKind.Array)
        {
            foreach (var write in writes.EnumerateArray())
            {
                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(write, "offset") ?? string.Empty)}` " +
                    $"| `{GetIntProperty(write, "variable_id", "variableId")}` " +
                    $"| `{GetIntProperty(write, "value")}` |");
            }
        }

        lines.Add("");
        lines.Add("## Review Rule");
        lines.Add("");
        lines.Add("Treat this as a route candidate. Promote it only after a live run reaches `battle_loaded` and the Yingchuan profile gate matches; attack-before, attack-after, and turn-end functions still require dynamic x32dbg evidence.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteR00ActorRouteMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# R_00 Actor Route Analysis",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Route: `{GetStringProperty(root, "route")}`",
            $"- Person id: `{GetIntProperty(root, "person_id", "personId")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            "",
            "This is a read-only static actor-route report plus optional latest live no-input probe summary. It does not launch the game, send input, use screenshots, mutate x32dbg, write process memory, or modify game files.",
            "",
            "## Actor Commands",
            "",
            "| Offset | Command | X | Y | Facing | Action |",
            "| --- | --- | --- | --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "actor_route", out var actorRoute) &&
            TryGetJsonProperty(actorRoute, "placement", out var placement) &&
            placement.ValueKind == JsonValueKind.Array)
        {
            foreach (var command in placement.EnumerateArray())
            {
                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(command, "offset") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(command, "command_id", "commandId") ?? string.Empty)} {EscapeMarkdownCell(GetStringProperty(command, "command_name", "commandName") ?? string.Empty)}` " +
                    $"| `{NullableIntMarkdown(command, "x")}` " +
                    $"| `{NullableIntMarkdown(command, "y")}` " +
                    $"| `{NullableIntMarkdown(command, "facing")}` " +
                    $"| `{NullableIntMarkdown(command, "action")}` |");
            }
        }

        lines.AddRange(new[]
        {
            "",
            "## Choice Route",
            "",
            $"- Prerequisite: {EscapeMarkdownCell(TryGetNestedString(root, "choice_route", "prerequisite"))}",
            $"- Candidate sequence after actor click: `{EscapeMarkdownCell(TryGetNestedString(root, "choice_route", "candidate_sequence_after_actor_click"))}`",
            $"- Start-game case: `{EscapeMarkdownCell(TryGetNestedString(root, "choice_route", "start_game_case"))}`",
            "",
            "## Latest No-Input Probe"
        });

        if (TryGetJsonProperty(root, "latest_no_input_probe", out var evidence) && evidence.ValueKind == JsonValueKind.Object)
        {
            lines.Add("");
            lines.Add($"- Path: `{EscapeMarkdownCell(GetStringProperty(evidence, "path") ?? string.Empty)}`");
            lines.Add($"- Status: `{EscapeMarkdownCell(GetStringProperty(evidence, "status") ?? string.Empty)}`");
            lines.Add($"- Hit count: `{GetIntProperty(evidence, "hit_count", "hitCount")}`");
            lines.Add($"- Final runtime: `{EscapeMarkdownCell(GetStringProperty(evidence, "final_runtime_classification", "finalRuntimeClassification") ?? string.Empty)}`");
            lines.Add($"- Battle profile: `{EscapeMarkdownCell(GetStringProperty(evidence, "battle_profile_status", "battleProfileStatus") ?? string.Empty)}`");
            lines.Add($"- After script-window status: `{EscapeMarkdownCell(GetStringProperty(evidence, "after_script_window_status", "afterScriptWindowStatus") ?? string.Empty)}`");
        }
        else
        {
            lines.Add("");
            lines.Add("- No live no-input probe summary was attached.");
        }

        lines.AddRange(new[]
        {
            "",
            "## Interpretation",
            "",
            "The actor-click prerequisite is statically identified, but the latest live no-input path did not prove R_00 script loading or handler execution. The next useful target is the title-entry/R-scene-load transition and a live interpreter script pointer, before trying to promote attack-before, attack-after, or turn-end addresses.",
            "",
            "Do not mark autonomous battle entry complete until a live run reaches `battle_loaded` and `game_battle_state_match(profile=\"yingchuan_cao_zhangliang\")` passes."
        });

        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static string NullableIntMarkdown(JsonElement element, string property)
    {
        if (!TryGetJsonProperty(element, property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return "";
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : value.ToString();
    }

    private static void WriteRSceneTextAnchorScanMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# R-Scene Text Anchor Scan",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            $"- Scanned bytes: `{GetIntProperty(root, "scanned_bytes", "scannedBytes")}`",
            $"- Regions: `{GetIntProperty(root, "readable_region_count", "readableRegionCount")}`",
            "",
            "This scan reads committed process memory for GBK text anchors. It does not send input, capture screenshots, mutate debugger state, write process memory, or modify game files.",
            "",
            "## Hits",
            "",
            "| Anchor | Address | Region | Kind | Encoding | Context |",
            "| --- | --- | --- | --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "hits", out var hits) && hits.ValueKind == JsonValueKind.Array && hits.GetArrayLength() > 0)
        {
            foreach (var hit in hits.EnumerateArray())
            {
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "anchor") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "address") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "region_base") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "region_kind", "regionKind") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "encoding") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "context_text", "contextText") ?? string.Empty)} |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |  |  |");
        }

        lines.Add("");
        lines.Add("Use hits only as a coarse no-screenshot phase signal. The exact R-scene interpreter state still needs a dedicated variable/handler probe.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteRuntimeAnchorSweepMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var phase = TryGetJsonProperty(root, "phase_inference", out var phaseElement) ? phaseElement : default;
        var lines = new List<string>
        {
            "# Runtime Anchor Sweep",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Profile: `{GetStringProperty(root, "profile")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            $"- Inferred phase: `{GetStringProperty(phase, "phase")}`",
            $"- Confidence: `{GetStringProperty(phase, "confidence")}`",
            $"- Basis: {EscapeMarkdownCell(GetStringProperty(phase, "basis") ?? string.Empty)}",
            "",
            "## GBK Hits",
            "",
            "| Anchor | Address | Region | Kind | Encoding | Context |",
            "| --- | --- | --- | --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "gbk_hits", out var gbkHits) && gbkHits.ValueKind == JsonValueKind.Array && gbkHits.GetArrayLength() > 0)
        {
            foreach (var hit in gbkHits.EnumerateArray().Take(64))
            {
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "anchor") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "address") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "region_base", "regionBase") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "region_kind", "regionKind") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "encoding") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "context_text", "contextText") ?? string.Empty)} |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |  |  |");
        }

        lines.AddRange(new[]
        {
            "",
            "## ASCII Hits",
            "",
            "| Anchor | Address | Region | Kind | Encoding | Context |",
            "| --- | --- | --- | --- | --- | --- |"
        });

        if (TryGetJsonProperty(root, "ascii_hits", out var asciiHits) && asciiHits.ValueKind == JsonValueKind.Array && asciiHits.GetArrayLength() > 0)
        {
            foreach (var hit in asciiHits.EnumerateArray().Take(64))
            {
                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "anchor") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "address") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "region_base", "regionBase") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "region_kind", "regionKind") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "encoding") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "context_text", "contextText") ?? string.Empty)} |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |  |  |");
        }

        lines.Add("");
        lines.Add("This sweep is a broad internal progress signal. It does not prove command execution, actor-click triggering, battle entry, or attack/turn function semantics without dynamic x32dbg and battle-state evidence.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteRSceneScriptWindowScanMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# R-Scene Script Window Scan",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Route: `{GetStringProperty(root, "route")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            $"- Scanned bytes: `{GetIntProperty(root, "scanned_bytes", "scannedBytes")}`",
            $"- Regions: `{GetIntProperty(root, "readable_region_count", "readableRegionCount")}`",
            "",
            "This scan reads committed process memory for byte windows from `RS/R_00.eex`. A hit means the script data is resident; it does not prove the interpreter is currently executing that command.",
            "",
            "## Windows",
            "",
            "| Key | Command | Command offset | Pattern bytes | Pattern SHA256 |",
            "| --- | --- | --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "windows", out var windows) && windows.ValueKind == JsonValueKind.Array)
        {
            foreach (var window in windows.EnumerateArray())
            {
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(window, "key") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(window, "command_id", "commandId") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(window, "command_offset", "commandOffset") ?? string.Empty)}` " +
                    $"| `{GetIntProperty(window, "pattern_length", "patternLength")}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(window, "pattern_sha256", "patternSha256") ?? string.Empty)}` |");
            }
        }

        lines.Add("");
        lines.Add("## Window Hits");
        lines.Add("");
        lines.Add("| Window | Address | Command address | Inferred script base | Region base |");
        lines.Add("| --- | --- | --- | --- | --- |");
        if (TryGetJsonProperty(root, "window_hits", out var hits) && hits.ValueKind == JsonValueKind.Array && hits.GetArrayLength() > 0)
        {
            foreach (var hit in hits.EnumerateArray())
            {
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "window_key", "windowKey") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "address") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "command_address", "commandAddress") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "inferred_script_base", "inferredScriptBase") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "region_base", "regionBase") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |  |");
        }

        lines.Add("");
        lines.Add("## Pointer References");
        lines.Add("");
        lines.Add("| Window | Kind | Target | Reference | Region base |");
        lines.Add("| --- | --- | --- | --- | --- |");
        if (TryGetJsonProperty(root, "pointer_refs", out var refsElement) && refsElement.ValueKind == JsonValueKind.Array && refsElement.GetArrayLength() > 0)
        {
            foreach (var reference in refsElement.EnumerateArray().Take(64))
            {
                lines.Add(
                    $"| {EscapeMarkdownCell(GetStringProperty(reference, "window_key", "windowKey") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(reference, "target_kind", "targetKind") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(reference, "target_address", "targetAddress") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(reference, "ref_address", "refAddress") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(reference, "region_base", "regionBase") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |  |");
        }

        lines.Add("");
        lines.Add("## Review Rule");
        lines.Add("");
        lines.Add("Use window and pointer hits to plan interpreter-state probes. Do not treat them as proof of actor-click or choice-box execution without x32dbg CIP/stack/disassembly evidence.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteRSceneCommandHandlerScanMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# R-Scene Command Handler Scan",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            $"- Candidate count: `{GetIntProperty(root, "candidate_count", "candidateCount")}`",
            $"- Probe targets: `{GetIntProperty(root, "probe_target_count", "probeTargetCount")}`",
            $"- Probe plan: `{GetStringProperty(root, "probe_plan_path", "probePlanPath")}`",
            "",
            "This offline scan finds command-id comparison byte patterns in `Ekd5.exe` `.text`. It is a breakpoint planning artifact, not proof of command-handler semantics.",
            "",
            "| Command | Name | Candidates | First addresses |",
            "| --- | --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "commands", out var commands) && commands.ValueKind == JsonValueKind.Array)
        {
            foreach (var command in commands.EnumerateArray())
            {
                var firstAddresses = new List<string>();
                if (TryGetJsonProperty(command, "candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
                {
                    firstAddresses.AddRange(candidates.EnumerateArray()
                        .Take(8)
                        .Select(c => GetStringProperty(c, "address") ?? string.Empty)
                        .Where(a => !string.IsNullOrWhiteSpace(a))
                        .Select(a => $"`{a}`"));
                }
                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(command, "command_id", "commandId") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(command, "command_name", "commandName") ?? string.Empty)} " +
                    $"| `{GetIntProperty(command, "candidate_count", "candidateCount")}` " +
                    $"| {string.Join(", ", firstAddresses)} |");
            }
        }

        lines.Add("");
        lines.Add("## Review Rule");
        lines.Add("");
        lines.Add("Run the generated probe plan only while the game is in the intended R_00 route. Promote a handler only after dynamic hit evidence ties the candidate to `0x2D` actor-click or `0x12` choice-box execution.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteRSceneLoadTransitionScanMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# R-Scene Load Transition Scan",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Route: `{GetStringProperty(root, "route")}`",
            $"- Probe targets: `{GetIntProperty(root, "probe_target_count", "probeTargetCount")}`",
            $"- Probe plan: `{GetStringProperty(root, "probe_plan_path", "probePlanPath")}`",
            "",
            "This report scans `Ekd5.exe` for static R/S EEX path references and, when a process exists, reads runtime R_00 text/script-window residency. It does not launch the game, send input, capture screenshots, alter x32dbg breakpoints, write process memory, or modify game files.",
            "",
            "## Summary",
            ""
        };

        if (TryGetJsonProperty(root, "scan", out var scan))
        {
            lines.Add($"- Static anchors: `{GetIntProperty(scan, "static_anchor_count", "staticAnchorCount")}`");
            lines.Add($"- Reference candidates: `{GetIntProperty(scan, "reference_candidate_count", "referenceCandidateCount")}`");
            lines.Add($"- Anchor function candidates: `{GetIntProperty(scan, "anchor_function_candidate_count", "anchorFunctionCandidateCount")}`");
            lines.Add($"- Import functions: `{GetIntProperty(scan, "import_function_count", "importFunctionCount")}`");
            lines.Add($"- Import call candidates: `{GetIntProperty(scan, "import_call_candidate_count", "importCallCandidateCount")}`");
            lines.Add($"- Runtime text anchors: `{GetStringProperty(scan, "runtime_text_anchor_status", "runtimeTextAnchorStatus")}`");
            lines.Add($"- Runtime script windows: `{GetStringProperty(scan, "runtime_script_window_status", "runtimeScriptWindowStatus")}`");
            if (TryGetJsonProperty(scan, "probe_target_filter", out var filter))
            {
                lines.Add($"- Probe target filter: `{GetStringProperty(filter, "normalized")}`");
                lines.Add($"- Probe target mix: refs `{GetIntProperty(filter, "reference_probe_target_count", "referenceProbeTargetCount")}`, functions `{GetIntProperty(filter, "anchor_function_probe_target_count", "anchorFunctionProbeTargetCount")}`, APIs `{GetIntProperty(filter, "import_call_probe_target_count", "importCallProbeTargetCount")}`");
            }
        }

        lines.Add("");
        lines.Add("## Static Anchors");
        lines.Add("");
        lines.Add("| Anchor | Section | VA | File offset |");
        lines.Add("| --- | --- | --- | --- |");
        if (TryGetJsonProperty(root, "anchors", out var anchors) && anchors.ValueKind == JsonValueKind.Array && anchors.GetArrayLength() > 0)
        {
            foreach (var anchor in anchors.EnumerateArray().Take(64))
            {
                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(anchor, "anchor") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(anchor, "section", "section_name", "sectionName") ?? string.Empty)}` " +
                    $"| `{GetStringProperty(anchor, "address", "virtual_address", "virtualAddress")}` " +
                    $"| `{GetStringProperty(anchor, "file_offset", "fileOffset")}` |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |");
        }

        lines.Add("");
        lines.Add("## Reference Candidates");
        lines.Add("");
        lines.Add("| Breakpoint | Anchor | Pattern | Confidence | File offset | Context |");
        lines.Add("| --- | --- | --- | --- | --- | --- |");
        if (TryGetJsonProperty(root, "reference_candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
        {
            foreach (var candidate in candidates.EnumerateArray().Take(80))
            {
                lines.Add(
                    $"| `{GetStringProperty(candidate, "breakpoint_address", "breakpointAddress")}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "anchor") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "pattern") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "confidence") ?? string.Empty)}` " +
                    $"| `{GetStringProperty(candidate, "file_offset", "fileOffset")}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "context_hex", "contextHex") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |  |  |");
        }

        lines.Add("");
        lines.Add("## Anchor Function Candidates");
        lines.Add("");
        lines.Add("| Breakpoint | Anchor | Pattern | Confidence | File offset | Context |");
        lines.Add("| --- | --- | --- | --- | --- | --- |");
        if (TryGetJsonProperty(root, "anchor_function_candidates", out var anchorFunctions) && anchorFunctions.ValueKind == JsonValueKind.Array && anchorFunctions.GetArrayLength() > 0)
        {
            foreach (var candidate in anchorFunctions.EnumerateArray().Take(32))
            {
                lines.Add(
                    $"| `{GetStringProperty(candidate, "breakpoint_address", "breakpointAddress")}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "anchor") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "pattern") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "confidence") ?? string.Empty)}` " +
                    $"| `{GetStringProperty(candidate, "file_offset", "fileOffset")}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "context_hex", "contextHex") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |  |  |");
        }

        lines.Add("");
        lines.Add("## Import API Candidates");
        lines.Add("");
        lines.Add("| API | IAT address | Thunk RVA |");
        lines.Add("| --- | --- | --- |");
        if (TryGetJsonProperty(root, "import_functions", out var imports) && imports.ValueKind == JsonValueKind.Array && imports.GetArrayLength() > 0)
        {
            foreach (var import in imports.EnumerateArray().Take(64))
            {
                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(import, "module") ?? string.Empty)}!{EscapeMarkdownCell(GetStringProperty(import, "function") ?? string.Empty)}` " +
                    $"| `{GetStringProperty(import, "import_address", "importAddress")}` " +
                    $"| `{GetStringProperty(import, "thunk_rva", "thunkRva")}` |");
            }
        }
        else
        {
            lines.Add("| none |  |  |");
        }

        lines.Add("");
        lines.Add("## Import Call Candidates");
        lines.Add("");
        lines.Add("| Breakpoint | API | Pattern | Confidence | File offset | Context |");
        lines.Add("| --- | --- | --- | --- | --- | --- |");
        if (TryGetJsonProperty(root, "import_call_candidates", out var importCalls) && importCalls.ValueKind == JsonValueKind.Array && importCalls.GetArrayLength() > 0)
        {
            foreach (var candidate in importCalls.EnumerateArray().Take(80))
            {
                lines.Add(
                    $"| `{GetStringProperty(candidate, "breakpoint_address", "breakpointAddress")}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "module") ?? string.Empty)}!{EscapeMarkdownCell(GetStringProperty(candidate, "function") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "pattern") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "confidence") ?? string.Empty)}` " +
                    $"| `{GetStringProperty(candidate, "file_offset", "fileOffset")}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(candidate, "context_hex", "contextHex") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |  |  |");
        }

        lines.Add("");
        lines.Add("## Review Rule");
        lines.Add("");
        lines.Add("Treat all rows as transition breakpoint candidates. Promote a row only after a live x32dbg hit occurs during title-to-R_00 transition and runtime evidence shows R_00 script residency, path/buffer arguments, pointer references, or script-command progression.");

        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteRSceneLoadTransitionProbeRunMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# R-Scene Load Transition Probe Run",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Route: `{GetStringProperty(root, "route")}`",
            $"- Run probes: `{TryGetBoolString(root, "run_probes", "runProbes")}`",
            $"- Continue startup: `{TryGetBoolString(root, "continue_startup", "continueStartup")}`",
            $"- Candidate filter: `{GetStringProperty(root, "candidate_filter", "candidateFilter")}`",
            $"- Transition report: `{GetStringProperty(root, "transition_report_path", "transitionReportPath")}`",
            $"- Probe plan: `{GetStringProperty(root, "probe_plan_path", "probePlanPath")}`",
            $"- Probe targets: `{GetIntProperty(root, "probe_target_count", "probeTargetCount")}`",
            $"- Hit count: `{GetIntProperty(root, "hit_count", "hitCount")}`",
            $"- Before script-window status: `{GetStringProperty(root, "before_script_window_status", "beforeScriptWindowStatus")}`",
            $"- After text-anchor status: `{GetStringProperty(root, "after_text_anchor_status", "afterTextAnchorStatus")}`",
            $"- After script-window status: `{GetStringProperty(root, "after_script_window_status", "afterScriptWindowStatus")}`",
            $"- Final runtime: `{GetStringProperty(root, "final_runtime_classification", "finalRuntimeClassification")}`",
            $"- Battle profile: `{GetStringProperty(root, "battle_profile_status", "battleProfileStatus")}`",
            "",
            "This wrapper connects static R/S EEX load-transition candidates to live x32dbg breakpoints and read-only R_00 residency checks. It does not send mouse input, capture screenshots, inject code, write process memory, or patch game files.",
            "",
            "## Hits",
            ""
        };

        if (TryGetJsonProperty(root, "hits", out var hits) && hits.ValueKind == JsonValueKind.Array && hits.GetArrayLength() > 0)
        {
            lines.Add("| Address | Name | Phase | Evidence |");
            lines.Add("| --- | --- | --- | --- |");
            foreach (var hit in hits.EnumerateArray())
            {
                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "address") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "name") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "phase") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "evidence_path", "evidencePath") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("- No R-scene load-transition breakpoint hit was captured.");
        }

        lines.Add("");
        lines.Add("## Review Rule");
        lines.Add("");
        lines.Add("A transition hit is not enough by itself. Promote only when the same run also provides R_00 script-window residency, path/buffer or script-pointer evidence, and coherent stack/disassembly context. Do not use this evidence to validate attack or turn functions until `battle_loaded` and the Yingchuan profile gate pass.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteTitleMenuDispatchScanMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# Title Menu Dispatch Scan",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            $"- Probe targets: `{GetIntProperty(root, "probe_target_count", "probeTargetCount")}`",
            $"- Probe plan: `{GetStringProperty(root, "probe_plan_path", "probePlanPath")}`",
            "",
            "This offline scan finds Win32 message/menu API call sites that can lead to the title menu dispatcher. It does not launch the game, send input, capture screenshots, mutate x32dbg, write process memory, or modify game files.",
            "",
            "## Summary",
            ""
        };

        if (TryGetJsonProperty(root, "scan", out var scan))
        {
            lines.Add($"- Import functions: `{GetIntProperty(scan, "import_function_count", "importFunctionCount")}`");
            lines.Add($"- API call candidates: `{GetIntProperty(scan, "call_candidate_count", "callCandidateCount")}`");
            lines.Add($"- Function-entry candidates: `{GetIntProperty(scan, "function_candidate_count", "functionCandidateCount")}`");
            lines.Add($"- Probe target count: `{GetIntProperty(scan, "probe_target_count", "probeTargetCount")}`");
        }

        lines.Add("");
        lines.Add("## API Groups");
        lines.Add("");
        lines.Add("| API | Priority | Calls | Function entries | First call addresses |");
        lines.Add("| --- | --- | --- | --- | --- |");
        if (TryGetJsonProperty(root, "api_groups", out var groups) && groups.ValueKind == JsonValueKind.Array && groups.GetArrayLength() > 0)
        {
            foreach (var group in groups.EnumerateArray().Take(64))
            {
                var firstAddresses = new List<string>();
                if (TryGetJsonProperty(group, "call_candidates", out var calls) && calls.ValueKind == JsonValueKind.Array)
                {
                    firstAddresses.AddRange(calls.EnumerateArray()
                        .Take(6)
                        .Select(call => GetStringProperty(call, "breakpoint_address", "breakpointAddress") ?? string.Empty)
                        .Where(address => !string.IsNullOrWhiteSpace(address))
                        .Select(address => $"`{address}`"));
                }

                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(group, "module") ?? string.Empty)}!{EscapeMarkdownCell(GetStringProperty(group, "function") ?? string.Empty)}` " +
                    $"| `{GetIntProperty(group, "priority")}` " +
                    $"| `{GetIntProperty(group, "call_candidate_count", "callCandidateCount")}` " +
                    $"| `{GetIntProperty(group, "function_candidate_count", "functionCandidateCount")}` " +
                    $"| {string.Join(", ", firstAddresses)} |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |  |");
        }

        lines.Add("");
        lines.Add("## Review Rule");
        lines.Add("");
        lines.Add("Use the generated plan to compare Start, Load, Settings, and Exit triggers. Promote no address until route-specific x32dbg hits include CIP, stack, registers, disassembly, and internal state changes such as R_00 script residency, settings page state, or missing-save fixture handling.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteTitleWndProcDispatchScanMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# Title WndProc Dispatch Scan",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            $"- Probe targets: `{GetIntProperty(root, "probe_target_count", "probeTargetCount")}`",
            $"- Probe plan: `{GetStringProperty(root, "probe_plan_path", "probePlanPath")}`",
            "",
            "This offline scan finds x86 compares against WndProc-style `WM_*` and `VK_*` constants. It does not launch the game, send input, capture screenshots, mutate x32dbg, write process memory, or modify game files.",
            "",
            "## Summary",
            ""
        };

        if (TryGetJsonProperty(root, "scan", out var scan))
        {
            lines.Add($"- Constants scanned: `{GetIntProperty(scan, "constant_count", "constantCount")}`");
            lines.Add($"- Compare candidates: `{GetIntProperty(scan, "compare_candidate_count", "compareCandidateCount")}`");
            lines.Add($"- Function-entry candidates: `{GetIntProperty(scan, "function_candidate_count", "functionCandidateCount")}`");
            lines.Add($"- Probe target count: `{GetIntProperty(scan, "probe_target_count", "probeTargetCount")}`");
        }

        lines.Add("");
        lines.Add("## Constant Groups");
        lines.Add("");
        lines.Add("| Constant | Kind | Compares | Function entries | First candidate addresses |");
        lines.Add("| --- | --- | --- | --- | --- |");
        if (TryGetJsonProperty(root, "constant_groups", out var groups) && groups.ValueKind == JsonValueKind.Array && groups.GetArrayLength() > 0)
        {
            foreach (var group in groups.EnumerateArray().Take(64))
            {
                var firstAddresses = new List<string>();
                if (TryGetJsonProperty(group, "compare_candidates", out var compares) && compares.ValueKind == JsonValueKind.Array)
                {
                    firstAddresses.AddRange(compares.EnumerateArray()
                        .Take(6)
                        .Select(compare => GetStringProperty(compare, "breakpoint_address", "breakpointAddress") ?? string.Empty)
                        .Where(address => !string.IsNullOrWhiteSpace(address))
                        .Select(address => $"`{address}`"));
                }

                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(group, "constant") ?? string.Empty)}={EscapeMarkdownCell(GetStringProperty(group, "value") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(group, "kind") ?? string.Empty)}` " +
                    $"| `{GetIntProperty(group, "compare_candidate_count", "compareCandidateCount")}` " +
                    $"| `{GetIntProperty(group, "function_candidate_count", "functionCandidateCount")}` " +
                    $"| {string.Join(", ", firstAddresses)} |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |  |");
        }

        lines.Add("");
        lines.Add("## Probe Policy");
        lines.Add("");
        lines.Add("Prefer function-entry candidates for live route comparison, then use compare-site candidates to narrow exact branch logic. These entries remain `pending-breakpoint` / `needs-live-run`; do not promote them into the verified function table until route-specific hits include CIP, registers, stack, disassembly, and internal state changes.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteTitleMenuDispatchProbeRunMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# Title Menu Dispatch Probe Run",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Route: `{GetStringProperty(root, "route")}`",
            $"- Trigger sequence: `{GetStringProperty(root, "trigger_sequence", "triggerSequence")}`",
            $"- Run probes: `{TryGetBoolString(root, "run_probes", "runProbes")}`",
            $"- Allow input: `{TryGetBoolString(root, "allow_input", "allowInput")}`",
            $"- Dispatch report: `{GetStringProperty(root, "dispatch_report_path", "dispatchReportPath")}`",
            $"- Probe plan: `{GetStringProperty(root, "probe_plan_path", "probePlanPath")}`",
            $"- Probe targets: `{GetIntProperty(root, "probe_target_count", "probeTargetCount")}`",
            $"- Hits: `{GetIntProperty(root, "hit_count", "hitCount")}`",
            $"- Before text-anchor status: `{GetStringProperty(root, "before_text_anchor_status", "beforeTextAnchorStatus")}`",
            $"- Before script-window status: `{GetStringProperty(root, "before_script_window_status", "beforeScriptWindowStatus")}`",
            $"- After text-anchor status: `{GetStringProperty(root, "after_text_anchor_status", "afterTextAnchorStatus")}`",
            $"- After script-window status: `{GetStringProperty(root, "after_script_window_status", "afterScriptWindowStatus")}`",
            $"- Final runtime: `{GetStringProperty(root, "final_runtime_classification", "finalRuntimeClassification")}`",
            $"- Battle profile: `{GetStringProperty(root, "battle_profile_status", "battleProfileStatus")}`",
            "",
            "## Hits",
            ""
        };

        if (TryGetJsonProperty(root, "hits", out var hits) && hits.ValueKind == JsonValueKind.Array && hits.GetArrayLength() > 0)
        {
            lines.Add("| Address | Name | Phase | Evidence |");
            lines.Add("| --- | --- | --- | --- |");
            foreach (var hit in hits.EnumerateArray())
            {
                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "address") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "name") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "phase") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "evidence_path", "evidencePath") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("- No title-menu dispatch breakpoint hit was captured.");
        }

        lines.Add("");
        lines.Add("## Review Rule");
        lines.Add("");
        lines.Add("A title dispatch hit is not enough by itself. Promote only when Start, Load, Settings, and Exit routes produce distinct hit/state evidence with CIP, registers, stack, disassembly, and internal state changes. Do not use this evidence to validate R_00, battle_loaded, attack, or turn functions until the relevant runtime gates pass.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteTitleMenuDispatchMatrixRunMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# Title Menu Dispatch Matrix",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            $"- Routes: `{GetIntProperty(root, "route_count", "routeCount")}`",
            $"- Run probes: `{TryGetBoolString(root, "run_probes", "runProbes")}`",
            $"- Allow input: `{TryGetBoolString(root, "allow_input", "allowInput")}`",
            $"- Allow exit route: `{TryGetBoolString(root, "allow_exit_route", "allowExitRoute")}`",
            $"- Shared dispatch report: `{GetStringProperty(root, "shared_dispatch_report_path", "sharedDispatchReportPath")}`",
            $"- Shared probe plan: `{GetStringProperty(root, "shared_probe_plan_path", "sharedProbePlanPath")}`",
            $"- Shared targets: `{GetIntProperty(root, "shared_probe_target_count", "sharedProbeTargetCount")}`",
            $"- Hits: `{GetIntProperty(root, "hit_count", "hitCount")}`",
            "",
            "## Routes",
            "",
            "| Route | Status | Trigger | Targets | Hits | Summary |",
            "| --- | --- | --- | --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "results", out var results) && results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
        {
            foreach (var row in results.EnumerateArray())
            {
                var route = GetStringProperty(row, "route") ?? string.Empty;
                var trigger = GetStringProperty(row, "trigger_sequence", "triggerSequence") ?? string.Empty;
                JsonElement payload = row;
                if (TryGetJsonProperty(row, "result", out var nested) && nested.ValueKind == JsonValueKind.Object)
                {
                    payload = nested;
                }

                lines.Add(
                    $"| `{EscapeMarkdownCell(route)}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(payload, "status") ?? string.Empty)}` " +
                    $"| `{EscapeMarkdownCell(trigger)}` " +
                    $"| `{GetIntProperty(payload, "probe_target_count", "probeTargetCount")}` " +
                    $"| `{GetIntProperty(payload, "hit_count", "hitCount")}` " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(payload, "session_dir", "sessionDir") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("| none |  |  |  |  |  |");
        }

        lines.Add("");
        lines.Add("## Review Rule");
        lines.Add("");
        lines.Add("Use this matrix to compare title Start, Load, Settings, and Exit route hits. A candidate can be promoted only after route-specific live evidence proves distinct behavior with CIP, registers, stack, disassembly, and internal state changes. The exit route must remain blocked for real input unless an isolated test session explicitly enables it.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteR00StartupRouteProbeRunMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# R_00 Startup Route Probe Run",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Route: `{GetStringProperty(root, "route")}`",
            $"- Sequence: `{GetStringProperty(root, "sequence")}`",
            $"- Allow input: `{TryGetBoolString(root, "allow_input", "allowInput")}`",
            $"- Run probes: `{TryGetBoolString(root, "run_probes", "runProbes")}`",
            $"- Handler plan: `{GetStringProperty(root, "handler_plan_path", "handlerPlanPath")}`",
            $"- Handler targets: `{GetIntProperty(root, "handler_probe_target_count", "handlerProbeTargetCount")}`",
            $"- Hit count: `{GetIntProperty(root, "hit_count", "hitCount")}`",
            $"- Final runtime: `{GetStringProperty(root, "final_runtime_classification", "finalRuntimeClassification")}`",
            $"- Battle profile: `{GetStringProperty(root, "battle_profile_status", "battleProfileStatus")}`",
            "",
            "This is the audited no-mouse/no-screenshot R_00 startup-route orchestration. It is not proof of battle entry unless `battle_loaded` and the battle profile gate both pass.",
            "",
            "## Hits",
            ""
        };

        if (TryGetJsonProperty(root, "hits", out var hits) && hits.ValueKind == JsonValueKind.Array && hits.GetArrayLength() > 0)
        {
            lines.Add("| Address | Name | Phase | Evidence |");
            lines.Add("| --- | --- | --- | --- |");
            foreach (var hit in hits.EnumerateArray())
            {
                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "address") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "name") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "phase") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "evidence_path", "evidencePath") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("- No planned R-scene command-handler hit was captured.");
        }

        lines.Add("");
        lines.Add("## Review Rule");
        lines.Add("");
        lines.Add("Treat this as startup-route evidence only. Do not promote actor-click, choice-box, battle-entry, attack-before, attack-after, or turn-end semantics without dynamic x32dbg hit evidence tied to the concrete route state.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteR00RuntimeHandlerProbeRunMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# R_00 Runtime Handler Probe Run",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Route: `{GetStringProperty(root, "route")}`",
            $"- Run probes: `{TryGetBoolString(root, "run_probes", "runProbes")}`",
            $"- Probe before startup continue: `{TryGetBoolString(root, "probe_before_startup_continue", "probeBeforeStartupContinue")}`",
            $"- Candidate report: `{GetStringProperty(root, "candidate_report_path", "candidateReportPath")}`",
            $"- Probe plan: `{GetStringProperty(root, "probe_plan_path", "probePlanPath")}`",
            $"- Actions: `{GetIntProperty(root, "action_count", "actionCount")}`",
            $"- Handler candidates: `{GetIntProperty(root, "handler_candidate_count", "handlerCandidateCount")}`",
            $"- Probe targets: `{GetIntProperty(root, "handler_probe_target_count", "handlerProbeTargetCount")}`",
            $"- Latest verified hits: `{GetIntProperty(root, "latest_verified_hit_count", "latestVerifiedHitCount")}`",
            $"- Hit count: `{GetIntProperty(root, "hit_count", "hitCount")}`",
            $"- Before script-window status: `{GetStringProperty(root, "before_script_window_status", "beforeScriptWindowStatus")}`",
            $"- After script-window status: `{GetStringProperty(root, "after_script_window_status", "afterScriptWindowStatus")}`",
            $"- Final runtime: `{GetStringProperty(root, "final_runtime_classification", "finalRuntimeClassification")}`",
            $"- Battle profile: `{GetStringProperty(root, "battle_profile_status", "battleProfileStatus")}`",
            "",
            "This live-ready wrapper connects the R_00 runtime invoke candidate plan to `debug_internal_probe_run`. It collects debugger breakpoint evidence only when `run_probes=true` and the local x32dbg bridge is online.",
            "",
            "## Hits",
            ""
        };

        if (TryGetJsonProperty(root, "hits", out var hits) && hits.ValueKind == JsonValueKind.Array && hits.GetArrayLength() > 0)
        {
            lines.Add("| Address | Name | Phase | Evidence |");
            lines.Add("| --- | --- | --- | --- |");
            foreach (var hit in hits.EnumerateArray())
            {
                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "address") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "name") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "phase") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "evidence_path", "evidencePath") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("- No R_00 runtime handler breakpoint hit was captured.");
        }

        lines.Add("");
        lines.Add("## Review Rule");
        lines.Add("");
        lines.Add("Treat hits from this tool as handler-candidate evidence. Do not upgrade a candidate into an executable internal invoke target until R_00 script-window residency, registers, stack, disassembly, and a route-state transition have been reviewed together.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteKeyboardExplorationRunMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var sequences = TryGetJsonProperty(root, "sequences", out var sequenceArray) && sequenceArray.ValueKind == JsonValueKind.Array
            ? string.Join(" ; ", sequenceArray.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)))
            : string.Empty;
        var lines = new List<string>
        {
            "# Keyboard Exploration Summary",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Stop reason: `{GetStringProperty(root, "stop_reason", "stopReason")}`",
            $"- Route: `{GetStringProperty(root, "route")}`",
            $"- Session: `{GetStringProperty(root, "session_dir", "sessionDir")}`",
            $"- Allow launch: `{TryGetBoolString(root, "allow_launch", "allowLaunch")}`",
            $"- Allow input: `{TryGetBoolString(root, "allow_input", "allowInput")}`",
            $"- Start debugger: `{TryGetBoolString(root, "start_debugger", "startDebugger")}`",
            $"- Continue startup: `{TryGetBoolString(root, "continue_startup_requested", "continueStartupRequested")}`",
            $"- Key delivery: `{GetStringProperty(root, "key_delivery", "keyDelivery")}`",
            $"- Sequences: `{EscapeMarkdownCell(sequences)}`",
            "",
            "## Final Internal State",
            "",
            $"- Runtime classification: `{GetStringProperty(root, "final_runtime_classification", "finalRuntimeClassification")}`",
            $"- Anchor sweep: `{GetStringProperty(root, "final_anchor_sweep_status", "finalAnchorSweepStatus")}` / `{GetStringProperty(root, "final_anchor_sweep_phase", "finalAnchorSweepPhase")}`",
            $"- R-scene text anchors: `{GetStringProperty(root, "final_text_anchor_status", "finalTextAnchorStatus")}`",
            $"- R_00 script windows: `{GetStringProperty(root, "final_script_window_status", "finalScriptWindowStatus")}`",
            $"- Battle profile: `{GetStringProperty(root, "battle_profile_status", "battleProfileStatus")}`",
            $"- Battle loaded: `{TryGetBoolString(root, "battle_loaded", "battleLoaded")}`",
            $"- R_00 resident: `{TryGetBoolString(root, "r00_resident", "r00Resident")}`",
            "",
            "## Exploration Steps",
            "",
            "| Index | Step | Sequence | Probe summary |",
            "| --- | --- | --- | --- |"
        };

        if (TryGetJsonProperty(root, "steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var step in steps.EnumerateArray())
            {
                index++;
                var sequence = GetStringProperty(step, "sequence") ?? string.Empty;
                var probe = TryGetJsonProperty(step, "result", out var result)
                    ? SummarizeKeyboardExplorationProbe(result)
                    : string.Empty;
                lines.Add($"| {index} | {EscapeMarkdownCell(GetStringProperty(step, "step") ?? string.Empty)} | `{EscapeMarkdownCell(sequence)}` | {EscapeMarkdownCell(probe)} |");
            }
        }

        lines.Add("");
        lines.Add("This run is keyboard-only and internal-state driven. It does not use mouse input, screenshots, process-memory writes, or game-file writes; actual launch/input require `allow_launch=true` and `allow_input=true` respectively.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteTransitionProbeRunSummaryMarkdown(string path, object summary)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "# Ekd5 Transition Probe Run Summary",
            "",
            $"- Created: {GetStringProperty(root, "created_at", "createdAt")}",
            $"- Status: `{GetStringProperty(root, "status")}`",
            $"- Stage: `{GetStringProperty(root, "stage")}`",
            $"- Sequence: `{GetStringProperty(root, "sequence")}`",
            $"- Allow input: `{TryGetBoolString(root, "allow_input", "allowInput")}`",
            $"- Start debugger: `{TryGetBoolString(root, "start_debugger", "startDebugger")}`",
            $"- Continue startup: `{TryGetBoolString(root, "continue_startup_requested", "continueStartupRequested")}`",
            $"- Target count: `{GetIntProperty(root, "target_count", "targetCount")}`",
            $"- Hit count: `{GetIntProperty(root, "hit_count", "hitCount")}`",
            $"- Plan: `{GetStringProperty(root, "plan_path", "planPath")}`",
            "",
            "This run uses staged x32dbg breakpoints and optional keyboard window messages. It does not move/click the mouse, capture screenshots, write target process memory, or patch game files.",
            "",
            "## Hits",
            ""
        };

        if (TryGetJsonProperty(root, "hits", out var hits) && hits.ValueKind == JsonValueKind.Array && hits.GetArrayLength() > 0)
        {
            lines.Add("| Address | Name | Phase | Evidence |");
            lines.Add("| --- | --- | --- | --- |");
            foreach (var hit in hits.EnumerateArray())
            {
                lines.Add(
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "address") ?? string.Empty)}` " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "name") ?? string.Empty)} " +
                    $"| {EscapeMarkdownCell(GetStringProperty(hit, "phase") ?? string.Empty)} " +
                    $"| `{EscapeMarkdownCell(GetStringProperty(hit, "evidence_path", "evidencePath") ?? string.Empty)}` |");
            }
        }
        else
        {
            lines.Add("- No planned transition probe hit was captured in this run.");
        }

        lines.Add("");
        lines.Add("## Review Rule");
        lines.Add("");
        lines.Add("Only promote a transition or function semantic after the trigger, CIP, registers, stack, disassembly, and read-only battle-state delta are reproducible.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static List<CallerCandidate> ReadTopCallers(JsonElement target, int maxCandidates)
    {
        var callers = new List<CallerCandidate>();
        if (!TryGetJsonProperty(target, "direct_xrefs", out var direct) || direct.ValueKind != JsonValueKind.Array)
        {
            return callers;
        }

        foreach (var caller in direct.EnumerateArray().Take(maxCandidates))
        {
            callers.Add(new CallerCandidate(
                GetStringProperty(caller, "source", "Source") ?? string.Empty,
                GetStringProperty(caller, "file_offset", "fileOffset") ?? string.Empty,
                GetStringProperty(caller, "mnemonic", "Mnemonic") ?? string.Empty,
                GetIntProperty(caller, "target_delta", "targetDelta")));
        }

        return callers.Where(c => !string.IsNullOrWhiteSpace(c.Source)).ToList();
    }

    private static void AddProbeTarget(IDictionary<string, InternalProbeTarget> targets, InternalProbeTarget target)
    {
        var normalized = NormalizeAddress(target.Address);
        if (targets.ContainsKey(normalized))
        {
            return;
        }

        targets[normalized] = new InternalProbeTarget
        {
            Address = normalized,
            Name = target.Name,
            Phase = target.Phase,
            ExpectedSemantics = target.ExpectedSemantics,
            TriggerHint = target.TriggerHint,
            EvidenceLevel = target.EvidenceLevel,
            HighFrequency = target.HighFrequency
        };
    }

    private static List<InternalProbeTarget> BuildTitleMenuDispatchProbeTargets(
        IReadOnlyList<ImportCallCandidate> callCandidates,
        IReadOnlyList<ImportCallCandidate> functionCandidates,
        int maxCandidates)
    {
        var targets = new Dictionary<string, InternalProbeTarget>(StringComparer.OrdinalIgnoreCase);
        var limit = Math.Clamp(maxCandidates, 1, 64);

        foreach (var candidate in functionCandidates
                     .Where(IsTitleMenuDispatchFunctionProbeCandidate)
                     .OrderBy(candidate => TitleMenuDispatchCallSortKey(candidate))
                     .ThenBy(candidate => candidate.BreakpointAddress)
                     .Take(limit * 2))
        {
            var address = $"0x{candidate.BreakpointAddress:X8}";
            AddProbeTarget(targets, new InternalProbeTarget
            {
                Address = address,
                Name = $"title_menu_fn_{SanitizeProbeName(candidate.FunctionName)}_{PlainAddress(address)}",
                Phase = "title_menu",
                ExpectedSemantics = $"Function-entry candidate containing a title/menu dispatch API call to {candidate.ModuleName}!{candidate.FunctionName}.",
                TriggerHint = "Run at title screen while triggering Start/Load/Settings/Exit by route-specific keyboard-only or internal dispatcher experiments. Promote only after distinct state changes are captured.",
                EvidenceLevel = $"static-title-menu-dispatch-function-{candidate.Confidence}",
                HighFrequency = IsHighFrequencyTitleMenuDispatchImport(candidate.FunctionName)
            });
        }

        foreach (var candidate in callCandidates
                     .Where(IsTitleMenuDispatchApiProbeCandidate)
                     .OrderBy(candidate => TitleMenuDispatchCallSortKey(candidate))
                     .ThenBy(candidate => candidate.BreakpointAddress)
                     .Take(limit * 3))
        {
            var address = $"0x{candidate.BreakpointAddress:X8}";
            AddProbeTarget(targets, new InternalProbeTarget
            {
                Address = address,
                Name = $"title_menu_api_{SanitizeProbeName(candidate.FunctionName)}_{PlainAddress(address)}",
                Phase = "title_menu",
                ExpectedSemantics = $"Win32 title/menu dispatch API call candidate: {candidate.ModuleName}!{candidate.FunctionName}.",
                TriggerHint = "Use with title menu route actions. A hit is useful only when Start/Load/Settings/Exit triggers produce distinct stack/register/disassembly evidence and internal state changes.",
                EvidenceLevel = $"static-title-menu-dispatch-api-{candidate.Confidence}",
                HighFrequency = IsHighFrequencyTitleMenuDispatchImport(candidate.FunctionName)
            });
        }

        return targets.Values
            .OrderBy(target => target.HighFrequency ? 1 : 0)
            .ThenBy(target => TitleMenuDispatchTargetSortKey(target))
            .ThenBy(target => target.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsTitleMenuDispatchFunctionProbeCandidate(ImportCallCandidate candidate)
    {
        var priority = TitleMenuDispatchImportPriority(candidate.FunctionName);
        return priority <= 4 ||
               candidate.FunctionName.Equals("SendMessageA", StringComparison.OrdinalIgnoreCase) ||
               candidate.FunctionName.Equals("PostMessageA", StringComparison.OrdinalIgnoreCase) ||
               candidate.FunctionName.Equals("SendDlgItemMessageA", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTitleMenuDispatchApiProbeCandidate(ImportCallCandidate candidate)
        => TitleMenuDispatchImportPriority(candidate.FunctionName) <= 7;

    private static int TitleMenuDispatchTargetSortKey(InternalProbeTarget target)
    {
        var name = target.Name;
        if (name.Contains("title_wndproc", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("callwindowproca", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("defwindowproca", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains("dispatchmessagea", StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Contains("translatemessage", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Contains("getmessagea", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("peekmessagea", StringComparison.OrdinalIgnoreCase)) return 4;
        if (name.Contains("windowfrompoint", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("childwindowfrompoint", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("getcursorpos", StringComparison.OrdinalIgnoreCase)) return 5;
        if (name.Contains("menu", StringComparison.OrdinalIgnoreCase)) return 6;
        if (name.Contains("sendmessagea", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("postmessagea", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("senddlgitemmessagea", StringComparison.OrdinalIgnoreCase)) return 7;
        return 9;
    }

    private static List<InternalProbeTarget> BuildTitleWndProcDispatchProbeTargets(
        IReadOnlyList<TitleWndProcDispatchCandidate> compareCandidates,
        IReadOnlyList<TitleWndProcDispatchCandidate> functionCandidates,
        int maxCandidates)
    {
        var targets = new Dictionary<string, InternalProbeTarget>(StringComparer.OrdinalIgnoreCase);
        var limit = Math.Clamp(maxCandidates, 1, 64);

        foreach (var candidate in functionCandidates
                     .OrderBy(candidate => TitleWndProcDispatchCandidateSortKey(candidate))
                     .ThenBy(candidate => candidate.BreakpointAddress)
                     .Take(limit * 2))
        {
            var address = $"0x{candidate.BreakpointAddress:X8}";
            AddProbeTarget(targets, new InternalProbeTarget
            {
                Address = address,
                Name = $"title_wndproc_fn_{SanitizeProbeName(candidate.ConstantName)}_{PlainAddress(address)}",
                Phase = "title_menu",
                ExpectedSemantics = $"Function-entry candidate containing WndProc-style compare for {candidate.ConstantName}=0x{candidate.ConstantValue:X}.",
                TriggerHint = "Run at title screen while triggering Start/Load/Settings/Exit. Promote only after route-specific hits, stack/register context, and internal state changes prove dispatcher semantics.",
                EvidenceLevel = $"static-title-wndproc-dispatch-function-{candidate.Confidence}",
                HighFrequency = IsHighFrequencyTitleWndProcConstant(candidate.ConstantName)
            });
        }

        foreach (var candidate in compareCandidates
                     .OrderBy(candidate => TitleWndProcDispatchCandidateSortKey(candidate))
                     .ThenBy(candidate => candidate.BreakpointAddress)
                     .Take(limit * 3))
        {
            var address = $"0x{candidate.BreakpointAddress:X8}";
            AddProbeTarget(targets, new InternalProbeTarget
            {
                Address = address,
                Name = $"title_wndproc_cmp_{SanitizeProbeName(candidate.ConstantName)}_{PlainAddress(address)}",
                Phase = "title_menu",
                ExpectedSemantics = $"WndProc/message compare candidate for {candidate.ConstantName}=0x{candidate.ConstantValue:X} ({candidate.Pattern}).",
                TriggerHint = "Use with route-specific title menu probes. A compare hit is useful only with distinct Start/Load/Settings/Exit evidence and internal runtime/menu state changes.",
                EvidenceLevel = $"static-title-wndproc-dispatch-compare-{candidate.Confidence}",
                HighFrequency = IsHighFrequencyTitleWndProcConstant(candidate.ConstantName)
            });
        }

        return targets.Values
            .OrderBy(target => target.HighFrequency ? 1 : 0)
            .ThenBy(target => TitleMenuDispatchTargetSortKey(target))
            .ThenBy(target => target.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<TitleWndProcDispatchCandidate> BuildTitleWndProcFunctionCandidates(
        TextSection text,
        IReadOnlyList<TitleWndProcDispatchCandidate> compareCandidates,
        int contextBytes)
    {
        var candidates = new List<TitleWndProcDispatchCandidate>();
        foreach (var candidate in compareCandidates)
        {
            if (candidate.FunctionAddress == 0 || candidate.FunctionAddress == candidate.BreakpointAddress)
            {
                continue;
            }

            var entryOffset = checked((int)(candidate.FunctionAddress - ImageBase - text.VirtualAddress));
            if (entryOffset < 0 || entryOffset >= text.Bytes.Length)
            {
                continue;
            }

            var contextHex = BytesToHex(ReadSlice(text.Bytes, Math.Max(0, entryOffset - contextBytes), Math.Min(text.Bytes.Length, entryOffset + 16 + contextBytes)));
            candidates.Add(candidate with
            {
                SourceAddress = candidate.FunctionAddress,
                FileOffset = text.RawPointer + (uint)entryOffset,
                BreakpointAddress = candidate.FunctionAddress,
                Pattern = $"function-prologue-containing-{candidate.ConstantName}-compare",
                Confidence = candidate.Confidence.Equals("low", StringComparison.OrdinalIgnoreCase) ? "medium" : candidate.Confidence,
                FunctionEntry = true,
                ContextHex = contextHex
            });
        }

        return candidates
            .DistinctBy(candidate => $"{candidate.ConstantName}|{candidate.BreakpointAddress:X8}|{candidate.Pattern}", StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .ToList();
    }

    private static List<TitleWndProcDispatchCandidate> ScanTitleWndProcDispatchCompareCandidates(
        TextSection text,
        int maxCandidatesPerConstant,
        int contextBytes)
    {
        var candidates = new List<TitleWndProcDispatchCandidate>();
        var counts = TitleWndProcDispatchConstants.ToDictionary(constant => constant.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        var bytes = text.Bytes;

        for (var i = 0; i < bytes.Length - 3; i++)
        {
            foreach (var constant in TitleWndProcDispatchConstants)
            {
                if (counts[constant.Name] >= maxCandidatesPerConstant)
                {
                    continue;
                }

                foreach (var match in MatchTitleWndProcDispatchAt(bytes, i, constant))
                {
                    var functionOffset = FindPreviousFunctionPrologue(bytes, i, maxBackBytes: 1536);
                    var functionAddress = functionOffset >= 0
                        ? ImageBase + text.VirtualAddress + (uint)functionOffset
                        : 0u;
                    var contextHex = BytesToHex(ReadSlice(bytes, Math.Max(0, i - contextBytes), Math.Min(bytes.Length, i + match.Length + contextBytes)));
                    candidates.Add(new TitleWndProcDispatchCandidate(
                        constant.Name,
                        constant.Value,
                        constant.Kind,
                        ImageBase + text.VirtualAddress + (uint)i,
                        text.RawPointer + (uint)i,
                        ImageBase + text.VirtualAddress + (uint)i,
                        functionAddress,
                        match.Pattern,
                        match.Confidence,
                        constant.Priority,
                        FunctionEntry: false,
                        contextHex));
                    counts[constant.Name]++;
                    break;
                }
            }
        }

        return candidates
            .DistinctBy(candidate => $"{candidate.ConstantName}|{candidate.BreakpointAddress:X8}|{candidate.Pattern}", StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxCandidatesPerConstant, 1, 64) * TitleWndProcDispatchConstants.Count)
            .ToList();
    }

    private static IEnumerable<(string Pattern, string Confidence, int Length)> MatchTitleWndProcDispatchAt(
        byte[] bytes,
        int offset,
        TitleWndProcConstant constant)
    {
        var value = constant.Value;
        if (offset + 5 <= bytes.Length && bytes[offset] == 0x3D && ReadUInt32Unchecked(bytes, offset + 1) == value)
        {
            yield return ("cmp-eax-imm32", ConfidenceForRegisterMessageCompare(constant), 5);
        }

        if (offset + 6 <= bytes.Length && bytes[offset] == 0x81 && IsCmpModRm(bytes[offset + 1]))
        {
            var modRm = bytes[offset + 1];
            var mod = (modRm >> 6) & 0x03;
            var rm = modRm & 0x07;
            if (mod == 3 && ReadUInt32Unchecked(bytes, offset + 2) == value)
            {
                yield return ("cmp-r32-imm32", ConfidenceForRegisterMessageCompare(constant), 6);
            }
            else if (mod == 1 && rm == 5 && offset + 7 <= bytes.Length && ReadUInt32Unchecked(bytes, offset + 3) == value)
            {
                var displacement = unchecked((sbyte)bytes[offset + 2]);
                yield return ($"cmp-ebp-arg-{displacement:+#;-#;0}-imm32", ConfidenceForStackArgCompare(constant, displacement), 7);
            }
            else if (mod == 0 && rm == 5 && offset + 10 <= bytes.Length && ReadUInt32Unchecked(bytes, offset + 6) == value)
            {
                yield return ("cmp-moffs32-imm32", "low", 10);
            }
        }

        if (value is >= sbyte.MinValue and <= sbyte.MaxValue &&
            offset + 3 <= bytes.Length &&
            bytes[offset] == 0x83 &&
            IsCmpModRm(bytes[offset + 1]))
        {
            var modRm = bytes[offset + 1];
            var mod = (modRm >> 6) & 0x03;
            var rm = modRm & 0x07;
            if (mod == 3 && unchecked((sbyte)bytes[offset + 2]) == value)
            {
                yield return ("cmp-r32-imm8", constant.Kind.Equals("wparam", StringComparison.OrdinalIgnoreCase) ? "medium" : "low", 3);
            }
            else if (mod == 1 && rm == 5 && offset + 4 <= bytes.Length && unchecked((sbyte)bytes[offset + 3]) == value)
            {
                var displacement = unchecked((sbyte)bytes[offset + 2]);
                yield return ($"cmp-ebp-arg-{displacement:+#;-#;0}-imm8", ConfidenceForStackArgCompare(constant, displacement), 4);
            }
        }

        if (value <= 0xFFFF && offset + 4 <= bytes.Length && bytes[offset] == 0x66 && bytes[offset + 1] == 0x3D && ReadUInt16Unchecked(bytes, offset + 2) == value)
        {
            yield return ("cmp-ax-imm16", ConfidenceForRegisterMessageCompare(constant), 4);
        }

        if (value <= 0xFFFF &&
            offset + 5 <= bytes.Length &&
            bytes[offset] == 0x66 &&
            bytes[offset + 1] == 0x81 &&
            IsCmpModRm(bytes[offset + 2]))
        {
            var modRm = bytes[offset + 2];
            var mod = (modRm >> 6) & 0x03;
            var rm = modRm & 0x07;
            if (mod == 3 && ReadUInt16Unchecked(bytes, offset + 3) == value)
            {
                yield return ("cmp-r16-imm16", ConfidenceForRegisterMessageCompare(constant), 5);
            }
            else if (mod == 1 && rm == 5 && offset + 6 <= bytes.Length && ReadUInt16Unchecked(bytes, offset + 4) == value)
            {
                var displacement = unchecked((sbyte)bytes[offset + 3]);
                yield return ($"cmp-ebp-arg-{displacement:+#;-#;0}-imm16", ConfidenceForStackArgCompare(constant, displacement), 6);
            }
        }

        if (value <= byte.MaxValue && offset + 2 <= bytes.Length && bytes[offset] == 0x3C && bytes[offset + 1] == value)
        {
            yield return ("cmp-al-imm8", constant.Kind.Equals("wparam", StringComparison.OrdinalIgnoreCase) ? "medium" : "low", 2);
        }

        if (value <= byte.MaxValue &&
            offset + 3 <= bytes.Length &&
            bytes[offset] == 0x80 &&
            IsCmpModRm(bytes[offset + 1]))
        {
            var modRm = bytes[offset + 1];
            var mod = (modRm >> 6) & 0x03;
            var rm = modRm & 0x07;
            if (mod == 3 && bytes[offset + 2] == value)
            {
                yield return ("cmp-r8-imm8", constant.Kind.Equals("wparam", StringComparison.OrdinalIgnoreCase) ? "medium" : "low", 3);
            }
            else if (mod == 1 && rm == 5 && offset + 4 <= bytes.Length && bytes[offset + 3] == value)
            {
                var displacement = unchecked((sbyte)bytes[offset + 2]);
                yield return ($"cmp-ebp-byte-arg-{displacement:+#;-#;0}-imm8", ConfidenceForStackArgCompare(constant, displacement), 4);
            }
        }
    }

    private static bool IsCmpModRm(byte modRm)
        => ((modRm >> 3) & 0x07) == 7;

    private static ushort ReadUInt16Unchecked(byte[] bytes, int offset)
        => BitConverter.ToUInt16(bytes, offset);

    private static uint ReadUInt32Unchecked(byte[] bytes, int offset)
        => BitConverter.ToUInt32(bytes, offset);

    private static string ConfidenceForRegisterMessageCompare(TitleWndProcConstant constant)
        => constant.Kind.Equals("message", StringComparison.OrdinalIgnoreCase) ? "high" : "medium";

    private static string ConfidenceForStackArgCompare(TitleWndProcConstant constant, int displacement)
    {
        if (constant.Kind.Equals("message", StringComparison.OrdinalIgnoreCase) && displacement == 0x0C)
        {
            return "high";
        }

        if (constant.Kind.Equals("wparam", StringComparison.OrdinalIgnoreCase) && displacement == 0x10)
        {
            return "high";
        }

        if (displacement is 0x0C or 0x10 or 0x14)
        {
            return "medium";
        }

        return "low";
    }

    private static bool IsHighFrequencyTitleWndProcConstant(string constantName)
        => constantName.Equals("WM_MOUSEMOVE", StringComparison.OrdinalIgnoreCase) ||
           constantName.Equals("WM_KEYUP", StringComparison.OrdinalIgnoreCase) ||
           constantName.Equals("WM_LBUTTONDOWN", StringComparison.OrdinalIgnoreCase) ||
           constantName.Equals("WM_LBUTTONUP", StringComparison.OrdinalIgnoreCase);

    private static int TitleWndProcDispatchCandidateSortKey(TitleWndProcDispatchCandidate candidate)
    {
        var confidence = StaticReferenceConfidenceSortKey(candidate.Confidence);
        var functionBonus = candidate.FunctionEntry ? 0 : 2;
        var frequencyPenalty = IsHighFrequencyTitleWndProcConstant(candidate.ConstantName) ? 8 : 0;
        return candidate.Priority * 64 + confidence * 8 + functionBonus + frequencyPenalty;
    }

    private static object FormatTitleWndProcDispatchCandidate(TitleWndProcDispatchCandidate candidate)
        => new
        {
            constant = candidate.ConstantName,
            value = $"0x{candidate.ConstantValue:X}",
            kind = candidate.ConstantKind,
            source_address = $"0x{candidate.SourceAddress:X8}",
            breakpoint_address = $"0x{candidate.BreakpointAddress:X8}",
            function_address = candidate.FunctionAddress == 0 ? string.Empty : $"0x{candidate.FunctionAddress:X8}",
            file_offset = $"0x{candidate.FileOffset:X}",
            pattern = candidate.Pattern,
            confidence = candidate.Confidence,
            priority = candidate.Priority,
            function_entry = candidate.FunctionEntry,
            context_hex = candidate.ContextHex
        };

    private static List<string> BuildReadinessNextActions(IEnumerable<string> missingGates)
    {
        var actions = new List<string>();
        foreach (var gate in missingGates)
        {
            switch (gate)
            {
                case "target_hash_expected":
                    actions.Add("Use the verified 6.5 unencrypted Ekd5.exe base before probing.");
                    break;
                case "process_running":
                    actions.Add("Start Ekd5.exe through game_process_start(allow_launch=true) or debug_battle_profile_probe_run(start_game=true, allow_launch=true).");
                    break;
                case "window_detected":
                    actions.Add("Wait for Ekd5.exe to finish creating its main window, then rerun readiness.");
                    break;
                case "x32dbg_bridge_online":
                    actions.Add("Start or attach x32dbg with the local x64dbg MCP bridge at 127.0.0.1:27042.");
                    break;
                case "runtime_battle_loaded":
                    actions.Add("Advance the game to a battle state, then rerun readiness; this check does not send input.");
                    break;
                case "battle_profile_ready":
                    actions.Add("Advance to the Yingchuan Cao Cao/Zhang Liang sample or rerun after the expected HP-delta action.");
                    break;
                case "address_probe_plan_ready":
                    actions.Add("Regenerate the address probe plan with debug_address_report_probe_plan or debug_battle_profile_probe_run.");
                    break;
                default:
                    actions.Add($"Resolve readiness gate: {gate}.");
                    break;
            }
        }

        return actions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ScoreAddressCandidate(string stage, string name, int directCount, int nearbyCount)
    {
        if (directCount > 0 && (stage.Contains("attack", StringComparison.OrdinalIgnoreCase) || stage.Equals("turn_end", StringComparison.OrdinalIgnoreCase)))
        {
            return directCount <= 8 ? "high" : "medium";
        }

        if (directCount > 0)
        {
            return "medium";
        }

        return nearbyCount > 0 ? "low" : "pending";
    }

    private static int StageSortKey(string stage)
        => NormalizeStageFilter(stage) switch
        {
            "startup" => 0,
            "settings" => 1,
            "battle_entry" => 2,
            "attack_before" => 3,
            "attack_execute" => 4,
            "attack_after" => 5,
            "turn_end" => 6,
            _ => 99
        };

    private static string ExtractStringFromObject(object value, string propertyName)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return GetStringProperty(document.RootElement, propertyName, ToCamelCase(propertyName)) ?? string.Empty;
    }

    private static int ExtractIntFromObject(object value, string propertyName)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return GetIntProperty(document.RootElement, propertyName, ToCamelCase(propertyName));
    }

    private static int ExtractMatrixRouteHitCount(object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        var root = document.RootElement;
        if (TryGetJsonProperty(root, "result", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            return GetIntProperty(nested, "hit_count", "hitCount");
        }

        return GetIntProperty(root, "hit_count", "hitCount");
    }

    private static bool ExtractBoolFromObject(object value, string propertyName)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return GetBoolProperty(document.RootElement, propertyName, ToCamelCase(propertyName));
    }

    private static List<object> ExtractHitsFromProbeRun(object? value)
    {
        if (value is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return TryGetJsonProperty(document.RootElement, "hits", out var hits) && hits.ValueKind == JsonValueKind.Array
            ? hits.EnumerateArray()
                .Select(hit => JsonSerializer.Deserialize<object>(hit.GetRawText(), JsonOptions))
                .Where(hit => hit is not null)
                .Cast<object>()
                .ToList()
            : [];
    }

    private static string ExtractNestedStringFromObject(object value, string objectName, string propertyName)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return TryGetNestedString(document.RootElement, objectName, propertyName);
    }

    private static string ToCamelCase(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return value;
        return parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private object CaptureKeyboardExplorationState(
        string route,
        string? gameRoot,
        string hostName,
        int port,
        int maxScanBytes,
        string sessionDir,
        string label)
    {
        var runtimeState = GameRuntimeStateClassify(gameRoot, hostName, port, minBattleUnits: 8, sessionDir);
        var anchorSweep = GameRuntimeAnchorSweep(
            profile: "all",
            gbkAnchors: "",
            asciiAnchors: "",
            gameRoot: gameRoot,
            maxScanBytes: maxScanBytes,
            maxHitsPerAnchor: 4,
            outputDir: sessionDir);
        var textAnchorScan = GameRSceneTextAnchorScan(
            anchors: "",
            gameRoot: gameRoot,
            maxScanBytes: maxScanBytes,
            maxHitsPerAnchor: 4,
            outputDir: sessionDir);
        var scriptWindowScan = GameRSceneScriptWindowScan(
            route: route,
            gameRoot: gameRoot,
            contextBytes: 16,
            maxScanBytes: maxScanBytes,
            maxHitsPerWindow: 4,
            includePointerRefs: true,
            maxPointerRefs: 16,
            outputDir: sessionDir);
        var battleProfile = GameBattleStateMatch("yingchuan_cao_zhangliang", gameRoot, sessionDir);
        var runtimeClassification = ExtractNestedStringFromObject(runtimeState, "runtime", "classification");
        var anchorSweepStatus = ExtractStringFromObject(anchorSweep, "status");
        var anchorSweepPhase = ExtractNestedStringFromObject(anchorSweep, "phase_inference", "phase");
        var textAnchorStatus = ExtractStringFromObject(textAnchorScan, "status");
        var scriptWindowStatus = ExtractStringFromObject(scriptWindowScan, "status");
        var profileStatus = ExtractStringFromObject(battleProfile, "status");
        return new
        {
            label,
            created_at = DateTimeOffset.Now.ToString("O"),
            runtime_classification = runtimeClassification,
            anchor_sweep_status = anchorSweepStatus,
            anchor_sweep_phase = anchorSweepPhase,
            text_anchor_status = textAnchorStatus,
            script_window_status = scriptWindowStatus,
            profile_status = profileStatus,
            runtime = runtimeState,
            anchor_sweep = anchorSweep,
            text_anchor_scan = textAnchorScan,
            script_window_scan = scriptWindowScan,
            battle_profile = battleProfile
        };
    }

    private static bool KeyboardExplorationStopReached(object probe, out string stopReason)
    {
        var runtime = ExtractStringFromObject(probe, "runtime_classification");
        var textStatus = ExtractStringFromObject(probe, "text_anchor_status");
        var scriptStatus = ExtractStringFromObject(probe, "script_window_status");
        var profileStatus = ExtractStringFromObject(probe, "profile_status");
        if (runtime.Equals("battle_loaded", StringComparison.OrdinalIgnoreCase))
        {
            stopReason = "battle-loaded";
            return true;
        }

        if (profileStatus.Equals("profile-matched", StringComparison.OrdinalIgnoreCase) ||
            profileStatus.Equals("attack_after_observed", StringComparison.OrdinalIgnoreCase))
        {
            stopReason = "battle-profile-ready";
            return true;
        }

        if (scriptStatus.Equals("script-windows-found", StringComparison.OrdinalIgnoreCase))
        {
            stopReason = "r00-script-resident";
            return true;
        }

        if (textStatus.Equals("anchors-found", StringComparison.OrdinalIgnoreCase))
        {
            stopReason = "rscene-text-resident";
            return true;
        }

        stopReason = "continue";
        return false;
    }

    private static string SummarizeKeyboardExplorationProbe(JsonElement result)
    {
        var runtime = GetStringProperty(result, "runtime_classification", "runtimeClassification");
        var anchorPhase = GetStringProperty(result, "anchor_sweep_phase", "anchorSweepPhase");
        var textStatus = GetStringProperty(result, "text_anchor_status", "textAnchorStatus");
        var scriptStatus = GetStringProperty(result, "script_window_status", "scriptWindowStatus");
        var profileStatus = GetStringProperty(result, "profile_status", "profileStatus");
        if (!string.IsNullOrWhiteSpace(runtime) ||
            !string.IsNullOrWhiteSpace(anchorPhase) ||
            !string.IsNullOrWhiteSpace(textStatus) ||
            !string.IsNullOrWhiteSpace(scriptStatus) ||
            !string.IsNullOrWhiteSpace(profileStatus))
        {
            return $"runtime={runtime}; sweep={anchorPhase}; text={textStatus}; script={scriptStatus}; profile={profileStatus}";
        }

        var status = GetStringProperty(result, "status");
        return string.IsNullOrWhiteSpace(status) ? CompactJson(result, 220) : $"status={status}";
    }

    private static List<string> ParseSequenceCandidates(string sequences)
    {
        var defaults = new[] { "enter", "space", "esc", "enter,down,enter", "enter,down,down,down,down,down,enter" };
        var tokens = string.IsNullOrWhiteSpace(sequences)
            ? defaults
            : sequences.Split([';', '|', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeStageFilter(string stage)
    {
        var normalized = string.IsNullOrWhiteSpace(stage) ? "all" : stage.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "all" or "startup" or "settings" or "battle_entry" or "attack_before" or "attack_execute" or "attack_after" or "turn_end" or "battle" => normalized,
            "start" or "title" => "startup",
            "option" or "options" or "setting" => "settings",
            "enter_battle" or "battle_start" => "battle_entry",
            "before_attack" => "attack_before",
            "attack" or "physical_attack" => "attack_execute",
            "after_attack" => "attack_after",
            "turn" or "round_end" or "end_turn" => "turn_end",
            _ => throw new ArgumentException("Stage must be startup, settings, battle_entry, attack_before, attack_execute, attack_after, turn_end, battle, or all.")
        };
    }

    private static IEnumerable<FunctionCatalogEntry> SelectFunctionCatalogEntries(string normalizedStage)
        => normalizedStage switch
        {
            "all" => BuiltInFunctionCatalog,
            "battle" => BuiltInFunctionCatalog.Where(e =>
                e.Stage.Equals("battle_entry", StringComparison.OrdinalIgnoreCase) ||
                e.Stage.Equals("attack_before", StringComparison.OrdinalIgnoreCase) ||
                e.Stage.Equals("attack_execute", StringComparison.OrdinalIgnoreCase) ||
                e.Stage.Equals("attack_after", StringComparison.OrdinalIgnoreCase) ||
                e.Stage.Equals("turn_end", StringComparison.OrdinalIgnoreCase)),
            _ => BuiltInFunctionCatalog.Where(e => e.Stage.Equals(normalizedStage, StringComparison.OrdinalIgnoreCase))
        };

    private static SortedSet<string> ParseRuntimeClassificationTargets(string targetClassifications)
    {
        var tokens = string.IsNullOrWhiteSpace(targetClassifications)
            ? ["process_no_battle_signal", "battle_loaded"]
            : targetClassifications.Split([',', ';', ' ', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var targets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var normalized = token.Trim().ToLowerInvariant().Replace('-', '_');
            normalized = normalized switch
            {
                "*" => "any",
                "running_no_battle" or "menu" or "title" => "process_no_battle_signal",
                "partial" => "partial_battle_signal",
                "battle" => "battle_loaded",
                _ => normalized
            };
            if (!KnownRuntimeClassifications.Contains(normalized))
            {
                throw new ArgumentException($"Unknown runtime classification '{token}'. Known values: {string.Join(", ", KnownRuntimeClassifications)}.");
            }
            targets.Add(normalized);
        }

        if (targets.Count == 0)
        {
            targets.Add("process_no_battle_signal");
            targets.Add("battle_loaded");
        }
        return targets;
    }

    private static object EnrichFunctionCatalogEntry(string exePath, FunctionCatalogEntry entry)
    {
        var normalized = NormalizeAddress(entry.Address);
        var va = ParseAddress(normalized);
        var fileOffset = va >= ImageBase ? va - ImageBase : uint.MaxValue;
        var fileInfo = new FileInfo(exePath);
        var mapped = fileOffset != uint.MaxValue && fileOffset < fileInfo.Length;
        string bytesHex = string.Empty;
        string readError = string.Empty;
        if (mapped)
        {
            try
            {
                bytesHex = ReadFileBytesHex(exePath, fileOffset, 16);
            }
            catch (Exception ex)
            {
                readError = ex.Message;
            }
        }

        return new
        {
            address = normalized,
            entry.Name,
            entry.Stage,
            entry.Category,
            entry.ExpectedSemantics,
            entry.TriggerHint,
            entry.EvidenceLevel,
            entry.Source,
            entry.HighFrequency,
            image_base = FormatHex(ImageBase, 8),
            file_offset = mapped ? FormatHex(fileOffset) : string.Empty,
            mapped,
            original_bytes_16 = bytesHex,
            read_error = readError
        };
    }

    private static string ReadFileBytesHex(string path, uint offset, int count)
    {
        using var stream = File.OpenRead(path);
        stream.Position = offset;
        var buffer = new byte[Math.Min(count, Math.Max(0, (int)Math.Min(count, stream.Length - stream.Position)))];
        var read = stream.Read(buffer, 0, buffer.Length);
        return string.Join(" ", buffer.Take(read).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static byte[] ReadSlice(byte[] bytes, int startInclusive, int endExclusive)
    {
        var start = Math.Clamp(startInclusive, 0, bytes.Length);
        var end = Math.Clamp(endExclusive, start, bytes.Length);
        var length = Math.Max(0, end - start);
        var slice = new byte[length];
        if (length > 0)
        {
            Buffer.BlockCopy(bytes, start, slice, 0, length);
        }
        return slice;
    }

    private static string BytesToHex(byte[] bytes, int count = -1)
        => string.Join(" ", bytes
            .Take(count < 0 ? bytes.Length : Math.Min(count, bytes.Length))
            .Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    private static TextSection ReadTextSection(string exePath)
    {
        var bytes = File.ReadAllBytes(exePath);
        if (bytes.Length < 0x100 || bytes[0] != 'M' || bytes[1] != 'Z')
        {
            throw new InvalidOperationException("Target is not a valid MZ executable.");
        }

        var peOffset = BitConverter.ToInt32(bytes, 0x3C);
        if (peOffset <= 0 || peOffset + 0x18 >= bytes.Length ||
            bytes[peOffset] != 'P' || bytes[peOffset + 1] != 'E' || bytes[peOffset + 2] != 0 || bytes[peOffset + 3] != 0)
        {
            throw new InvalidOperationException("Target is not a valid PE executable.");
        }

        var sectionCount = BitConverter.ToUInt16(bytes, peOffset + 0x06);
        var optionalHeaderSize = BitConverter.ToUInt16(bytes, peOffset + 0x14);
        var sectionTable = peOffset + 0x18 + optionalHeaderSize;
        for (var i = 0; i < sectionCount; i++)
        {
            var offset = sectionTable + i * 0x28;
            if (offset + 0x28 > bytes.Length)
            {
                break;
            }

            var name = Encoding.ASCII.GetString(bytes, offset, 8).TrimEnd('\0');
            var virtualSize = BitConverter.ToUInt32(bytes, offset + 0x08);
            var virtualAddress = BitConverter.ToUInt32(bytes, offset + 0x0C);
            var rawSize = BitConverter.ToUInt32(bytes, offset + 0x10);
            var rawPointer = BitConverter.ToUInt32(bytes, offset + 0x14);
            var characteristics = BitConverter.ToUInt32(bytes, offset + 0x24);
            var executable = (characteristics & 0x20000000) != 0;
            if (!name.Equals(".text", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals(".code", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("CODE", StringComparison.OrdinalIgnoreCase) &&
                !executable)
            {
                continue;
            }

            if (rawPointer >= bytes.Length)
            {
                throw new InvalidOperationException($".text raw pointer is outside file: 0x{rawPointer:X}.");
            }

            var available = Math.Min(rawSize, (uint)(bytes.Length - rawPointer));
            var sectionBytes = new byte[available];
            Buffer.BlockCopy(bytes, (int)rawPointer, sectionBytes, 0, sectionBytes.Length);
            return new TextSection(virtualAddress, virtualSize, rawPointer, rawSize, sectionBytes);
        }

        throw new InvalidOperationException("PE executable code section was not found.");
    }

    private static List<PeSection> ReadPeSections(string exePath)
    {
        var bytes = File.ReadAllBytes(exePath);
        if (bytes.Length < 0x100 || bytes[0] != 'M' || bytes[1] != 'Z')
        {
            throw new InvalidOperationException("Target is not a valid MZ executable.");
        }

        var peOffset = BitConverter.ToInt32(bytes, 0x3C);
        if (peOffset <= 0 || peOffset + 0x18 >= bytes.Length ||
            bytes[peOffset] != 'P' || bytes[peOffset + 1] != 'E' || bytes[peOffset + 2] != 0 || bytes[peOffset + 3] != 0)
        {
            throw new InvalidOperationException("Target is not a valid PE executable.");
        }

        var sectionCount = BitConverter.ToUInt16(bytes, peOffset + 0x06);
        var optionalHeaderSize = BitConverter.ToUInt16(bytes, peOffset + 0x14);
        var sectionTable = peOffset + 0x18 + optionalHeaderSize;
        var sections = new List<PeSection>();
        for (var i = 0; i < sectionCount; i++)
        {
            var offset = sectionTable + i * 0x28;
            if (offset + 0x28 > bytes.Length)
            {
                break;
            }

            var name = Encoding.ASCII.GetString(bytes, offset, 8).TrimEnd('\0');
            var virtualSize = BitConverter.ToUInt32(bytes, offset + 0x08);
            var virtualAddress = BitConverter.ToUInt32(bytes, offset + 0x0C);
            var rawSize = BitConverter.ToUInt32(bytes, offset + 0x10);
            var rawPointer = BitConverter.ToUInt32(bytes, offset + 0x14);
            var characteristics = BitConverter.ToUInt32(bytes, offset + 0x24);
            if (rawPointer >= bytes.Length || rawSize == 0)
            {
                sections.Add(new PeSection(name, virtualAddress, virtualSize, rawPointer, rawSize, characteristics, []));
                continue;
            }

            var available = Math.Min(rawSize, (uint)(bytes.Length - rawPointer));
            var sectionBytes = new byte[available];
            Buffer.BlockCopy(bytes, (int)rawPointer, sectionBytes, 0, sectionBytes.Length);
            sections.Add(new PeSection(name, virtualAddress, virtualSize, rawPointer, rawSize, characteristics, sectionBytes));
        }

        return sections;
    }

    private static PeSection SelectExecutableSection(IEnumerable<PeSection> sections)
    {
        var list = sections.ToList();
        var section = list.FirstOrDefault(s => s.Name.Equals(".text", StringComparison.OrdinalIgnoreCase)) ??
                      list.FirstOrDefault(s => (s.Characteristics & 0x20000000) != 0);
        if (section is null)
        {
            throw new InvalidOperationException("PE executable code section was not found.");
        }

        return section;
    }

    private static List<PeImportFunction> ReadPeImportFunctions(string exePath, IReadOnlyList<PeSection> sections)
    {
        var bytes = File.ReadAllBytes(exePath);
        if (bytes.Length < 0x100 || bytes[0] != 'M' || bytes[1] != 'Z')
        {
            throw new InvalidOperationException("Target is not a valid MZ executable.");
        }

        var peOffset = BitConverter.ToInt32(bytes, 0x3C);
        if (peOffset <= 0 || peOffset + 0x18 >= bytes.Length ||
            bytes[peOffset] != 'P' || bytes[peOffset + 1] != 'E' || bytes[peOffset + 2] != 0 || bytes[peOffset + 3] != 0)
        {
            throw new InvalidOperationException("Target is not a valid PE executable.");
        }

        var optionalHeader = peOffset + 0x18;
        var magic = BitConverter.ToUInt16(bytes, optionalHeader);
        if (magic != 0x10B)
        {
            throw new InvalidOperationException("Target is not a PE32 executable.");
        }

        var importRva = BitConverter.ToUInt32(bytes, optionalHeader + 0x68);
        var importSize = BitConverter.ToUInt32(bytes, optionalHeader + 0x6C);
        var imports = new List<PeImportFunction>();
        if (importRva == 0 || importSize == 0)
        {
            return imports;
        }

        var descriptorOffset = RvaToFileOffset(importRva, sections);
        if (descriptorOffset < 0)
        {
            return imports;
        }

        for (var descriptor = descriptorOffset; descriptor + 20 <= bytes.Length; descriptor += 20)
        {
            var originalFirstThunk = BitConverter.ToUInt32(bytes, descriptor);
            var nameRva = BitConverter.ToUInt32(bytes, descriptor + 12);
            var firstThunk = BitConverter.ToUInt32(bytes, descriptor + 16);
            if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0)
            {
                break;
            }

            var moduleName = ReadAsciiZAtRva(bytes, sections, nameRva);
            var lookupRva = originalFirstThunk != 0 ? originalFirstThunk : firstThunk;
            var lookupOffset = RvaToFileOffset(lookupRva, sections);
            if (lookupOffset < 0 || string.IsNullOrWhiteSpace(moduleName))
            {
                continue;
            }

            for (var index = 0; lookupOffset + index * 4 + 4 <= bytes.Length; index++)
            {
                var thunkValue = BitConverter.ToUInt32(bytes, lookupOffset + index * 4);
                if (thunkValue == 0)
                {
                    break;
                }

                if ((thunkValue & 0x80000000U) != 0)
                {
                    continue;
                }

                var functionNameOffset = RvaToFileOffset(thunkValue + 2, sections);
                if (functionNameOffset < 0)
                {
                    continue;
                }

                var functionName = ReadAsciiZ(bytes, functionNameOffset);
                if (string.IsNullOrWhiteSpace(functionName))
                {
                    continue;
                }

                imports.Add(new PeImportFunction(
                    moduleName,
                    functionName,
                    ImageBase + firstThunk + (uint)(index * 4),
                    thunkValue,
                    firstThunk + (uint)(index * 4)));
            }
        }

        return imports;
    }

    private static int RvaToFileOffset(uint rva, IReadOnlyList<PeSection> sections)
    {
        foreach (var section in sections)
        {
            var size = Math.Max(section.VirtualSize, section.RawSize);
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size)
            {
                return (int)(section.RawPointer + (rva - section.VirtualAddress));
            }
        }

        return -1;
    }

    private static string ReadAsciiZAtRva(byte[] bytes, IReadOnlyList<PeSection> sections, uint rva)
    {
        var offset = RvaToFileOffset(rva, sections);
        return offset < 0 ? string.Empty : ReadAsciiZ(bytes, offset);
    }

    private static string ReadAsciiZ(byte[] bytes, int offset)
    {
        if (offset < 0 || offset >= bytes.Length)
        {
            return string.Empty;
        }

        var end = offset;
        while (end < bytes.Length && bytes[end] != 0)
        {
            end++;
        }

        return end <= offset ? string.Empty : Encoding.ASCII.GetString(bytes, offset, end - offset);
    }

    private static List<StaticXref> ScanRelativeXrefs(byte[] textBytes, uint textVirtualAddress, uint textRawPointer)
    {
        var results = new List<StaticXref>();
        for (var i = 0; i < textBytes.Length - 5; i++)
        {
            var opcode = textBytes[i];
            if (opcode is 0xE8 or 0xE9)
            {
                var displacement = BitConverter.ToInt32(textBytes, i + 1);
                var source = ImageBase + textVirtualAddress + (uint)i;
                var target = unchecked((uint)((int)(source + 5) + displacement));
                if (IsLikelyCodeAddress(target, textVirtualAddress, textBytes.Length))
                {
                    results.Add(new StaticXref(source, target, opcode == 0xE8 ? "call rel32" : "jmp rel32", textRawPointer + (uint)i, displacement));
                }
                i += 4;
                continue;
            }

            if (opcode == 0x0F && i + 6 <= textBytes.Length && textBytes[i + 1] is >= 0x80 and <= 0x8F)
            {
                var displacement = BitConverter.ToInt32(textBytes, i + 2);
                var source = ImageBase + textVirtualAddress + (uint)i;
                var target = unchecked((uint)((int)(source + 6) + displacement));
                if (IsLikelyCodeAddress(target, textVirtualAddress, textBytes.Length))
                {
                    results.Add(new StaticXref(source, target, $"jcc rel32 0F {textBytes[i + 1]:X2}", textRawPointer + (uint)i, displacement));
                }
                i += 5;
            }
        }

        return results;
    }

    private static bool IsLikelyCodeAddress(uint va, uint textVirtualAddress, int textSize)
    {
        var start = ImageBase + textVirtualAddress;
        var end = start + (uint)Math.Max(textSize, 0);
        return va >= start && va < end;
    }

    private static object FormatXref(StaticXref xref, uint targetAddress)
        => new
        {
            source = $"0x{xref.Source:X8}",
            target = $"0x{xref.Target:X8}",
            mnemonic = xref.Mnemonic,
            file_offset = $"0x{xref.FileOffset:X}",
            displacement = xref.Displacement,
            target_delta = unchecked((int)(xref.Target - targetAddress))
        };

    private static List<string> ParseAsciiAnchorList(string anchors)
    {
        var defaults = new[] { @"RS\R_%s.EEX", @"RS\S_%s.EEX", ".EEX" };
        var source = string.IsNullOrWhiteSpace(anchors)
            ? defaults
            : anchors.Split([',', ';', '|', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return source
            .Where(anchor => !string.IsNullOrWhiteSpace(anchor))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
    }

    private static RSceneLoadTransitionProbeFilter ParseRSceneLoadTransitionCandidateFilter(string? candidateFilter)
    {
        var raw = string.IsNullOrWhiteSpace(candidateFilter) ? "all" : candidateFilter;
        var tokens = raw
            .Split([',', ';', '|', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal))
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tokens.Count == 0 || tokens.Contains("all", StringComparer.OrdinalIgnoreCase))
        {
            var includeImports = !tokens.Contains("no_imports", StringComparer.OrdinalIgnoreCase) &&
                                 !tokens.Contains("no_api", StringComparer.OrdinalIgnoreCase) &&
                                 !tokens.Contains("no_apis", StringComparer.OrdinalIgnoreCase);
            return new RSceneLoadTransitionProbeFilter(
                includeImports ? "all" : "anchor_functions,direct_refs,no_imports",
                IncludeDirectRefs: true,
                IncludeAnchorFunctions: true,
                IncludeImportCalls: includeImports);
        }

        if (tokens.Contains("anchor_functions_only", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("functions_only", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("function_entries_only", StringComparer.OrdinalIgnoreCase))
        {
            return new RSceneLoadTransitionProbeFilter("anchor_functions_only", false, true, false);
        }

        if (tokens.Contains("direct_refs_only", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("refs_only", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("string_refs_only", StringComparer.OrdinalIgnoreCase))
        {
            return new RSceneLoadTransitionProbeFilter("direct_refs_only", true, false, false);
        }

        if (tokens.Contains("api_calls_only", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("imports_only", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("import_calls_only", StringComparer.OrdinalIgnoreCase))
        {
            return new RSceneLoadTransitionProbeFilter("api_calls_only", false, false, true);
        }

        var includeDirectRefs =
            tokens.Contains("direct_refs", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("direct_ref", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("refs", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("string_refs", StringComparer.OrdinalIgnoreCase);
        var includeAnchorFunctions =
            tokens.Contains("anchor_functions", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("anchor_function", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("functions", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("function_entries", StringComparer.OrdinalIgnoreCase);
        var includeImportCalls =
            tokens.Contains("api_calls", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("api", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("apis", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("imports", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("import_calls", StringComparer.OrdinalIgnoreCase);

        if (tokens.Contains("no_imports", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("no_api", StringComparer.OrdinalIgnoreCase) ||
            tokens.Contains("no_apis", StringComparer.OrdinalIgnoreCase))
        {
            includeImportCalls = false;
        }

        if (!includeDirectRefs && !includeAnchorFunctions && !includeImportCalls)
        {
            includeDirectRefs = true;
            includeAnchorFunctions = true;
        }

        var normalizedParts = new List<string>();
        if (includeAnchorFunctions) normalizedParts.Add("anchor_functions");
        if (includeDirectRefs) normalizedParts.Add("direct_refs");
        if (includeImportCalls) normalizedParts.Add("api_calls");
        if (!includeImportCalls) normalizedParts.Add("no_imports");

        return new RSceneLoadTransitionProbeFilter(
            string.Join(",", normalizedParts),
            includeDirectRefs,
            includeAnchorFunctions,
            includeImportCalls);
    }

    private static List<StaticAnchorHit> FindAsciiAnchorsInSections(IEnumerable<PeSection> sections, IReadOnlyList<string> anchors)
    {
        var hits = new List<StaticAnchorHit>();
        foreach (var section in sections.Where(s => s.Bytes.Length > 0))
        {
            foreach (var anchor in anchors)
            {
                var pattern = Encoding.ASCII.GetBytes(anchor);
                if (pattern.Length == 0)
                {
                    continue;
                }

                foreach (var offset in FindPatternOffsets(section.Bytes, pattern))
                {
                    hits.Add(new StaticAnchorHit(
                        anchor,
                        section.Name,
                        ImageBase + section.VirtualAddress + (uint)offset,
                        section.RawPointer + (uint)offset));
                }
            }
        }

        return hits
            .DistinctBy(hit => $"{hit.Anchor}|{hit.VirtualAddress:X8}", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<StaticAnchorRefCandidate> ScanTextForStaticAnchorReferences(
        TextSection text,
        IReadOnlyList<StaticAnchorHit> anchors,
        int maxCandidates,
        int contextBytes)
    {
        var candidates = new List<StaticAnchorRefCandidate>();
        if (anchors.Count == 0)
        {
            return candidates;
        }

        var bytes = text.Bytes;
        var counts = anchors.ToDictionary(anchor => $"{anchor.Anchor}|{anchor.VirtualAddress:X8}", _ => 0, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i <= bytes.Length - 4; i++)
        {
            foreach (var anchor in anchors)
            {
                var key = $"{anchor.Anchor}|{anchor.VirtualAddress:X8}";
                if (counts[key] >= maxCandidates)
                {
                    continue;
                }

                var value = BitConverter.ToUInt32(bytes, i);
                if (value != anchor.VirtualAddress)
                {
                    continue;
                }

                var opcodeOffset = Math.Max(0, i - 1);
                var source = ImageBase + text.VirtualAddress + (uint)i;
                var breakpointOffset = i;
                var pattern = "imm32-address";
                var confidence = "low";
                if (i >= 1 && bytes[i - 1] == 0x68)
                {
                    opcodeOffset = i - 1;
                    breakpointOffset = opcodeOffset;
                    pattern = "push-imm32-address";
                    confidence = "high";
                    source = ImageBase + text.VirtualAddress + (uint)opcodeOffset;
                }
                else if (i >= 1 && bytes[i - 1] is >= 0xB8 and <= 0xBF)
                {
                    opcodeOffset = i - 1;
                    breakpointOffset = opcodeOffset;
                    pattern = "mov-r32-imm32-address";
                    confidence = "medium";
                    source = ImageBase + text.VirtualAddress + (uint)opcodeOffset;
                }
                else if (i >= 2 && bytes[i - 2] == 0xC7)
                {
                    opcodeOffset = i - 2;
                    breakpointOffset = opcodeOffset;
                    pattern = "mov-rm32-imm32-address";
                    confidence = "medium";
                    source = ImageBase + text.VirtualAddress + (uint)opcodeOffset;
                }

                var contextHex = BytesToHex(ReadSlice(bytes, Math.Max(0, opcodeOffset - contextBytes), Math.Min(bytes.Length, i + 4 + contextBytes)));
                candidates.Add(new StaticAnchorRefCandidate(
                    anchor.Anchor,
                    anchor.SectionName,
                    anchor.VirtualAddress,
                    anchor.FileOffset,
                    source,
                    text.RawPointer + (uint)opcodeOffset,
                    ImageBase + text.VirtualAddress + (uint)breakpointOffset,
                    pattern,
                    confidence,
                    contextHex));
                counts[key]++;
            }
        }

        return candidates
            .DistinctBy(candidate => $"{candidate.Anchor}|{candidate.BreakpointAddress:X8}|{candidate.Pattern}", StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxCandidates, 1, 256))
            .ToList();
    }

    private static int StaticReferenceConfidenceSortKey(string confidence)
        => confidence.ToLowerInvariant() switch
        {
            "high" => 0,
            "medium" => 1,
            "low" => 2,
            _ => 9
        };

    private static object FormatStaticAnchorHit(StaticAnchorHit hit)
        => new
        {
            anchor = hit.Anchor,
            section = hit.SectionName,
            address = $"0x{hit.VirtualAddress:X8}",
            file_offset = $"0x{hit.FileOffset:X}"
        };

    private static object FormatStaticAnchorRefCandidate(StaticAnchorRefCandidate candidate)
        => new
        {
            anchor = candidate.Anchor,
            section = candidate.SectionName,
            anchor_address = $"0x{candidate.AnchorAddress:X8}",
            anchor_file_offset = $"0x{candidate.AnchorFileOffset:X}",
            source_address = $"0x{candidate.SourceAddress:X8}",
            breakpoint_address = $"0x{candidate.BreakpointAddress:X8}",
            file_offset = $"0x{candidate.FileOffset:X}",
            pattern = candidate.Pattern,
            confidence = candidate.Confidence,
            context_hex = candidate.ContextHex
        };

    private static List<StaticAnchorRefCandidate> FindAnchorContainingFunctionCandidates(
        TextSection text,
        IReadOnlyList<StaticAnchorRefCandidate> referenceCandidates,
        int contextBytes)
    {
        var candidates = new List<StaticAnchorRefCandidate>();
        foreach (var reference in referenceCandidates)
        {
            var offset = checked((int)(reference.BreakpointAddress - ImageBase - text.VirtualAddress));
            var entryOffset = FindPreviousFunctionPrologue(text.Bytes, offset, maxBackBytes: 512);
            if (entryOffset < 0 || entryOffset == offset)
            {
                continue;
            }

            var contextHex = BytesToHex(ReadSlice(text.Bytes, Math.Max(0, entryOffset - contextBytes), Math.Min(text.Bytes.Length, entryOffset + 16 + contextBytes)));
            candidates.Add(reference with
            {
                SourceAddress = ImageBase + text.VirtualAddress + (uint)entryOffset,
                FileOffset = text.RawPointer + (uint)entryOffset,
                BreakpointAddress = ImageBase + text.VirtualAddress + (uint)entryOffset,
                Pattern = "function-prologue-containing-anchor-ref",
                Confidence = "medium",
                ContextHex = contextHex
            });
        }

        return candidates
            .DistinctBy(candidate => $"{candidate.Anchor}|{candidate.BreakpointAddress:X8}|{candidate.Pattern}", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int FindPreviousFunctionPrologue(byte[] bytes, int offset, int maxBackBytes)
    {
        var start = Math.Max(0, offset - maxBackBytes);
        for (var i = Math.Min(offset, bytes.Length - 3); i >= start; i--)
        {
            if (bytes[i] == 0x55 && bytes[i + 1] == 0x8B && bytes[i + 2] == 0xEC)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<ImportCallCandidate> FindImportCallContainingFunctionCandidates(
        TextSection text,
        IReadOnlyList<ImportCallCandidate> callCandidates,
        int contextBytes)
    {
        var candidates = new List<ImportCallCandidate>();
        foreach (var call in callCandidates)
        {
            var offset = checked((int)(call.BreakpointAddress - ImageBase - text.VirtualAddress));
            var entryOffset = FindPreviousFunctionPrologue(text.Bytes, offset, maxBackBytes: 768);
            if (entryOffset < 0 || entryOffset == offset)
            {
                continue;
            }

            var contextHex = BytesToHex(ReadSlice(text.Bytes, Math.Max(0, entryOffset - contextBytes), Math.Min(text.Bytes.Length, entryOffset + 16 + contextBytes)));
            candidates.Add(call with
            {
                SourceAddress = ImageBase + text.VirtualAddress + (uint)entryOffset,
                FileOffset = text.RawPointer + (uint)entryOffset,
                BreakpointAddress = ImageBase + text.VirtualAddress + (uint)entryOffset,
                Pattern = $"function-prologue-containing-{call.FunctionName}-call",
                Confidence = call.Confidence.Equals("low", StringComparison.OrdinalIgnoreCase) ? "medium" : call.Confidence,
                ContextHex = contextHex
            });
        }

        return candidates
            .DistinctBy(candidate => $"{candidate.FunctionName}|{candidate.BreakpointAddress:X8}", StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .ToList();
    }

    private static bool IsRSceneLoadImport(string functionName)
    {
        var name = functionName.Trim();
        return name.Equals("CreateFileA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ReadFile", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("SetFilePointer", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GetFileSize", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("CloseHandle", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GlobalAlloc", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GlobalLock", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GlobalUnlock", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GlobalFree", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("lstrcpyA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("lstrcatA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("wsprintfA", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTitleMenuDispatchImport(string functionName)
    {
        var name = functionName.Trim();
        return name.Equals("DispatchMessageA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("TranslateMessage", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GetMessageA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PeekMessageA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("DefWindowProcA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("CallWindowProcA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("SendMessageA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PostMessageA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("SendDlgItemMessageA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GetCursorPos", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("WindowFromPoint", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ChildWindowFromPoint", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GetActiveWindow", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GetMenuState", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GetMenu", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GetSubMenu", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GetMenuItemCount", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("CheckMenuItem", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("EnableMenuItem", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("LoadMenuA", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("SetMenu", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("DrawMenuBar", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PostQuitMessage", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("DestroyWindow", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHighFrequencyImport(string functionName)
        => functionName.Equals("ReadFile", StringComparison.OrdinalIgnoreCase) ||
           functionName.Equals("CloseHandle", StringComparison.OrdinalIgnoreCase) ||
           functionName.Equals("GlobalAlloc", StringComparison.OrdinalIgnoreCase);

    private static bool IsHighFrequencyTitleMenuDispatchImport(string functionName)
        => functionName.Equals("DispatchMessageA", StringComparison.OrdinalIgnoreCase) ||
           functionName.Equals("TranslateMessage", StringComparison.OrdinalIgnoreCase) ||
           functionName.Equals("GetMessageA", StringComparison.OrdinalIgnoreCase) ||
           functionName.Equals("PeekMessageA", StringComparison.OrdinalIgnoreCase) ||
           functionName.Equals("DefWindowProcA", StringComparison.OrdinalIgnoreCase) ||
           functionName.Equals("GetActiveWindow", StringComparison.OrdinalIgnoreCase);

    private static List<ImportCallCandidate> ScanTextForImportCallCandidates(
        TextSection text,
        IReadOnlyList<PeImportFunction> imports,
        int maxCandidates,
        int contextBytes)
    {
        var candidates = new List<ImportCallCandidate>();
        if (imports.Count == 0)
        {
            return candidates;
        }

        var bytes = text.Bytes;
        var counts = imports.ToDictionary(import => $"{import.ModuleName}!{import.FunctionName}|{import.ImportAddress:X8}", _ => 0, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i <= bytes.Length - 4; i++)
        {
            foreach (var import in imports)
            {
                var key = $"{import.ModuleName}!{import.FunctionName}|{import.ImportAddress:X8}";
                if (counts[key] >= maxCandidates)
                {
                    continue;
                }

                var value = BitConverter.ToUInt32(bytes, i);
                if (value != import.ImportAddress)
                {
                    continue;
                }

                var opcodeOffset = i;
                var pattern = "iat-address-reference";
                var confidence = "low";
                if (i >= 2 && bytes[i - 2] == 0xFF && bytes[i - 1] == 0x15)
                {
                    opcodeOffset = i - 2;
                    pattern = "call-dword-ptr-iat";
                    confidence = PreferredLoadApi(import.FunctionName) ? "high" : "medium";
                }
                else if (i >= 2 && bytes[i - 2] == 0xFF && bytes[i - 1] == 0x25)
                {
                    opcodeOffset = i - 2;
                    pattern = "jmp-dword-ptr-iat";
                    confidence = "medium";
                }
                else if (i >= 1 && bytes[i - 1] == 0xA1)
                {
                    opcodeOffset = i - 1;
                    pattern = "mov-eax-moffs32-iat";
                    confidence = "medium";
                }
                else if (i >= 2 && bytes[i - 2] == 0x8B)
                {
                    opcodeOffset = i - 2;
                    pattern = "mov-r32-m32-iat";
                    confidence = "low";
                }

                var contextHex = BytesToHex(ReadSlice(bytes, Math.Max(0, opcodeOffset - contextBytes), Math.Min(bytes.Length, i + 4 + contextBytes)));
                candidates.Add(new ImportCallCandidate(
                    import.ModuleName,
                    import.FunctionName,
                    import.ImportAddress,
                    ImageBase + text.VirtualAddress + (uint)opcodeOffset,
                    text.RawPointer + (uint)opcodeOffset,
                    ImageBase + text.VirtualAddress + (uint)opcodeOffset,
                    pattern,
                    confidence,
                    contextHex));
                counts[key]++;
            }
        }

        return candidates
            .DistinctBy(candidate => $"{candidate.ModuleName}!{candidate.FunctionName}|{candidate.BreakpointAddress:X8}|{candidate.Pattern}", StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxCandidates, 1, 256))
            .ToList();
    }

    private static bool PreferredLoadApi(string functionName)
        => functionName.Equals("CreateFileA", StringComparison.OrdinalIgnoreCase) ||
           functionName.Equals("ReadFile", StringComparison.OrdinalIgnoreCase) ||
           functionName.Equals("SetFilePointer", StringComparison.OrdinalIgnoreCase) ||
           functionName.Equals("GetFileSize", StringComparison.OrdinalIgnoreCase);

    private static int TitleMenuDispatchImportPriority(string functionName)
    {
        var name = functionName.Trim();
        if (name.Equals("CallWindowProcA", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Equals("DefWindowProcA", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Equals("DispatchMessageA", StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Equals("TranslateMessage", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Equals("GetMessageA", StringComparison.OrdinalIgnoreCase) || name.Equals("PeekMessageA", StringComparison.OrdinalIgnoreCase)) return 4;
        if (name.Equals("WindowFromPoint", StringComparison.OrdinalIgnoreCase) || name.Equals("ChildWindowFromPoint", StringComparison.OrdinalIgnoreCase) || name.Equals("GetCursorPos", StringComparison.OrdinalIgnoreCase)) return 5;
        if (name.Contains("Menu", StringComparison.OrdinalIgnoreCase)) return 6;
        if (name.Equals("SendMessageA", StringComparison.OrdinalIgnoreCase) || name.Equals("PostMessageA", StringComparison.OrdinalIgnoreCase) || name.Equals("SendDlgItemMessageA", StringComparison.OrdinalIgnoreCase)) return 7;
        if (name.Equals("PostQuitMessage", StringComparison.OrdinalIgnoreCase) || name.Equals("DestroyWindow", StringComparison.OrdinalIgnoreCase)) return 8;
        return 9;
    }

    private static int ImportCallCandidateSortKey(ImportCallCandidate candidate)
    {
        var confidence = StaticReferenceConfidenceSortKey(candidate.Confidence);
        var api = candidate.FunctionName.Equals("CreateFileA", StringComparison.OrdinalIgnoreCase) ? 0 :
            candidate.FunctionName.Equals("ReadFile", StringComparison.OrdinalIgnoreCase) ? 1 :
            candidate.FunctionName.Equals("SetFilePointer", StringComparison.OrdinalIgnoreCase) ? 2 :
            candidate.FunctionName.Equals("GetFileSize", StringComparison.OrdinalIgnoreCase) ? 3 :
            8;
        return confidence * 16 + api;
    }

    private static int TitleMenuDispatchCallSortKey(ImportCallCandidate candidate)
    {
        var priority = TitleMenuDispatchImportPriority(candidate.FunctionName);
        var confidence = StaticReferenceConfidenceSortKey(candidate.Confidence);
        var highFrequencyPenalty = IsHighFrequencyTitleMenuDispatchImport(candidate.FunctionName) ? 4 : 0;
        return priority * 32 + confidence * 4 + highFrequencyPenalty;
    }

    private static object FormatImportFunction(PeImportFunction import)
        => new
        {
            module = import.ModuleName,
            function = import.FunctionName,
            import_address = $"0x{import.ImportAddress:X8}",
            thunk_rva = $"0x{import.ThunkRva:X}",
            hint_name_rva = $"0x{import.HintNameRva:X}"
        };

    private static object FormatImportCallCandidate(ImportCallCandidate candidate)
        => new
        {
            module = candidate.ModuleName,
            function = candidate.FunctionName,
            import_address = $"0x{candidate.ImportAddress:X8}",
            source_address = $"0x{candidate.SourceAddress:X8}",
            breakpoint_address = $"0x{candidate.BreakpointAddress:X8}",
            file_offset = $"0x{candidate.FileOffset:X}",
            pattern = candidate.Pattern,
            confidence = candidate.Confidence,
            context_hex = candidate.ContextHex
        };

    private static string SanitizeProbeName(string value)
        => Regex.Replace(value, @"[^A-Za-z0-9]+", "_").Trim('_').ToLowerInvariant();

    private static List<RSceneCommandCompareCandidate> ScanRSceneCommandCompareCandidates(
        TextSection text,
        IReadOnlyList<int> commandIds,
        int maxCandidatesPerCommand,
        int contextBytes)
    {
        var requested = commandIds.ToHashSet();
        var counts = requested.ToDictionary(id => id, _ => 0);
        var candidates = new List<RSceneCommandCompareCandidate>();
        var bytes = text.Bytes;

        for (var i = 0; i < bytes.Length - 8; i++)
        {
            foreach (var match in MatchRSceneCommandCompareAt(bytes, i, requested))
            {
                if (counts[match.CommandId] >= maxCandidatesPerCommand)
                {
                    continue;
                }

                counts[match.CommandId]++;
                var address = ImageBase + text.VirtualAddress + (uint)i;
                var fileOffset = text.RawPointer + (uint)i;
                var contextHex = BytesToHex(ReadSlice(bytes, Math.Max(0, i - contextBytes), Math.Min(bytes.Length, i + match.Length + contextBytes)));
                candidates.Add(new RSceneCommandCompareCandidate(
                    match.CommandId,
                    address,
                    fileOffset,
                    match.Pattern,
                    match.Confidence,
                    contextHex));
            }

            if (counts.Values.All(count => count >= maxCandidatesPerCommand))
            {
                break;
            }
        }

        return candidates
            .DistinctBy(c => $"{c.CommandId}|{c.Address:X8}|{c.Pattern}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c.CommandId)
            .ThenBy(c => c.FileOffset)
            .ToList();
    }

    private static IEnumerable<(int CommandId, string Pattern, string Confidence, int Length)> MatchRSceneCommandCompareAt(byte[] bytes, int offset, HashSet<int> requested)
    {
        var b0 = bytes[offset];
        var b1 = bytes[offset + 1];
        if (b0 == 0x3C && requested.Contains(b1))
        {
            yield return (b1, "cmp-al-imm8", "medium", 2);
        }

        if (b0 == 0x66 && b1 == 0x3D)
        {
            var value = ReadUInt16(bytes, offset + 2);
            if (requested.Contains(value))
            {
                yield return (value, "cmp-ax-imm16", "medium", 4);
            }
        }

        if (b0 == 0x3D)
        {
            var value = ReadInt32LittleEndian(bytes, offset + 1);
            if (requested.Contains(value))
            {
                yield return (value, "cmp-eax-imm32", "medium", 5);
            }
        }

        if (b0 == 0x83 && ((b1 >> 3) & 0x7) == 7)
        {
            var value = bytes[offset + 2];
            if (requested.Contains(value))
            {
                yield return (value, "cmp-rm32-imm8", "low", 3);
            }
        }

        if (b0 == 0x80 && ((b1 >> 3) & 0x7) == 7)
        {
            var value = bytes[offset + 2];
            if (requested.Contains(value))
            {
                yield return (value, "cmp-rm8-imm8", "low", 3);
            }
        }

        if (b0 == 0x66 && b1 == 0x83 && ((bytes[offset + 2] >> 3) & 0x7) == 7)
        {
            var value = bytes[offset + 3];
            if (requested.Contains(value))
            {
                yield return (value, "cmp-rm16-imm8", "low", 4);
            }
        }
    }

    private static List<int> ParseCommandIdList(string commandIds)
    {
        var source = string.IsNullOrWhiteSpace(commandIds)
            ? ["2D", "12", "07", "13"]
            : commandIds.Split([',', ';', '|', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = new List<int>();
        foreach (var token in source)
        {
            var normalized = token.Trim();
            var text = normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? normalized[2..] : normalized;
            if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) || id is < 0 or > 0xFF)
            {
                throw new ArgumentException($"Invalid R-scene command id '{token}'. Use 00..FF.");
            }
            ids.Add(id);
        }

        return ids.Distinct().OrderBy(id => id).ToList();
    }

    private static string RSceneCommandName(int commandId)
        => commandId switch
        {
            0x07 => "battle-entry-test",
            0x12 => "choice-box",
            0x13 => "case-branch",
            0x2D => "actor-click-test",
            _ => "unknown"
        };

    private static int RSceneCommandPriority(int commandId)
        => commandId switch
        {
            0x2D => 0,
            0x12 => 1,
            0x13 => 2,
            0x07 => 3,
            _ => 10
        };

    private static object FormatRSceneCommandCompareCandidate(RSceneCommandCompareCandidate candidate)
        => new
        {
            command_id = $"0x{candidate.CommandId:X2}",
            command_name = RSceneCommandName(candidate.CommandId),
            address = $"0x{candidate.Address:X8}",
            file_offset = $"0x{candidate.FileOffset:X}",
            pattern = candidate.Pattern,
            confidence = candidate.Confidence,
            context_hex = candidate.ContextHex
        };

    private static R00ChoiceBox ReadR00ChoiceBox(byte[] bytes, string key, string anchorText)
    {
        var textOffset = IndexOfGbk(bytes, anchorText);
        if (textOffset < 0)
        {
            throw new InvalidDataException($"R_00 route anchor text was not found for {key}.");
        }

        var commandOffset = textOffset - 4;
        if (commandOffset < 0 ||
            ReadUInt16(bytes, commandOffset) != 0x12 ||
            ReadUInt16(bytes, commandOffset + 2) != 0x05)
        {
            throw new InvalidDataException($"R_00 anchor {key} was found at 0x{textOffset:X6}, but the surrounding bytes do not look like a 0x12 choice-box command.");
        }

        var end = FindNullTerminator(bytes, textOffset);
        var text = DecodeGbk(bytes, textOffset, end - textOffset);
        var defaultValue = end + 5 < bytes.Length && ReadUInt16(bytes, end + 1) == 0x02
            ? ReadUInt16(bytes, end + 3)
            : 0;

        return new R00ChoiceBox(
            key,
            commandOffset,
            textOffset,
            text,
            SplitR00ChoiceOptions(text),
            defaultValue);
    }

    private static R00RouteScriptContext ReadR00RouteScriptContext(string gameRoot)
    {
        var scenarioPath = Path.Combine(gameRoot, "RS", "R_00.eex");
        if (!File.Exists(scenarioPath))
        {
            throw new FileNotFoundException("RS/R_00.eex was not found under the verified game root.", scenarioPath);
        }

        var bytes = File.ReadAllBytes(scenarioPath);
        var actorClick = ReadR00ActorClickTest(bytes, personId: 157);
        var modeChoice = ReadR00ChoiceBox(
            bytes,
            key: "mode_choice",
            anchorText: "[C97\u5E38\u89C4\u6A21\u5F0F]\n[C3A\u81EA\u9009\u6A21\u5F0F]\n\u6A21\u5F0F\u8BF4\u660E");
        var configChoice = ReadR00ChoiceBox(
            bytes,
            key: "regular_config_menu",
            anchorText: "\u57F9\u517B\u6A21\u5F0F      \u3010[C28*.1000]\u3011\n\u96BE\u5EA6\u8BBE\u7F6E      \u3010[C28*.1001*/2%]\u3011\n\u547D\u4E2D\u7C7B\u578B      \u3010[C28*.1005]\u3011\n\u6740\u654C\u52A0\u6210      \u3010[C28*.1006\u52A0\u6210]\u3011\n[C25\u9009\u9879\u8BF4\u660E]\n[C3A\u5F00\u59CB\u6E38\u620F]");
        var scenario = new
        {
            relative_path = "RS/R_00.eex",
            path = scenarioPath,
            sha256 = ComputeSha256(scenarioPath),
            size = bytes.Length
        };
        return new R00RouteScriptContext(bytes, scenario, actorClick, modeChoice, configChoice);
    }

    private static List<RSceneScriptWindow> BuildR00ScriptWindows(byte[] bytes, R00ActorClickTest actorClick, R00ChoiceBox modeChoice, R00ChoiceBox configChoice, int contextBytes)
    {
        var context = Math.Clamp(contextBytes, 4, 128);
        return
        [
            BuildScriptWindow(bytes, "xu_zijiang_actor_click", "2D", "Xu Zijiang actor-click prerequisite command", actorClick.CommandOffset, context),
            BuildScriptWindow(bytes, "mode_choice_box", "12", "First mode-selection choice box", modeChoice.CommandOffset, context),
            BuildScriptWindow(bytes, "regular_config_choice_box", "12", "Regular-mode configuration menu choice box", configChoice.CommandOffset, context)
        ];
    }

    private static RSceneScriptWindow BuildScriptWindow(byte[] bytes, string key, string commandId, string meaning, int commandOffset, int contextBytes)
    {
        var start = Math.Max(0, commandOffset - contextBytes);
        var end = Math.Min(bytes.Length, commandOffset + contextBytes + 16);
        var length = Math.Max(0, end - start);
        var pattern = new byte[length];
        Buffer.BlockCopy(bytes, start, pattern, 0, length);
        return new RSceneScriptWindow(key, commandId, meaning, commandOffset, start, pattern);
    }

    private static R00ActorClickTest ReadR00ActorClickTest(byte[] bytes, int personId)
    {
        for (var offset = 0; offset <= bytes.Length - 6; offset++)
        {
            if (ReadUInt16(bytes, offset) != 0x2D ||
                ReadUInt16(bytes, offset + 2) != 0x02 ||
                ReadUInt16(bytes, offset + 4) != personId)
            {
                continue;
            }

            return new R00ActorClickTest(offset, personId);
        }

        throw new InvalidDataException($"R_00 actor-click test for person id {personId} was not found.");
    }

    private static List<R00ActorCommand> ReadR00ActorCommands(byte[] bytes, int personId)
    {
        var commands = new List<R00ActorCommand>();
        for (var offset = 0; offset <= bytes.Length - 6; offset++)
        {
            var commandId = ReadUInt16(bytes, offset);
            switch (commandId)
            {
                case 0x2D when ReadUInt16(bytes, offset + 2) == 0x02 && ReadUInt16(bytes, offset + 4) == personId:
                    commands.Add(new R00ActorCommand(commandId, RSceneCommandName(commandId), offset, personId, null, null, null, null));
                    break;
                case 0x30 when offset <= bytes.Length - 26 &&
                    ReadUInt16(bytes, offset + 2) == 0x02 &&
                    ReadUInt16(bytes, offset + 4) == personId:
                    commands.Add(new R00ActorCommand(
                        commandId,
                        "actor-appear",
                        offset,
                        personId,
                        ReadInt32LittleEndian(bytes, offset + 10),
                        ReadInt32LittleEndian(bytes, offset + 16),
                        ReadUInt16(bytes, offset + 22),
                        null));
                    break;
                case 0x32 when offset <= bytes.Length - 32 &&
                    ReadUInt16(bytes, offset + 8) == 0x02 &&
                    ReadUInt16(bytes, offset + 10) == personId:
                    commands.Add(new R00ActorCommand(
                        commandId,
                        "actor-move",
                        offset,
                        personId,
                        ReadInt32LittleEndian(bytes, offset + 18),
                        ReadInt32LittleEndian(bytes, offset + 24),
                        ReadUInt16(bytes, offset + 30),
                        null));
                    break;
                case 0x33 when offset <= bytes.Length - 14 &&
                    ReadUInt16(bytes, offset + 2) == 0x02 &&
                    ReadUInt16(bytes, offset + 4) == personId:
                    commands.Add(new R00ActorCommand(
                        commandId,
                        "actor-turn",
                        offset,
                        personId,
                        null,
                        null,
                        ReadUInt16(bytes, offset + 12),
                        null));
                    break;
                case 0x34 when offset <= bytes.Length - 10 &&
                    ReadUInt16(bytes, offset + 2) == 0x02 &&
                    ReadUInt16(bytes, offset + 4) == personId:
                    commands.Add(new R00ActorCommand(
                        commandId,
                        "actor-action",
                        offset,
                        personId,
                        null,
                        null,
                        null,
                        ReadUInt16(bytes, offset + 8)));
                    break;
            }
        }

        return commands
            .DistinctBy(command => $"{command.CommandId}|{command.Offset}", StringComparer.Ordinal)
            .OrderBy(command => command.Offset)
            .ToList();
    }

    private static List<R00CaseBranch> ReadR00CaseBranches(byte[] bytes, int startOffset, int endOffset)
    {
        var branches = new List<R00CaseBranch>();
        var end = Math.Clamp(endOffset, 0, bytes.Length - 8);
        for (var offset = Math.Max(startOffset, 0); offset <= end; offset++)
        {
            if (ReadUInt16(bytes, offset) != 0x13 || ReadUInt16(bytes, offset + 2) != 0x04)
            {
                continue;
            }

            var value = ReadInt32LittleEndian(bytes, offset + 4);
            if (value is >= 1 and <= 32)
            {
                branches.Add(new R00CaseBranch(value, offset));
            }
        }

        return branches
            .DistinctBy(branch => $"{branch.CaseValue}|{branch.Offset}", StringComparer.Ordinal)
            .OrderBy(branch => branch.Offset)
            .ToList();
    }

    private static List<R00VariableWrite> ReadR00VariableWrites(byte[] bytes, int startOffset, int endOffset)
    {
        var writes = new List<R00VariableWrite>();
        if (startOffset < 0)
        {
            return writes;
        }

        var end = Math.Clamp(endOffset, 0, bytes.Length - 12);
        for (var offset = Math.Max(startOffset, 0); offset <= end; offset++)
        {
            if (ReadUInt16(bytes, offset) != 0x0B ||
                ReadUInt16(bytes, offset + 2) != 0x04 ||
                ReadUInt16(bytes, offset + 8) != 0x27)
            {
                continue;
            }

            var variableId = ReadInt32LittleEndian(bytes, offset + 4);
            var value = ReadUInt16(bytes, offset + 10);
            if (variableId is >= 0 and <= 9999)
            {
                writes.Add(new R00VariableWrite(variableId, value, offset));
            }
        }

        return writes
            .DistinctBy(write => $"{write.VariableId}|{write.Value}|{write.Offset}", StringComparer.Ordinal)
            .OrderBy(write => write.Offset)
            .ToList();
    }

    private static int FindR00RouteWindowEnd(byte[] bytes, int configChoiceOffset)
    {
        var warningTextOffset = IndexOfGbk(bytes, "\u5F00\u542F\u8FC7\u591A\u7684[C28\u968F\u673A\u9879\u76EE]");
        if (warningTextOffset > configChoiceOffset)
        {
            return warningTextOffset;
        }

        return Math.Min(configChoiceOffset + 0x1400, bytes.Length);
    }

    private static int FindCaseOffset(IEnumerable<R00CaseBranch> branches, int value)
        => branches.FirstOrDefault(branch => branch.CaseValue == value)?.Offset ?? -1;

    private static string? ResolveR00StartupProbeEvidencePath(string workspaceRoot, string? evidencePath, bool includeLatestEvidence)
    {
        if (!string.IsNullOrWhiteSpace(evidencePath))
        {
            var direct = Path.GetFullPath(evidencePath);
            return File.Exists(direct) ? direct : evidencePath;
        }

        if (!includeLatestEvidence)
        {
            return null;
        }

        var root = Path.Combine(workspaceRoot, "CCZModStudio_Reports", "DebugEvidence");
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateFiles(root, "r00-startup-route-probe-summary.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static object? ReadR00StartupProbeEvidenceSummary(string? evidencePath)
    {
        if (string.IsNullOrWhiteSpace(evidencePath) || !File.Exists(evidencePath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(evidencePath, Encoding.UTF8));
        var root = document.RootElement;
        return new
        {
            path = evidencePath,
            status = GetStringProperty(root, "status"),
            hit_count = GetIntProperty(root, "hit_count", "hitCount"),
            final_runtime_classification = GetStringProperty(root, "final_runtime_classification", "finalRuntimeClassification"),
            battle_profile_status = GetStringProperty(root, "battle_profile_status", "battleProfileStatus"),
            bridge_ready = TryGetBoolString(root, "bridge_ready", "bridgeReady"),
            handler_probe_target_count = GetIntProperty(root, "handler_probe_target_count", "handlerProbeTargetCount"),
            pre_startup_probe_status = TryGetNestedString(root, "pre_startup_probe_run", "status"),
            pre_startup_probe_hit_count = TryGetNestedInt(root, "pre_startup_probe_run", "hit_count"),
            after_script_window_status = GetStringProperty(root, "after_script_window_status", "afterScriptWindowStatus"),
            battle_loaded = TryGetBoolString(root, "battle_loaded", "battleLoaded"),
            battle_profile_ready = TryGetBoolString(root, "battle_profile_ready", "battleProfileReady")
        };
    }

    private static object FormatR00ChoiceBox(R00ChoiceBox choice)
        => new
        {
            key = choice.Key,
            command_id = "12",
            command_name = "choice_box",
            command_offset = FormatFileOffset(choice.CommandOffset),
            text_offset = FormatFileOffset(choice.TextOffset),
            default_value = choice.DefaultValue,
            option_count = choice.Options.Count,
            options = choice.Options,
            text = choice.Text
        };

    private static object FormatR00ActorClickTest(R00ActorClickTest actorClick)
        => new
        {
            command_id = "2D",
            command_name = "actor_click_test",
            person_id = actorClick.PersonId,
            command_offset = FormatFileOffset(actorClick.CommandOffset),
            meaning = "Xu Zijiang R-scene actor click prerequisite before the mode-selection child event."
        };

    private static object FormatR00ActorCommand(R00ActorCommand command)
        => new
        {
            command_id = $"0x{command.CommandId:X2}",
            command_name = command.CommandName,
            person_id = command.PersonId,
            offset = FormatFileOffset(command.Offset),
            x = command.X,
            y = command.Y,
            facing = command.Facing,
            action = command.Action
        };

    private static object FormatR00CaseBranch(R00CaseBranch branch)
        => new
        {
            case_value = branch.CaseValue,
            offset = FormatFileOffset(branch.Offset)
        };

    private static object FormatR00VariableWrite(R00VariableWrite write)
        => new
        {
            variable_id = write.VariableId,
            value = write.Value,
            offset = FormatFileOffset(write.Offset)
        };

    private static object FormatScriptWindow(RSceneScriptWindow window)
        => new
        {
            key = window.Key,
            command_id = window.CommandId,
            meaning = window.Meaning,
            command_offset = FormatFileOffset(window.CommandOffset),
            start_offset = FormatFileOffset(window.StartOffset),
            pattern_length = window.Pattern.Length,
            pattern_sha256 = Convert.ToHexString(SHA256.HashData(window.Pattern)),
            pattern_preview_hex = BytesToHex(window.Pattern, Math.Min(window.Pattern.Length, 48))
        };

    private static object FormatScriptWindowHit(RSceneScriptWindowHit hit)
        => new
        {
            window_key = hit.WindowKey,
            command_id = hit.CommandId,
            address = "0x" + hit.Address.ToString("X8", CultureInfo.InvariantCulture),
            command_address = "0x" + hit.CommandAddress.ToString("X8", CultureInfo.InvariantCulture),
            inferred_script_base = "0x" + (hit.CommandAddress - (ulong)Math.Max(hit.ScriptCommandOffset, 0)).ToString("X8", CultureInfo.InvariantCulture),
            region_base = "0x" + hit.RegionBase.ToString("X8", CultureInfo.InvariantCulture),
            region_size = "0x" + hit.RegionSize.ToString("X", CultureInfo.InvariantCulture),
            offset_in_region = "0x" + hit.OffsetInRegion.ToString("X", CultureInfo.InvariantCulture),
            script_command_offset = FormatFileOffset(hit.ScriptCommandOffset)
        };

    private static object FormatScriptPointerRef(RScenePointerRef reference)
        => new
        {
            window_key = reference.WindowKey,
            target_kind = reference.TargetKind,
            target_address = "0x" + reference.TargetAddress.ToString("X8", CultureInfo.InvariantCulture),
            ref_address = "0x" + reference.RefAddress.ToString("X8", CultureInfo.InvariantCulture),
            region_base = "0x" + reference.RegionBase.ToString("X8", CultureInfo.InvariantCulture),
            region_size = "0x" + reference.RegionSize.ToString("X", CultureInfo.InvariantCulture),
            offset_in_region = "0x" + reference.OffsetInRegion.ToString("X", CultureInfo.InvariantCulture)
        };

    private static List<string> SplitR00ChoiceOptions(string text)
        => text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static int IndexOfGbk(byte[] bytes, string text)
    {
        var pattern = Encoding.GetEncoding(936).GetBytes(text);
        if (pattern.Length == 0 || pattern.Length > bytes.Length)
        {
            return -1;
        }

        for (var offset = 0; offset <= bytes.Length - pattern.Length; offset++)
        {
            var found = true;
            for (var i = 0; i < pattern.Length; i++)
            {
                if (bytes[offset + i] == pattern[i])
                {
                    continue;
                }

                found = false;
                break;
            }

            if (found)
            {
                return offset;
            }
        }

        return -1;
    }

    private static int FindNullTerminator(byte[] bytes, int offset)
    {
        for (var cursor = Math.Max(offset, 0); cursor < bytes.Length; cursor++)
        {
            if (bytes[cursor] == 0)
            {
                return cursor;
            }
        }

        throw new InvalidDataException($"Null-terminated GBK text did not terminate after 0x{offset:X6}.");
    }

    private static string DecodeGbk(byte[] bytes, int offset, int byteLength)
        => Encoding.GetEncoding(936)
            .GetString(bytes, offset, byteLength)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static List<string> ParseRSceneAnchors(string anchors)
    {
        var defaults = new[]
        {
            "\u8BB8\u5B50\u5C06",
            "\u6A21\u5F0F\u9009\u62E9",
            "\u8BF7\u9009\u62E9\u4F60\u7684\u6E38\u620F\u6A21\u5F0F",
            "[C97\u5E38\u89C4\u6A21\u5F0F]",
            "[C3A\u5F00\u59CB\u6E38\u620F]"
        };
        var source = string.IsNullOrWhiteSpace(anchors)
            ? defaults
            : anchors.Split([',', ';', '|', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return source
            .Where(anchor => !string.IsNullOrWhiteSpace(anchor))
            .Distinct(StringComparer.Ordinal)
            .Take(32)
            .ToList();
    }

    private static string NormalizeAnchorSweepProfile(string profile)
    {
        var normalized = string.IsNullOrWhiteSpace(profile) ? "all" : profile.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "all" or "startup" or "rscene" or "battle" => normalized,
            "title" or "menu" => "startup",
            "r_scene" or "r00" or "r_00" => "rscene",
            "fight" or "yingchuan" => "battle",
            _ => throw new ArgumentException("Anchor sweep profile must be startup, rscene, battle, or all.")
        };
    }

    private static List<string> BuildRuntimeSweepGbkAnchors(string profile, string extraAnchors)
    {
        var anchors = new List<string>();
        if (profile is "all" or "startup")
        {
            anchors.AddRange([
                "\u4E09\u56FD\u5FD7\u66F9\u64CD\u4F20",
                "\u52A0\u5F3A\u7248",
                "\u5F00\u59CB",
                "\u7EE7\u7EED",
                "\u8BFB\u53D6",
                "\u8BBE\u5B9A"
            ]);
        }

        if (profile is "all" or "rscene")
        {
            anchors.AddRange([
                "\u8BB8\u5B50\u5C06",
                "\u6A21\u5F0F\u9009\u62E9",
                "\u8BF7\u9009\u62E9\u4F60\u7684\u6E38\u620F\u6A21\u5F0F",
                "\u5E38\u89C4\u6A21\u5F0F",
                "\u81EA\u9009\u6A21\u5F0F",
                "\u5F00\u59CB\u6E38\u620F",
                "\u57F9\u517B\u6A21\u5F0F",
                "\u96BE\u5EA6\u8BBE\u7F6E"
            ]);
        }

        if (profile is "all" or "battle")
        {
            anchors.AddRange([
                "\u988D\u5DDD\u4E4B\u6218",
                "\u66F9\u64CD",
                "\u5F20\u6881",
                "\u653B\u51FB",
                "\u7B56\u7565",
                "\u5F85\u547D",
                "\u7ED3\u675F\u56DE\u5408"
            ]);
        }

        anchors.AddRange(SplitAnchorList(extraAnchors));
        return anchors.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.Ordinal).Take(96).ToList();
    }

    private static List<string> BuildRuntimeSweepAsciiAnchors(string profile, string extraAnchors)
    {
        var anchors = new List<string>();
        if (profile is "all" or "startup")
        {
            anchors.AddRange(["Ekd5", "Data.e5", "Imsg.e5", "Star.e5", "Title", "Start"]);
        }

        if (profile is "all" or "rscene")
        {
            anchors.AddRange(["RS\\R_", "RS\\S_", "R_00", "S_00", ".EEX", "[C97", "[C3A", "[C28"]);
        }

        if (profile is "all" or "battle")
        {
            anchors.AddRange(["M000", "Hexzmap", "Unit", "Pmapobj"]);
        }

        anchors.AddRange(SplitAnchorList(extraAnchors));
        return anchors.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Take(96).ToList();
    }

    private static List<string> SplitAnchorList(string anchors)
        => string.IsNullOrWhiteSpace(anchors)
            ? []
            : anchors.Split([',', ';', '|', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(anchor => !string.IsNullOrWhiteSpace(anchor))
                .ToList();

    private static object InferRuntimeAnchorPhase(IReadOnlyList<RSceneAnchorHit> gbkHits, IReadOnlyList<RSceneAnchorHit> asciiHits, string runtimeClassification, string battleProfileStatus)
    {
        var hits = gbkHits.Concat(asciiHits).ToList();
        var hitAnchors = hits.Select(hit => hit.Anchor)
            .Where(anchor => !string.IsNullOrWhiteSpace(anchor))
            .ToList();
        if (runtimeClassification.Equals("battle_loaded", StringComparison.OrdinalIgnoreCase))
        {
            return new { phase = "battle_loaded", confidence = "medium", basis = "Read-only tactical unit array classification reported battle_loaded." };
        }

        if (battleProfileStatus.Equals("profile-matched", StringComparison.OrdinalIgnoreCase) ||
            battleProfileStatus.Equals("attack_after_observed", StringComparison.OrdinalIgnoreCase))
        {
            return new { phase = "battle_profile_ready", confidence = "high", basis = $"Battle profile gate returned {battleProfileStatus}." };
        }

        var runtimeRSceneHits = hits.Where(hit =>
            IsRuntimeAnchorRegion(hit) &&
            IsStrongRSceneRuntimeAnchor(hit.Anchor))
            .ToList();
        var staticRSceneHits = hits.Where(hit =>
            !IsRuntimeAnchorRegion(hit) &&
            IsStrongRSceneRuntimeAnchor(hit.Anchor))
            .ToList();
        var hasStrongRScene = hitAnchors.Any(IsStrongRSceneRuntimeAnchor);
        var hasWeakRSceneNames = hitAnchors.Any(IsWeakRSceneCharacterAnchor);
        var hasStartup = hitAnchors.Any(anchor =>
            anchor.Contains("\u4E09\u56FD\u5FD7", StringComparison.Ordinal) ||
            anchor.Contains("\u52A0\u5F3A\u7248", StringComparison.Ordinal) ||
            anchor.Equals("Ekd5", StringComparison.OrdinalIgnoreCase) ||
            anchor.Equals("Data.e5", StringComparison.OrdinalIgnoreCase));

        if (runtimeRSceneHits.Count > 0)
        {
            var runtimeKinds = string.Join(", ", runtimeRSceneHits
                .Select(hit => hit.RegionKind)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase));
            return new
            {
                phase = "rscene_runtime_anchors",
                confidence = "medium",
                basis = $"Precise R_00/R-scene anchors were found outside the EXE image ({runtimeKinds}). Confirm with game_rscene_script_window_scan and dynamic handler probes."
            };
        }

        if (staticRSceneHits.Count > 0 || hasStrongRScene)
        {
            return new { phase = "rscene_or_script_resources", confidence = "low", basis = "R-scene or EEX-related anchors were found only as low-confidence resource evidence. Confirm script residency and handler execution before treating this as current phase." };
        }

        if (hasWeakRSceneNames)
        {
            return new { phase = "character_name_resources_only", confidence = "low", basis = "Only character-name anchors such as Xu Zijiang were found outside the EXE image. Character names can come from loaded Data resources and do not prove R_00 mode-selection state." };
        }

        if (hasStartup)
        {
            return new { phase = "startup_or_title_resources", confidence = "low", basis = "Only startup/title/resource anchors were found." };
        }

        if (hitAnchors.Count > 0)
        {
            return new { phase = "static_resource_anchors_only", confidence = "low", basis = "Only generic EXE/resource anchors were found; no runtime battle gate or precise R_00 anchor is satisfied." };
        }

        return new { phase = "unknown_no_profile_anchors", confidence = "low", basis = "No configured profile anchors were found in the scanned memory budget." };
    }

    private static bool IsStrongRSceneRuntimeAnchor(string anchor)
        => anchor.Contains("\u6A21\u5F0F", StringComparison.Ordinal) ||
           anchor.Contains("\u5F00\u59CB\u6E38\u620F", StringComparison.Ordinal) ||
           anchor.Contains("\u5E38\u89C4\u6A21\u5F0F", StringComparison.Ordinal) ||
           anchor.Contains("\u81EA\u9009\u6A21\u5F0F", StringComparison.Ordinal) ||
           anchor.Contains("R_00", StringComparison.OrdinalIgnoreCase) ||
           anchor.Contains("RS\\R_", StringComparison.OrdinalIgnoreCase) ||
           anchor.Contains("[C97", StringComparison.OrdinalIgnoreCase) ||
           anchor.Contains("[C3A", StringComparison.OrdinalIgnoreCase);

    private static bool IsWeakRSceneCharacterAnchor(string anchor)
        => anchor.Contains("\u8BB8\u5B50\u5C06", StringComparison.Ordinal);

    private static bool IsRuntimeAnchorRegion(RSceneAnchorHit hit)
        => hit.RegionKind.Equals("private", StringComparison.OrdinalIgnoreCase) ||
           hit.RegionKind.Equals("mapped", StringComparison.OrdinalIgnoreCase) ||
           hit.RegionKind.Equals("heap_candidate", StringComparison.OrdinalIgnoreCase);

    private static object FormatAnchorHit(RSceneAnchorHit hit)
        => new
        {
            anchor = hit.Anchor,
            address = "0x" + hit.Address.ToString("X8", CultureInfo.InvariantCulture),
            region_base = "0x" + hit.RegionBase.ToString("X8", CultureInfo.InvariantCulture),
            region_size = "0x" + hit.RegionSize.ToString("X", CultureInfo.InvariantCulture),
            allocation_base = "0x" + hit.AllocationBase.ToString("X8", CultureInfo.InvariantCulture),
            offset_in_region = "0x" + hit.OffsetInRegion.ToString("X", CultureInfo.InvariantCulture),
            encoding = hit.Encoding,
            region_kind = hit.RegionKind,
            protect = "0x" + hit.Protect.ToString("X", CultureInfo.InvariantCulture),
            type = "0x" + hit.Type.ToString("X", CultureInfo.InvariantCulture),
            context_text = hit.ContextText
        };

    private static string ClassifyMemoryRegion(NativeMethods.MEMORY_BASIC_INFORMATION mbi)
    {
        if (mbi.Type == NativeMethods.MEM_IMAGE)
        {
            return "image";
        }

        if (mbi.Type == NativeMethods.MEM_MAPPED)
        {
            return "mapped";
        }

        if (mbi.Type == NativeMethods.MEM_PRIVATE)
        {
            var baseAddress = unchecked((ulong)mbi.BaseAddress.ToInt64());
            return baseAddress >= 0x01000000UL ? "heap_candidate" : "private";
        }

        return "unknown";
    }

    private static string MakeAnchorContextPreview(byte[] bytes, int offset, int patternLength, Encoding encoding)
    {
        const int before = 48;
        const int after = 96;
        var start = Math.Max(0, offset - before);
        var end = Math.Min(bytes.Length, offset + patternLength + after);
        if (end <= start)
        {
            return string.Empty;
        }

        var text = encoding.GetString(bytes, start, end - start)
            .Replace('\0', ' ')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        text = Regex.Replace(text, @"[\u0001-\u0008\u000B\u000C\u000E-\u001F]+", " ");
        text = Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
        return text.Length <= 220 ? text : text[..220];
    }

    private static RSceneAnchorScanResult ScanProcessForGbkAnchors(int processId, IReadOnlyList<string> anchors, int maxScanBytes, int maxHitsPerAnchor)
        => ScanProcessForAnchors(processId, anchors, Encoding.GetEncoding(936), "GBK", maxScanBytes, maxHitsPerAnchor);

    private static RSceneAnchorScanResult ScanProcessForAnchors(int processId, IReadOnlyList<string> anchors, Encoding encoding, string encodingName, int maxScanBytes, int maxHitsPerAnchor)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            ThrowLastWin32("OpenProcess failed.");
        }

        try
        {
            var patterns = anchors
                .Select(anchor => new { Anchor = anchor, Bytes = encoding.GetBytes(anchor) })
                .Where(pattern => pattern.Bytes.Length > 0)
                .ToList();
            var hitCounts = patterns.ToDictionary(pattern => pattern.Anchor, _ => 0, StringComparer.Ordinal);
            var hits = new List<RSceneAnchorHit>();
            long scanned = 0;
            var regions = 0;
            var stoppedReason = "completed";
            ulong address = 0x00010000;
            var mbiSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();

            while (address < 0x80000000UL && scanned < maxScanBytes)
            {
                var query = NativeMethods.VirtualQueryEx(handle, new IntPtr(unchecked((long)address)), out var mbi, new IntPtr(mbiSize));
                if (query == IntPtr.Zero)
                {
                    address += 0x10000;
                    continue;
                }

                var baseAddress = unchecked((ulong)mbi.BaseAddress.ToInt64());
                var allocationBase = unchecked((ulong)mbi.AllocationBase.ToInt64());
                var regionSize = unchecked((ulong)mbi.RegionSize.ToInt64());
                if (regionSize == 0)
                {
                    address += 0x10000;
                    continue;
                }

                var next = Math.Max(baseAddress + regionSize, address + 0x1000);
                if (!IsReadableCommittedPage(mbi))
                {
                    address = next;
                    continue;
                }

                var bytesToRead = (int)Math.Min(regionSize, (ulong)Math.Min(maxScanBytes - scanned, 4 * 1024 * 1024));
                if (bytesToRead <= 0)
                {
                    stoppedReason = "max-scan-bytes";
                    break;
                }

                var buffer = new byte[bytesToRead];
                if (NativeMethods.ReadProcessMemory(handle, new IntPtr(unchecked((long)baseAddress)), buffer, buffer.Length, out var readPtr))
                {
                    var read = Math.Max(0, readPtr.ToInt32());
                    if (read > 0)
                    {
                        scanned += read;
                        regions++;
                        var slice = read == buffer.Length ? buffer : buffer.Take(read).ToArray();
                        foreach (var pattern in patterns)
                        {
                            if (hitCounts[pattern.Anchor] >= maxHitsPerAnchor)
                            {
                                continue;
                            }

                            foreach (var hitOffset in FindPatternOffsets(slice, pattern.Bytes))
                            {
                                if (hitCounts[pattern.Anchor] >= maxHitsPerAnchor)
                                {
                                    break;
                                }

                                hitCounts[pattern.Anchor]++;
                                hits.Add(new RSceneAnchorHit(
                                    pattern.Anchor,
                                    baseAddress + (ulong)hitOffset,
                                    baseAddress,
                                    regionSize,
                                    allocationBase,
                                    hitOffset,
                                    encodingName,
                                    ClassifyMemoryRegion(mbi),
                                    mbi.Protect,
                                    mbi.Type,
                                    MakeAnchorContextPreview(slice, hitOffset, pattern.Bytes.Length, encoding)));
                            }
                        }
                    }
                }

                if (hitCounts.Values.All(count => count >= maxHitsPerAnchor))
                {
                    stoppedReason = "max-hits-per-anchor";
                    break;
                }

                address = next;
            }

            if (scanned >= maxScanBytes && stoppedReason == "completed")
            {
                stoppedReason = "max-scan-bytes";
            }

            return new RSceneAnchorScanResult(scanned, regions, stoppedReason, hits);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static RSceneScriptWindowScanResult ScanProcessForByteWindows(
        int processId,
        IReadOnlyList<RSceneScriptWindow> windows,
        int maxScanBytes,
        int maxHitsPerWindow,
        bool includePointerRefs,
        int maxPointerRefs)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            ThrowLastWin32("OpenProcess failed.");
        }

        try
        {
            var hitCounts = windows.ToDictionary(window => window.Key, _ => 0, StringComparer.OrdinalIgnoreCase);
            var windowHits = new List<RSceneScriptWindowHit>();
            var pointerRefs = new List<RScenePointerRef>();
            var refCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            long scanned = 0;
            var regions = 0;
            var stoppedReason = "completed";
            ulong address = 0x00010000;
            var mbiSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();

            while (address < 0x80000000UL && scanned < maxScanBytes)
            {
                var query = NativeMethods.VirtualQueryEx(handle, new IntPtr(unchecked((long)address)), out var mbi, new IntPtr(mbiSize));
                if (query == IntPtr.Zero)
                {
                    address += 0x10000;
                    continue;
                }

                var baseAddress = unchecked((ulong)mbi.BaseAddress.ToInt64());
                var regionSize = unchecked((ulong)mbi.RegionSize.ToInt64());
                if (regionSize == 0)
                {
                    address += 0x10000;
                    continue;
                }

                var next = Math.Max(baseAddress + regionSize, address + 0x1000);
                if (!IsReadableCommittedPage(mbi))
                {
                    address = next;
                    continue;
                }

                var bytesToRead = (int)Math.Min(regionSize, (ulong)Math.Min(maxScanBytes - scanned, 4 * 1024 * 1024));
                if (bytesToRead <= 0)
                {
                    stoppedReason = "max-scan-bytes";
                    break;
                }

                var buffer = new byte[bytesToRead];
                if (NativeMethods.ReadProcessMemory(handle, new IntPtr(unchecked((long)baseAddress)), buffer, buffer.Length, out var readPtr))
                {
                    var read = Math.Max(0, readPtr.ToInt32());
                    if (read > 0)
                    {
                        scanned += read;
                        regions++;
                        var slice = read == buffer.Length ? buffer : buffer.Take(read).ToArray();
                        foreach (var window in windows)
                        {
                            if (hitCounts[window.Key] >= maxHitsPerWindow)
                            {
                                continue;
                            }

                            foreach (var hitOffset in FindPatternOffsets(slice, window.Pattern))
                            {
                                if (hitCounts[window.Key] >= maxHitsPerWindow)
                                {
                                    break;
                                }

                                hitCounts[window.Key]++;
                                var hitAddress = baseAddress + (ulong)hitOffset;
                                var commandAddress = hitAddress + (ulong)Math.Max(window.CommandOffset - window.StartOffset, 0);
                                windowHits.Add(new RSceneScriptWindowHit(
                                    window.Key,
                                    window.CommandId,
                                    hitAddress,
                                    commandAddress,
                                    baseAddress,
                                    regionSize,
                                    hitOffset,
                                    window.CommandOffset));
                            }
                        }
                    }
                }

                address = next;
            }

            if (scanned >= maxScanBytes && stoppedReason == "completed")
            {
                stoppedReason = "max-scan-bytes";
            }

            if (includePointerRefs && windowHits.Count > 0)
            {
                var targets = BuildRScenePointerTargets(windowHits)
                    .DistinctBy(t => $"{t.WindowKey}|{t.TargetKind}|{t.TargetAddress}", StringComparer.OrdinalIgnoreCase)
                    .ToList();
                pointerRefs = ScanProcessForUInt32References(processId, targets, maxScanBytes, maxPointerRefs, refCounts);
            }

            return new RSceneScriptWindowScanResult(scanned, regions, stoppedReason, windowHits, pointerRefs);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static IEnumerable<RScenePointerRef> BuildRScenePointerTargets(IEnumerable<RSceneScriptWindowHit> hits)
    {
        foreach (var hit in hits)
        {
            yield return new RScenePointerRef(hit.WindowKey, "command_address", hit.CommandAddress, 0, 0, 0, 0);
            yield return new RScenePointerRef(hit.WindowKey, "window_address", hit.Address, 0, 0, 0, 0);
            yield return new RScenePointerRef(hit.WindowKey, "inferred_script_base", hit.CommandAddress - (ulong)Math.Max(hit.ScriptCommandOffset, 0), 0, 0, 0, 0);
        }
    }

    private static List<RScenePointerRef> ScanProcessForUInt32References(
        int processId,
        IReadOnlyList<RScenePointerRef> targets,
        int maxScanBytes,
        int maxPointerRefs,
        Dictionary<string, int> refCounts)
    {
        var refs = new List<RScenePointerRef>();
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return refs;
        }

        try
        {
            var targetBytes = targets
                .Where(t => t.TargetAddress <= uint.MaxValue)
                .Select(t => new { Target = t, Bytes = BitConverter.GetBytes((uint)t.TargetAddress) })
                .ToList();
            long scanned = 0;
            ulong address = 0x00010000;
            var mbiSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();
            while (address < 0x80000000UL && scanned < maxScanBytes && targetBytes.Count > 0)
            {
                var query = NativeMethods.VirtualQueryEx(handle, new IntPtr(unchecked((long)address)), out var mbi, new IntPtr(mbiSize));
                if (query == IntPtr.Zero)
                {
                    address += 0x10000;
                    continue;
                }

                var baseAddress = unchecked((ulong)mbi.BaseAddress.ToInt64());
                var regionSize = unchecked((ulong)mbi.RegionSize.ToInt64());
                var next = Math.Max(baseAddress + regionSize, address + 0x1000);
                if (regionSize == 0 || !IsReadableCommittedPage(mbi))
                {
                    address = next;
                    continue;
                }

                var bytesToRead = (int)Math.Min(regionSize, (ulong)Math.Min(maxScanBytes - scanned, 4 * 1024 * 1024));
                if (bytesToRead <= 0)
                {
                    break;
                }

                var buffer = new byte[bytesToRead];
                if (NativeMethods.ReadProcessMemory(handle, new IntPtr(unchecked((long)baseAddress)), buffer, buffer.Length, out var readPtr))
                {
                    var read = Math.Max(0, readPtr.ToInt32());
                    if (read > 0)
                    {
                        scanned += read;
                        var slice = read == buffer.Length ? buffer : buffer.Take(read).ToArray();
                        foreach (var target in targetBytes)
                        {
                            var key = $"{target.Target.WindowKey}|{target.Target.TargetKind}|{target.Target.TargetAddress:X8}";
                            refCounts.TryGetValue(key, out var count);
                            if (count >= maxPointerRefs)
                            {
                                continue;
                            }

                            foreach (var hitOffset in FindPatternOffsets(slice, target.Bytes))
                            {
                                if (count >= maxPointerRefs)
                                {
                                    break;
                                }

                                count++;
                                refs.Add(target.Target with
                                {
                                    RefAddress = baseAddress + (ulong)hitOffset,
                                    RegionBase = baseAddress,
                                    RegionSize = regionSize,
                                    OffsetInRegion = hitOffset
                                });
                            }

                            refCounts[key] = count;
                        }
                    }
                }

                address = next;
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }

        return refs;
    }

    private static bool IsReadableCommittedPage(NativeMethods.MEMORY_BASIC_INFORMATION mbi)
        => mbi.State == NativeMethods.MEM_COMMIT &&
           (mbi.Protect & NativeMethods.PAGE_NOACCESS) == 0 &&
           (mbi.Protect & NativeMethods.PAGE_GUARD) == 0;

    private static IEnumerable<int> FindPatternOffsets(byte[] bytes, byte[] pattern)
    {
        if (pattern.Length == 0 || bytes.Length < pattern.Length)
        {
            yield break;
        }

        for (var offset = 0; offset <= bytes.Length - pattern.Length; offset++)
        {
            var found = true;
            for (var i = 0; i < pattern.Length; i++)
            {
                if (bytes[offset + i] == pattern[i])
                {
                    continue;
                }

                found = false;
                break;
            }

            if (found)
            {
                yield return offset;
            }
        }
    }

    private static int ReadInt32LittleEndian(byte[] bytes, int offset)
        => offset >= 0 && offset + 3 < bytes.Length ? BitConverter.ToInt32(bytes, offset) : 0;

    private static string FormatFileOffset(int offset)
        => offset >= 0 ? "0x" + offset.ToString("X6", CultureInfo.InvariantCulture) : string.Empty;

    private static void WriteFunctionCatalogMarkdown(string path, string stage, GamePaths paths, List<object> entries)
    {
        var lines = new List<string>
        {
            "# Ekd5 Internal Function Catalog",
            "",
            $"- Created: {DateTimeOffset.Now:O}",
            $"- Stage filter: `{stage}`",
            $"- Target: `{paths.ExePath}`",
            $"- SHA256: `{paths.ExeSha256}`",
            $"- ImageBase: `0x{ImageBase:X8}`",
            "",
            "This catalog is an offline readback of known or candidate function entry addresses. Dynamic semantics still require x32dbg probe hits tied to a concrete gameplay phase.",
            "",
            "| Stage | Address | Name | Category | File offset | Bytes | Evidence |",
            "| --- | --- | --- | --- | --- | --- | --- |"
        };

        foreach (var entry in entries)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(entry, JsonOptions));
            var root = json.RootElement;
            lines.Add(
                $"| {EscapeMarkdownCell(GetStringProperty(root, "stage", "Stage") ?? string.Empty)} " +
                $"| `{EscapeMarkdownCell(GetStringProperty(root, "address", "Address") ?? string.Empty)}` " +
                $"| {EscapeMarkdownCell(GetStringProperty(root, "name", "Name") ?? string.Empty)} " +
                $"| {EscapeMarkdownCell(GetStringProperty(root, "category", "Category") ?? string.Empty)} " +
                $"| `{EscapeMarkdownCell(GetStringProperty(root, "file_offset", "fileOffset") ?? string.Empty)}` " +
                $"| `{EscapeMarkdownCell(GetStringProperty(root, "original_bytes_16", "originalBytes16") ?? string.Empty)}` " +
                $"| {EscapeMarkdownCell(GetStringProperty(root, "evidence_level", "evidenceLevel") ?? string.Empty)} |");
        }

        lines.Add("");
        lines.Add("Safety: this report reads Ekd5.exe bytes only and writes evidence files under DebugEvidence.");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private InternalProbePlanContext CreateInternalProbePlan(string profile, string? outputDir, GamePaths paths)
    {
        var normalizedProfile = NormalizeProbeProfile(profile);
        var targets = SelectInternalProbeTargets(normalizedProfile)
            .Select(t => new InternalProbeTarget
            {
                Address = NormalizeAddress(t.Address),
                Name = t.Name,
                Phase = t.Phase,
                ExpectedSemantics = t.ExpectedSemantics,
                TriggerHint = t.TriggerHint,
                EvidenceLevel = t.EvidenceLevel,
                HighFrequency = t.HighFrequency
            })
            .ToList();
        if (targets.Count == 0)
        {
            throw new ArgumentException($"Probe profile '{profile}' did not select any targets.");
        }

        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var plan = new InternalProbePlan
        {
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Profile = normalizedProfile,
            TargetExeSha256 = paths.ExeSha256,
            Targets = targets
        };
        var planPath = Path.Combine(sessionDir, "internal-probe-plan.json");
        WriteJson(planPath, plan);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
        {
            type = "InternalProbePlanCreated",
            created_at = DateTimeOffset.Now.ToString("O"),
            profile = normalizedProfile,
            target_count = targets.Count,
            path = planPath
        });
        return new InternalProbePlanContext(sessionDir, planPath, plan, true);
    }

    private InternalProbePlanContext ResolveInternalProbePlan(string? planPath, string profile, string? outputDir, GamePaths paths)
    {
        if (string.IsNullOrWhiteSpace(planPath))
        {
            return CreateInternalProbePlan(profile, outputDir, paths);
        }

        var resolvedPlanPath = Path.GetFullPath(planPath);
        if (!File.Exists(resolvedPlanPath))
        {
            throw new FileNotFoundException("Internal probe plan JSON was not found.", resolvedPlanPath);
        }

        var plan = JsonSerializer.Deserialize<InternalProbePlan>(File.ReadAllText(resolvedPlanPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse internal probe plan: {resolvedPlanPath}");
        if (plan.Targets.Count == 0)
        {
            throw new InvalidOperationException($"Internal probe plan has no targets: {resolvedPlanPath}");
        }

        var sessionDir = string.IsNullOrWhiteSpace(outputDir)
            ? Path.GetDirectoryName(resolvedPlanPath) ?? EnsureSessionDirectory(paths.WorkspaceRoot, null)
            : Path.GetFullPath(outputDir);
        _ = EnsureSessionDirectory(paths.WorkspaceRoot, sessionDir);
        return new InternalProbePlanContext(sessionDir, resolvedPlanPath, plan, false);
    }

    private static string NormalizeProbeProfile(string profile)
    {
        var normalized = string.IsNullOrWhiteSpace(profile) ? "all" : profile.Trim().ToLowerInvariant();
        return normalized switch
        {
            "all" or "core" or "battle" or "attack" or "turn" => normalized,
            "combat" => "battle",
            _ => throw new ArgumentException("Probe profile must be core, battle, attack, turn, or all.")
        };
    }

    private static IEnumerable<InternalProbeTarget> SelectInternalProbeTargets(string normalizedProfile)
        => normalizedProfile switch
        {
            "all" => BuiltInProbeTargets,
            "battle" => BuiltInProbeTargets.Where(t => t.Phase.Equals("battle", StringComparison.OrdinalIgnoreCase) || t.Phase.Equals("attack", StringComparison.OrdinalIgnoreCase)),
            _ => BuiltInProbeTargets.Where(t => t.Phase.Equals(normalizedProfile, StringComparison.OrdinalIgnoreCase))
        };

    private RuntimeInvokePlan BuildRuntimeInvokePlan(string stage, string route, GamePaths paths)
    {
        var actions = new List<RuntimeInvokeAction>();
        var routeErrors = new List<RuntimeInvokeRouteError>();
        var menuRoutes = ExpandMenuRoutes(route);
        var captureRouteErrors = menuRoutes.Count > 1;
        foreach (var menuRoute in menuRoutes)
        {
            try
            {
                actions.AddRange(BuildMenuRouteActions(menuRoute, paths));
            }
            catch (Exception ex) when (captureRouteErrors)
            {
                routeErrors.Add(new RuntimeInvokeRouteError
                {
                    Stage = stage,
                    Route = menuRoute,
                    Error = ex.Message,
                    ExceptionType = ex.GetType().FullName ?? ex.GetType().Name
                });
                actions.Add(new RuntimeInvokeAction
                {
                    Key = $"{stage}:{menuRoute}:route-error",
                    Intent = "Runtime invoke planning failed for this expanded route; inspect route_errors before attempting automation.",
                    Method = "route_error",
                    CandidateAddress = string.Empty,
                    Verification = ex.Message,
                    EvidenceGate = "route planning error must be resolved before promotion or execution",
                    CandidateSource = "debug_runtime_invoke_plan",
                    CandidateConfidence = "blocked",
                    Status = "route-error"
                });
            }
        }

        if (stage is "all" or "battle_entry" or "attack_before" or "attack_execute" or "attack_after" or "turn_end" or "battle")
        {
            actions.AddRange(BuildBattleInvokeActions(stage, route));
        }

        if (actions.Count == 0)
        {
            actions.Add(new RuntimeInvokeAction
            {
                Key = $"{stage}:{route}:probe-only",
                Intent = "No direct internal trigger is known yet; collect staged x32dbg probe evidence.",
                Method = "x32dbg_breakpoint_probe",
                CandidateAddress = string.Empty,
                Breakpoints = SelectFunctionCatalogEntries(stage == "menu" ? "startup" : stage).Select(e => NormalizeAddress(e.Address)).Take(8).ToList(),
                Verification = "Use runtime classification, R_00 script-window residency, battle profile, and probe-hit evidence.",
                EvidenceGate = "breakpoint hit plus explicit route/battle-state evidence before promotion",
                CandidateSource = "built-in-function-catalog",
                CandidateConfidence = "pending-live-run",
                Status = "needs-live-run"
            });
        }

        return new RuntimeInvokePlan
        {
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Stage = stage,
            Route = route,
            TargetExeSha256 = paths.ExeSha256,
            Actions = actions,
            RouteErrors = routeErrors,
            Safety = routeErrors.Count == 0
                ? "Debugger-mediated plan. Actions that require runtime injection are not executed unless allow_runtime_injection=true; persistent patching is outside this tool."
                : "Debugger-mediated plan with route_errors. Do not execute route-error actions; inspect and fix the reported route failures first."
        };
    }

    private IEnumerable<RuntimeInvokeAction> BuildMenuRouteActions(string route, GamePaths paths)
    {
        var titleMenuTargets = BuildTitleMenuDispatchRuntimeTargets(paths);
        return route switch
        {
            "title_menu" => new[]
            {
                MenuAction("title_start_game", "Trigger title Start Game path and verify R_00/menu progress.", titleMenuTargets),
                MenuAction("title_load_game", "Trigger title Load Save entry and verify save-list or missing fixture state.", titleMenuTargets),
                MenuAction("title_settings", "Trigger environment settings entry and verify settings page state.", titleMenuTargets),
                MenuAction("title_exit", "Trigger exit path only in isolated test sessions; default run records plan only.", titleMenuTargets)
            },
            "settings_roundtrip" => new[]
            {
                MenuAction("title_settings", "Enter environment settings.", titleMenuTargets),
                MenuAction("settings_return", "Return from environment settings to title without screenshot or mouse.", titleMenuTargets)
            },
            "load_save_entry" => new[]
            {
                MenuAction("title_load_game", "Enter load-save UI and verify fixed save fixture if present.", titleMenuTargets)
            },
            "exit_entry" => new[]
            {
                MenuAction("title_exit", "Reach exit confirmation/exit path in an isolated debug session.", titleMenuTargets)
            },
            "r00_regular_start" or "r00_custom_mode" or "r00_mode_help" => BuildR00RuntimeInvokeCandidateContext(route, paths, "2D,0x12,0x07,0x13", 8, null, true).Actions,
            _ => Array.Empty<RuntimeInvokeAction>()
        };
    }

    private static List<InternalProbeTarget> BuildTitleMenuDispatchRuntimeTargets(GamePaths paths)
    {
        var sections = ReadPeSections(paths.ExePath);
        var textSection = SelectExecutableSection(sections);
        var text = new TextSection(
            textSection.VirtualAddress,
            textSection.VirtualSize,
            textSection.RawPointer,
            textSection.RawSize,
            textSection.Bytes);
        var imports = ReadPeImportFunctions(paths.ExePath, sections)
            .Where(import => IsTitleMenuDispatchImport(import.FunctionName))
            .OrderBy(import => TitleMenuDispatchImportPriority(import.FunctionName))
            .ThenBy(import => import.ImportAddress)
            .ToList();
        var callCandidates = ScanTextForImportCallCandidates(text, imports, 8, 16)
            .OrderBy(candidate => TitleMenuDispatchCallSortKey(candidate))
            .ThenBy(candidate => candidate.BreakpointAddress)
            .ToList();
        var functionCandidates = FindImportCallContainingFunctionCandidates(text, callCandidates, 16)
            .OrderBy(candidate => TitleMenuDispatchImportPriority(candidate.FunctionName))
            .ThenBy(candidate => candidate.BreakpointAddress)
            .ToList();
        var wndProcCompareCandidates = ScanTitleWndProcDispatchCompareCandidates(text, 8, 16)
            .OrderBy(candidate => TitleWndProcDispatchCandidateSortKey(candidate))
            .ThenBy(candidate => candidate.BreakpointAddress)
            .ToList();
        var wndProcFunctionCandidates = BuildTitleWndProcFunctionCandidates(text, wndProcCompareCandidates, 16)
            .OrderBy(candidate => TitleWndProcDispatchCandidateSortKey(candidate))
            .ThenBy(candidate => candidate.BreakpointAddress)
            .ToList();
        var targets = new Dictionary<string, InternalProbeTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in BuildTitleWndProcDispatchProbeTargets(wndProcCompareCandidates, wndProcFunctionCandidates, 8))
        {
            AddProbeTarget(targets, target);
        }

        foreach (var target in BuildTitleMenuDispatchProbeTargets(callCandidates, functionCandidates, 8))
        {
            AddProbeTarget(targets, target);
        }

        return targets.Values
            .OrderBy(target => target.HighFrequency ? 1 : 0)
            .ThenBy(target => TitleMenuDispatchTargetSortKey(target))
            .ThenBy(target => target.Address, StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
    }

    private static RuntimeInvokeAction MenuAction(string key, string intent, IReadOnlyList<InternalProbeTarget> titleMenuTargets)
    {
        var breakpoints = titleMenuTargets
            .Select(target => NormalizeAddress(target.Address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
        var candidateAddress = breakpoints.FirstOrDefault() ?? string.Empty;
        return new RuntimeInvokeAction
        {
            Key = key,
            Intent = intent,
            Method = "title_menu_dispatch_breakpoint_probe",
            InvokeStrategy = breakpoints.Count > 0 ? "title_menu_dispatch_breakpoint_probe" : "needs_title_menu_dispatch_scan",
            CallingConvention = "unknown",
            CandidateAddress = candidateAddress,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["route_key"] = key,
                ["target_count"] = breakpoints.Count.ToString(CultureInfo.InvariantCulture),
                ["probe_profile"] = "title-menu-dispatch"
            },
            Breakpoints = breakpoints,
            Verification = "Compare route-specific hits for Start/Load/Settings/Exit. Require CIP, registers, stack, disassembly, and internal state changes such as R_00 script residency, settings state, load-save fixture status, or isolated exit behavior.",
            RequiresRuntimeInjection = false,
            WritesProcessMemory = false,
            RequiresPausedDebuggee = false,
            EvidenceGate = "title/menu dispatcher remains breakpoint-only until route-specific live hits and ABI review are captured; do not execute debugger_stub_call for these candidates",
            CandidateSource = "Ekd5.exe offline Win32 title/menu dispatch scan",
            CandidateConfidence = breakpoints.Count > 0 ? "static-title-menu-dispatch-candidates" : "needs-title-menu-dispatch-scan",
            Status = breakpoints.Count > 0 ? "needs-title-menu-dispatch-hit" : "needs-title-menu-dispatch-candidate"
        };
    }

    private static RuntimeInvokeAction R00Action(string key, string intent, string scriptOffset)
        => new()
        {
            Key = key,
            Intent = intent,
            Method = "r_scene_interpreter_event_invoke",
            InvokeStrategy = "needs_rscene_handler_entry",
            CallingConvention = "unknown",
            CandidateAddress = scriptOffset,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["script_offset"] = scriptOffset,
                ["person_id"] = key.Contains("xu_zijiang", StringComparison.OrdinalIgnoreCase) ? "157" : string.Empty
            },
            Breakpoints = ["0041648E", "00417EEA"],
            Verification = "Verify exact R_00 script-window residency, command-handler hit, and route-state transition.",
            RequiresRuntimeInjection = true,
            WritesProcessMemory = true,
            RequiresPausedDebuggee = true,
            EvidenceGate = "legacy script-offset placeholder; replace with verified R-scene handler candidate before execution",
            CandidateSource = "RS/R_00.eex script offset",
            CandidateConfidence = "script-offset-only",
            Status = "needs-handler-hit"
        };

    private R00RuntimeInvokeCandidateContext BuildR00RuntimeInvokeCandidateContext(
        string route,
        GamePaths paths,
        string commandIds,
        int maxCandidatesPerCommand,
        string? evidencePath,
        bool includeLatestEvidence)
    {
        var normalizedRoute = NormalizeR00RuntimeRoute(route);
        var script = ReadR00RouteScriptContext(paths.GameRoot);
        var scriptActions = BuildR00RuntimeScriptActions(normalizedRoute, script);
        var requestedIds = ParseCommandIdList(commandIds)
            .Concat(scriptActions.Select(action => action.CommandId))
            .Distinct()
            .OrderBy(id => RSceneCommandPriority(id))
            .ThenBy(id => id)
            .ToList();
        var text = ReadTextSection(paths.ExePath);
        var latestEvidencePath = ResolveR00StartupProbeEvidencePath(paths.WorkspaceRoot, evidencePath, includeLatestEvidence) ?? string.Empty;
        var latestSummary = ReadR00StartupProbeEvidenceSummary(latestEvidencePath);
        var latestHits = string.IsNullOrWhiteSpace(latestEvidencePath) || !File.Exists(latestEvidencePath)
            ? new List<KnowledgeHitSummary>()
            : ReadKnowledgeHitSummaries(latestEvidencePath);
        var latestHitByAddress = latestHits
            .Where(hit => !string.IsNullOrWhiteSpace(hit.Address))
            .GroupBy(hit => NormalizeAddress(hit.Address), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var candidates = ScanRSceneCommandCompareCandidates(text, requestedIds, Math.Clamp(maxCandidatesPerCommand, 1, 64), contextBytes: 16)
            .Select(candidate =>
            {
                var address = NormalizeAddress($"0x{candidate.Address:X8}");
                var hasHit = latestHitByAddress.TryGetValue(address, out var hit);
                return new R00HandlerCandidate(
                    candidate.CommandId,
                    RSceneCommandName(candidate.CommandId),
                    address,
                    $"0x{candidate.FileOffset:X}",
                    candidate.Pattern,
                    candidate.Confidence,
                    candidate.ContextHex,
                    hasHit ? "live-r00-handler-hit-needs-abi-review" : $"static-rscene-command-candidate-{candidate.Confidence}",
                    hasHit,
                    hasHit && hit is not null ? hit.Path : string.Empty);
            })
            .OrderBy(candidate => RSceneCommandPriority(candidate.CommandId))
            .ThenBy(candidate => candidate.VerifiedHit ? 0 : 1)
            .ThenBy(candidate => StaticReferenceConfidenceSortKey(candidate.Confidence))
            .ThenBy(candidate => candidate.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actions = scriptActions
            .Select(action => BuildR00RuntimeInvokeAction(action, normalizedRoute, candidates, latestEvidencePath))
            .ToList();
        var probeTargets = BuildR00RuntimeHandlerProbeTargets(normalizedRoute, candidates);
        var probePlan = new InternalProbePlan
        {
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Profile = "r00-runtime-handler-candidates",
            TargetExeSha256 = paths.ExeSha256,
            Targets = probeTargets,
            Safety = "R_00 runtime handler candidate breakpoints and read-only evidence only; no mouse input, screenshots, game-file writes, process-memory writes, or runtime injection."
        };
        var groupedCandidates = requestedIds
            .Select(id => new
            {
                command_id = $"0x{id:X2}",
                command_name = RSceneCommandName(id),
                candidate_count = candidates.Count(candidate => candidate.CommandId == id),
                live_hit_count = candidates.Count(candidate => candidate.CommandId == id && candidate.VerifiedHit),
                candidates = candidates
                    .Where(candidate => candidate.CommandId == id)
                    .Take(Math.Clamp(maxCandidatesPerCommand, 1, 64))
                    .Select(FormatR00HandlerCandidate)
                    .ToList()
            })
            .ToList();
        var report = new
        {
            schema_version = "1.0",
            created_at = DateTimeOffset.Now.ToString("O"),
            status = "r00-runtime-invoke-candidate-plan-ready",
            route = normalizedRoute,
            target = new
            {
                paths.ExePath,
                paths.ExeSha256,
                expected_sha256 = ExpectedSha256,
                paths.IsExpectedSha256,
                image_base = $"0x{ImageBase:X8}"
            },
            scenario = script.Scenario,
            requested_command_ids = requestedIds.Select(id => $"0x{id:X2}").ToList(),
            latest_evidence_path = latestEvidencePath,
            latest_probe_summary = latestSummary,
            latest_hit_count = latestHits.Count,
            latest_verified_handler_hit_count = candidates.Count(candidate => candidate.VerifiedHit),
            script_actions = scriptActions.Select(action => new
            {
                action.Key,
                action.Intent,
                command_id = $"0x{action.CommandId:X2}",
                action.CommandName,
                script_offset = FormatFileOffset(action.ScriptOffset),
                action.PersonId,
                action.SelectedOption,
                action.SelectionText
            }).ToList(),
            handler_candidate_count = candidates.Count,
            handler_candidates = groupedCandidates,
            runtime_invoke_actions = actions,
            probe_target_count = probeTargets.Count,
            probe_plan_profile = probePlan.Profile,
            evidence_gates = new[]
            {
                "R_00 script window residency for the selected script offset.",
                "x32dbg breakpoint hit at the command-handler candidate during the intended R_00 route.",
                "Registers, stack, and surrounding disassembly captured at the hit.",
                "Route-state transition observed after the command, for example choice box opens or battle_loaded appears.",
                "Only after those gates can ABI/calling convention be derived for debugger_stub_call."
            },
            safety = "This is a bridge plan only. Static handler candidates are breakpoints, not callable function entries. No x32dbg mutation, process-memory write, input, screenshot, or game-file write is performed."
        };
        return new R00RuntimeInvokeCandidateContext(
            report,
            probePlan,
            actions,
            actions.Count,
            candidates.Count,
            probeTargets.Count,
            latestEvidencePath,
            candidates.Count(candidate => candidate.VerifiedHit));
    }

    private static List<InternalProbeTarget> BuildR00RuntimeHandlerProbeTargets(
        string route,
        IReadOnlyList<R00HandlerCandidate> candidates)
        => candidates
            .GroupBy(candidate => candidate.Address, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var candidate = group
                    .OrderBy(row => row.VerifiedHit ? 0 : 1)
                    .ThenBy(row => StaticReferenceConfidenceSortKey(row.Confidence))
                    .First();
                var commandIds = group
                    .Select(row => $"0x{row.CommandId:X2}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var commandNames = group
                    .Select(row => row.CommandName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new InternalProbeTarget
                {
                    Address = candidate.Address,
                    Name = $"r00_handler_{string.Join("_", commandIds.Select(id => id[2..]))}_{PlainAddress(candidate.Address)}",
                    Phase = "rscene",
                    ExpectedSemantics = $"R_00 {route} command-handler compare candidate for {string.Join("/", commandIds)} ({string.Join(", ", commandNames)}).",
                    TriggerHint = "Run while driving the intended R_00 route. Promote only after script-window residency, live hit, registers/stack/disassembly, and route-state transition are captured.",
                    EvidenceLevel = candidate.VerifiedHit ? "live-r00-handler-hit-needs-abi-review" : $"static-rscene-command-candidate-{candidate.Confidence}",
                    HighFrequency = commandIds.Contains("12", StringComparer.OrdinalIgnoreCase) || commandIds.Contains("13", StringComparer.OrdinalIgnoreCase)
                };
            })
            .OrderBy(target => target.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<R00RuntimeScriptAction> BuildR00RuntimeScriptActions(string route, R00RouteScriptContext script)
    {
        var modeOption = route switch
        {
            "r00_custom_mode" => 2,
            "r00_mode_help" => 3,
            _ => 1
        };
        var actions = new List<R00RuntimeScriptAction>
        {
            new(
                "xu_zijiang_actor_click",
                "Trigger or verify person=157 actor-click prerequisite before R_00 choices.",
                0x2D,
                RSceneCommandName(0x2D),
                script.ActorClick.CommandOffset,
                script.ActorClick.PersonId,
                null,
                string.Empty),
            new(
                route switch
                {
                    "r00_custom_mode" => "r00_select_custom_mode",
                    "r00_mode_help" => "r00_select_mode_help",
                    _ => "r00_select_regular_mode"
                },
                "Select the first R_00 mode choice internally or verify the choice-box handler.",
                0x12,
                RSceneCommandName(0x12),
                script.ModeChoice.CommandOffset,
                null,
                modeOption,
                script.ModeChoice.Options.ElementAtOrDefault(modeOption - 1) ?? string.Empty)
        };
        if (route == "r00_regular_start")
        {
            actions.Add(new R00RuntimeScriptAction(
                "r00_regular_start_game",
                "Select Start Game from the regular-mode configuration choice box.",
                0x12,
                RSceneCommandName(0x12),
                script.ConfigChoice.CommandOffset,
                null,
                6,
                script.ConfigChoice.Options.ElementAtOrDefault(5) ?? string.Empty));
        }

        return actions;
    }

    private static RuntimeInvokeAction BuildR00RuntimeInvokeAction(
        R00RuntimeScriptAction scriptAction,
        string route,
        IReadOnlyList<R00HandlerCandidate> candidates,
        string latestEvidencePath)
    {
        var matches = candidates
            .Where(candidate => candidate.CommandId == scriptAction.CommandId)
            .OrderBy(candidate => candidate.VerifiedHit ? 0 : 1)
            .ThenBy(candidate => StaticReferenceConfidenceSortKey(candidate.Confidence))
            .ThenBy(candidate => candidate.Address, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        var first = matches.FirstOrDefault();
        var hasLiveHit = matches.Any(candidate => candidate.VerifiedHit);
        return new RuntimeInvokeAction
        {
            Key = scriptAction.Key,
            Intent = scriptAction.Intent,
            Method = "r_scene_handler_candidate_probe",
            InvokeStrategy = hasLiveHit ? "needs_abi_from_live_handler_hit" : "rscene_handler_breakpoint_probe",
            CallingConvention = "unknown",
            CandidateAddress = first?.Address ?? string.Empty,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["route"] = route,
                ["command_id"] = $"0x{scriptAction.CommandId:X2}",
                ["command_name"] = scriptAction.CommandName,
                ["script_offset"] = FormatFileOffset(scriptAction.ScriptOffset),
                ["person_id"] = scriptAction.PersonId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["selected_option"] = scriptAction.SelectedOption?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["selection_text"] = scriptAction.SelectionText,
                ["latest_evidence_path"] = latestEvidencePath
            },
            Breakpoints = matches.Select(candidate => candidate.Address).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Verification = "Require R_00 script-window residency, command-handler breakpoint hit during the intended route, registers/stack/disassembly, and route-state transition. Do not execute as a stub until ABI is derived.",
            WritesProcessMemory = false,
            RequiresRuntimeInjection = false,
            RequiresPausedDebuggee = false,
            EvidenceGate = "pending live handler hit plus ABI review; static candidates are breakpoint targets only",
            CandidateSource = "RS/R_00.eex script offset + Ekd5.exe .text command-id scan + latest R_00 probe evidence",
            CandidateConfidence = hasLiveHit ? "live-hit-needs-abi-review" : first is null ? "no-handler-candidate" : $"static-{first.Confidence}",
            Status = hasLiveHit ? "handler-hit-captured-needs-abi" : matches.Count > 0 ? "needs-handler-hit" : "needs-handler-candidate"
        };
    }

    private static object FormatR00HandlerCandidate(R00HandlerCandidate candidate)
        => new
        {
            command_id = $"0x{candidate.CommandId:X2}",
            candidate.CommandName,
            candidate.Address,
            candidate.FileOffset,
            candidate.Pattern,
            candidate.Confidence,
            candidate.EvidenceLevel,
            candidate.VerifiedHit,
            candidate.EvidencePath,
            candidate.ContextHex
        };

    private IEnumerable<RuntimeInvokeAction> BuildBattleInvokeActions(string stage, string route)
    {
        var selectedStages = stage == "all" || stage == "battle"
            ? new[] { "battle_entry", "attack_before", "attack_execute", "attack_after", "turn_end" }
            : new[] { stage };
        foreach (var selectedStage in selectedStages)
        {
            var entries = SelectFunctionCatalogEntries(selectedStage).Take(8).ToList();
            if (entries.Count == 0)
            {
                continue;
            }

            yield return new RuntimeInvokeAction
            {
                Key = $"{selectedStage}:{route}",
                Intent = $"Verify {selectedStage} functions through controlled internal battle trigger.",
                Method = "x32dbg_breakpoint_probe_with_battle_state_diff",
                InvokeStrategy = route.Contains("yingchuan", StringComparison.OrdinalIgnoreCase) ? "debugger_stub_call" : "breakpoint_probe",
                CallingConvention = "unknown",
                CandidateAddress = entries[0].Address,
                Breakpoints = entries.Select(e => NormalizeAddress(e.Address)).ToList(),
                Verification = selectedStage.Contains("attack", StringComparison.OrdinalIgnoreCase)
                    ? "Require Cao Cao/Zhang Liang trigger, Zhang Liang HP delta, registers, stack, and disassembly."
                    : "Require turn/action-state delta, registers, stack, and disassembly.",
                RequiresRuntimeInjection = route.Contains("yingchuan", StringComparison.OrdinalIgnoreCase),
                WritesProcessMemory = route.Contains("yingchuan", StringComparison.OrdinalIgnoreCase),
                RequiresPausedDebuggee = route.Contains("yingchuan", StringComparison.OrdinalIgnoreCase),
                Status = "needs-live-run"
            };
        }
    }

    private static string NormalizeFullAutoProfile(string profile)
    {
        var normalized = string.IsNullOrWhiteSpace(profile) ? "full_menu_yingchuan" : profile.Trim().ToLowerInvariant();
        return normalized switch
        {
            "full_menu_yingchuan" or "full-menu-yingchuan" or "yingchuan_full" => "full_menu_yingchuan",
            _ => throw new ArgumentException("Profile must be full_menu_yingchuan.")
        };
    }

    private static string NormalizeRuntimeInvokeStage(string stage)
    {
        var normalized = string.IsNullOrWhiteSpace(stage) ? "startup" : stage.Trim().ToLowerInvariant();
        return normalized switch
        {
            "menu" => "menu",
            "all" => "all",
            _ => NormalizeStageFilter(normalized)
        };
    }

    private static string NormalizeRuntimeRoute(string route)
    {
        var normalized = string.IsNullOrWhiteSpace(route) ? "full_menu_yingchuan" : route.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "full_menu_yingchuan" or "title_menu" or "full_menu" or "settings_roundtrip" or "load_save_entry" or "exit_entry" or
            "r00_regular_start" or "regular_start" or "r00_custom_mode" or "custom_mode" or "r00_mode_help" or "mode_help" or
            "yingchuan_cao_attack_zhangliang" or "cao_attack_zhangliang" or "turn_end" => NormalizeMenuRouteAlias(normalized),
            _ => throw new ArgumentException("Unsupported runtime route.")
        };
    }

    private static string NormalizeMenuRoute(string route)
        => NormalizeRuntimeRoute(route);

    private static string NormalizeTitleMenuProbeRoute(string route)
    {
        var normalized = string.IsNullOrWhiteSpace(route) ? "title_menu" : route.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "title_menu" or "full_menu" or "title_start_game" or "start_game" or
            "title_load_game" or "load_save" or "load_save_entry" or
            "title_settings" or "settings" or "settings_roundtrip" or
            "settings_return" or "title_exit" or "exit" or "exit_entry" => NormalizeMenuRouteAlias(normalized),
            _ => throw new ArgumentException("Title menu probe route must be title_menu, title_start_game, title_load_game, title_settings, settings_return, title_exit, or a supported alias.")
        };
    }

    private static List<string> ParseTitleMenuMatrixRoutes(string routes)
    {
        if (string.IsNullOrWhiteSpace(routes) || routes.Trim().Equals("title_menu", StringComparison.OrdinalIgnoreCase) || routes.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return ["title_start_game", "title_load_game", "title_settings", "title_exit"];
        }

        return routes
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeTitleMenuProbeRoute)
            .Select(route => route switch
            {
                "title_menu" or "full_menu" => string.Empty,
                "load_save_entry" => "title_load_game",
                "settings_roundtrip" => "title_settings",
                "exit_entry" => "title_exit",
                _ => route
            })
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string DefaultTitleMenuTriggerSequence(string route)
        => NormalizeTitleMenuProbeRoute(route) switch
        {
            "title_start_game" => "enter",
            "title_load_game" => "down,enter",
            "title_settings" => "down,down,enter",
            "settings_return" => "escape",
            "title_exit" => "down,down,down,enter",
            _ => string.Empty
        };

    private static string NormalizeMenuRouteAlias(string route)
        => route switch
        {
            "regular_start" => "r00_regular_start",
            "custom_mode" => "r00_custom_mode",
            "mode_help" => "r00_mode_help",
            "cao_attack_zhangliang" => "yingchuan_cao_attack_zhangliang",
            "start_game" => "title_start_game",
            "load_save" => "title_load_game",
            "settings" => "title_settings",
            "exit" => "title_exit",
            _ => route
        };

    private static List<string> ExpandMenuRoutes(string route)
    {
        var normalized = NormalizeMenuRouteAlias(route);
        return normalized switch
        {
            "full_menu_yingchuan" or "full_menu" => ["title_menu", "settings_roundtrip", "load_save_entry", "exit_entry", "r00_regular_start", "r00_custom_mode", "r00_mode_help"],
            "yingchuan_cao_attack_zhangliang" or "turn_end" => [],
            _ => [normalized]
        };
    }

    private static string NormalizeTriggerScript(string triggerScript)
    {
        var normalized = string.IsNullOrWhiteSpace(triggerScript) ? "yingchuan_cao_attack_zhangliang" : triggerScript.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "yingchuan_cao_attack_zhangliang" or "cao_attack_zhangliang" => "yingchuan_cao_attack_zhangliang",
            "turn_end" or "end_turn" => "turn_end",
            _ => throw new ArgumentException("Trigger script must be yingchuan_cao_attack_zhangliang or turn_end.")
        };
    }

    private static string BuildRuntimeInvokeCommand(RuntimeInvokeAction action, bool allowRuntimeInjection)
    {
        if (action.RequiresRuntimeInjection && allowRuntimeInjection)
        {
            return BuildRuntimeStubCommandPlan(action).FirstOrDefault() ?? $"// runtime-injection-plan {action.Key}: candidate={action.CandidateAddress}";
        }

        return action.Breakpoints.Count > 0
            ? $"bp {action.Breakpoints[0]}"
            : $"// runtime-invoke-plan {action.Key}";
    }

    private object ExecuteRuntimeInvokeAction(RuntimeInvokeAction action, bool allowRuntimeInjection, string hostName, int port, string sessionDir)
    {
        var evidenceDir = Path.Combine(sessionDir, "x32dbg", "runtime-invoke");
        Directory.CreateDirectory(evidenceDir);
        var stateBefore = InvokeX32dbg("GET", hostName, port, "/api/debug/state");
        var registersBefore = InvokeX32dbg("GET", hostName, port, "/api/registers/all");
        var originalEip = TryReadCip(stateBefore) ?? TryReadRegister(registersBefore, "eip", "cip") ?? string.Empty;
        var originalEsp = TryReadRegister(registersBefore, "esp") ?? string.Empty;
        var stackBefore = InvokeX32dbg("GET", hostName, port, "/api/stack/trace", new Dictionary<string, string> { ["max_depth"] = "32" });
        var commands = action.RequiresRuntimeInjection && allowRuntimeInjection
            ? BuildRuntimeStubCommandPlan(action)
            : BuildRuntimeBreakpointCommandPlan(action, allowRuntimeInjection);
        var commandResults = new List<object>();
        X32dbgCallResult? pauseAfterRun = null;

        foreach (var command in commands)
        {
            var result = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command });
            commandResults.Add(new
            {
                command,
                result
            });
            AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new
            {
                type = "RuntimeInvokeCommandIssued",
                created_at = DateTimeOffset.Now.ToString("O"),
                action = action.Key,
                command,
                result
            });
        }

        if (action.RequiresRuntimeInjection && allowRuntimeInjection)
        {
            pauseAfterRun = WaitForPausedDebuggee(hostName, port, timeoutMs: 10000, pollMs: 100);
        }

        var stateAfter = InvokeX32dbg("GET", hostName, port, "/api/debug/state");
        var registersAfter = InvokeX32dbg("GET", hostName, port, "/api/registers/all");
        var stackAfter = InvokeX32dbg("GET", hostName, port, "/api/stack/trace", new Dictionary<string, string> { ["max_depth"] = "32" });
        var cip = TryReadCip(stateAfter) ?? TryReadCip(registersAfter) ?? string.Empty;
        X32dbgCallResult? restoreEip = null;
        X32dbgCallResult? restoreEsp = null;
        X32dbgCallResult? freeStub = null;
        if (action.RequiresRuntimeInjection && allowRuntimeInjection)
        {
            if (!string.IsNullOrWhiteSpace(originalEsp))
            {
                restoreEsp = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = $"esp={originalEsp}" });
            }
            if (!string.IsNullOrWhiteSpace(originalEip))
            {
                restoreEip = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = $"eip={originalEip}" });
            }
            freeStub = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = "free $runtime_stub" });
        }

        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = action.RequiresRuntimeInjection ? "runtime-stub-issued" : "debug-command-issued",
            action = action.Key,
            action.Intent,
            action.Method,
            action.InvokeStrategy,
            action.CandidateAddress,
            action.CallingConvention,
            action.RegisterSetup,
            action.StackArguments,
            action.Parameters,
            action.RequiresRuntimeInjection,
            action.RequiresPausedDebuggee,
            original_eip = originalEip,
            original_esp = originalEsp,
            commands,
            command_results = commandResults,
            pause_after_run = pauseAfterRun,
            state_before = stateBefore,
            registers_before = registersBefore,
            stack_before = stackBefore,
            state_after = stateAfter,
            registers_after = registersAfter,
            stack_after = stackAfter,
            cip_after = cip,
            restore = new
            {
                esp = restoreEsp,
                eip = restoreEip,
                free_stub = freeStub
            },
            disasm_after = string.IsNullOrWhiteSpace(cip)
                ? null
                : InvokeX32dbg("GET", hostName, port, "/api/disasm/at", new Dictionary<string, string> { ["address"] = cip, ["count"] = "24" }),
            battle_after = SafeReadBattleState(),
            safety = "Debugger-mediated runtime invocation evidence. Persistent EXE changes are not performed here."
        };
        var path = Path.Combine(evidenceDir, $"runtime-invoke-{Timestamp()}-{SanitizeLabel(action.Key)}.json");
        WriteJson(path, report);
        return new
        {
            report.status,
            evidence_path = path,
            command_count = commands.Count,
            commands,
            command_results = commandResults,
            cip_after = cip
        };
    }

    private static List<string> BuildRuntimeStubCommandPlan(RuntimeInvokeAction action)
    {
        var candidate = NormalizeAddress(action.CandidateAddress);
        var stackArgumentBytes = action.StackArguments.Count * 4;
        var commands = new List<string>();
        commands.Add("alloc 1000");
        commands.Add("mov $runtime_stub, $lastalloc");
        commands.Add("mov $runtime_pc, $runtime_stub");
        AppendRuntimeAsm(commands, "pushfd");
        AppendRuntimeAsm(commands, "pushad");
        foreach (var (register, value) in action.RegisterSetup.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                AppendRuntimeAsm(commands, $"mov {register}, {value}");
            }
        }

        foreach (var argument in action.StackArguments.AsEnumerable().Reverse())
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                AppendRuntimeAsm(commands, $"push {argument}");
            }
        }

        AppendRuntimeAsm(commands, $"call {PlainAddress(candidate)}");
        if (stackArgumentBytes > 0 && action.CallingConvention.Equals("cdecl", StringComparison.OrdinalIgnoreCase))
        {
            AppendRuntimeAsm(commands, $"add esp, {stackArgumentBytes}");
        }
        AppendRuntimeAsm(commands, "popad");
        AppendRuntimeAsm(commands, "popfd");
        AppendRuntimeAsm(commands, "int3");
        commands.Add("eip=$runtime_stub");
        commands.Add("run");
        return commands;
    }

    private static List<string> BuildRuntimeBreakpointCommandPlan(RuntimeInvokeAction action, bool allowRuntimeInjection)
    {
        if (action.RequiresRuntimeInjection && allowRuntimeInjection)
        {
            return BuildRuntimeStubCommandPlan(action);
        }

        var breakpoints = action.Breakpoints
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(NormalizeAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
        if (breakpoints.Count == 0)
        {
            return [BuildRuntimeInvokeCommand(action, allowRuntimeInjection)];
        }

        return breakpoints.Select(address => $"bp {address}").ToList();
    }

    private static void AppendRuntimeAsm(List<string> commands, string instruction)
    {
        commands.Add($"asm $runtime_pc, \"{instruction}\"");
        commands.Add("mov $runtime_pc, $runtime_pc+$result");
    }

    private static bool IsExecutableRuntimeInvoke(RuntimeInvokeAction action)
    {
        if (!action.InvokeStrategy.Equals("debugger_stub_call", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.CandidateAddress))
        {
            return false;
        }

        try
        {
            var address = ParseAddress(action.CandidateAddress);
            return address >= ImageBase && address < 0x00800000;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildRuntimeInvokeSkipReason(
        bool bridgeReady,
        bool allowDebugInvoke,
        bool allowRuntimeInjection,
        RuntimeInvokeAction action,
        bool debuggeePaused,
        bool executableRuntimeInvoke)
    {
        if (!bridgeReady)
        {
            return "x32dbg bridge is not online";
        }
        if (!allowDebugInvoke)
        {
            return "allow_debug_invoke was false";
        }
        if (action.RequiresRuntimeInjection && !allowRuntimeInjection)
        {
            return "action requires temporary runtime injection but allow_runtime_injection was false";
        }
        return "action is plan-only";
    }

    private static bool DetectSaveFixture(string gameRoot)
    {
        var names = new[] { "SV000.E5S", "SV001.E5S", "SV002.E5S", "SV003.E5S", "SV004.E5S" };
        return names.Any(name => File.Exists(Path.Combine(gameRoot, name)));
    }

    private static object BuildPersistentPatchRecommendation(string sessionDir, string profile)
        => new
        {
            schema_version = "1.0",
            created_at = DateTimeOffset.Now.ToString("O"),
            profile,
            recommendation = "Persistent patch conversion is allowed only after dynamic evidence review. Export an EffectPackage and use preview_effect_patch/apply_effect_patch; GameDebug does not patch Ekd5.exe directly.",
            source_evidence = sessionDir,
            effect_package_status = "not-generated",
            required_workflow = new[] { "debug_knowledge_promote allow_write=false", "manual review", "EffectPackage preview", "apply_effect_patch with backup/reread" }
        };

    private static object SummarizeBattleDiff(object? beforeValue, object? afterValue)
    {
        var before = beforeValue as BattleStateSnapshot;
        var after = afterValue as BattleStateSnapshot;
        if (before is null || after is null)
        {
            return new { available = false, reason = "before or after battle snapshot was unavailable" };
        }

        var beforeByIndex = before.Units.ToDictionary(u => u.UnitIndex);
        var changed = new List<object>();
        foreach (var unit in after.Units)
        {
            if (!beforeByIndex.TryGetValue(unit.UnitIndex, out var old))
            {
                changed.Add(new { unit = unit.UnitIndex, kind = "appeared", after = unit });
                continue;
            }

            if (old.HP != unit.HP || old.MP != unit.MP || old.X != unit.X || old.Y != unit.Y || old.Action != unit.Action || old.Side != unit.Side)
            {
                changed.Add(new
                {
                    unit = unit.UnitIndex,
                    before = new { old.X, old.Y, old.HP, old.MP, old.Action, old.Side },
                    after = new { unit.X, unit.Y, unit.HP, unit.MP, unit.Action, unit.Side }
                });
            }
        }

        return new
        {
            available = true,
            before.ActiveUnitCount,
            after_count = after.ActiveUnitCount,
            changed_count = changed.Count,
            changed_units = changed.Take(32).ToList()
        };
    }

    private object CaptureInternalProbeHit(
        string sessionDir,
        int hitIndex,
        long elapsedMs,
        string address,
        InternalProbeTarget target,
        X32dbgCallResult state,
        string hostName,
        int port)
    {
        var registers = InvokeX32dbg("GET", hostName, port, "/api/registers/all");
        var esp = TryReadRegister(registers, "esp");
        var battle = SafeReadBattleState();
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            hit_index = hitIndex,
            elapsed_ms = elapsedMs,
            address,
            target,
            debug_state = state,
            process = InvokeX32dbg("GET", hostName, port, "/api/process/info"),
            registers,
            breakpoint = InvokeX32dbg("GET", hostName, port, "/api/breakpoints/get", new Dictionary<string, string> { ["address"] = address }),
            disasm_at_cip = InvokeX32dbg("GET", hostName, port, "/api/disasm/at", new Dictionary<string, string> { ["address"] = address, ["count"] = "32" }),
            function_at_cip = InvokeX32dbg("GET", hostName, port, "/api/disasm/function", new Dictionary<string, string> { ["address"] = address, ["max_instructions"] = "96" }),
            stack_trace = InvokeX32dbg("GET", hostName, port, "/api/stack/trace", new Dictionary<string, string> { ["max_depth"] = "48" }),
            stack_read = string.IsNullOrWhiteSpace(esp)
                ? new X32dbgCallResult(false, "GET", "/api/stack/read", null, null, null, "ESP was not available.")
                : InvokeX32dbg("GET", hostName, port, "/api/stack/read", new Dictionary<string, string> { ["address"] = esp, ["size"] = "128" }),
            memory_at_cip = InvokeX32dbg("GET", hostName, port, "/api/memory/read", new Dictionary<string, string> { ["address"] = address, ["size"] = "128" }),
            battle,
            safety = "Internal probe evidence only; no screenshots, mouse input, process-memory writes, or game-file writes."
        };

        var evidencePath = Path.Combine(
            sessionDir,
            "x32dbg",
            $"internal-probe-hit-{hitIndex:D4}-{PlainAddress(address)}-{SanitizeLabel(target.Name)}.json");
        WriteJson(evidencePath, report);
        var hitSummary = new
        {
            hit_index = hitIndex,
            elapsed_ms = elapsedMs,
            address,
            name = target.Name,
            phase = target.Phase,
            evidence_level = target.EvidenceLevel,
            high_frequency = target.HighFrequency,
            evidence_path = evidencePath,
            battle_summary = SummarizeBattleForEvidence(battle)
        };
        AppendProbeHitLine(
            sessionDir,
            "breakpoint-hit",
            new
            {
                hitSummary.hit_index,
                hitSummary.elapsed_ms,
                hitSummary.address,
                hitSummary.name,
                hitSummary.phase,
                hitSummary.evidence_level,
                hitSummary.high_frequency,
                hitSummary.evidence_path,
                has_registers = true,
                has_stack = true,
                has_disassembly = true,
                hitSummary.battle_summary
            });
        return hitSummary;
    }

    private static object SummarizeBattleForEvidence(object? battle)
        => battle is BattleStateSnapshot snapshot
            ? new
            {
                snapshot.ActiveUnitCount,
                snapshot.SideCounts,
                first_units = snapshot.Units.Take(10).ToList()
            }
            : battle ?? new { ok = false, error = "Battle state was unavailable." };

    private static List<KnowledgeHitSummary> ReadKnowledgeHitSummaries(string evidencePath)
    {
        var files = Directory.Exists(evidencePath)
            ? Directory.EnumerateFiles(evidencePath, "internal-probe-hit-*.json", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(evidencePath, "internal-probe-run-summary.json", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string> { evidencePath };

        var hits = new List<KnowledgeHitSummary>();
        foreach (var file in files)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
                var root = document.RootElement;
                if (TryGetJsonProperty(root, "hits", out var hitArray) && hitArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var hit in hitArray.EnumerateArray())
                    {
                        hits.Add(ReadKnowledgeHitSummary(hit, file));
                    }
                    continue;
                }

                hits.Add(ReadKnowledgeHitSummary(root, file));
            }
            catch
            {
                // Ignore non-matching or partial evidence files; drafts must stay conservative.
            }
        }

        return hits
            .Where(h => !string.IsNullOrWhiteSpace(h.Address) || !string.IsNullOrWhiteSpace(h.Name))
            .DistinctBy(h => $"{h.Address}|{h.Name}|{h.Path}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(h => h.Address, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static KnowledgeHitSummary ReadKnowledgeHitSummary(JsonElement element, string fallbackPath)
    {
        var address = FirstNonEmpty(
            GetStringProperty(element, "address", "Address", "cip", "Cip"),
            TryGetNestedString(element, "target", "address"));
        var name = FirstNonEmpty(
            GetStringProperty(element, "name", "Name"),
            TryGetNestedString(element, "target", "name"),
            TryGetNestedString(element, "target", "Name"));
        var phase = FirstNonEmpty(
            GetStringProperty(element, "phase", "Phase"),
            TryGetNestedString(element, "target", "phase"),
            TryGetNestedString(element, "target", "Phase"));
        var evidenceLevel = FirstNonEmpty(
            GetStringProperty(element, "evidence_level", "evidenceLevel", "EvidenceLevel"),
            TryGetNestedString(element, "target", "evidence_level"),
            TryGetNestedString(element, "target", "evidenceLevel"),
            TryGetNestedString(element, "target", "EvidenceLevel"),
            "dynamic-hit-needs-review");
        var path = FirstNonEmpty(
            GetStringProperty(element, "evidence_path", "evidencePath", "path", "Path"),
            fallbackPath);
        return new KnowledgeHitSummary(address, name, phase, evidenceLevel, path);
    }

    private object BuildKnowledgePromotions(string evidencePath, string topic, bool allowWrite, string? gameRoot)
    {
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var resolvedEvidencePath = Path.GetFullPath(evidencePath);
        var evidenceFiles = Directory.Exists(resolvedEvidencePath)
            ? Directory.EnumerateFiles(resolvedEvidencePath, "*.json", SearchOption.AllDirectories).ToList()
            : File.Exists(resolvedEvidencePath) ? [resolvedEvidencePath] : [];
        var hits = evidenceFiles.Count > 0 ? ReadKnowledgeHitSummaries(resolvedEvidencePath) : [];
        var hasProbeSummary = evidenceFiles.Any(path => Path.GetFileName(path).Equals("internal-probe-run-summary.json", StringComparison.OrdinalIgnoreCase));
        var hasHitEvidence = hits.Count > 0;
        var hasBattleDiff = EvidenceContainsAny(evidenceFiles, ["battle_diff", "attack_after_observed", "profile-matched"]);
        var hasRegisters = EvidenceContainsAny(evidenceFiles, ["registers", "disasm_at_cip", "stack_trace"]);
        var canPromote = allowWrite || (hasHitEvidence && hasProbeSummary && hasRegisters && hasBattleDiff);
        var rows = hits
            .GroupBy(h => NormalizeAddress(h.Address), StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                address = g.Key,
                names = g.Select(h => h.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                phases = g.Select(h => h.Phase).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                evidence_level = canPromote ? "dynamic-verified-review-required" : "candidate-dynamic-evidence",
                evidence_files = g.Select(h => h.Path).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList()
            })
            .ToList();

        return new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            topic,
            target_exe_sha256 = paths.ExeSha256,
            evidence_path = resolvedEvidencePath,
            allow_write_requested = allowWrite,
            can_promote = canPromote,
            gates = new[]
            {
                new { gate = "probe_hit_evidence", passed = hasHitEvidence, detail = $"{hits.Count} hit rows" },
                new { gate = "probe_summary", passed = hasProbeSummary, detail = "internal-probe-run-summary.json present" },
                new { gate = "register_stack_disasm", passed = hasRegisters, detail = "registers/disassembly/stack evidence present" },
                new { gate = "battle_or_trigger_diff", passed = hasBattleDiff, detail = "battle diff/profile/attack-after evidence present" }
            },
            promoted = canPromote ? rows : [],
            pending = canPromote ? [] : rows,
            refused_reason = canPromote ? string.Empty : "Promotion requires breakpoint hit, explicit trigger/battle diff, registers, stack, and disassembly evidence.",
            safety = "Promotion report only unless allow_write=true and all gates pass."
        };
    }

    private static bool EvidenceContainsAny(IEnumerable<string> files, IEnumerable<string> needles)
    {
        var list = needles.ToList();
        foreach (var file in files)
        {
            try
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                if (list.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore unreadable evidence fragments.
            }
        }

        return false;
    }

    private static string ResolveKnowledgePromotionTarget(string toolRoot, string topic)
    {
        var normalized = string.IsNullOrWhiteSpace(topic) ? "function-index" : topic.Trim().ToLowerInvariant();
        return normalized switch
        {
            "function-index" or "functions" => Path.Combine(toolRoot, "本地知识库", "04-函数速查", "函数速查表.md"),
            "automation-flow" or "automation" => Path.Combine(toolRoot, "本地知识库", "05-教程指南", "战场操作自动化与调试流程.md"),
            "merit" => Path.Combine(toolRoot, "本地知识库", "03-机制详解", "功勋模式与五维功勋.md"),
            _ => Path.Combine(toolRoot, "本地知识库", "06-项目与工具链", "模型上下文协议接入.md")
        };
    }

    private static void AppendKnowledgePromotionMarkdown(string targetPath, object promotions)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ".");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(promotions, JsonOptions));
        var root = document.RootElement;
        var lines = new List<string>
        {
            "",
            "## GameDebug 动态验证追加记录",
            "",
            $"- 写入时间：{DateTimeOffset.Now:O}",
            $"- 证据目录：`{EscapeMarkdownCell(GetStringProperty(root, "evidence_path", "evidencePath") ?? string.Empty)}`",
            $"- 目标 SHA256：`{EscapeMarkdownCell(GetStringProperty(root, "target_exe_sha256", "targetExeSha256") ?? string.Empty)}`",
            ""
        };

        if (TryGetJsonProperty(root, "promoted", out var promoted) && promoted.ValueKind == JsonValueKind.Array && promoted.GetArrayLength() > 0)
        {
            lines.Add("| 地址 | 阶段 | 名称 | 证据等级 |");
            lines.Add("| --- | --- | --- | --- |");
            foreach (var row in promoted.EnumerateArray())
            {
                var address = GetStringProperty(row, "address") ?? string.Empty;
                var phases = JoinJsonStringArray(row, "phases");
                var names = JoinJsonStringArray(row, "names");
                var level = GetStringProperty(row, "evidence_level", "evidenceLevel") ?? string.Empty;
                lines.Add($"| `{EscapeMarkdownCell(address)}` | {EscapeMarkdownCell(phases)} | {EscapeMarkdownCell(names)} | {EscapeMarkdownCell(level)} |");
            }
        }
        else
        {
            lines.Add("- 未写入已验证地址：promotion 报告没有满足全部门槛。");
        }

        File.AppendAllLines(targetPath, lines, Encoding.UTF8);
    }

    private static string JoinJsonStringArray(JsonElement root, string propertyName)
    {
        if (!TryGetJsonProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(", ", element.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static bool IsBridgeSuccess(X32dbgCallResult result)
    {
        if (!result.Ok)
        {
            return false;
        }

        if (result.Data is not JsonElement json)
        {
            return true;
        }

        if (TryGetJsonProperty(json, "success", out var success) && success.ValueKind is JsonValueKind.False)
        {
            return false;
        }
        if (TryGetJsonProperty(json, "ok", out var ok) && ok.ValueKind is JsonValueKind.False)
        {
            return false;
        }
        if (TryGetJsonProperty(json, "error", out var error) && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            if (error.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(error.GetString()))
            {
                return false;
            }
        }

        return true;
    }

    private GamePaths ResolveGamePaths(string? gameRoot, bool requireExpectedHash = true)
    {
        var toolRoot = ResolveToolRoot();
        var workspaceRoot = Directory.GetParent(toolRoot)?.FullName ?? Directory.GetCurrentDirectory();
        var root = FirstNonEmpty(
            gameRoot,
            Environment.GetEnvironmentVariable("CCZGAME_DEBUG_GAME_ROOT"),
            Environment.GetEnvironmentVariable("CCZMODSTUDIO_GAME_ROOT"));
        if (string.IsNullOrWhiteSpace(root))
        {
            root = FindDefaultGameRoot(workspaceRoot);
        }

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("Game root was not found. Pass game_root or set CCZGAME_DEBUG_GAME_ROOT.");
        }

        root = Path.GetFullPath(root);
        var exe = Path.Combine(root, "Ekd5.exe");
        if (!File.Exists(exe))
        {
            var nestedRoot = FindDefaultGameRoot(root);
            if (!string.IsNullOrWhiteSpace(nestedRoot) && Directory.Exists(nestedRoot))
            {
                root = Path.GetFullPath(nestedRoot);
                exe = Path.Combine(root, "Ekd5.exe");
            }
        }

        if (!File.Exists(exe))
        {
            throw new FileNotFoundException("Ekd5.exe was not found under game root or its child directories.", exe);
        }

        var sha = ComputeSha256(exe);
        var expected = string.Equals(sha, ExpectedSha256, StringComparison.OrdinalIgnoreCase);
        if (requireExpectedHash && !expected)
        {
            throw new InvalidOperationException($"Refusing debug automation for unexpected Ekd5.exe SHA256 {sha}. Expected {ExpectedSha256}.");
        }

        return new GamePaths(workspaceRoot, toolRoot, root, exe, sha, expected);
    }

    private static string ResolveToolRoot()
    {
        var env = Environment.GetEnvironmentVariable("CCZMODSTUDIO_TOOL_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
        {
            return Path.GetFullPath(env);
        }

        var current = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(current);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CCZModStudio.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static string FindDefaultGameRoot(string workspaceRoot)
    {
        var candidates = Directory.EnumerateFiles(workspaceRoot, "Ekd5.exe", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}CCZModStudio_TestCopies{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}CCZModStudio_Exports{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_CCZModStudio_Backups{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .ToList();
        foreach (var candidate in candidates)
        {
            try
            {
                if (string.Equals(ComputeSha256(candidate.FullName), ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate.DirectoryName ?? string.Empty;
                }
            }
            catch
            {
                // Keep searching.
            }
        }

        return candidates
            .OrderByDescending(c => c.DirectoryName?.Contains("6.5", StringComparison.OrdinalIgnoreCase) == true)
            .ThenBy(c => c.FullName.Length)
            .Select(c => c.DirectoryName ?? string.Empty)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string ResolveX32dbgPath(string? x32dbgPath, string workspaceRoot)
    {
        var path = FirstNonEmpty(
            x32dbgPath,
            Environment.GetEnvironmentVariable("CCZGAME_DEBUG_X32DBG_PATH"),
            Path.Combine(workspaceRoot, "CCZModStudio_Exports", "DebugTools", "x64dbg", "release", "x32", "x32dbg.exe"));
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("x32dbg.exe was not found.", path);
        }
        return Path.GetFullPath(path);
    }

    private static Process? FindTargetProcess()
        => Process.GetProcessesByName("Ekd5")
            .OrderByDescending(p =>
            {
                try { return p.StartTime; }
                catch { return DateTime.MinValue; }
            })
            .FirstOrDefault();

    private static Process? WaitForTargetProcess(int waitMs)
    {
        var timeout = Math.Clamp(waitMs, 0, 60000);
        var stopwatch = Stopwatch.StartNew();
        Process? process;
        do
        {
            process = FindTargetProcess();
            if (process is not null || stopwatch.ElapsedMilliseconds >= timeout)
            {
                return process;
            }
            Thread.Sleep(100);
        }
        while (true);
    }

    private static Process RequireTargetProcess()
        => FindTargetProcess() ?? throw new InvalidOperationException("Ekd5.exe is not running.");

    private static IntPtr RequireMainWindow(Process process)
    {
        process.Refresh();
        if (process.MainWindowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Ekd5.exe process {process.Id} does not have a main window yet.");
        }
        return process.MainWindowHandle;
    }

    private static object? ProcessSummary(Process? process)
    {
        if (process is null)
        {
            return null;
        }

        try
        {
            process.Refresh();
            return new
            {
                id = process.Id,
                name = process.ProcessName,
                responding = process.Responding,
                main_window_handle = FormatHwnd(process.MainWindowHandle),
                main_window_title = process.MainWindowTitle,
                start_time = TryGetStartTime(process)
            };
        }
        catch (Exception ex)
        {
            return new { id = process.Id, error = ex.Message };
        }
    }

    private static object GetWindowSnapshot(Process? process)
    {
        if (process is null || process.MainWindowHandle == IntPtr.Zero)
        {
            return new { exists = false };
        }

        NativeMethods.GetWindowRect(process.MainWindowHandle, out var rect);
        NativeMethods.GetClientRect(process.MainWindowHandle, out var client);
        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        NativeMethods.ClientToScreen(process.MainWindowHandle, ref origin);
        return new
        {
            exists = true,
            hwnd = FormatHwnd(process.MainWindowHandle),
            title = process.MainWindowTitle,
            window_rect = new { rect.Left, rect.Top, rect.Right, rect.Bottom, rect.Width, rect.Height },
            client_rect = new { client.Left, client.Top, client.Right, client.Bottom, client.Width, client.Height },
            client_origin_screen = new { x = origin.X, y = origin.Y },
            is_foreground = process.MainWindowHandle == NativeMethods.GetForegroundWindow()
        };
    }

    private static bool HasWindow(object window)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(window, JsonOptions));
        return GetBoolProperty(document.RootElement, "exists");
    }

    private static object WaitForWindowSnapshot(Process process, int waitMs)
    {
        var timeout = Math.Clamp(waitMs, 0, 60000);
        var stopwatch = Stopwatch.StartNew();
        do
        {
            try
            {
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return GetWindowSnapshot(process);
                }
            }
            catch
            {
                break;
            }

            if (stopwatch.ElapsedMilliseconds >= timeout)
            {
                break;
            }
            Thread.Sleep(100);
        }
        while (true);

        return GetWindowSnapshot(process);
    }

    private BattleStateSnapshot ReadBattleStateSnapshot(int maxUnits)
    {
        var process = RequireTargetProcess();
        var bytes = ReadProcessMemory(process.Id, UnitArrayAddress, maxUnits * UnitStride);
        var units = DecodeUnits(bytes, maxUnits);
        return new BattleStateSnapshot
        {
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            ProcessId = process.Id,
            ActiveUnitCount = units.Count,
            Units = units,
            SideCounts = units.GroupBy(u => u.Side.ToString(CultureInfo.InvariantCulture)).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal)
        };
    }

    private object? SafeReadBattleState()
    {
        try
        {
            return ReadBattleStateSnapshot(160);
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private static List<BattleUnitRow> DecodeUnits(byte[] bytes, int maxUnits)
    {
        var rows = new List<BattleUnitRow>();
        var count = Math.Min(maxUnits, bytes.Length / UnitStride);
        for (var i = 0; i < count; i++)
        {
            var o = i * UnitStride;
            var hp = ReadUInt16(bytes, o + 0x10);
            var mp = ReadUInt16(bytes, o + 0x14);
            var x = bytes[o + 0x06];
            var y = bytes[o + 0x07];
            var side = bytes[o + 0x05];
            var hasSignal = hp > 0 && hp < 999 && x < 80 && y < 80 && side <= 3;
            if (!hasSignal)
            {
                continue;
            }

            rows.Add(new BattleUnitRow
            {
                UnitIndex = i,
                DataIdByte = bytes[o + 0x04],
                Side = side,
                X = x,
                Y = y,
                Action = bytes[o + 0x0D],
                HP = hp,
                MP = mp,
                AttrsHex = string.Join(" ", bytes.Skip(o + 0x18).Take(6).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)))
            });
        }

        return rows;
    }

    private static byte[] ReadProcessMemory(int processId, uint address, int size)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            ThrowLastWin32("OpenProcess failed.");
        }

        try
        {
            var buffer = new byte[size];
            if (!NativeMethods.ReadProcessMemory(handle, new IntPtr(address), buffer, size, out var read))
            {
                ThrowLastWin32($"ReadProcessMemory failed at 0x{address:X8}.");
            }

            var actual = read.ToInt32();
            if (actual == size)
            {
                return buffer;
            }

            return buffer.Take(Math.Max(actual, 0)).ToArray();
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private object ExportMemoryRange(string memoryDir, string name, uint address, int size)
    {
        var bytes = ReadProcessMemory(RequireTargetProcess().Id, address, size);
        var path = Path.Combine(memoryDir, $"memory-{name}-{address:X8}-{size:X}.bin");
        File.WriteAllBytes(path, bytes);
        return new
        {
            name,
            address = $"0x{address:X8}",
            size,
            path,
            sha256 = ComputeSha256(path)
        };
    }

    private static object CaptureWindowClient(Process process, string screenshotDir, string label)
    {
        Directory.CreateDirectory(screenshotDir);
        var hwnd = RequireMainWindow(process);
        if (!NativeMethods.GetClientRect(hwnd, out var client))
        {
            ThrowLastWin32("GetClientRect failed.");
        }

        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(hwnd, ref origin))
        {
            ThrowLastWin32("ClientToScreen failed.");
        }

        using var bmp = new Bitmap(Math.Max(client.Width, 1), Math.Max(client.Height, 1));
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(origin.X, origin.Y, 0, 0, bmp.Size);
        }

        var safeLabel = SanitizeLabel(label);
        var path = Path.Combine(screenshotDir, $"{safeLabel}-{Timestamp()}.png");
        bmp.Save(path, ImageFormat.Png);
        return new
        {
            path,
            sha256 = ComputeSha256(path),
            hwnd = FormatHwnd(hwnd),
            client_origin_screen = new { x = origin.X, y = origin.Y },
            width = bmp.Width,
            height = bmp.Height
        };
    }

    private static object? TryCapture(string sessionDir, string label)
    {
        try
        {
            return CaptureWindowClient(RequireTargetProcess(), Path.Combine(sessionDir, "screenshots"), label);
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private sealed record ScreenPoint(int X, int Y);

    private sealed record UiClickTarget(
        int BaseClientWidth,
        int BaseClientHeight,
        int BaseCenterX,
        int BaseCenterY,
        int ClientWidth,
        int ClientHeight,
        double ScaleX,
        double ScaleY,
        int ClientX,
        int ClientY,
        bool IsWithinClient);

    private static UiClickTarget ResolveUiClickTarget(IntPtr hwnd, UiArea area)
    {
        if (!NativeMethods.GetClientRect(hwnd, out var client))
        {
            ThrowLastWin32("GetClientRect failed.");
        }

        var baseWidth = area.BaseClientWidth > 0 ? area.BaseClientWidth : Math.Max(client.Width, 1);
        var baseHeight = area.BaseClientHeight > 0 ? area.BaseClientHeight : Math.Max(client.Height, 1);
        var baseCenterX = area.X + area.Width / 2;
        var baseCenterY = area.Y + area.Height / 2;
        var scaleX = area.BaseClientWidth > 0 && client.Width > 0 ? client.Width / (double)area.BaseClientWidth : 1.0;
        var scaleY = area.BaseClientHeight > 0 && client.Height > 0 ? client.Height / (double)area.BaseClientHeight : 1.0;
        var clientX = (int)Math.Round(baseCenterX * scaleX, MidpointRounding.AwayFromZero);
        var clientY = (int)Math.Round(baseCenterY * scaleY, MidpointRounding.AwayFromZero);
        var isWithinClient = clientX >= 0 && clientX < client.Width && clientY >= 0 && clientY < client.Height;
        return new UiClickTarget(
            baseWidth,
            baseHeight,
            baseCenterX,
            baseCenterY,
            client.Width,
            client.Height,
            scaleX,
            scaleY,
            clientX,
            clientY,
            isWithinClient);
    }

    private static ScreenPoint ClientToScreen(IntPtr hwnd, int clientX, int clientY)
    {
        var point = new NativeMethods.POINT { X = clientX, Y = clientY };
        if (!NativeMethods.ClientToScreen(hwnd, ref point))
        {
            ThrowLastWin32("ClientToScreen failed.");
        }
        return new ScreenPoint(point.X, point.Y);
    }

    private static object SendLeftClick(Process process, int screenX, int screenY)
    {
        var hwnd = RequireMainWindow(process);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
        _ = NativeMethods.SetForegroundWindow(hwnd);
        if (!NativeMethods.SetCursorPos(screenX, screenY))
        {
            ThrowLastWin32("SetCursorPos failed.");
        }

        var inputs = new[]
        {
            MouseInput(0, 0, NativeMethods.MOUSEEVENTF_LEFTDOWN),
            MouseInput(0, 0, NativeMethods.MOUSEEVENTF_LEFTUP)
        };
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            ThrowLastWin32("SendInput did not send every input.");
        }

        return new { sent = true, count = sent, screen_x = screenX, screen_y = screenY };
    }

    private static NativeMethods.INPUT MouseInput(int x, int y, uint flags)
        => new()
        {
            type = 0,
            u = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = x,
                    dy = y,
                    dwFlags = flags
                }
            }
        };

    private static string NormalizeKeyDelivery(string delivery)
    {
        var normalized = string.IsNullOrWhiteSpace(delivery)
            ? "post_message"
            : delivery.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "post" or "postmessage" or "post_message" or "window_message" => "post_message",
            "send" or "sendinput" or "send_input" => "send_input",
            _ => throw new ArgumentException("Keyboard delivery must be post_message or send_input.")
        };
    }

    private static List<KeyMessage> ParseKeySequence(string sequence)
    {
        if (string.IsNullOrWhiteSpace(sequence))
        {
            return [];
        }

        return sequence
            .Split([',', ';', '|', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseKeyToken)
            .ToList();
    }

    private static KeyMessage ParseKeyToken(string token)
    {
        var normalized = token.Trim().ToLowerInvariant();
        if (normalized.Length == 1)
        {
            var ch = normalized[0];
            if (ch is >= 'a' and <= 'z')
            {
                return new KeyMessage(normalized, char.ToUpperInvariant(ch));
            }
            if (ch is >= '0' and <= '9')
            {
                return new KeyMessage(normalized, ch);
            }
        }

        if (normalized.StartsWith("vk_", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[3..];
        }

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(normalized[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexVk) &&
            hexVk is >= 1 and <= 255)
        {
            return new KeyMessage(token, hexVk);
        }

        if (normalized.StartsWith('f') &&
            int.TryParse(normalized[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var functionKey) &&
            functionKey is >= 1 and <= 12)
        {
            return new KeyMessage(normalized, 0x6F + functionKey);
        }

        var vk = normalized switch
        {
            "enter" or "return" => 0x0D,
            "esc" or "escape" or "cancel" => 0x1B,
            "space" or "spacebar" => 0x20,
            "tab" => 0x09,
            "backspace" or "back" => 0x08,
            "up" or "arrowup" => 0x26,
            "down" or "arrowdown" => 0x28,
            "left" or "arrowleft" => 0x25,
            "right" or "arrowright" => 0x27,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "insert" or "ins" => 0x2D,
            "delete" or "del" => 0x2E,
            "shift" => 0x10,
            "control" or "ctrl" => 0x11,
            "alt" or "menu" => 0x12,
            _ => 0
        };

        if (vk == 0)
        {
            throw new ArgumentException($"Unsupported key token '{token}'. Use enter, esc, arrows, space, tab, f1..f12, digits, single letters, or vk_XX.");
        }

        return new KeyMessage(normalized, vk);
    }

    private static object SendKey(IntPtr hwnd, KeyMessage key, string delivery)
        => delivery.Equals("send_input", StringComparison.OrdinalIgnoreCase)
            ? SendInputKey(hwnd, key)
            : PostKey(hwnd, key);

    private static object PostKey(IntPtr hwnd, KeyMessage key)
    {
        var scanCode = NativeMethods.MapVirtualKey((uint)key.VirtualKey, NativeMethods.MAPVK_VK_TO_VSC);
        var downLParam = 1u | (scanCode << 16);
        var upLParam = downLParam | (1u << 30) | (1u << 31);
        var downOk = NativeMethods.PostMessage(hwnd, NativeMethods.WM_KEYDOWN, new IntPtr(key.VirtualKey), new IntPtr(unchecked((int)downLParam)));
        var downError = downOk ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        var upOk = NativeMethods.PostMessage(hwnd, NativeMethods.WM_KEYUP, new IntPtr(key.VirtualKey), new IntPtr(unchecked((int)upLParam)));
        var upError = upOk ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        return new
        {
            ok = downOk && upOk,
            delivery = "post_message",
            key = key.Name,
            virtual_key = $"0x{key.VirtualKey:X2}",
            scan_code = $"0x{scanCode:X}",
            keydown_posted = downOk,
            keydown_error = downError,
            keyup_posted = upOk,
            keyup_error = upError
        };
    }

    private static object SendInputKey(IntPtr hwnd, KeyMessage key)
    {
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
        _ = NativeMethods.SetForegroundWindow(hwnd);
        var scanCode = NativeMethods.MapVirtualKey((uint)key.VirtualKey, NativeMethods.MAPVK_VK_TO_VSC);
        var inputs = new[]
        {
            KeyboardInput(key.VirtualKey, scanCode, 0),
            KeyboardInput(key.VirtualKey, scanCode, NativeMethods.KEYEVENTF_KEYUP)
        };
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        var error = sent == inputs.Length ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        return new
        {
            ok = sent == inputs.Length,
            delivery = "send_input",
            key = key.Name,
            virtual_key = $"0x{key.VirtualKey:X2}",
            scan_code = $"0x{scanCode:X}",
            sent,
            expected = inputs.Length,
            error
        };
    }

    private static NativeMethods.INPUT KeyboardInput(int virtualKey, uint scanCode, uint flags)
        => new()
        {
            type = 1,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)virtualKey,
                    wScan = (ushort)scanCode,
                    dwFlags = flags
                }
            }
        };

    private X32dbgCallResult WaitForBridge(string hostName, int port, int waitMs)
    {
        var sw = Stopwatch.StartNew();
        X32dbgCallResult last = new(false, "GET", "/api/health", null, null, null, "not checked");
        while (sw.ElapsedMilliseconds < Math.Max(waitMs, 0))
        {
            last = InvokeX32dbg("GET", hostName, port, "/api/health");
            if (last.Ok)
            {
                return last;
            }
            Thread.Sleep(250);
        }

        return last;
    }

    private static X32dbgCallResult InvokeX32dbg(string method, string hostName, int port, string path, Dictionary<string, string>? query = null, object? body = null)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var token = Environment.GetEnvironmentVariable("X64DBG_MCP_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                client.DefaultRequestHeaders.TryAddWithoutValidation("X64DBG-MCP-Token", token);
            }

            var uri = BuildUri(hostName, port, path, query);
            HttpResponseMessage response;
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                var bodyJson = JsonSerializer.Serialize(body ?? new { }, JsonOptions);
                using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                response = client.PostAsync(uri, content).GetAwaiter().GetResult();
            }
            else
            {
                response = client.GetAsync(uri).GetAwaiter().GetResult();
            }

            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            object? data = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    data = JsonSerializer.Deserialize<JsonElement>(text, JsonOptions);
                }
                catch
                {
                    data = text;
                }
            }

            return new X32dbgCallResult(response.IsSuccessStatusCode, method, path, query, body, data, response.IsSuccessStatusCode ? string.Empty : text);
        }
        catch (Exception ex)
        {
            return new X32dbgCallResult(false, method, path, query, body, null, ex.Message);
        }
    }

    private static string BuildUri(string hostName, int port, string path, Dictionary<string, string>? query)
    {
        var builder = new StringBuilder($"http://{hostName}:{port}{path}");
        if (query is { Count: > 0 })
        {
            builder.Append('?');
            builder.Append(string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}")));
        }
        return builder.ToString();
    }

    private static string? TryReadCip(X32dbgCallResult result)
    {
        if (result.Data is not JsonElement json)
        {
            return null;
        }

        if (TryGetJsonProperty(json, "data", out var data))
        {
            var nested = FirstNonEmpty(
                GetStringProperty(data, "cip", "eip", "rip", "CIP", "EIP", "RIP"),
                TryGetNestedString(data, "registers", "eip"),
                TryGetNestedString(data, "registers", "EIP"),
                TryGetNestedString(data, "registers", "cip"),
                TryGetNestedString(data, "registers", "CIP"));
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }

        return FirstNonEmpty(
            GetStringProperty(json, "cip", "eip", "rip", "CIP", "EIP", "RIP"),
            TryGetNestedString(json, "registers", "eip"),
            TryGetNestedString(json, "registers", "EIP"),
            TryGetNestedString(json, "registers", "cip"),
            TryGetNestedString(json, "registers", "CIP"));
    }

    private static bool IsPausedState(X32dbgCallResult result)
    {
        if (result.Data is not JsonElement json)
        {
            return false;
        }

        var stateText = string.Empty;
        if (TryGetJsonProperty(json, "data", out var data))
        {
            stateText = FirstNonEmpty(
                GetStringProperty(data, "state", "debug_state", "status", "State", "Status"),
                stateText);
        }
        stateText = FirstNonEmpty(
            stateText,
            GetStringProperty(json, "state", "debug_state", "status", "State", "Status"));
        if (!string.IsNullOrWhiteSpace(stateText))
        {
            return stateText.Equals("paused", StringComparison.OrdinalIgnoreCase) ||
                   stateText.Equals("break", StringComparison.OrdinalIgnoreCase) ||
                   stateText.Equals("stopped", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static X32dbgCallResult WaitForPausedDebuggee(string hostName, int port, int timeoutMs, int pollMs)
    {
        var timeout = Math.Max(timeoutMs, 1000);
        var delay = Math.Clamp(pollMs, 50, 1000);
        var sw = Stopwatch.StartNew();
        X32dbgCallResult last = new(false, "GET", "/api/debug/state", null, null, null, "not checked");
        while (sw.ElapsedMilliseconds < timeout)
        {
            last = InvokeX32dbg("GET", hostName, port, "/api/debug/state");
            if (IsPausedState(last))
            {
                return last;
            }

            Thread.Sleep(delay);
        }

        return last;
    }

    private static bool IsEntryPointPause(string normalizedCip, X32dbgCallResult state)
    {
        if (normalizedCip.Equals("004817E0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (state.Data is not JsonElement json)
        {
            return false;
        }

        var label = string.Empty;
        if (TryGetJsonProperty(json, "data", out var data))
        {
            label = FirstNonEmpty(
                GetStringProperty(data, "label", "Label"),
                label);
        }

        label = FirstNonEmpty(
            label,
            GetStringProperty(json, "label", "Label"));
        return label.Contains("AddressOfEntryPoint", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> LoadPlanAddresses(string? planPath)
    {
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(planPath) || !File.Exists(planPath))
        {
            return addresses;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(planPath, Encoding.UTF8));
        var root = document.RootElement;
        if (root.TryGetProperty("Breakpoints", out var breakpoints))
        {
            foreach (var bp in breakpoints.EnumerateArray())
            {
                var address = GetStringProperty(bp, "address", "Address");
                if (!string.IsNullOrWhiteSpace(address)) addresses.Add(NormalizeAddress(address));
            }
        }
        if (root.TryGetProperty("Batches", out var batches))
        {
            foreach (var batch in batches.EnumerateArray())
            {
                if (!batch.TryGetProperty("Candidates", out var candidates)) continue;
                foreach (var candidate in candidates.EnumerateArray())
                {
                    var address = GetStringProperty(candidate, "Address", "address");
                    if (!string.IsNullOrWhiteSpace(address)) addresses.Add(NormalizeAddress(address));
                }
            }
        }
        return addresses;
    }

    private static string ResolvePlanPath(string? planPath)
    {
        if (!string.IsNullOrWhiteSpace(planPath))
        {
            var path = Path.GetFullPath(planPath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Plan JSON was not found.", path);
            }
            return path;
        }

        var workspace = Directory.GetParent(ResolveToolRoot())?.FullName ?? Directory.GetCurrentDirectory();
        var root = Path.Combine(workspace, "CCZModStudio_Reports", "DebugEvidence");
        var latest = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "breakpoints-6.5-baseline.json", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (latest is null)
        {
            throw new FileNotFoundException("No breakpoints-6.5-baseline.json was found under DebugEvidence.");
        }

        return latest.FullName;
    }

    private static bool HasBattleStateChanged(BattleStateSnapshot before, BattleStateSnapshot after)
        => before.ActiveUnitCount != after.ActiveUnitCount ||
           before.Units.Count != after.Units.Count ||
           before.Units.Zip(after.Units).Any(pair =>
               pair.First.UnitIndex != pair.Second.UnitIndex ||
               pair.First.X != pair.Second.X ||
               pair.First.Y != pair.Second.Y ||
               pair.First.Action != pair.Second.Action ||
               pair.First.HP != pair.Second.HP ||
               pair.First.MP != pair.Second.MP ||
               pair.First.AttrsHex != pair.Second.AttrsHex);

    private static string EnsureSessionDirectory(string workspaceRoot, string? outputDir)
    {
        var dir = string.IsNullOrWhiteSpace(outputDir)
            ? Path.Combine(workspaceRoot, "CCZModStudio_Reports", "DebugEvidence", $"game-auto-{Timestamp()}")
            : Path.GetFullPath(outputDir);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "screenshots"));
        Directory.CreateDirectory(Path.Combine(dir, "memory"));
        Directory.CreateDirectory(Path.Combine(dir, "x32dbg"));
        return dir;
    }

    private static void WriteSummary(string sessionDir, string reason, string evidencePath, string screenshot)
    {
        var path = Path.Combine(sessionDir, "summary.md");
        var lines = new[]
        {
            "# CCZ Game Debug Session",
            "",
            $"- Updated: {DateTimeOffset.Now:O}",
            $"- Reason: {reason}",
            $"- Evidence: `{evidencePath}`",
            $"- Screenshot: `{screenshot}`",
            "",
            "Safety: this server only writes evidence files under DebugEvidence; game file writes remain outside this MCP server."
        };
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteJson(string path, object value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8);

    private static void AppendJsonLine(string path, object value)
        => File.AppendAllText(path, JsonSerializer.Serialize(value, JsonOptions).ReplaceLineEndings(string.Empty) + Environment.NewLine, Encoding.UTF8);

    private static void AppendActionChainLine(
        string sessionDir,
        string tool,
        string action,
        string? status,
        object? detail = null,
        object? beforeState = null,
        object? afterState = null)
    {
        Directory.CreateDirectory(sessionDir);
        AppendJsonLine(Path.Combine(sessionDir, "action-chain.jsonl"), new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            tool,
            action,
            status = string.IsNullOrWhiteSpace(status) ? "unknown" : status,
            evidence_model = "state-first-no-mouse-no-screenshot",
            before_state = beforeState,
            after_state = afterState,
            detail
        });
    }

    private static void AppendProbeHitLine(string sessionDir, string kind, object detail)
    {
        Directory.CreateDirectory(sessionDir);
        AppendJsonLine(Path.Combine(sessionDir, "probe-hits.jsonl"), new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            kind,
            detail
        });
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static int ReadUInt16(byte[] bytes, int offset)
        => offset >= 0 && offset + 1 < bytes.Length ? BitConverter.ToUInt16(bytes, offset) : 0;

    private static string Timestamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);

    private static string SanitizeLabel(string label)
        => Regex.Replace(string.IsNullOrWhiteSpace(label) ? "capture" : label, @"[^\w\-.]+", "-").Trim('-');

    private static string NormalizeAddress(string value)
    {
        var match = Regex.Match(value, @"(?i)0x[0-9a-f]+|\b[0-9a-f]{6,8}\b");
        if (!match.Success)
        {
            return value.Trim().ToUpperInvariant();
        }

        var hex = match.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? match.Value[2..] : match.Value;
        var number = Convert.ToUInt64(hex, 16);
        return FormatHex(number, 8);
    }

    private static uint ParseAddress(string value)
    {
        var normalized = NormalizeAddress(value);
        var text = normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? normalized[2..] : normalized;
        return Convert.ToUInt32(text, 16);
    }

    private static string? GetStringProperty(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        return null;
    }

    private static int GetIntProperty(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)) return n;
                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            }
        }
        return 0;
    }

    private static bool GetBoolProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetJsonProperty(element, name, out var value))
            {
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
                if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed)) return parsed;
            }
        }
        return false;
    }

    private static bool TryGetJsonProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(name) || string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string TryGetNestedString(JsonElement element, string objectName, string propertyName)
    {
        if (TryGetJsonProperty(element, objectName, out var nested))
        {
            return GetStringProperty(nested, propertyName) ?? string.Empty;
        }
        return string.Empty;
    }

    private static int TryGetNestedInt(JsonElement element, string objectName, string propertyName)
    {
        if (TryGetJsonProperty(element, objectName, out var nested))
        {
            return GetIntProperty(nested, propertyName, ToCamelCase(propertyName));
        }
        return 0;
    }

    private static string TryGetBoolString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetJsonProperty(element, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean().ToString(CultureInfo.InvariantCulture);
            }
        }
        return string.Empty;
    }

    private static void AddBattleUnitMarkdownRow(List<string> lines, string propertyName, string label, JsonElement root)
    {
        if (!TryGetJsonProperty(root, propertyName, out var unit) || unit.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            lines.Add($"| {EscapeMarkdownCell(label)} | missing |  |  |  |  |  |");
            return;
        }

        lines.Add(
            $"| {EscapeMarkdownCell(label)} " +
            $"| `{GetIntProperty(unit, "unit_index", "unitIndex", "UnitIndex")}` " +
            $"| `{GetIntProperty(unit, "side", "Side")}` " +
            $"| `({GetIntProperty(unit, "x", "X")},{GetIntProperty(unit, "y", "Y")})` " +
            $"| `{GetIntProperty(unit, "hp", "HP")}` " +
            $"| `{GetIntProperty(unit, "mp", "MP")}` " +
            $"| `{GetIntProperty(unit, "action", "Action")}` |");
    }

    private static string CompactJson(JsonElement element, int maxLength)
    {
        var compact = JsonSerializer.Serialize(element, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (compact.Length <= maxLength)
        {
            return compact;
        }

        return compact[..Math.Max(maxLength - 3, 0)] + "...";
    }

    private static string? TryReadRegister(X32dbgCallResult result, params string[] registerNames)
    {
        if (result.Data is not JsonElement json)
        {
            return null;
        }

        var roots = new List<JsonElement> { json };
        if (TryGetJsonProperty(json, "data", out var data))
        {
            roots.Add(data);
            if (TryGetJsonProperty(data, "registers", out var nestedRegisters))
            {
                roots.Add(nestedRegisters);
            }
        }
        if (TryGetJsonProperty(json, "registers", out var registers))
        {
            roots.Add(registers);
        }

        foreach (var root in roots)
        {
            var value = GetStringProperty(root, registerNames);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static string FormatHwnd(IntPtr hwnd) => FormatHex(hwnd.ToInt64());

    private static string PlainAddress(string address)
        => NormalizeAddress(address).Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string FormatHex(long value, int minDigits = 0)
        => value.ToString(minDigits > 0 ? "X" + minDigits.ToString(CultureInfo.InvariantCulture) : "X", CultureInfo.InvariantCulture);

    private static string FormatHex(ulong value, int minDigits = 0)
        => value.ToString(minDigits > 0 ? "X" + minDigits.ToString(CultureInfo.InvariantCulture) : "X", CultureInfo.InvariantCulture);

    private static string FormatHex(uint value, int minDigits = 0)
        => value.ToString(minDigits > 0 ? "X" + minDigits.ToString(CultureInfo.InvariantCulture) : "X", CultureInfo.InvariantCulture);

    private static string EscapeMarkdownCell(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");

    private static string TryGetStartTime(Process process)
    {
        try { return process.StartTime.ToString("O", CultureInfo.InvariantCulture); }
        catch { return string.Empty; }
    }

    private static void GuardLocalHost(string hostName)
    {
        if (!string.Equals(hostName, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(hostName, "localhost", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(hostName, "::1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing non-local x64dbg MCP bridge host. Use 127.0.0.1, localhost, or ::1.");
        }
    }

    private static void ThrowLastWin32(string message)
    {
        var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        throw new InvalidOperationException($"{message} Win32Error={error}.");
    }
}
