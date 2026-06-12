using System.Text.Json.Serialization;

namespace CCZModStudio.GameDebugMcpServer;

public sealed record GamePaths(
    string WorkspaceRoot,
    string ToolRoot,
    string GameRoot,
    string ExePath,
    string ExeSha256,
    bool IsExpectedSha256);

public sealed record X32dbgCallResult(
    bool Ok,
    string Method,
    string Path,
    object? Query,
    object? Body,
    object? Data,
    string Error);

public sealed class BattleUnitRow
{
    [JsonPropertyName("unit_index")]
    public int UnitIndex { get; init; }

    [JsonPropertyName("data_id_byte")]
    public int DataIdByte { get; init; }

    [JsonPropertyName("side")]
    public int Side { get; init; }

    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("action")]
    public int Action { get; init; }

    [JsonPropertyName("hp")]
    public int HP { get; init; }

    [JsonPropertyName("mp")]
    public int MP { get; init; }

    [JsonPropertyName("attrs_hex")]
    public string AttrsHex { get; init; } = string.Empty;
}

public sealed class BattleStateSnapshot
{
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("process_id")]
    public int ProcessId { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = "ReadProcessMemory";

    [JsonPropertyName("unit_array_address")]
    public string UnitArrayAddress { get; init; } = "0x004A7B20";

    [JsonPropertyName("unit_stride")]
    public string UnitStride { get; init; } = "0x30";

    [JsonPropertyName("active_unit_count")]
    public int ActiveUnitCount { get; init; }

    [JsonPropertyName("units")]
    public List<BattleUnitRow> Units { get; init; } = [];

    [JsonPropertyName("side_counts")]
    public Dictionary<string, int> SideCounts { get; init; } = new(StringComparer.Ordinal);
}

public sealed class BattleOccupiedCell
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("unit_index")]
    public int UnitIndex { get; init; }

    [JsonPropertyName("side")]
    public int Side { get; init; }

    [JsonPropertyName("hp")]
    public int HP { get; init; }
}

public sealed class BattleCombatContextSnapshot
{
    [JsonPropertyName("base_address")]
    public string BaseAddress { get; init; } = "0x004927F0";

    [JsonPropertyName("bytes_read")]
    public int BytesRead { get; init; }

    [JsonPropertyName("attacker_unit_index")]
    public int? AttackerUnitIndex { get; init; }

    [JsonPropertyName("target_unit_index")]
    public int? TargetUnitIndex { get; init; }

    [JsonPropertyName("attacker_data_id")]
    public int? AttackerDataId { get; init; }

    [JsonPropertyName("attacker_unit_ptr")]
    public string AttackerUnitPtr { get; init; } = string.Empty;

    [JsonPropertyName("attacker_side")]
    public int? AttackerSide { get; init; }

    [JsonPropertyName("target_side")]
    public int? TargetSide { get; init; }

    [JsonPropertyName("post_damage_value")]
    public int? PostDamageValue { get; init; }

    [JsonPropertyName("personal_exp_cache")]
    public int? PersonalExpCache { get; init; }

    [JsonPropertyName("weapon_exp_cache")]
    public int? WeaponExpCache { get; init; }

    [JsonPropertyName("armor_exp_cache")]
    public int? ArmorExpCache { get; init; }

    [JsonPropertyName("critical_flag")]
    public int? CriticalFlag { get; init; }

    [JsonPropertyName("double_attack_counter")]
    public int? DoubleAttackCounter { get; init; }

    [JsonPropertyName("counter_flag")]
    public int? CounterFlag { get; init; }

    [JsonPropertyName("raw_header_hex")]
    public string RawHeaderHex { get; init; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}

public sealed class BattlefieldRuntimeSnapshot
{
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("phase")]
    public string Phase { get; init; } = "unknown";

    [JsonPropertyName("units")]
    public List<BattleUnitRow> Units { get; init; } = [];

    [JsonPropertyName("controllable_units")]
    public List<BattleUnitRow> ControllableUnits { get; init; } = [];

    [JsonPropertyName("occupied_cells")]
    public List<BattleOccupiedCell> OccupiedCells { get; init; } = [];

    [JsonPropertyName("last_combat_context")]
    public BattleCombatContextSnapshot LastCombatContext { get; init; } = new();

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("reasons")]
    public List<string> Reasons { get; init; } = [];
}

public sealed class UiArea
{
    public string Key { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int BaseClientWidth { get; init; }
    public int BaseClientHeight { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class InternalProbeTarget
{
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("phase")]
    public string Phase { get; init; } = string.Empty;

    [JsonPropertyName("expected_semantics")]
    public string ExpectedSemantics { get; init; } = string.Empty;

    [JsonPropertyName("trigger_hint")]
    public string TriggerHint { get; init; } = string.Empty;

    [JsonPropertyName("evidence_level")]
    public string EvidenceLevel { get; init; } = "pending-breakpoint";

    [JsonPropertyName("high_frequency")]
    public bool HighFrequency { get; init; }
}

public sealed class InternalProbePlan
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("profile")]
    public string Profile { get; init; } = "all";

