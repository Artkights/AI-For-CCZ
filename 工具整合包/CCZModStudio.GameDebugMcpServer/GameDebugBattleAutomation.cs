using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CCZModStudio.GameDebugMcpServer;

public sealed partial class GameDebugRuntime
{
    private const uint CombatContextAddress = 0x004927F0;
    private const int DefaultGridOriginX = 76;
    private const int DefaultGridOriginY = 44;
    private const int DefaultGridCellWidth = 32;
    private const int DefaultGridCellHeight = 32;

    private static readonly IReadOnlyList<InternalProbeTarget> BattleAutoProbeTargets =
    [
        new() { Address = "00405AD5", Name = "physical_post_damage_apply", Phase = "battle-physical", ExpectedSemantics = "Physical damage has been applied; use with HP/context diff for on_post_damage.", TriggerHint = "Execute a physical attack that deals damage.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false },
        new() { Address = "0043F70C", Name = "unit_hp_setter", Phase = "battle-hp", ExpectedSemantics = "Writes tactical unit HP; target unit should be recoverable from stack/registers and unit array diff.", TriggerHint = "Any HP damage or recovery.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = true },
        new() { Address = "00406043", Name = "personal_exp_cache_write", Phase = "battle-exp", ExpectedSemantics = "Writes personal EXP cache at combat context +428.", TriggerHint = "Physical attack settle.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false },
        new() { Address = "00406054", Name = "weapon_exp_cache_write", Phase = "battle-exp", ExpectedSemantics = "Writes weapon EXP cache at combat context +42C.", TriggerHint = "Physical attack settle.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false },
        new() { Address = "00406074", Name = "armor_exp_cache_write", Phase = "battle-exp", ExpectedSemantics = "Writes armor EXP cache at combat context +430 + slot*4.", TriggerHint = "Physical attack settle or target equipment EXP.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false },
        new() { Address = "00406555", Name = "double_attack_candidate_before_rng", Phase = "battle-double", ExpectedSemantics = "Double/chase attack candidate before helper call.", TriggerHint = "Physical attack where attacker may double.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false },
        new() { Address = "0040655C", Name = "double_attack_candidate_after_rng", Phase = "battle-double", ExpectedSemantics = "Double/chase attack decision after helper call.", TriggerHint = "Physical attack where attacker may double.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false },
        new() { Address = "00406520", Name = "double_attack_counter_increment", Phase = "battle-double", ExpectedSemantics = "Increments combat context +608 after double/chase branch.", TriggerHint = "A successful double/chase attack.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false },
        new() { Address = "0041797E", Name = "counter_check_entry", Phase = "battle-counter", ExpectedSemantics = "Counterattack eligibility route.", TriggerHint = "Attack an enemy that can retaliate.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false },
        new() { Address = "004064DA", Name = "counter_execute_chain", Phase = "battle-counter", ExpectedSemantics = "Counterattack execution chain before returning to action settle.", TriggerHint = "Enemy counterattack.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false },
        new() { Address = "00406581", Name = "counter_execute_tail", Phase = "battle-counter", ExpectedSemantics = "Counterattack execution tail.", TriggerHint = "Enemy counterattack.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false },
        new() { Address = "0044AF8F", Name = "turn_start_reset_loop", Phase = "battle-turn", ExpectedSemantics = "Turn-start reset loop over tactical units.", TriggerHint = "End player turn or enter a new round.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false },
        new() { Address = "00406690", Name = "restore_action_flag", Phase = "battle-turn", ExpectedSemantics = "Clears unit+0D action bits for turn start or extra-action restore.", TriggerHint = "Turn start or extra action restoration.", EvidenceLevel = "dynamic-confirmed-local", HighFrequency = false }
    ];

    private sealed record BattleGridCalibration(int OriginX, int OriginY, int CellWidth, int CellHeight);
    private sealed record BattleClientPoint(int ClientX, int ClientY, int ClientWidth, int ClientHeight, bool WithinClient);
    private sealed record BattleInputPoint(string Kind, string Label, int? GridX, int? GridY, int ClientX, int ClientY, int ScreenX, int ScreenY, bool WithinClient);
    private sealed record BattleActionPlan(string Action, string Status, string Reason, IReadOnlyList<BattleInputPoint> Inputs);

    public object GameReadBattlefieldRuntimeSnapshot(string? gameRoot, string? outputDir)
    {
        var paths = ResolveBattleAutomationPaths(gameRoot, requireExpectedHash: false, allowMissingExe: true);
        var sessionDir = string.IsNullOrWhiteSpace(outputDir) ? string.Empty : EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var snapshot = ReadBattlefieldRuntimeSnapshot();
        if (!string.IsNullOrWhiteSpace(sessionDir))
        {
            var path = Path.Combine(sessionDir, $"battlefield-runtime-{Timestamp()}.json");
            WriteJson(path, snapshot);
            AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattlefieldRuntimeSnapshot", path, snapshot.Phase, snapshot.Confidence });
            return new { snapshot, session_dir = sessionDir, path };
        }

        return new { snapshot, session_dir = sessionDir };
    }

    public object GameBattleCalibrateGrid(
        string? gameRoot,
        int originX,
        int originY,
        int cellWidth,
        int cellHeight,
        string knownPoints,
        string? outputDir)
    {
        var paths = ResolveBattleAutomationPaths(gameRoot, requireExpectedHash: false, allowMissingExe: true);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var process = RequireTargetProcess();
        var hwnd = RequireMainWindow(process);
        var calibration = NormalizeGridCalibration(originX, originY, cellWidth, cellHeight);
        var points = ParseKnownGridPoints(knownPoints);
        var inferred = InferGridCalibration(points, calibration);
        var current = GetClientSize(hwnd);
        var snapshot = ReadBattlefieldRuntimeSnapshot();
        var unitPoints = snapshot.Units
            .Select(u => new
            {
                u.UnitIndex,
                u.DataId,
                u.DisplayId,
                u.Side,
                u.X,
                u.Y,
                client = BattleClientPointReport(GridToClientPoint(hwnd, u.X, u.Y, inferred))
            })
            .ToList();
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status = points.Count >= 2 ? "calibrated-from-known-points" : "default-or-single-point-calibration",
            session_dir = sessionDir,
            requested = new { origin_x = originX, origin_y = originY, cell_width = cellWidth, cell_height = cellHeight },
            known_points = points,
            calibration = new { origin_x = inferred.OriginX, origin_y = inferred.OriginY, cell_width = inferred.CellWidth, cell_height = inferred.CellHeight },
            current_client = new { width = current.Width, height = current.Height },
            unit_grid_points = unitPoints,
            snapshot,
            safety = "Read-only calibration report; no mouse input, screenshots, process-memory writes, debugger changes, or game-file writes."
        };
        var path = Path.Combine(sessionDir, $"battle-grid-calibration-{Timestamp()}.json");
        WriteJson(path, report);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattleGridCalibration", path, report.status });
        return new { session_dir = sessionDir, path, report };
    }

    public object GameBattleGridToClient(
        int gridX,
        int gridY,
        string? gameRoot,
        int originX,
        int originY,
        int cellWidth,
        int cellHeight,
        string? outputDir)
    {
        var paths = ResolveBattleAutomationPaths(gameRoot, requireExpectedHash: false, allowMissingExe: true);
        var sessionDir = string.IsNullOrWhiteSpace(outputDir) ? string.Empty : EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var process = RequireTargetProcess();
        var hwnd = RequireMainWindow(process);
        var calibration = NormalizeGridCalibration(originX, originY, cellWidth, cellHeight);
        var client = GridToClientPoint(hwnd, gridX, gridY, calibration);
        var screen = ClientToScreen(hwnd, client.ClientX, client.ClientY);
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            grid = new { x = gridX, y = gridY },
            calibration = new { origin_x = calibration.OriginX, origin_y = calibration.OriginY, cell_width = calibration.CellWidth, cell_height = calibration.CellHeight },
            current_client = new { width = client.ClientWidth, height = client.ClientHeight },
            client_point = new { x = client.ClientX, y = client.ClientY },
            screen_point = new { x = screen.X, y = screen.Y },
            within_client = client.WithinClient
        };
        if (!string.IsNullOrWhiteSpace(sessionDir))
        {
            var path = Path.Combine(sessionDir, $"battle-grid-to-client-{Timestamp()}.json");
            WriteJson(path, report);
            AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattleGridToClient", path, report.within_client });
            return new { session_dir = sessionDir, path, report };
        }

        return report;
    }

    public object GameBattleVerifyClickTarget(
        int gridX,
        int gridY,
        bool allowOccupied,
        int expectedUnitIndex,
        string? gameRoot,
        int originX,
        int originY,
        int cellWidth,
        int cellHeight,
        string? outputDir)
    {
        var paths = ResolveBattleAutomationPaths(gameRoot, requireExpectedHash: false, allowMissingExe: true);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var process = RequireTargetProcess();
        var hwnd = RequireMainWindow(process);
        var calibration = NormalizeGridCalibration(originX, originY, cellWidth, cellHeight);
        var snapshot = ReadBattlefieldRuntimeSnapshot();
        var client = GridToClientPoint(hwnd, gridX, gridY, calibration);
        var occupant = FindOccupant(snapshot, gridX, gridY);
        var expectedOk = expectedUnitIndex < 0 || occupant?.UnitIndex == expectedUnitIndex;
        var occupancyOk = allowOccupied || occupant is null || expectedUnitIndex >= 0;
        var status = client.WithinClient && expectedOk && occupancyOk ? "ok" : "blocked";
        var reasons = new List<string>();
        if (!client.WithinClient) reasons.Add("target grid center is outside current client area");
        if (!occupancyOk) reasons.Add("target cell is occupied and allow_occupied=false");
        if (!expectedOk) reasons.Add("target cell occupant does not match expected_unit_index");
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            status,
            reasons,
            grid = new { x = gridX, y = gridY },
            calibration = new { origin_x = calibration.OriginX, origin_y = calibration.OriginY, cell_width = calibration.CellWidth, cell_height = calibration.CellHeight },
            client_point = new { x = client.ClientX, y = client.ClientY },
            current_client = new { width = client.ClientWidth, height = client.ClientHeight },
            within_client = client.WithinClient,
            allow_occupied = allowOccupied,
            expected_unit_index = expectedUnitIndex,
            occupant,
            snapshot,
            safety = "Read-only click-target verification; no input, screenshots, process-memory writes, debugger changes, or game-file writes."
        };
        var path = Path.Combine(sessionDir, $"battle-click-target-{Timestamp()}.json");
        WriteJson(path, report);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattleClickTargetVerification", path, status });
        return new { session_dir = sessionDir, path, report };
    }

    public object GameBattleSelectUnit(
        int unitIndex,
        bool allowInput,
        string? gameRoot,
        int originX,
        int originY,
        int cellWidth,
        int cellHeight,
        int settleMs,
        string? outputDir)
    {
        try
        {
            return ExecuteBattleAction(
                "game_battle_select_unit",
                gameRoot,
                allowInput,
                originX,
                originY,
                cellWidth,
                cellHeight,
                settleMs,
                outputDir,
                before => BuildSelectUnitPlan(before, unitIndex));
        }
        catch (Exception ex) when (!allowInput)
        {
            return SafeBattleDryRunError("game_battle_select_unit", gameRoot, outputDir, ex);
        }
    }

    public object GameBattleMoveUnit(
        int unitIndex,
        int toX,
        int toY,
        bool allowInput,
        string? gameRoot,
        int originX,
        int originY,
        int cellWidth,
        int cellHeight,
        int settleMs,
        string? outputDir)
    {
        try
        {
            return ExecuteBattleAction(
                "game_battle_move_unit",
                gameRoot,
                allowInput,
                originX,
                originY,
                cellWidth,
                cellHeight,
                settleMs,
                outputDir,
                before => BuildMoveUnitPlan(before, unitIndex, toX, toY));
        }
        catch (Exception ex) when (!allowInput)
        {
            return SafeBattleDryRunError("game_battle_move_unit", gameRoot, outputDir, ex);
        }
    }

    public object GameBattleAttack(
        int attackerIndex,
        int targetIndex,
        bool moveFirst,
        bool allowInput,
        string? gameRoot,
        int originX,
        int originY,
        int cellWidth,
        int cellHeight,
        int settleMs,
        string? outputDir)
    {
        try
        {
            return ExecuteBattleAction(
                "game_battle_attack",
                gameRoot,
                allowInput,
                originX,
                originY,
                cellWidth,
                cellHeight,
                settleMs,
                outputDir,
                before => BuildAttackPlan(before, attackerIndex, targetIndex, moveFirst));
        }
        catch (Exception ex) when (!allowInput)
        {
            return SafeBattleDryRunError("game_battle_attack", gameRoot, outputDir, ex);
        }
    }

    public object GameBattleWait(
        int unitIndex,
        bool allowInput,
        string? gameRoot,
        int originX,
        int originY,
        int cellWidth,
        int cellHeight,
        int settleMs,
        string? outputDir)
    {
        try
        {
            return ExecuteBattleAction(
                "game_battle_wait",
                gameRoot,
                allowInput,
                originX,
                originY,
                cellWidth,
                cellHeight,
                settleMs,
                outputDir,
                before => BuildWaitPlan(before, unitIndex));
        }
        catch (Exception ex) when (!allowInput)
        {
            return SafeBattleDryRunError("game_battle_wait", gameRoot, outputDir, ex);
        }
    }

    public object GameBattleEndTurn(bool allowInput, string? gameRoot, int settleMs, string? outputDir)
    {
        try
        {
            return ExecuteBattleAction(
                "game_battle_end_turn",
                gameRoot,
                allowInput,
                DefaultGridOriginX,
                DefaultGridOriginY,
                DefaultGridCellWidth,
                DefaultGridCellHeight,
                settleMs,
                outputDir,
                BuildEndTurnPlan);
        }
        catch (Exception ex) when (!allowInput)
        {
            return SafeBattleDryRunError("game_battle_end_turn", gameRoot, outputDir, ex);
        }
    }

    public object GameBattleAutoStep(
        string policy,
        bool allowInput,
        string? gameRoot,
        int originX,
        int originY,
        int cellWidth,
        int cellHeight,
        int settleMs,
        string? outputDir)
    {
        try
        {
            return ExecuteBattleAction(
                "game_battle_auto_step",
                gameRoot,
                allowInput,
                originX,
                originY,
                cellWidth,
                cellHeight,
                settleMs,
                outputDir,
                before => BuildAutoStepPlan(before, policy));
        }
        catch (Exception ex) when (!allowInput)
        {
            return SafeBattleDryRunError("game_battle_auto_step", gameRoot, outputDir, ex);
        }
    }

    public object GameBattleAutoRun(
        int maxSteps,
        bool stopOnUnknown,
        string policy,
        bool allowInput,
        string? gameRoot,
        int originX,
        int originY,
        int cellWidth,
        int cellHeight,
        int settleMs,
        string? outputDir)
    {
        try
        {
            return GameBattleAutoRunCore(maxSteps, stopOnUnknown, policy, allowInput, gameRoot, originX, originY, cellWidth, cellHeight, settleMs, outputDir);
        }
        catch (Exception ex) when (!allowInput)
        {
            return SafeBattleDryRunError("game_battle_auto_run", gameRoot, outputDir, ex);
        }
    }

    private object GameBattleAutoRunCore(
        int maxSteps,
        bool stopOnUnknown,
        string policy,
        bool allowInput,
        string? gameRoot,
        int originX,
        int originY,
        int cellWidth,
        int cellHeight,
        int settleMs,
        string? outputDir)
    {
        var paths = ResolveBattleAutomationPaths(gameRoot, requireExpectedHash: allowInput, allowMissingExe: !allowInput);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var process = FindTargetProcess();
        if (process is null && !allowInput)
        {
            var dryNoProcess = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                session_dir = sessionDir,
                status = "dry-run-no-process",
                reason = "Ekd5.exe is not running; live battle memory is required to generate concrete auto-step plans.",
                policy = NormalizeBattlePolicy(policy),
                allow_input = allowInput,
                max_steps = Math.Clamp(maxSteps, 1, 100),
                stop_on_unknown = stopOnUnknown,
                steps = Array.Empty<object>(),
                safety = "Dry-run only; no mouse input, screenshots, process-memory writes, debugger changes, or game-file writes."
            };
            var dryPath = Path.Combine(sessionDir, "battle-auto-run-summary.json");
            WriteJson(dryPath, dryNoProcess);
            AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattleAutoRunSummary", path = dryPath, dryNoProcess.status });
            AppendActionChainLine(sessionDir, "game_battle_auto_run", "summary", "dry-run-no-process", new { path = dryPath });
            return dryNoProcess;
        }
        if (process is null)
        {
            throw new InvalidOperationException("Ekd5.exe is not running; cannot send battlefield input.");
        }
        var steps = new List<object>();
        var count = Math.Clamp(maxSteps, 1, 100);
        var status = "completed";
        for (var i = 0; i < count; i++)
        {
            BattlefieldRuntimeSnapshot before;
            try
            {
                before = ReadBattlefieldRuntimeSnapshot();
            }
            catch (Exception ex) when (!allowInput)
            {
                status = "dry-run-read-failed";
                steps.Add(new { index = i + 1, status, error = ex.Message });
                break;
            }
            if (stopOnUnknown && before.Phase is not "player_control" and not "unit_selected" and not "command_menu" and not "target_select")
            {
                status = "stopped-on-phase";
                steps.Add(new { index = i + 1, status, phase = before.Phase, reason = "phase does not allow safe player input" });
                break;
            }
            if (before.ControllableUnits.Count == 0)
            {
                status = "no-controllable-units";
                steps.Add(new { index = i + 1, status, phase = before.Phase });
                break;
            }

            var step = GameBattleAutoStep(policy, allowInput, gameRoot, originX, originY, cellWidth, cellHeight, settleMs, sessionDir);
            steps.Add(new { index = i + 1, result = step });
            var stepStatus = ExtractStringFromObject(step, "status");
            if (stepStatus.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
                stepStatus.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                status = $"stopped-{stepStatus}";
                break;
            }
            if (!allowInput)
            {
                status = "dry-run";
                break;
            }
        }

        var finalSnapshot = TryReadBattlefieldRuntimeSnapshot();
        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            session_dir = sessionDir,
            status,
            policy = NormalizeBattlePolicy(policy),
            allow_input = allowInput,
            max_steps = count,
            stop_on_unknown = stopOnUnknown,
            steps,
            final_snapshot = finalSnapshot,
            safety = allowInput
                ? "Sent controlled mouse input only after snapshot and target validation; no process-memory writes, debugger mutation, or game-file writes."
                : "Dry-run only; no mouse input, screenshots, process-memory writes, debugger changes, or game-file writes."
        };
        var path = Path.Combine(sessionDir, "battle-auto-run-summary.json");
        WriteJson(path, summary);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattleAutoRunSummary", path, status, step_count = steps.Count });
        AppendActionChainLine(sessionDir, "game_battle_auto_run", "summary", status, new { path, step_count = steps.Count });
        return summary;
    }

    public object DebugBattleAutoProbeRun(
        int maxSteps,
        string policy,
        bool runProbes,
        bool allowInput,
        string hostName,
        int port,
        int maxHits,
        int timeoutMs,
        string? gameRoot,
        string? outputDir)
    {
        try
        {
            return DebugBattleAutoProbeRunCore(maxSteps, policy, runProbes, allowInput, hostName, port, maxHits, timeoutMs, gameRoot, outputDir);
        }
        catch (Exception ex) when (!runProbes && !allowInput)
        {
            return SafeBattleDryRunError("debug_battle_auto_probe_run", gameRoot, outputDir, ex);
        }
    }

    private object DebugBattleAutoProbeRunCore(
        int maxSteps,
        string policy,
        bool runProbes,
        bool allowInput,
        string hostName,
        int port,
        int maxHits,
        int timeoutMs,
        string? gameRoot,
        string? outputDir)
    {
        GuardLocalHost(hostName);
        var paths = ResolveBattleAutomationPaths(gameRoot, requireExpectedHash: runProbes || allowInput, allowMissingExe: !runProbes && !allowInput);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var plan = new InternalProbePlan
        {
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Profile = "battle_auto_v1",
            TargetExeSha256 = paths.ExeSha256,
            Targets = BattleAutoProbeTargets.ToList(),
            Safety = "Battle automation probe plan. Breakpoint changes only occur when run_probes=true; mouse input only occurs when allow_input=true."
        };
        var planPath = Path.Combine(sessionDir, "battle-auto-probe-plan.json");
        var planMarkdownPath = Path.Combine(sessionDir, "battle-auto-probe-plan.md");
        WriteJson(planPath, plan);
        WriteInternalProbePlanMarkdown(planMarkdownPath, plan, "battle-auto-probe-run");
        AppendActionChainLine(sessionDir, "debug_battle_auto_probe_run", "probe-plan", "created", new { plan_path = planPath, target_count = plan.Targets.Count });

        object? probeRun = null;
        if (runProbes)
        {
            probeRun = DebugInternalProbeRun(
                planPath,
                "battle_auto_v1",
                hostName,
                port,
                maxHits,
                timeoutMs,
                disableAfterHit: false,
                continueAfterEntryPointPause: false,
                gameRoot: gameRoot,
                outputDir: sessionDir);
            AppendActionChainLine(sessionDir, "debug_battle_auto_probe_run", "debug_internal_probe_run", ExtractStringFromObject(probeRun, "status"), new { result = probeRun });
        }
        else
        {
            AppendActionChainLine(sessionDir, "debug_battle_auto_probe_run", "debug_internal_probe_run", "skipped-plan-only", new { run_probes = runProbes });
        }

        object autoRun;
        var process = FindTargetProcess();
        if (process is null && !allowInput)
        {
            autoRun = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                session_dir = sessionDir,
                status = "dry-run-no-process",
                reason = "Ekd5.exe is not running; probe plan was written but battle auto steps require live battle memory.",
                policy = NormalizeBattlePolicy(policy),
                max_steps = Math.Clamp(maxSteps, 1, 100),
                safety = "Plan-only path; no mouse input, process-memory writes, debugger changes, or game-file writes."
            };
            WriteJson(Path.Combine(sessionDir, "battle-auto-run-summary.json"), autoRun);
        }
        else
        {
            autoRun = GameBattleAutoRun(maxSteps, true, policy, allowInput, gameRoot, DefaultGridOriginX, DefaultGridOriginY, DefaultGridCellWidth, DefaultGridCellHeight, 500, sessionDir);
        }
        AppendActionChainLine(sessionDir, "debug_battle_auto_probe_run", "game_battle_auto_run", ExtractStringFromObject(autoRun, "status"), new { result = autoRun });

        var summary = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            session_dir = sessionDir,
            status = runProbes ? $"probe-{ExtractStringFromObject(probeRun ?? new { status = "not-run" }, "status")}" : "plan-only",
            run_probes = runProbes,
            allow_input = allowInput,
            policy = NormalizeBattlePolicy(policy),
            plan_path = planPath,
            plan_markdown_path = planMarkdownPath,
            target_count = plan.Targets.Count,
            probe_run = probeRun,
            auto_run = autoRun,
            safety = "run_probes=false skips debugger breakpoint mutation; allow_input=false skips mouse input."
        };
        var summaryPath = Path.Combine(sessionDir, "battle-auto-probe-summary.json");
        var markdownPath = Path.Combine(sessionDir, "battle-auto-probe-summary.md");
        WriteJson(summaryPath, summary);
        WriteBattleAutoProbeSummaryMarkdown(markdownPath, summary);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattleAutoProbeRunSummary", path = summaryPath, summary.status });
        AppendActionChainLine(sessionDir, "debug_battle_auto_probe_run", "summary", ExtractStringFromObject(summary, "status"), new { summary_path = summaryPath });
        return summary;
    }

    private object ExecuteBattleAction(
        string toolName,
        string? gameRoot,
        bool allowInput,
        int originX,
        int originY,
        int cellWidth,
        int cellHeight,
        int settleMs,
        string? outputDir,
        Func<BattlefieldRuntimeSnapshot, BattleActionPlan> buildPlan)
    {
        var paths = ResolveBattleAutomationPaths(gameRoot, requireExpectedHash: allowInput, allowMissingExe: !allowInput);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var process = FindTargetProcess();
        if (process is null && !allowInput)
        {
            var dryNoProcess = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                session_dir = sessionDir,
                tool = toolName,
                status = "dry-run-no-process",
                reason = "Ekd5.exe is not running; live battle memory is required to generate a concrete action plan.",
                allow_input = allowInput,
                safety = "Dry-run only; no mouse input, screenshots, process-memory writes, debugger changes, or game-file writes."
            };
            var dryPath = Path.Combine(sessionDir, $"{toolName}-{Timestamp()}.json");
            WriteJson(dryPath, dryNoProcess);
            AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattleAction", path = dryPath, tool = toolName, dryNoProcess.status });
            AppendActionChainLine(sessionDir, toolName, "plan", "dry-run-no-process", new { path = dryPath });
            return dryNoProcess;
        }
        if (process is null)
        {
            throw new InvalidOperationException("Ekd5.exe is not running; cannot send battlefield input.");
        }
        var calibration = NormalizeGridCalibration(originX, originY, cellWidth, cellHeight);
        process.Refresh();
        if (process.MainWindowHandle == IntPtr.Zero)
        {
            if (allowInput)
            {
                throw new InvalidOperationException($"Ekd5.exe process {process.Id} does not have a main window yet; cannot send battlefield input.");
            }

            object? beforeObject;
            BattleActionPlan? planObject = null;
            try
            {
                var snapshot = ReadBattlefieldRuntimeSnapshot();
                beforeObject = snapshot;
                planObject = buildPlan(snapshot);
            }
            catch (Exception ex)
            {
                beforeObject = new { ok = false, error = ex.Message };
            }

            var noWindow = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                session_dir = sessionDir,
                tool = toolName,
                status = "dry-run-no-window",
                reason = "Ekd5.exe is running but has no main window; generated whatever read-only plan was possible without resolving click coordinates.",
                allow_input = allowInput,
                plan = planObject is null ? null : BattleActionPlanReport(planObject),
                before = beforeObject,
                safety = "Dry-run only; no mouse input, screenshots, process-memory writes, debugger changes, or game-file writes."
            };
            var noWindowPath = Path.Combine(sessionDir, $"{toolName}-{Timestamp()}.json");
            WriteJson(noWindowPath, noWindow);
            AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattleAction", path = noWindowPath, tool = toolName, noWindow.status });
            AppendActionChainLine(sessionDir, toolName, planObject?.Action ?? "plan", "dry-run-no-window", new { path = noWindowPath, plan = planObject is null ? null : BattleActionPlanReport(planObject) }, beforeObject, null);
            return noWindow;
        }

        var hwnd = process.MainWindowHandle;
        BattlefieldRuntimeSnapshot before;
        try
        {
            before = ReadBattlefieldRuntimeSnapshot();
        }
        catch (Exception ex) when (!allowInput)
        {
            var readFailed = new
            {
                created_at = DateTimeOffset.Now.ToString("O"),
                session_dir = sessionDir,
                tool = toolName,
                status = "dry-run-read-failed",
                reason = "Live battle memory could not be decoded; generated a structured dry-run failure instead of sending input.",
                error = ex.Message,
                allow_input = allowInput,
                safety = "Dry-run only; no mouse input, screenshots, process-memory writes, debugger changes, or game-file writes."
            };
            var readFailedPath = Path.Combine(sessionDir, $"{toolName}-{Timestamp()}.json");
            WriteJson(readFailedPath, readFailed);
            AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattleAction", path = readFailedPath, tool = toolName, readFailed.status });
            AppendActionChainLine(sessionDir, toolName, "plan", "dry-run-read-failed", new { path = readFailedPath, error = ex.Message });
            return readFailed;
        }
        var plan = buildPlan(before);
        var resolvedInputs = plan.Inputs
            .Select(p => ResolveBattleInputPoint(hwnd, p, calibration))
            .ToList();
        var inputGate = ValidateBattleActionInputGate(before, plan, resolvedInputs);
        var inputResults = new List<object>();
        var status = inputGate.Status;
        var reason = inputGate.Reason;

        if (allowInput && inputGate.CanSend)
        {
            foreach (var point in resolvedInputs)
            {
                var result = SendLeftClick(process, point.ScreenX, point.ScreenY);
                inputResults.Add(new { point = BattleInputPointReport(point), result });
                Thread.Sleep(120);
            }
            Thread.Sleep(Math.Clamp(settleMs, 0, 10000));
            status = "input-sent";
            reason = "all planned input points were sent";
        }
        else
        {
            inputResults.Add(new { sent = false, allow_input = allowInput, reason });
        }

        var after = TryReadBattlefieldRuntimeSnapshot();
        var changed = after is BattlefieldRuntimeSnapshot afterSnapshot && HasBattlefieldStateChanged(before, afterSnapshot);
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            session_dir = sessionDir,
            tool = toolName,
            status = allowInput ? status : $"dry-run-{status}",
            reason = allowInput ? reason : "allow_input=false; generated and validated the action plan without sending input",
            action = plan.Action,
            plan_status = plan.Status,
            plan_reason = plan.Reason,
            plan = BattleActionPlanReport(plan),
            allow_input = allowInput,
            calibration = new { origin_x = calibration.OriginX, origin_y = calibration.OriginY, cell_width = calibration.CellWidth, cell_height = calibration.CellHeight },
            input_gate = BattleInputGateReport(inputGate),
            planned_inputs = resolvedInputs.Select(BattleInputPointReport).ToList(),
            input_results = inputResults,
            before,
            after,
            memory_changed = changed,
            validation = new
            {
                expected_change = allowInput && plan.Action is not "select_unit",
                observed_change = changed,
                note = allowInput
                    ? "Use unit diff and combat context diff to classify success; dialogs/animations may delay visible state changes."
                    : "Dry-run intentionally does not change game state."
            },
            safety = allowInput
                ? "Controlled mouse input was sent only after phase, window, and target validation."
                : "Dry-run only; no mouse input, screenshots, process-memory writes, debugger changes, or game-file writes."
        };
        var path = Path.Combine(sessionDir, $"{toolName}-{Timestamp()}.json");
        WriteJson(path, report);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattleAction", path, tool = toolName, report.status });
        AppendActionChainLine(sessionDir, toolName, plan.Action, ExtractStringFromObject(report, "status"), new { path, plan = BattleActionPlanReport(plan), input_gate = BattleInputGateReport(inputGate) }, before, after);
        return report;
    }

    private object SafeBattleDryRunError(string toolName, string? gameRoot, string? outputDir, Exception ex)
    {
        var paths = ResolveBattleAutomationPaths(gameRoot, requireExpectedHash: false, allowMissingExe: true);
        var sessionDir = EnsureSessionDirectory(paths.WorkspaceRoot, outputDir);
        var report = new
        {
            created_at = DateTimeOffset.Now.ToString("O"),
            session_dir = sessionDir,
            tool = toolName,
            status = "dry-run-error",
            reason = "Dry-run path caught an exception and returned structured evidence instead of failing the MCP call.",
            error = new
            {
                type = ex.GetType().FullName,
                message = ex.Message,
                stack = ex.StackTrace
            },
            allow_input = false,
            safety = "Dry-run only; no mouse input, screenshots, process-memory writes, debugger changes, or game-file writes."
        };
        var path = Path.Combine(sessionDir, $"{toolName}-dry-run-error-{Timestamp()}.json");
        WriteJson(path, report);
        AppendJsonLine(Path.Combine(sessionDir, "events.jsonl"), new { type = "BattleDryRunError", path, tool = toolName, error = ex.Message });
        AppendActionChainLine(sessionDir, toolName, "dry-run-error", "dry-run-error", new { path, error = ex.Message });
        return report;
    }

    private GamePaths ResolveBattleAutomationPaths(string? gameRoot, bool requireExpectedHash, bool allowMissingExe)
    {
        try
        {
            return ResolveGamePaths(gameRoot, requireExpectedHash);
        }
        catch when (allowMissingExe)
        {
            var toolRoot = ResolveToolRoot();
            var workspaceRoot = Directory.GetParent(toolRoot)?.FullName ?? Directory.GetCurrentDirectory();
            var root = !string.IsNullOrWhiteSpace(gameRoot)
                ? Path.GetFullPath(gameRoot)
                : FirstExistingDirectory(
                    Environment.GetEnvironmentVariable("CCZGAME_DEBUG_GAME_ROOT"),
                    Environment.GetEnvironmentVariable("CCZMODSTUDIO_GAME_ROOT"),
                    Directory.GetCurrentDirectory()) ?? workspaceRoot;
            return new GamePaths(
                workspaceRoot,
                toolRoot,
                root,
                Path.Combine(root, "Ekd5.exe"),
                string.Empty,
                IsExpectedSha256: false);
        }
    }

    private static string? FirstExistingDirectory(params string?[] values)
        => values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Path.GetFullPath(v!))
            .FirstOrDefault(Directory.Exists);

    private BattlefieldRuntimeSnapshot ReadBattlefieldRuntimeSnapshot()
    {
        var battle = ReadBattleStateSnapshot(160);
        var combat = ReadCombatContextSnapshot(battle);
        var controllable = battle.Units
            .Where(IsUnitControllable)
            .OrderBy(u => u.UnitIndex)
            .ToList();
        var occupied = battle.Units
            .Select(u => new BattleOccupiedCell { X = u.X, Y = u.Y, UnitIndex = u.UnitIndex, Side = u.Side, HP = u.HP })
            .OrderBy(c => c.Y)
            .ThenBy(c => c.X)
            .ThenBy(c => c.UnitIndex)
            .ToList();
        var reasons = new List<string>();
        var phase = ClassifyBattlefieldPhase(battle, combat, controllable, reasons, out var confidence);
        return new BattlefieldRuntimeSnapshot
        {
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            Phase = phase,
            Units = battle.Units,
            ControllableUnits = controllable,
            OccupiedCells = occupied,
            LastCombatContext = combat,
            Confidence = confidence,
            Reasons = reasons
        };
    }

    private object TryReadBattlefieldRuntimeSnapshot()
    {
        try
        {
            return ReadBattlefieldRuntimeSnapshot();
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private BattleCombatContextSnapshot ReadCombatContextSnapshot(BattleStateSnapshot battle)
    {
        try
        {
            var bytes = ReadProcessMemory(battle.ProcessId, CombatContextAddress, 0x620);
            int? attackerIndex = ReadByteNullable(bytes, 0x00);
            int? targetIndex = ReadByteNullable(bytes, 0x01);
            var attackerUnit = attackerIndex is >= 0 ? battle.Units.FirstOrDefault(u => u.UnitIndex == attackerIndex) : null;
            var targetUnit = targetIndex is >= 0 ? battle.Units.FirstOrDefault(u => u.UnitIndex == targetIndex) : null;
            return new BattleCombatContextSnapshot
            {
                BytesRead = bytes.Length,
                AttackerUnitIndex = attackerIndex,
                TargetUnitIndex = targetIndex,
                AttackerDataId = ReadInt32Nullable(bytes, 0x04),
                AttackerRuntimeCharacterPtr = FormatNullableAddress(ReadUInt32Nullable(bytes, 0x08)),
                AttackerUnitPtr = FormatNullableAddress(ReadUInt32Nullable(bytes, 0x0C)),
                AttackerSide = attackerUnit?.Side,
                TargetSide = targetUnit?.Side,
                PostDamageValue = ReadInt32Nullable(bytes, 0x84),
                PersonalExpCache = ReadInt32Nullable(bytes, 0x428),
                WeaponExpCache = ReadInt32Nullable(bytes, 0x42C),
                ArmorExpCache = ReadInt32Nullable(bytes, 0x430),
                CriticalFlag = ReadInt32Nullable(bytes, 0x604),
                DoubleAttackCounter = ReadByteNullable(bytes, 0x608),
                CounterFlag = ReadInt32Nullable(bytes, 0x614),
                RawHeaderHex = ToHex(bytes.Take(Math.Min(bytes.Length, 0x40)))
            };
        }
        catch (Exception ex)
        {
            return new BattleCombatContextSnapshot
            {
                Error = ex.Message
            };
        }
    }

    private static string ClassifyBattlefieldPhase(
        BattleStateSnapshot battle,
        BattleCombatContextSnapshot combat,
        List<BattleUnitRow> controllable,
        List<string> reasons,
        out double confidence)
    {
        if (battle.Units.Count == 0)
        {
            confidence = 0.9;
            reasons.Add("no active tactical units decoded from unit array");
            return "not_battle";
        }

        if (controllable.Count > 0)
        {
            confidence = 0.72;
            reasons.Add("side=0 alive units with action bit not completed were decoded");
            reasons.Add("action completion heuristic: (unit+0D & 02) == 0; pending cross-battle validation");
            return "player_control";
        }

        if (combat.AttackerSide == 1)
        {
            confidence = 0.48;
            reasons.Add("no side=0 controllable units; last combat context attacker side is friendly/ally side=1");
            return "ally_auto";
        }

        if (combat.AttackerSide == 2)
        {
            confidence = 0.48;
            reasons.Add("no side=0 controllable units; last combat context attacker side is enemy side=2");
            return "enemy_auto";
        }

        if (battle.Units.Any(u => u.Side == 0 && u.HP > 0))
        {
            confidence = 0.42;
            reasons.Add("side=0 units exist, but all appear action-completed or blocked by dialog/turn prompt");
            return "turn_end_prompt";
        }

        confidence = 0.25;
        reasons.Add("battle units exist, but phase cannot be inferred from unit/action/combat-context fields");
        return "unknown";
    }

    private BattleActionPlan BuildSelectUnitPlan(BattlefieldRuntimeSnapshot before, int unitIndex)
    {
        var unit = before.Units.FirstOrDefault(u => u.UnitIndex == unitIndex);
        if (unit is null)
        {
            return BlockedPlan("select_unit", $"unit_index {unitIndex} was not found");
        }
        if (!IsPhaseInputCandidate(before.Phase))
        {
            return BlockedPlan("select_unit", $"phase {before.Phase} does not allow safe player input");
        }
        if (!IsUnitControllable(unit))
        {
            return BlockedPlan("select_unit", $"unit_index {unitIndex} is not a controllable side=0 active unit by v1 action heuristic");
        }

        return new BattleActionPlan(
            "select_unit",
            "ready",
            "click side=0 controllable unit grid cell",
            [GridInput("unit", unit.X, unit.Y)]);
    }

    private BattleActionPlan BuildMoveUnitPlan(BattlefieldRuntimeSnapshot before, int unitIndex, int toX, int toY)
    {
        var select = BuildSelectUnitPlan(before, unitIndex);
        if (select.Status == "blocked")
        {
            return select with { Action = "move_unit" };
        }
        var unit = before.Units.First(u => u.UnitIndex == unitIndex);
        var occupant = FindOccupant(before, toX, toY);
        if (occupant is not null)
        {
            return BlockedPlan("move_unit", $"destination ({toX},{toY}) is occupied by unit_index {occupant.UnitIndex}");
        }
        var distance = Manhattan(unit.X, unit.Y, toX, toY);
        if (distance > 6)
        {
            return BlockedPlan("move_unit", $"destination distance {distance} exceeds conservative v1 movement cap 6; range_unknown");
        }

        return new BattleActionPlan(
            "move_unit",
            "ready",
            "select unit, click destination, then click wait to finish movement in v1 external-input path",
            [GridInput("unit", unit.X, unit.Y), GridInput("move_destination", toX, toY), UiInput("wait")]);
    }

    private BattleActionPlan BuildAttackPlan(BattlefieldRuntimeSnapshot before, int attackerIndex, int targetIndex, bool moveFirst)
    {
        var attacker = before.Units.FirstOrDefault(u => u.UnitIndex == attackerIndex);
        var target = before.Units.FirstOrDefault(u => u.UnitIndex == targetIndex);
        if (attacker is null)
        {
            return BlockedPlan("attack", $"attacker_index {attackerIndex} was not found");
        }
        if (target is null)
        {
            return BlockedPlan("attack", $"target_index {targetIndex} was not found");
        }
        if (!IsPhaseInputCandidate(before.Phase))
        {
            return BlockedPlan("attack", $"phase {before.Phase} does not allow safe player input");
        }
        if (!IsUnitControllable(attacker))
        {
            return BlockedPlan("attack", $"attacker_index {attackerIndex} is not controllable by v1 action heuristic");
        }
        if (target.Side is 0 or 1)
        {
            return BlockedPlan("attack", $"target_index {targetIndex} is not an enemy side unit");
        }

        var distance = Manhattan(attacker.X, attacker.Y, target.X, target.Y);
        if (distance <= 1)
        {
            return new BattleActionPlan(
                "attack",
                "ready",
                "adjacent target; v1 uses conservative melee attack route",
                [GridInput("attacker", attacker.X, attacker.Y), UiInput("attack"), GridInput("target", target.X, target.Y)]);
        }

        if (!moveFirst)
        {
            return BlockedPlan("attack", $"target distance {distance} exceeds conservative v1 attack range; range_unknown");
        }

        var moveCell = FindAdjacentAttackCell(before, attacker, target);
        if (moveCell is null)
        {
            return BlockedPlan("attack", $"target distance {distance}; no conservative empty adjacent attack cell found; range_unknown");
        }

        return BlockedPlan(
            "attack",
            $"target distance {distance}; candidate move cell ({moveCell.Value.X},{moveCell.Value.Y}) exists but v1 refuses move-then-attack without validated move range/route ABI; range_unknown");
    }

    private BattleActionPlan BuildWaitPlan(BattlefieldRuntimeSnapshot before, int unitIndex)
    {
        var unit = before.Units.FirstOrDefault(u => u.UnitIndex == unitIndex);
        if (unit is null)
        {
            return BlockedPlan("wait", $"unit_index {unitIndex} was not found");
        }
        if (!IsPhaseInputCandidate(before.Phase))
        {
            return BlockedPlan("wait", $"phase {before.Phase} does not allow safe player input");
        }
        if (!IsUnitControllable(unit))
        {
            return BlockedPlan("wait", $"unit_index {unitIndex} is not controllable by v1 action heuristic");
        }

        return new BattleActionPlan(
            "wait",
            "ready",
            "select unit and click wait command",
            [GridInput("unit", unit.X, unit.Y), UiInput("wait")]);
    }

    private BattleActionPlan BuildEndTurnPlan(BattlefieldRuntimeSnapshot before)
    {
        if (before.Phase is "ally_auto" or "enemy_auto" or "animation" or "dialog")
        {
            return BlockedPlan("end_turn", $"phase {before.Phase} is automatic/non-control; v1 will not send end-turn input");
        }

        return new BattleActionPlan(
            "end_turn",
            "ready",
            "click registered end_turn UI area; confirmation dialogs must be handled by caller with keyboard/input gate",
            [UiInput("end_turn")]);
    }

    private BattleActionPlan BuildAutoStepPlan(BattlefieldRuntimeSnapshot before, string policy)
    {
        var normalized = NormalizeBattlePolicy(policy);
        if (normalized != "safe_attack")
        {
            return BlockedPlan("auto_step", $"unsupported policy '{policy}'");
        }
        if (!IsPhaseInputCandidate(before.Phase))
        {
            return BlockedPlan("auto_step", $"phase {before.Phase} does not allow safe player input");
        }
        var actor = before.ControllableUnits.OrderBy(u => u.UnitIndex).FirstOrDefault();
        if (actor is null)
        {
            return BlockedPlan("auto_step", "no controllable side=0 units");
        }

        var adjacentTargets = before.Units
            .Where(u => u.Side == 2 && u.HP > 0 && Manhattan(actor.X, actor.Y, u.X, u.Y) <= 1)
            .OrderBy(u => u.HP)
            .ThenBy(u => u.UnitIndex)
            .ToList();
        var target = adjacentTargets.FirstOrDefault();
        if (target is not null)
        {
            return BuildAttackPlan(before, actor.UnitIndex, target.UnitIndex, moveFirst: false);
        }

        var enemies = before.Units.Where(u => u.Side == 2 && u.HP > 0).OrderBy(u => Manhattan(actor.X, actor.Y, u.X, u.Y)).ToList();
        if (enemies.Count > 0)
        {
            var nearest = enemies[0];
            return BuildWaitPlan(before, actor.UnitIndex) with
            {
                Reason = $"no adjacent enemy for safe_attack; nearest enemy unit_index {nearest.UnitIndex} distance {Manhattan(actor.X, actor.Y, nearest.X, nearest.Y)}; range_unknown so wait"
            };
        }

        return BuildWaitPlan(before, actor.UnitIndex) with { Reason = "no alive side=2 enemies found; wait" };
    }

    private (bool CanSend, string Status, string Reason) ValidateBattleActionInputGate(BattlefieldRuntimeSnapshot before, BattleActionPlan plan, List<BattleInputPoint> inputs)
    {
        if (plan.Status == "blocked")
        {
            return (false, "blocked", plan.Reason);
        }
        if (!IsPhaseInputCandidate(before.Phase) && plan.Action != "end_turn")
        {
            return (false, "blocked", $"phase {before.Phase} does not allow safe player input");
        }
        var outside = inputs.FirstOrDefault(p => !p.WithinClient);
        if (outside is not null)
        {
            return (false, "blocked", $"planned input '{outside.Label}' is outside the current client area");
        }
        return (true, "ready", "input gate passed");
    }

    private BattleInputPoint ResolveBattleInputPoint(IntPtr hwnd, BattleInputPoint point, BattleGridCalibration calibration)
    {
        if (point.Kind.Equals("grid", StringComparison.OrdinalIgnoreCase) && point.GridX.HasValue && point.GridY.HasValue)
        {
            var client = GridToClientPoint(hwnd, point.GridX.Value, point.GridY.Value, calibration);
            var screen = ClientToScreen(hwnd, client.ClientX, client.ClientY);
            return point with
            {
                ClientX = client.ClientX,
                ClientY = client.ClientY,
                ScreenX = screen.X,
                ScreenY = screen.Y,
                WithinClient = client.WithinClient
            };
        }

        if (point.Kind.Equals("ui", StringComparison.OrdinalIgnoreCase) && UiAreas.TryGetValue(point.Label, out var area))
        {
            var target = ResolveUiClickTarget(hwnd, area);
            var screen = ClientToScreen(hwnd, target.ClientX, target.ClientY);
            return point with
            {
                ClientX = target.ClientX,
                ClientY = target.ClientY,
                ScreenX = screen.X,
                ScreenY = screen.Y,
                WithinClient = target.IsWithinClient
            };
        }

        return point with { WithinClient = false };
    }

    private static BattleActionPlan BlockedPlan(string action, string reason)
        => new(action, "blocked", reason, []);

    private static object BattleActionPlanReport(BattleActionPlan plan)
        => new
        {
            action = plan.Action,
            status = plan.Status,
            reason = plan.Reason,
            inputs = plan.Inputs.Select(BattleInputPointReport).ToList()
        };

    private static object BattleInputPointReport(BattleInputPoint point)
        => new
        {
            kind = point.Kind,
            label = point.Label,
            grid = point.GridX.HasValue && point.GridY.HasValue ? new { x = point.GridX.Value, y = point.GridY.Value } : null,
            client_point = new { x = point.ClientX, y = point.ClientY },
            screen_point = new { x = point.ScreenX, y = point.ScreenY },
            within_client = point.WithinClient
        };

    private static object BattleInputGateReport((bool CanSend, string Status, string Reason) gate)
        => new
        {
            can_send = gate.CanSend,
            status = gate.Status,
            reason = gate.Reason
        };

    private static object BattleClientPointReport(BattleClientPoint point)
        => new
        {
            client_point = new { x = point.ClientX, y = point.ClientY },
            current_client = new { width = point.ClientWidth, height = point.ClientHeight },
            within_client = point.WithinClient
        };

    private static BattleInputPoint GridInput(string label, int x, int y)
        => new("grid", label, x, y, 0, 0, 0, 0, false);

    private static BattleInputPoint UiInput(string label)
        => new("ui", label, null, null, 0, 0, 0, 0, false);

    private static bool IsPhaseInputCandidate(string phase)
        => phase is "player_control" or "unit_selected" or "command_menu" or "target_select";

    private static bool IsUnitControllable(BattleUnitRow unit)
        => unit.Side == 0 && unit.HP > 0 && (unit.Action & 0x02) == 0;

    private static BattleOccupiedCell? FindOccupant(BattlefieldRuntimeSnapshot snapshot, int x, int y)
        => snapshot.OccupiedCells.FirstOrDefault(c => c.X == x && c.Y == y && c.HP > 0);

    private static int Manhattan(int x1, int y1, int x2, int y2)
        => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);

    private static (int X, int Y)? FindAdjacentAttackCell(BattlefieldRuntimeSnapshot snapshot, BattleUnitRow attacker, BattleUnitRow target)
    {
        var candidates = new[]
        {
            (X: target.X, Y: target.Y - 1),
            (X: target.X - 1, Y: target.Y),
            (X: target.X + 1, Y: target.Y),
            (X: target.X, Y: target.Y + 1)
        };
        return candidates
            .Where(c => c.X >= 0 && c.Y >= 0 && c.X < 80 && c.Y < 80)
            .Where(c => FindOccupant(snapshot, c.X, c.Y) is null)
            .OrderBy(c => Manhattan(attacker.X, attacker.Y, c.X, c.Y))
            .Select(c => ((int X, int Y)?)c)
            .FirstOrDefault();
    }

    private static string NormalizeBattlePolicy(string policy)
        => string.IsNullOrWhiteSpace(policy) ? "safe_attack" : policy.Trim().ToLowerInvariant().Replace('-', '_');

    private static BattleGridCalibration NormalizeGridCalibration(int originX, int originY, int cellWidth, int cellHeight)
        => new(originX, originY, Math.Max(1, cellWidth), Math.Max(1, cellHeight));

    private static BattleClientPoint GridToClientPoint(IntPtr hwnd, int gridX, int gridY, BattleGridCalibration calibration)
    {
        if (!NativeMethods.GetClientRect(hwnd, out var client))
        {
            ThrowLastWin32("GetClientRect failed.");
        }
        var clientX = calibration.OriginX + gridX * calibration.CellWidth + calibration.CellWidth / 2;
        var clientY = calibration.OriginY + gridY * calibration.CellHeight + calibration.CellHeight / 2;
        var within = clientX >= 0 && clientX < client.Width && clientY >= 0 && clientY < client.Height;
        return new BattleClientPoint(clientX, clientY, client.Width, client.Height, within);
    }

    private static (int Width, int Height) GetClientSize(IntPtr hwnd)
    {
        if (!NativeMethods.GetClientRect(hwnd, out var client))
        {
            ThrowLastWin32("GetClientRect failed.");
        }
        return (client.Width, client.Height);
    }

    private static List<object> ParseKnownGridPoints(string knownPoints)
    {
        var list = new List<object>();
        if (string.IsNullOrWhiteSpace(knownPoints))
        {
            return list;
        }

        foreach (var token in knownPoints.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = token.Split([',', ':', '='], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 4)
            {
                continue;
            }
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gridX) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gridY) &&
                int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var clientX) &&
                int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var clientY))
            {
                list.Add(new { grid_x = gridX, grid_y = gridY, client_x = clientX, client_y = clientY });
            }
        }

        return list;
    }

    private static BattleGridCalibration InferGridCalibration(List<object> knownPoints, BattleGridCalibration fallback)
    {
        if (knownPoints.Count < 2)
        {
            return fallback;
        }

        var parsed = knownPoints
            .Select(p =>
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(p, JsonOptions));
                var root = doc.RootElement;
                return new
                {
                    GridX = root.GetProperty("grid_x").GetInt32(),
                    GridY = root.GetProperty("grid_y").GetInt32(),
                    ClientX = root.GetProperty("client_x").GetInt32(),
                    ClientY = root.GetProperty("client_y").GetInt32()
                };
            })
            .ToList();

        var xPairs = parsed.SelectMany(a => parsed.Where(b => b.GridX != a.GridX).Select(b => (A: a, B: b))).ToList();
        var yPairs = parsed.SelectMany(a => parsed.Where(b => b.GridY != a.GridY).Select(b => (A: a, B: b))).ToList();
        var cellWidth = xPairs.Count > 0
            ? (int)Math.Round(xPairs.Average(p => Math.Abs((p.B.ClientX - p.A.ClientX) / (double)(p.B.GridX - p.A.GridX))), MidpointRounding.AwayFromZero)
            : fallback.CellWidth;
        var cellHeight = yPairs.Count > 0
            ? (int)Math.Round(yPairs.Average(p => Math.Abs((p.B.ClientY - p.A.ClientY) / (double)(p.B.GridY - p.A.GridY))), MidpointRounding.AwayFromZero)
            : fallback.CellHeight;
        cellWidth = Math.Max(1, cellWidth);
        cellHeight = Math.Max(1, cellHeight);
        var originX = (int)Math.Round(parsed.Average(p => p.ClientX - p.GridX * cellWidth - cellWidth / 2.0), MidpointRounding.AwayFromZero);
        var originY = (int)Math.Round(parsed.Average(p => p.ClientY - p.GridY * cellHeight - cellHeight / 2.0), MidpointRounding.AwayFromZero);
        return new BattleGridCalibration(originX, originY, cellWidth, cellHeight);
    }

    private static bool HasBattlefieldStateChanged(BattlefieldRuntimeSnapshot before, BattlefieldRuntimeSnapshot after)
        => before.Phase != after.Phase ||
           before.Units.Count != after.Units.Count ||
           before.Units.Zip(after.Units).Any(pair =>
               pair.First.UnitIndex != pair.Second.UnitIndex ||
               pair.First.X != pair.Second.X ||
               pair.First.Y != pair.Second.Y ||
               pair.First.Action != pair.Second.Action ||
               pair.First.HP != pair.Second.HP ||
               pair.First.MP != pair.Second.MP) ||
           before.LastCombatContext.PostDamageValue != after.LastCombatContext.PostDamageValue ||
           before.LastCombatContext.DoubleAttackCounter != after.LastCombatContext.DoubleAttackCounter ||
           before.LastCombatContext.CounterFlag != after.LastCombatContext.CounterFlag;

    private static int? ReadByteNullable(byte[] bytes, int offset)
        => offset >= 0 && offset < bytes.Length ? bytes[offset] : null;

    private static int? ReadInt32Nullable(byte[] bytes, int offset)
        => offset >= 0 && offset + 3 < bytes.Length ? BitConverter.ToInt32(bytes, offset) : null;

    private static uint? ReadUInt32Nullable(byte[] bytes, int offset)
        => offset >= 0 && offset + 3 < bytes.Length ? BitConverter.ToUInt32(bytes, offset) : null;

    private static string FormatNullableAddress(uint? value)
        => value.HasValue ? value.Value.ToString("X8", CultureInfo.InvariantCulture) : string.Empty;

    private static string ToHex(IEnumerable<byte> bytes)
        => string.Join(" ", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    private static void WriteBattleAutoProbeSummaryMarkdown(string path, object summary)
    {
        var status = ExtractStringFromObject(summary, "status");
        var lines = new List<string>
        {
            "# Battle Auto Probe Summary",
            "",
            $"- Updated: {DateTimeOffset.Now:O}",
            $"- Status: `{status}`",
            "",
            "This report is a v1 automation/probe summary. `run_probes=false` means no debugger breakpoint mutation; `allow_input=false` means no mouse input.",
            "",
            "Key outputs:",
            "- `battle-auto-probe-plan.json` / `.md`",
            "- `battle-auto-probe-summary.json`",
            "- `action-chain.jsonl`",
            "- `events.jsonl`"
        };
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }
}