    [JsonPropertyName("target_exe_sha256")]
    public string TargetExeSha256 { get; init; } = string.Empty;

    [JsonPropertyName("targets")]
    public List<InternalProbeTarget> Targets { get; init; } = [];

    [JsonPropertyName("safety")]
    public string Safety { get; init; } = "Debugger breakpoints and read-only evidence only; no mouse input, screenshots, game-file writes, or process-memory writes.";
}

public sealed class FunctionCatalogEntry
{
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("expected_semantics")]
    public string ExpectedSemantics { get; init; } = string.Empty;

    [JsonPropertyName("trigger_hint")]
    public string TriggerHint { get; init; } = string.Empty;

    [JsonPropertyName("evidence_level")]
    public string EvidenceLevel { get; init; } = "pending-breakpoint";

    [JsonPropertyName("source")]
    public string Source { get; init; } = "local-knowledge-base";

    [JsonPropertyName("high_frequency")]
    public bool HighFrequency { get; init; }
}

public sealed class AutonomyPlanStep
{
    [JsonPropertyName("step")]
    public string Step { get; init; } = string.Empty;

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = string.Empty;

    [JsonPropertyName("tool")]
    public string Tool { get; init; } = string.Empty;

    [JsonPropertyName("purpose")]
    public string Purpose { get; init; } = string.Empty;

    [JsonPropertyName("requires_bridge")]
    public bool RequiresBridge { get; init; }

    [JsonPropertyName("writes_input_or_memory")]
    public bool WritesInputOrMemory { get; init; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, string> Arguments { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AutonomyPlan
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = "yingchuan_cao_attack_zhangliang";

    [JsonPropertyName("target_exe_sha256")]
    public string TargetExeSha256 { get; init; } = string.Empty;

    [JsonPropertyName("stages")]
    public List<string> Stages { get; init; } = [];

    [JsonPropertyName("steps")]
    public List<AutonomyPlanStep> Steps { get; init; } = [];

    [JsonPropertyName("safety")]
    public string Safety { get; init; } = "Internal/debugger/read-only automation plan only; no mouse input, screenshots, process-memory writes, or game-file writes.";
}

public sealed class RuntimeInvokePlan
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = string.Empty;

    [JsonPropertyName("route")]
    public string Route { get; init; } = string.Empty;

    [JsonPropertyName("target_exe_sha256")]
    public string TargetExeSha256 { get; init; } = string.Empty;

    [JsonPropertyName("actions")]
    public List<RuntimeInvokeAction> Actions { get; init; } = [];

    [JsonPropertyName("safety")]
    public string Safety { get; init; } = "Plan only; no process-memory writes, game-file writes, mouse input, or screenshots.";
}

public sealed class RuntimeInvokeAction
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("intent")]
    public string Intent { get; init; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; init; } = string.Empty;

    [JsonPropertyName("candidate_address")]
    public string CandidateAddress { get; init; } = string.Empty;

    [JsonPropertyName("invoke_strategy")]
    public string InvokeStrategy { get; init; } = "breakpoint_probe";

    [JsonPropertyName("calling_convention")]
    public string CallingConvention { get; init; } = "unknown";

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("register_setup")]
    public Dictionary<string, string> RegisterSetup { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("stack_arguments")]
    public List<string> StackArguments { get; init; } = [];

    [JsonPropertyName("breakpoints")]
    public List<string> Breakpoints { get; init; } = [];

    [JsonPropertyName("verification")]
    public string Verification { get; init; } = string.Empty;

    [JsonPropertyName("writes_process_memory")]
    public bool WritesProcessMemory { get; init; }

    [JsonPropertyName("requires_runtime_injection")]
    public bool RequiresRuntimeInjection { get; init; }

    [JsonPropertyName("requires_paused_debuggee")]
    public bool RequiresPausedDebuggee { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "plan-only";

    [JsonPropertyName("evidence_gate")]
    public string EvidenceGate { get; init; } = string.Empty;

    [JsonPropertyName("candidate_source")]
    public string CandidateSource { get; init; } = string.Empty;

    [JsonPropertyName("candidate_confidence")]
    public string CandidateConfidence { get; init; } = string.Empty;
}

public sealed class RuntimeStateSnapshot
{
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("classification")]
    public string Classification { get; init; } = "unknown";

    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = "low";

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("target_process")]
    public object? TargetProcess { get; init; }

    [JsonPropertyName("x32dbg_state")]
    public object? X32dbgState { get; init; }

    [JsonPropertyName("battle_summary")]
    public object? BattleSummary { get; init; }

    [JsonPropertyName("recommended_next_stages")]
    public List<string> RecommendedNextStages { get; init; } = [];

    [JsonPropertyName("safety")]
    public string Safety { get; init; } = "Read-only classification only; no mouse input, screenshots, process-memory writes, or game-file writes.";
}
