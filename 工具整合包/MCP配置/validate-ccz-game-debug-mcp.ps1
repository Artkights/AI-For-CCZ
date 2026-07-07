param(
    [string]$GameRoot = "",
    [int]$TimeoutMs = 60000,
    [int]$MinimumToolCount = 65
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot
$expectedEkd5Sha256 = "84E3A1DC085AE6F9900D1E8C388A9CD6766379832DDF51BC7BDF780C6615B4A3"
$startScript = Join-Path $configRoot "start-ccz-game-debug-mcp.ps1"

if (-not (Test-Path -LiteralPath $startScript -PathType Leaf)) {
    throw "Game debug MCP start script not found: $startScript"
}

function Test-ExpectedGameDebugRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return $false
    }

    $exe = Join-Path $Path "Ekd5.exe"
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        return $false
    }

    foreach ($required in @("Data.e5", "Imsg.e5", "Star.e5", "RS")) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $required))) {
            return $false
        }
    }

    try {
        return (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -eq $ExpectedSha256
    }
    catch {
        return $false
    }
}

function Find-ExpectedGameDebugRoot {
    param([Parameter(Mandatory = $true)][string]$WorkspaceRoot)

    $skipParts = @(
        "\.git\",
        "\.vs\",
        "\bin\",
        "\obj\",
        "\_BuildCheck\",
        "\CCZModStudio_Reports\",
        "\CCZModStudio_Exports\",
        "\_CCZModStudio_Backups\"
    )

    $candidates = foreach ($exe in Get-ChildItem -LiteralPath $WorkspaceRoot -Recurse -File -Filter "Ekd5.exe" -ErrorAction SilentlyContinue) {
        $fullName = $exe.FullName
        if (($skipParts | Where-Object { $fullName.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -gt 0) {
            continue
        }

        try {
            if ((Get-FileHash -LiteralPath $fullName -Algorithm SHA256).Hash -ne $expectedEkd5Sha256) {
                continue
            }
        }
        catch {
            continue
        }

        $root = $exe.DirectoryName
        if (-not (Test-ExpectedGameDebugRoot -Path $root -ExpectedSha256 $expectedEkd5Sha256)) {
            continue
        }

        $leaf = Split-Path -Leaf $root
        [pscustomobject]@{
            Root = $root
            PreferredName = if ($leaf -like "*加强版6.5未加密版*") { 0 } else { 1 }
            InTestCopy = if ($root.IndexOf("\CCZModStudio_TestCopies\", [StringComparison]::OrdinalIgnoreCase) -ge 0) { 1 } else { 0 }
            PathLength = $root.Length
            LastWriteTime = $exe.LastWriteTimeUtc
        }
    }

    $selected = $candidates |
        Sort-Object PreferredName, InTestCopy, PathLength, Root |
        Select-Object -First 1

    if (-not $selected) {
        throw "Could not find an Ekd5.exe with expected SHA256 $expectedEkd5Sha256 under $WorkspaceRoot. Pass -GameRoot explicitly."
    }

    return $selected.Root
}

function Read-LineWithTimeout {
    param(
        [Parameter(Mandatory = $true)] [System.IO.StreamReader]$Reader,
        [Parameter(Mandatory = $true)] [int]$Timeout,
        [string]$Label = "MCP response"
    )

    $task = $Reader.ReadLineAsync()
    if (-not $task.Wait($Timeout)) {
        throw "Timed out waiting for $Label."
    }

    return $task.Result
}

function Assert-McpResponseSucceeded {
    param(
        [Parameter(Mandatory = $true)]$Response,
        [Parameter(Mandatory = $true)][string]$ToolName
    )

    if ($Response.error) {
        throw "MCP $ToolName failed: $($Response.error.message)"
    }

    if ($Response.result -and $Response.result.isError -eq $true) {
        $text = ""
        $content = @($Response.result.content)
        if ($content.Count -gt 0 -and $content[0].text) {
            $text = [string]$content[0].text
        }
        throw "MCP $ToolName returned tool error: $text"
    }
}

function ConvertFrom-McpToolTextJson {
    param(
        [Parameter(Mandatory = $true)]$Response,
        [Parameter(Mandatory = $true)][string]$ToolName
    )

    Assert-McpResponseSucceeded -Response $Response -ToolName $ToolName
    $content = @($Response.result.content)
    if ($content.Count -lt 1 -or -not $content[0].text) {
        throw "MCP $ToolName returned no text content."
    }

    $text = [string]$content[0].text
    try {
        return $text | ConvertFrom-Json
    }
    catch {
        throw "MCP $ToolName returned non-JSON text: $text"
    }
}

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $GameRoot = Find-ExpectedGameDebugRoot -WorkspaceRoot $workspaceRoot
}

if (-not (Test-ExpectedGameDebugRoot -Path $GameRoot -ExpectedSha256 $expectedEkd5Sha256)) {
    throw "Game root must contain Ekd5.exe with expected SHA256 $expectedEkd5Sha256 and core 6.5 resource files: $GameRoot"
}

$resolvedGameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
Write-Host ("GAME_DEBUG_MCP_GAME_ROOT {0}" -f $resolvedGameRoot)
$resolvedStartScript = (Resolve-Path -LiteralPath $startScript).Path
$argsList = @(
    "-NoLogo",
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    "`"$resolvedStartScript`"",
    "-NoBuild"
)

if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
    $argsList += @("-GameRoot", "`"$resolvedGameRoot`"")
}

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "powershell.exe"
$psi.Arguments = $argsList -join " "
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

$proc = [System.Diagnostics.Process]::Start($psi)
try {
    $initialize = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"ccz-game-debug-mcp-validate","version":"1.0.0"}}}'
    $initialized = '{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}'
    $toolsList = '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
    $stateCall = '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"debug_session_state","arguments":{}}}'
    $processStartCall = '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"game_process_start","arguments":{"allow_launch":false,"wait_ms":1000}}}'
    $catalogCall = '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"debug_function_catalog","arguments":{"stage":"attack_after"}}}'
    $xrefCall = '{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"debug_static_xref_scan","arguments":{"stage":"attack_after","near_bytes":64}}}'
    $addressReportCall = '{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"debug_address_report","arguments":{"stages":"attack_before,attack_after,turn_end","near_bytes":64,"max_candidates_per_function":4}}}'
    $addressProbePlanCall = '{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"debug_address_report_probe_plan","arguments":{"stages":"attack_before,attack_after,turn_end","near_bytes":64,"max_candidates_per_function":4,"include_targets":true,"include_callers":true}}}'
    $addressProbeRunCall = '{"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"debug_address_probe_run","arguments":{"stages":"attack_before,attack_after,turn_end","run_probes":false,"start_game":false,"start_debugger":false}}}'
    $autonomyCall = '{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"debug_autonomy_run","arguments":{"scenario":"generic_internal_probe","stages":"startup,attack_after","start_game":false,"start_debugger":false,"run_probes":false}}}'
    $runtimeStateCall = '{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"game_runtime_state_classify","arguments":{}}}'
    $runtimeWaitCall = '{"jsonrpc":"2.0","id":12,"method":"tools/call","params":{"name":"game_runtime_wait_for_state","arguments":{"target_classifications":"not_running","timeout_ms":1000,"poll_interval_ms":100}}}'
    $rsceneAnchorCall = '{"jsonrpc":"2.0","id":13,"method":"tools/call","params":{"name":"game_rscene_text_anchor_scan","arguments":{"max_scan_bytes":1048576,"max_hits_per_anchor":2}}}'
    $rsceneScriptWindowCall = '{"jsonrpc":"2.0","id":23,"method":"tools/call","params":{"name":"game_rscene_script_window_scan","arguments":{"route":"regular_start","max_scan_bytes":1048576,"max_hits_per_window":2,"include_pointer_refs":false}}}'
    $rsceneHandlerScanCall = '{"jsonrpc":"2.0","id":24,"method":"tools/call","params":{"name":"debug_rscene_command_handler_scan","arguments":{"command_ids":"2D,12,07,13","max_candidates_per_command":8,"context_bytes":12,"write_probe_plan":true}}}'
    $rsceneLoadTransitionScanCall = '{"jsonrpc":"2.0","id":27,"method":"tools/call","params":{"name":"debug_rscene_load_transition_scan","arguments":{"route":"regular_start","max_candidates":16,"context_bytes":12,"write_probe_plan":true,"include_runtime_scan":false}}}'
    $rsceneLoadTransitionProbeCall = '{"jsonrpc":"2.0","id":38,"method":"tools/call","params":{"name":"debug_rscene_load_transition_probe_run","arguments":{"route":"regular_start","start_debugger":false,"continue_startup":false,"run_probes":false,"max_candidates":16,"context_bytes":12,"candidate_filter":"anchor_functions,direct_refs,no_imports","timeout_ms":1000,"max_scan_bytes":1048576}}}'
    $titleMenuDispatchScanCall = '{"jsonrpc":"2.0","id":39,"method":"tools/call","params":{"name":"debug_title_menu_dispatch_scan","arguments":{"max_candidates_per_api":6,"context_bytes":12,"include_function_entries":true,"write_probe_plan":true}}}'
    $titleWndProcDispatchScanCall = '{"jsonrpc":"2.0","id":42,"method":"tools/call","params":{"name":"debug_title_wndproc_dispatch_scan","arguments":{"max_candidates_per_constant":6,"context_bytes":12,"include_function_entries":true,"write_probe_plan":true}}}'
    $titleMenuDispatchProbeCall = '{"jsonrpc":"2.0","id":40,"method":"tools/call","params":{"name":"debug_title_menu_dispatch_probe_run","arguments":{"route":"title_menu","trigger_sequence":"","allow_input":false,"start_debugger":false,"continue_startup":false,"run_probes":false,"max_candidates_per_api":6,"context_bytes":12,"timeout_ms":1000,"max_scan_bytes":1048576}}}'
    $titleMenuDispatchMatrixCall = '{"jsonrpc":"2.0","id":41,"method":"tools/call","params":{"name":"debug_title_menu_dispatch_matrix_run","arguments":{"routes":"title_menu","allow_input":false,"allow_exit_route":false,"use_default_triggers":true,"start_debugger":false,"continue_startup":false,"run_probes":false,"max_candidates_per_api":6,"context_bytes":12,"timeout_ms":1000,"max_scan_bytes":1048576}}}'
    $internalEvidenceCall = '{"jsonrpc":"2.0","id":14,"method":"tools/call","params":{"name":"debug_capture_evidence","arguments":{"reason":"validate-internal-evidence","include_screenshot":false}}}'
    $scriptDryRunCall = '{"jsonrpc":"2.0","id":15,"method":"tools/call","params":{"name":"debug_script_run","arguments":{"script":"yj_cao_attack_zhangliang","allow_input":false}}}'
    $battleMatchCall = '{"jsonrpc":"2.0","id":16,"method":"tools/call","params":{"name":"game_battle_state_match","arguments":{"profile":"yingchuan_cao_zhangliang"}}}'
    $battleProfileProbeCall = '{"jsonrpc":"2.0","id":17,"method":"tools/call","params":{"name":"debug_battle_profile_probe_run","arguments":{"profile":"yingchuan_cao_zhangliang","stages":"attack_before,attack_after,turn_end","start_game":false,"start_debugger":false,"run_probes":false}}}'
    $liveReadinessCall = '{"jsonrpc":"2.0","id":18,"method":"tools/call","params":{"name":"debug_live_probe_readiness","arguments":{"profile":"yingchuan_cao_zhangliang","stages":"attack_before,attack_after,turn_end"}}}'
    $liveAutoRunCall = '{"jsonrpc":"2.0","id":19,"method":"tools/call","params":{"name":"debug_live_probe_auto_run","arguments":{"profile":"yingchuan_cao_zhangliang","stages":"attack_before,attack_after,turn_end","start_game":false,"start_debugger":false,"run_probes":false}}}'
    $battleAutoStepCall = '{"jsonrpc":"2.0","id":43,"method":"tools/call","params":{"name":"game_battle_auto_step","arguments":{"policy":"safe_attack","allow_input":false,"settle_ms":10}}}'
    $battleAutoRunCall = '{"jsonrpc":"2.0","id":44,"method":"tools/call","params":{"name":"game_battle_auto_run","arguments":{"max_steps":1,"policy":"safe_attack","allow_input":false,"settle_ms":10}}}'
    $battleAutoProbeCall = '{"jsonrpc":"2.0","id":45,"method":"tools/call","params":{"name":"debug_battle_auto_probe_run","arguments":{"max_steps":1,"policy":"safe_attack","run_probes":false,"allow_input":false,"timeout_ms":1000}}}'
    $keySequenceCall = '{"jsonrpc":"2.0","id":20,"method":"tools/call","params":{"name":"game_key_sequence","arguments":{"sequence":"enter","allow_input":false,"delay_ms":10}}}'
    $transitionProbeCall = '{"jsonrpc":"2.0","id":21,"method":"tools/call","params":{"name":"debug_transition_probe_run","arguments":{"stage":"startup","sequence":"enter","allow_input":false,"start_debugger":false,"continue_startup":false,"max_hits":1,"timeout_ms":1000}}}'
    $r00RouteCall = '{"jsonrpc":"2.0","id":22,"method":"tools/call","params":{"name":"debug_r00_mode_route_analyze","arguments":{"route":"regular_start"}}}'
    $r00StartupProbeCall = '{"jsonrpc":"2.0","id":25,"method":"tools/call","params":{"name":"debug_r00_startup_route_probe_run","arguments":{"route":"regular_start","allow_input":false,"start_debugger":false,"continue_startup":false,"run_probes":false,"max_candidates_per_command":4,"timeout_ms":1000}}}'
    $r00ActorRouteCall = '{"jsonrpc":"2.0","id":26,"method":"tools/call","params":{"name":"debug_r00_actor_route_analyze","arguments":{"route":"regular_start","person_id":157,"include_latest_evidence":true}}}'
    $r00RuntimeInvokeCandidateCall = '{"jsonrpc":"2.0","id":36,"method":"tools/call","params":{"name":"debug_r00_runtime_invoke_candidate_plan","arguments":{"route":"regular_start","max_candidates_per_command":4,"include_latest_evidence":true}}}'
    $r00RuntimeHandlerProbeCall = '{"jsonrpc":"2.0","id":37,"method":"tools/call","params":{"name":"debug_r00_runtime_handler_probe_run","arguments":{"route":"regular_start","start_debugger":false,"continue_startup":false,"run_probes":false,"max_candidates_per_command":4,"timeout_ms":1000}}}'
    $keyboardExplorationCall = '{"jsonrpc":"2.0","id":28,"method":"tools/call","params":{"name":"debug_keyboard_exploration_run","arguments":{"route":"regular_start","sequences":"enter;space","allow_launch":false,"allow_input":false,"start_debugger":false,"continue_startup":false,"max_sequences":2,"max_scan_bytes":1048576,"settle_ms":10,"delay_ms":10}}}'
    $runtimeAnchorSweepCall = '{"jsonrpc":"2.0","id":29,"method":"tools/call","params":{"name":"game_runtime_anchor_sweep","arguments":{"profile":"all","max_scan_bytes":1048576,"max_hits_per_anchor":2}}}'
    $runtimeInvokePlanCall = '{"jsonrpc":"2.0","id":30,"method":"tools/call","params":{"name":"debug_runtime_invoke_plan","arguments":{"stage":"menu","route":"full_menu"}}}'
    $runtimeInvokeRunCall = $null
    $menuRouteCall = '{"jsonrpc":"2.0","id":32,"method":"tools/call","params":{"name":"debug_menu_route_run","arguments":{"route":"title_menu","allow_runtime_injection":false,"allow_debug_invoke":false}}}'
    $addressVerifyCall = '{"jsonrpc":"2.0","id":33,"method":"tools/call","params":{"name":"debug_address_verify_run","arguments":{"stages":"attack_before,attack_after,turn_end","trigger_script":"yingchuan_cao_attack_zhangliang","run_probes":false}}}'
    $knowledgePromoteCall = $null
    $fullAutoCall = '{"jsonrpc":"2.0","id":35,"method":"tools/call","params":{"name":"debug_full_auto_run","arguments":{"profile":"full_menu_yingchuan","start_game":false,"start_debugger":false,"continue_startup":false,"run_probes":false,"allow_debug_invoke":false,"allow_runtime_injection":false,"allow_persistent_patch":false}}}'

    $proc.StandardInput.WriteLine($initialize)
    $proc.StandardInput.Flush()
    $initLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "initialize"

    $proc.StandardInput.WriteLine($initialized)
    $proc.StandardInput.Flush()

    $proc.StandardInput.WriteLine($toolsList)
    $proc.StandardInput.Flush()
    $toolsLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/list"

    $proc.StandardInput.WriteLine($stateCall)
    $proc.StandardInput.Flush()
    $stateLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_session_state"

    $proc.StandardInput.WriteLine($processStartCall)
    $proc.StandardInput.Flush()
    $processStartLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call game_process_start"

    $proc.StandardInput.WriteLine($catalogCall)
    $proc.StandardInput.Flush()
    $catalogLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_function_catalog"

    $proc.StandardInput.WriteLine($xrefCall)
    $proc.StandardInput.Flush()
    $xrefLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_static_xref_scan"

    $proc.StandardInput.WriteLine($addressReportCall)
    $proc.StandardInput.Flush()
    $addressReportLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_address_report"

    $proc.StandardInput.WriteLine($addressProbePlanCall)
    $proc.StandardInput.Flush()
    $addressProbePlanLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_address_report_probe_plan"

    $proc.StandardInput.WriteLine($addressProbeRunCall)
    $proc.StandardInput.Flush()
    $addressProbeRunLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_address_probe_run"

    $proc.StandardInput.WriteLine($autonomyCall)
    $proc.StandardInput.Flush()
    $autonomyLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_autonomy_run"

    $proc.StandardInput.WriteLine($runtimeStateCall)
    $proc.StandardInput.Flush()
    $runtimeStateLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call game_runtime_state_classify"

    $proc.StandardInput.WriteLine($runtimeWaitCall)
    $proc.StandardInput.Flush()
    $runtimeWaitLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call game_runtime_wait_for_state"

    $proc.StandardInput.WriteLine($rsceneAnchorCall)
    $proc.StandardInput.Flush()
    $rsceneAnchorLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call game_rscene_text_anchor_scan"

    $proc.StandardInput.WriteLine($rsceneScriptWindowCall)
    $proc.StandardInput.Flush()
    $rsceneScriptWindowLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call game_rscene_script_window_scan"

    $proc.StandardInput.WriteLine($rsceneHandlerScanCall)
    $proc.StandardInput.Flush()
    $rsceneHandlerScanLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_rscene_command_handler_scan"

    $proc.StandardInput.WriteLine($rsceneLoadTransitionScanCall)
    $proc.StandardInput.Flush()
    $rsceneLoadTransitionScanLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_rscene_load_transition_scan"

    $proc.StandardInput.WriteLine($rsceneLoadTransitionProbeCall)
    $proc.StandardInput.Flush()
    $rsceneLoadTransitionProbeLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_rscene_load_transition_probe_run"

    $proc.StandardInput.WriteLine($titleMenuDispatchScanCall)
    $proc.StandardInput.Flush()
    $titleMenuDispatchScanLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_title_menu_dispatch_scan"

    $proc.StandardInput.WriteLine($titleWndProcDispatchScanCall)
    $proc.StandardInput.Flush()
    $titleWndProcDispatchScanLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_title_wndproc_dispatch_scan"

    $proc.StandardInput.WriteLine($titleMenuDispatchProbeCall)
    $proc.StandardInput.Flush()
    $titleMenuDispatchProbeLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_title_menu_dispatch_probe_run"

    $proc.StandardInput.WriteLine($titleMenuDispatchMatrixCall)
    $proc.StandardInput.Flush()
    $titleMenuDispatchMatrixLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_title_menu_dispatch_matrix_run"

    $proc.StandardInput.WriteLine($internalEvidenceCall)
    $proc.StandardInput.Flush()
    $internalEvidenceLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_capture_evidence"

    $proc.StandardInput.WriteLine($scriptDryRunCall)
    $proc.StandardInput.Flush()
    $scriptDryRunLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_script_run"

    $proc.StandardInput.WriteLine($battleMatchCall)
    $proc.StandardInput.Flush()
    $battleMatchLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call game_battle_state_match"

    $proc.StandardInput.WriteLine($battleProfileProbeCall)
    $proc.StandardInput.Flush()
    $battleProfileProbeLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_battle_profile_probe_run"

    $proc.StandardInput.WriteLine($liveReadinessCall)
    $proc.StandardInput.Flush()
    $liveReadinessLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_live_probe_readiness"

    $proc.StandardInput.WriteLine($liveAutoRunCall)
    $proc.StandardInput.Flush()
    $liveAutoRunLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_live_probe_auto_run"

    $proc.StandardInput.WriteLine($battleAutoStepCall)
    $proc.StandardInput.Flush()
    $battleAutoStepLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call game_battle_auto_step"

    $proc.StandardInput.WriteLine($battleAutoRunCall)
    $proc.StandardInput.Flush()
    $battleAutoRunLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call game_battle_auto_run"

    $proc.StandardInput.WriteLine($battleAutoProbeCall)
    $proc.StandardInput.Flush()
    $battleAutoProbeLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_battle_auto_probe_run"

    $proc.StandardInput.WriteLine($keySequenceCall)
    $proc.StandardInput.Flush()
    $keySequenceLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call game_key_sequence"

    $proc.StandardInput.WriteLine($transitionProbeCall)
    $proc.StandardInput.Flush()
    $transitionProbeLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_transition_probe_run"

    $proc.StandardInput.WriteLine($r00RouteCall)
    $proc.StandardInput.Flush()
    $r00RouteLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_r00_mode_route_analyze"

    $proc.StandardInput.WriteLine($r00StartupProbeCall)
    $proc.StandardInput.Flush()
    $r00StartupProbeLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_r00_startup_route_probe_run"

    $proc.StandardInput.WriteLine($r00ActorRouteCall)
    $proc.StandardInput.Flush()
    $r00ActorRouteLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_r00_actor_route_analyze"

    $proc.StandardInput.WriteLine($r00RuntimeInvokeCandidateCall)
    $proc.StandardInput.Flush()
    $r00RuntimeInvokeCandidateLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_r00_runtime_invoke_candidate_plan"

    $proc.StandardInput.WriteLine($r00RuntimeHandlerProbeCall)
    $proc.StandardInput.Flush()
    $r00RuntimeHandlerProbeLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_r00_runtime_handler_probe_run"

    $proc.StandardInput.WriteLine($keyboardExplorationCall)
    $proc.StandardInput.Flush()
    $keyboardExplorationLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_keyboard_exploration_run"

    $proc.StandardInput.WriteLine($runtimeAnchorSweepCall)
    $proc.StandardInput.Flush()
    $runtimeAnchorSweepLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call game_runtime_anchor_sweep"

    $proc.StandardInput.WriteLine($runtimeInvokePlanCall)
    $proc.StandardInput.Flush()
    $runtimeInvokePlanLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_runtime_invoke_plan"

    $runtimeInvokePlanPreview = $runtimeInvokePlanLine | ConvertFrom-Json
    if ($runtimeInvokePlanPreview.error) {
        throw "MCP debug_runtime_invoke_plan failed: $($runtimeInvokePlanPreview.error.message)"
    }
    $runtimeInvokePlanTextPreview = ConvertFrom-McpToolTextJson -Response $runtimeInvokePlanPreview -ToolName "runtimeInvokePlanPreview"
    $runtimeInvokeRunCall = ('{{"jsonrpc":"2.0","id":31,"method":"tools/call","params":{{"name":"debug_runtime_invoke_run","arguments":{{"plan_path":"{0}","allow_runtime_injection":false,"allow_debug_invoke":false}}}}}}' -f (($runtimeInvokePlanTextPreview.plan_path -replace '\\','\\') -replace '"','\"'))
    $proc.StandardInput.WriteLine($runtimeInvokeRunCall)
    $proc.StandardInput.Flush()
    $runtimeInvokeRunLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_runtime_invoke_run"

    $proc.StandardInput.WriteLine($menuRouteCall)
    $proc.StandardInput.Flush()
    $menuRouteLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_menu_route_run"

    $proc.StandardInput.WriteLine($addressVerifyCall)
    $proc.StandardInput.Flush()
    $addressVerifyLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_address_verify_run"

    $addressVerifyPreview = $addressVerifyLine | ConvertFrom-Json
    if ($addressVerifyPreview.error) {
        throw "MCP debug_address_verify_run failed: $($addressVerifyPreview.error.message)"
    }
    $addressVerifyTextPreview = ConvertFrom-McpToolTextJson -Response $addressVerifyPreview -ToolName "addressVerifyPreview"
    $knowledgePromoteCall = ('{{"jsonrpc":"2.0","id":34,"method":"tools/call","params":{{"name":"debug_knowledge_promote","arguments":{{"evidence_path":"{0}","topic":"function-index","allow_write":false}}}}}}' -f (($addressVerifyTextPreview.session_dir -replace '\\','\\') -replace '"','\"'))
    $proc.StandardInput.WriteLine($knowledgePromoteCall)
    $proc.StandardInput.Flush()
    $knowledgePromoteLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_knowledge_promote"

    $proc.StandardInput.WriteLine($fullAutoCall)
    $proc.StandardInput.Flush()
    $fullAutoLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call debug_full_auto_run"

    $init = $initLine | ConvertFrom-Json
    $tools = $toolsLine | ConvertFrom-Json
    $state = $stateLine | ConvertFrom-Json
    $processStart = $processStartLine | ConvertFrom-Json
    $catalog = $catalogLine | ConvertFrom-Json
    $xref = $xrefLine | ConvertFrom-Json
    $addressReport = $addressReportLine | ConvertFrom-Json
    $addressProbePlan = $addressProbePlanLine | ConvertFrom-Json
    $addressProbeRun = $addressProbeRunLine | ConvertFrom-Json
    $autonomy = $autonomyLine | ConvertFrom-Json
    $runtimeState = $runtimeStateLine | ConvertFrom-Json
    $runtimeWait = $runtimeWaitLine | ConvertFrom-Json
    $rsceneAnchor = $rsceneAnchorLine | ConvertFrom-Json
    $rsceneScriptWindow = $rsceneScriptWindowLine | ConvertFrom-Json
    $rsceneHandlerScan = $rsceneHandlerScanLine | ConvertFrom-Json
    $rsceneLoadTransitionScan = $rsceneLoadTransitionScanLine | ConvertFrom-Json
    $rsceneLoadTransitionProbe = $rsceneLoadTransitionProbeLine | ConvertFrom-Json
    $titleMenuDispatchScan = $titleMenuDispatchScanLine | ConvertFrom-Json
    $titleWndProcDispatchScan = $titleWndProcDispatchScanLine | ConvertFrom-Json
    $titleMenuDispatchProbe = $titleMenuDispatchProbeLine | ConvertFrom-Json
    $titleMenuDispatchMatrix = $titleMenuDispatchMatrixLine | ConvertFrom-Json
    $internalEvidence = $internalEvidenceLine | ConvertFrom-Json
    $scriptDryRun = $scriptDryRunLine | ConvertFrom-Json
    $battleMatch = $battleMatchLine | ConvertFrom-Json
    $battleProfileProbe = $battleProfileProbeLine | ConvertFrom-Json
    $liveReadiness = $liveReadinessLine | ConvertFrom-Json
    $liveAutoRun = $liveAutoRunLine | ConvertFrom-Json
    $battleAutoStep = $battleAutoStepLine | ConvertFrom-Json
    $battleAutoRun = $battleAutoRunLine | ConvertFrom-Json
    $battleAutoProbe = $battleAutoProbeLine | ConvertFrom-Json
    $keySequence = $keySequenceLine | ConvertFrom-Json
    $transitionProbe = $transitionProbeLine | ConvertFrom-Json
    $r00Route = $r00RouteLine | ConvertFrom-Json
    $r00StartupProbe = $r00StartupProbeLine | ConvertFrom-Json
    $r00ActorRoute = $r00ActorRouteLine | ConvertFrom-Json
    $r00RuntimeInvokeCandidate = $r00RuntimeInvokeCandidateLine | ConvertFrom-Json
    $r00RuntimeHandlerProbe = $r00RuntimeHandlerProbeLine | ConvertFrom-Json
    $keyboardExploration = $keyboardExplorationLine | ConvertFrom-Json
    $runtimeAnchorSweep = $runtimeAnchorSweepLine | ConvertFrom-Json
    $runtimeInvokePlan = $runtimeInvokePlanLine | ConvertFrom-Json
    $runtimeInvokeRun = $runtimeInvokeRunLine | ConvertFrom-Json
    $menuRoute = $menuRouteLine | ConvertFrom-Json
    $addressVerify = $addressVerifyLine | ConvertFrom-Json
    $knowledgePromote = $knowledgePromoteLine | ConvertFrom-Json
    $fullAuto = $fullAutoLine | ConvertFrom-Json

    if ($init.error) {
        throw "MCP initialize failed: $($init.error.message)"
    }
    if ($tools.error) {
        throw "MCP tools/list failed: $($tools.error.message)"
    }
    if ($state.error) {
        throw "MCP debug_session_state failed: $($state.error.message)"
    }
    if ($processStart.error) {
        throw "MCP game_process_start failed: $($processStart.error.message)"
    }
    if ($catalog.error) {
        throw "MCP debug_function_catalog failed: $($catalog.error.message)"
    }
    if ($xref.error) {
        throw "MCP debug_static_xref_scan failed: $($xref.error.message)"
    }
    if ($addressReport.error) {
        throw "MCP debug_address_report failed: $($addressReport.error.message)"
    }
    if ($addressProbePlan.error) {
        throw "MCP debug_address_report_probe_plan failed: $($addressProbePlan.error.message)"
    }
    if ($addressProbeRun.error) {
        throw "MCP debug_address_probe_run failed: $($addressProbeRun.error.message)"
    }
    if ($autonomy.error) {
        throw "MCP debug_autonomy_run failed: $($autonomy.error.message)"
    }
    if ($runtimeState.error) {
        throw "MCP game_runtime_state_classify failed: $($runtimeState.error.message)"
    }
    if ($runtimeWait.error) {
        throw "MCP game_runtime_wait_for_state failed: $($runtimeWait.error.message)"
    }
    if ($rsceneAnchor.error) {
        throw "MCP game_rscene_text_anchor_scan failed: $($rsceneAnchor.error.message)"
    }
    if ($rsceneScriptWindow.error) {
        throw "MCP game_rscene_script_window_scan failed: $($rsceneScriptWindow.error.message)"
    }
    if ($rsceneHandlerScan.error) {
        throw "MCP debug_rscene_command_handler_scan failed: $($rsceneHandlerScan.error.message)"
    }
    if ($rsceneLoadTransitionScan.error) {
        throw "MCP debug_rscene_load_transition_scan failed: $($rsceneLoadTransitionScan.error.message)"
    }
    if ($rsceneLoadTransitionProbe.error) {
        throw "MCP debug_rscene_load_transition_probe_run failed: $($rsceneLoadTransitionProbe.error.message)"
    }
    if ($titleMenuDispatchScan.error) {
        throw "MCP debug_title_menu_dispatch_scan failed: $($titleMenuDispatchScan.error.message)"
    }
    if ($titleWndProcDispatchScan.error) {
        throw "MCP debug_title_wndproc_dispatch_scan failed: $($titleWndProcDispatchScan.error.message)"
    }
    if ($titleMenuDispatchProbe.error) {
        throw "MCP debug_title_menu_dispatch_probe_run failed: $($titleMenuDispatchProbe.error.message)"
    }
    if ($titleMenuDispatchMatrix.error) {
        throw "MCP debug_title_menu_dispatch_matrix_run failed: $($titleMenuDispatchMatrix.error.message)"
    }
    if ($internalEvidence.error) {
        throw "MCP debug_capture_evidence failed: $($internalEvidence.error.message)"
    }
    if ($scriptDryRun.error) {
        throw "MCP debug_script_run failed: $($scriptDryRun.error.message)"
    }
    if ($battleMatch.error) {
        throw "MCP game_battle_state_match failed: $($battleMatch.error.message)"
    }
    if ($battleProfileProbe.error) {
        throw "MCP debug_battle_profile_probe_run failed: $($battleProfileProbe.error.message)"
    }
    if ($liveReadiness.error) {
        throw "MCP debug_live_probe_readiness failed: $($liveReadiness.error.message)"
    }
    if ($liveAutoRun.error) {
        throw "MCP debug_live_probe_auto_run failed: $($liveAutoRun.error.message)"
    }
    if ($battleAutoStep.error) {
        throw "MCP game_battle_auto_step failed: $($battleAutoStep.error.message)"
    }
    if ($battleAutoRun.error) {
        throw "MCP game_battle_auto_run failed: $($battleAutoRun.error.message)"
    }
    if ($battleAutoProbe.error) {
        throw "MCP debug_battle_auto_probe_run failed: $($battleAutoProbe.error.message)"
    }
    if ($keySequence.error) {
        throw "MCP game_key_sequence failed: $($keySequence.error.message)"
    }
    if ($transitionProbe.error) {
        throw "MCP debug_transition_probe_run failed: $($transitionProbe.error.message)"
    }
    if ($r00Route.error) {
        throw "MCP debug_r00_mode_route_analyze failed: $($r00Route.error.message)"
    }
    if ($r00StartupProbe.error) {
        throw "MCP debug_r00_startup_route_probe_run failed: $($r00StartupProbe.error.message)"
    }
    if ($r00ActorRoute.error) {
        throw "MCP debug_r00_actor_route_analyze failed: $($r00ActorRoute.error.message)"
    }
    if ($r00RuntimeInvokeCandidate.error) {
        throw "MCP debug_r00_runtime_invoke_candidate_plan failed: $($r00RuntimeInvokeCandidate.error.message)"
    }
    if ($r00RuntimeHandlerProbe.error) {
        throw "MCP debug_r00_runtime_handler_probe_run failed: $($r00RuntimeHandlerProbe.error.message)"
    }
    if ($keyboardExploration.error) {
        throw "MCP debug_keyboard_exploration_run failed: $($keyboardExploration.error.message)"
    }
    if ($runtimeAnchorSweep.error) {
        throw "MCP game_runtime_anchor_sweep failed: $($runtimeAnchorSweep.error.message)"
    }
    if ($runtimeInvokePlan.error) {
        throw "MCP debug_runtime_invoke_plan failed: $($runtimeInvokePlan.error.message)"
    }
    if ($runtimeInvokeRun.error) {
        throw "MCP debug_runtime_invoke_run failed: $($runtimeInvokeRun.error.message)"
    }
    if ($menuRoute.error) {
        throw "MCP debug_menu_route_run failed: $($menuRoute.error.message)"
    }
    if ($addressVerify.error) {
        throw "MCP debug_address_verify_run failed: $($addressVerify.error.message)"
    }
    if ($knowledgePromote.error) {
        throw "MCP debug_knowledge_promote failed: $($knowledgePromote.error.message)"
    }
    if ($fullAuto.error) {
        throw "MCP debug_full_auto_run failed: $($fullAuto.error.message)"
    }

    $toolNames = @($tools.result.tools | ForEach-Object { $_.name })
    if ($toolNames.Count -lt $MinimumToolCount) {
        throw "Expected at least $MinimumToolCount tools, got $($toolNames.Count)."
    }

    $requiredTools = @(
        "debug_session_start",
        "game_process_start",
        "debug_session_state",
        "game_window_prepare",
        "game_capture_frame",
        "game_read_battle_state",
        "game_runtime_state_classify",
        "game_runtime_wait_for_state",
        "game_rscene_text_anchor_scan",
        "game_runtime_anchor_sweep",
        "game_rscene_script_window_scan",
        "game_battle_state_match",
        "game_read_battlefield_runtime_snapshot",
        "game_battle_calibrate_grid",
        "game_battle_grid_to_client",
        "game_battle_verify_click_target",
        "game_battle_select_unit",
        "game_battle_move_unit",
        "game_battle_attack",
        "game_battle_wait",
        "game_battle_end_turn",
        "game_battle_auto_step",
        "game_battle_auto_run",
        "game_click_grid",
        "game_click_ui",
        "game_key_sequence",
        "debug_r00_mode_route_analyze",
        "debug_r00_actor_route_analyze",
        "debug_rscene_command_handler_scan",
        "debug_rscene_load_transition_probe_run",
        "debug_title_menu_dispatch_scan",
        "debug_title_wndproc_dispatch_scan",
        "debug_title_menu_dispatch_probe_run",
        "debug_title_menu_dispatch_matrix_run",
        "debug_r00_startup_route_probe_run",
        "debug_r00_runtime_invoke_candidate_plan",
        "debug_r00_runtime_handler_probe_run",
        "debug_keyboard_exploration_run",
        "debug_breakpoint_plan_apply",
        "debug_function_catalog",
        "debug_static_xref_scan",
        "debug_address_report",
        "debug_address_report_probe_plan",
        "debug_address_probe_run",
        "debug_battle_profile_probe_run",
        "debug_live_probe_readiness",
        "debug_live_probe_auto_run",
        "debug_battle_auto_probe_run",
        "debug_full_auto_run",
        "debug_runtime_invoke_plan",
        "debug_runtime_invoke_run",
        "debug_menu_route_run",
        "debug_address_verify_run",
        "debug_knowledge_promote",
        "debug_phase_probe_plan",
        "debug_autonomy_plan",
        "debug_autonomy_run",
        "debug_internal_probe_plan",
        "debug_internal_probe_run",
        "debug_transition_probe_run",
        "debug_run_until_event",
        "debug_capture_evidence",
        "debug_write_knowledge_draft",
        "debug_script_run"
    )
    $missing = @($requiredTools | Where-Object { $toolNames -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Missing required tools: $($missing -join ', ')"
    }

    $catalogText = ConvertFrom-McpToolTextJson -Response $catalog -ToolName "catalog"
    $processStartText = ConvertFrom-McpToolTextJson -Response $processStart -ToolName "processStart"
    if (-not $processStartText.dry_run) {
        throw "game_process_start validation must stay dry-run when allow_launch=false."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $processStartText.session_dir "process-start.json") -PathType Leaf)) {
        throw "game_process_start did not write process-start.json."
    }

    if (-not (Test-Path -LiteralPath $catalogText.catalog_path -PathType Leaf)) {
        throw "debug_function_catalog did not write catalog_path: $($catalogText.catalog_path)"
    }
    if (-not (Test-Path -LiteralPath $catalogText.markdown_path -PathType Leaf)) {
        throw "debug_function_catalog did not write markdown_path: $($catalogText.markdown_path)"
    }
    $xrefText = ConvertFrom-McpToolTextJson -Response $xref -ToolName "xref"
    if (-not (Test-Path -LiteralPath $xrefText.xref_path -PathType Leaf)) {
        throw "debug_static_xref_scan did not write xref_path: $($xrefText.xref_path)"
    }
    if (-not (Test-Path -LiteralPath $xrefText.markdown_path -PathType Leaf)) {
        throw "debug_static_xref_scan did not write markdown_path: $($xrefText.markdown_path)"
    }
    $addressReportText = ConvertFrom-McpToolTextJson -Response $addressReport -ToolName "addressReport"
    if (-not (Test-Path -LiteralPath $addressReportText.report_path -PathType Leaf)) {
        throw "debug_address_report did not write report_path: $($addressReportText.report_path)"
    }
    if (-not (Test-Path -LiteralPath $addressReportText.markdown_path -PathType Leaf)) {
        throw "debug_address_report did not write markdown_path: $($addressReportText.markdown_path)"
    }
    $addressProbePlanText = ConvertFrom-McpToolTextJson -Response $addressProbePlan -ToolName "addressProbePlan"
    if (-not (Test-Path -LiteralPath $addressProbePlanText.plan_path -PathType Leaf)) {
        throw "debug_address_report_probe_plan did not write plan_path: $($addressProbePlanText.plan_path)"
    }
    if ($addressProbePlanText.target_count -lt 1) {
        throw "debug_address_report_probe_plan returned no probe targets."
    }

    $addressProbeRunText = ConvertFrom-McpToolTextJson -Response $addressProbeRun -ToolName "addressProbeRun"
    if ($addressProbeRunText.status -ne "plan-ready") {
        throw "debug_address_probe_run safe validation expected status plan-ready, got: $($addressProbeRunText.status)"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $addressProbeRunText.session_dir "address-probe-run-summary.json") -PathType Leaf)) {
        throw "debug_address_probe_run did not write address-probe-run-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $addressProbeRunText.session_dir "address-probe-run-summary.md") -PathType Leaf)) {
        throw "debug_address_probe_run did not write address-probe-run-summary.md."
    }
    if ($addressProbeRunText.target_count -lt 1) {
        throw "debug_address_probe_run returned no probe targets."
    }

    $autonomyText = ConvertFrom-McpToolTextJson -Response $autonomy -ToolName "autonomy"
    $autonomySummaryPath = Join-Path $autonomyText.session_dir "autonomy-run-summary.json"
    if (-not (Test-Path -LiteralPath $autonomySummaryPath -PathType Leaf)) {
        throw "debug_autonomy_run did not write summary: $autonomySummaryPath"
    }
    if ($autonomyText.continue_startup_requested) {
        throw "debug_autonomy_run safe validation must not continue startup unless continue_startup=true."
    }
    if ($null -ne $autonomyText.startup_continue) {
        throw "debug_autonomy_run safe validation executed startup continuation unexpectedly."
    }

    $runtimeStateText = ConvertFrom-McpToolTextJson -Response $runtimeState -ToolName "runtimeState"
    if ([string]::IsNullOrWhiteSpace($runtimeStateText.runtime.classification)) {
        throw "game_runtime_state_classify did not return a classification."
    }
    $runtimeWaitText = ConvertFrom-McpToolTextJson -Response $runtimeWait -ToolName "runtimeWait"
    if ([string]::IsNullOrWhiteSpace($runtimeWaitText.final_classification)) {
        throw "game_runtime_wait_for_state did not return a final_classification."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeWaitText.session_dir "runtime-wait-summary.json") -PathType Leaf)) {
        throw "game_runtime_wait_for_state did not write runtime-wait-summary.json."
    }

    $rsceneAnchorText = ConvertFrom-McpToolTextJson -Response $rsceneAnchor -ToolName "rsceneAnchor"
    if ([string]::IsNullOrWhiteSpace($rsceneAnchorText.status)) {
        throw "game_rscene_text_anchor_scan did not return a status."
    }
    if ($rsceneAnchorText.status -notin @("not-running", "anchors-found", "anchors-not-found")) {
        throw "game_rscene_text_anchor_scan returned unexpected status: $($rsceneAnchorText.status)"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $rsceneAnchorText.session_dir "rscene-text-anchor-scan.json") -PathType Leaf)) {
        throw "game_rscene_text_anchor_scan did not write rscene-text-anchor-scan.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $rsceneAnchorText.session_dir "rscene-text-anchor-scan.md") -PathType Leaf)) {
        throw "game_rscene_text_anchor_scan did not write rscene-text-anchor-scan.md."
    }
    if (@($rsceneAnchorText.anchors).Count -lt 1) {
        throw "game_rscene_text_anchor_scan did not preserve anchor list."
    }

    $rsceneScriptWindowText = ConvertFrom-McpToolTextJson -Response $rsceneScriptWindow -ToolName "rsceneScriptWindow"
    if ([string]::IsNullOrWhiteSpace($rsceneScriptWindowText.status)) {
        throw "game_rscene_script_window_scan did not return a status."
    }
    if ($rsceneScriptWindowText.status -notin @("not-running", "script-windows-found", "script-windows-not-found", "route-script-unavailable")) {
        throw "game_rscene_script_window_scan returned unexpected status: $($rsceneScriptWindowText.status)"
    }
    if ($rsceneScriptWindowText.status -ne "route-script-unavailable" -and @($rsceneScriptWindowText.windows).Count -lt 3) {
        throw "game_rscene_script_window_scan did not build the expected R_00 route windows."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $rsceneScriptWindowText.session_dir "rscene-script-window-scan.json") -PathType Leaf)) {
        throw "game_rscene_script_window_scan did not write rscene-script-window-scan.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $rsceneScriptWindowText.session_dir "rscene-script-window-scan.md") -PathType Leaf)) {
        throw "game_rscene_script_window_scan did not write rscene-script-window-scan.md."
    }

    $rsceneHandlerScanText = ConvertFrom-McpToolTextJson -Response $rsceneHandlerScan -ToolName "rsceneHandlerScan"
    if ($rsceneHandlerScanText.status -ne "handler-candidates-found") {
        throw "debug_rscene_command_handler_scan expected handler-candidates-found, got: $($rsceneHandlerScanText.status)"
    }
    if ($rsceneHandlerScanText.command_count -lt 4 -or $rsceneHandlerScanText.candidate_count -lt 1) {
        throw "debug_rscene_command_handler_scan did not return command candidates."
    }
    if ($rsceneHandlerScanText.probe_target_count -lt 1) {
        throw "debug_rscene_command_handler_scan did not write probe targets."
    }
    if (-not (Test-Path -LiteralPath $rsceneHandlerScanText.report_path -PathType Leaf)) {
        throw "debug_rscene_command_handler_scan did not write report_path: $($rsceneHandlerScanText.report_path)"
    }
    if (-not (Test-Path -LiteralPath $rsceneHandlerScanText.markdown_path -PathType Leaf)) {
        throw "debug_rscene_command_handler_scan did not write markdown_path: $($rsceneHandlerScanText.markdown_path)"
    }
    if (-not (Test-Path -LiteralPath $rsceneHandlerScanText.probe_plan_path -PathType Leaf)) {
        throw "debug_rscene_command_handler_scan did not write probe_plan_path: $($rsceneHandlerScanText.probe_plan_path)"
    }

    $rsceneLoadTransitionScanText = ConvertFrom-McpToolTextJson -Response $rsceneLoadTransitionScan -ToolName "rsceneLoadTransitionScan"
    if ($rsceneLoadTransitionScanText.status -ne "transition-candidates-found" -and $rsceneLoadTransitionScanText.status -ne "anchors-found-no-text-refs") {
        throw "debug_rscene_load_transition_scan returned unexpected status: $($rsceneLoadTransitionScanText.status)"
    }
    if ($rsceneLoadTransitionScanText.static_anchor_count -lt 1) {
        throw "debug_rscene_load_transition_scan found no static R/S EEX anchors."
    }
    if (-not (Test-Path -LiteralPath $rsceneLoadTransitionScanText.report_path -PathType Leaf)) {
        throw "debug_rscene_load_transition_scan did not write report_path: $($rsceneLoadTransitionScanText.report_path)"
    }
    if (-not (Test-Path -LiteralPath $rsceneLoadTransitionScanText.markdown_path -PathType Leaf)) {
        throw "debug_rscene_load_transition_scan did not write markdown_path: $($rsceneLoadTransitionScanText.markdown_path)"
    }
    if (-not (Test-Path -LiteralPath $rsceneLoadTransitionScanText.probe_plan_path -PathType Leaf)) {
        throw "debug_rscene_load_transition_scan did not write probe_plan_path: $($rsceneLoadTransitionScanText.probe_plan_path)"
    }

    $rsceneLoadTransitionProbeText = ConvertFrom-McpToolTextJson -Response $rsceneLoadTransitionProbe -ToolName "rsceneLoadTransitionProbe"
    if ($rsceneLoadTransitionProbeText.status -ne "plan-ready") {
        throw "debug_rscene_load_transition_probe_run dry validation expected plan-ready, got: $($rsceneLoadTransitionProbeText.status)"
    }
    if ($rsceneLoadTransitionProbeText.run_probes) {
        throw "debug_rscene_load_transition_probe_run dry validation must not run probes."
    }
    if ($rsceneLoadTransitionProbeText.probe_target_count -lt 1) {
        throw "debug_rscene_load_transition_probe_run returned no probe targets."
    }
    if (-not (Test-Path -LiteralPath $rsceneLoadTransitionProbeText.transition_report_path -PathType Leaf)) {
        throw "debug_rscene_load_transition_probe_run did not write transition_report_path: $($rsceneLoadTransitionProbeText.transition_report_path)"
    }
    if (-not (Test-Path -LiteralPath $rsceneLoadTransitionProbeText.probe_plan_path -PathType Leaf)) {
        throw "debug_rscene_load_transition_probe_run did not write probe_plan_path: $($rsceneLoadTransitionProbeText.probe_plan_path)"
    }
    $rsceneLoadTransitionProbePlanJson = Get-Content -LiteralPath $rsceneLoadTransitionProbeText.probe_plan_path -Raw | ConvertFrom-Json
    if ($rsceneLoadTransitionProbePlanJson.profile -ne "rscene-load-transition") {
        throw "debug_rscene_load_transition_probe_run returned unexpected probe profile: $($rsceneLoadTransitionProbePlanJson.profile)"
    }
    if (($rsceneLoadTransitionProbePlanJson.targets | Where-Object { $_.name -like "rscene_load_api_*" -or $_.evidence_level -like "static-rscene-load-api-*" }).Count -gt 0) {
        throw "Focused debug_rscene_load_transition_probe_run plan must exclude generic import/API targets."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $rsceneLoadTransitionProbeText.session_dir "rscene-load-transition-probe-summary.json") -PathType Leaf)) {
        throw "debug_rscene_load_transition_probe_run did not write rscene-load-transition-probe-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $rsceneLoadTransitionProbeText.session_dir "rscene-load-transition-probe-summary.md") -PathType Leaf)) {
        throw "debug_rscene_load_transition_probe_run did not write rscene-load-transition-probe-summary.md."
    }

    $titleMenuDispatchScanText = ConvertFrom-McpToolTextJson -Response $titleMenuDispatchScan -ToolName "titleMenuDispatchScan"
    if ($titleMenuDispatchScanText.status -ne "title-menu-dispatch-candidates-found") {
        throw "debug_title_menu_dispatch_scan expected candidates-found, got: $($titleMenuDispatchScanText.status)"
    }
    if ($titleMenuDispatchScanText.call_candidate_count -lt 1) {
        throw "debug_title_menu_dispatch_scan returned no API call candidates."
    }
    if ($titleMenuDispatchScanText.probe_target_count -lt 1) {
        throw "debug_title_menu_dispatch_scan returned no probe targets."
    }
    if (-not (Test-Path -LiteralPath $titleMenuDispatchScanText.report_path -PathType Leaf)) {
        throw "debug_title_menu_dispatch_scan did not write report_path: $($titleMenuDispatchScanText.report_path)"
    }
    if (-not (Test-Path -LiteralPath $titleMenuDispatchScanText.markdown_path -PathType Leaf)) {
        throw "debug_title_menu_dispatch_scan did not write markdown_path: $($titleMenuDispatchScanText.markdown_path)"
    }
    if (-not (Test-Path -LiteralPath $titleMenuDispatchScanText.probe_plan_path -PathType Leaf)) {
        throw "debug_title_menu_dispatch_scan did not write probe_plan_path: $($titleMenuDispatchScanText.probe_plan_path)"
    }
    if (-not (Test-Path -LiteralPath $titleMenuDispatchScanText.probe_plan_markdown_path -PathType Leaf)) {
        throw "debug_title_menu_dispatch_scan did not write probe_plan_markdown_path: $($titleMenuDispatchScanText.probe_plan_markdown_path)"
    }
    $titleMenuDispatchProbePlanJson = Get-Content -LiteralPath $titleMenuDispatchScanText.probe_plan_path -Raw | ConvertFrom-Json
    if ($titleMenuDispatchProbePlanJson.profile -ne "title-menu-dispatch") {
        throw "debug_title_menu_dispatch_scan returned unexpected probe profile: $($titleMenuDispatchProbePlanJson.profile)"
    }
    if (@($titleMenuDispatchProbePlanJson.targets).Count -ne $titleMenuDispatchScanText.probe_target_count) {
        throw "debug_title_menu_dispatch_scan probe target count did not match generated plan."
    }
    if (($titleMenuDispatchProbePlanJson.targets | Where-Object { $_.name -like "*sendmessagea*" }).Count -lt 1) {
        throw "debug_title_menu_dispatch_scan should include SendMessageA candidates for title/menu dispatch probing."
    }

    $titleWndProcDispatchScanText = ConvertFrom-McpToolTextJson -Response $titleWndProcDispatchScan -ToolName "titleWndProcDispatchScan"
    if ($titleWndProcDispatchScanText.status -ne "title-wndproc-dispatch-candidates-found") {
        throw "debug_title_wndproc_dispatch_scan expected candidates-found, got: $($titleWndProcDispatchScanText.status)"
    }
    if ($titleWndProcDispatchScanText.compare_candidate_count -lt 1) {
        throw "debug_title_wndproc_dispatch_scan returned no compare candidates."
    }
    if ($titleWndProcDispatchScanText.probe_target_count -lt 1) {
        throw "debug_title_wndproc_dispatch_scan returned no probe targets."
    }
    if (-not (Test-Path -LiteralPath $titleWndProcDispatchScanText.report_path -PathType Leaf)) {
        throw "debug_title_wndproc_dispatch_scan did not write report_path: $($titleWndProcDispatchScanText.report_path)"
    }
    if (-not (Test-Path -LiteralPath $titleWndProcDispatchScanText.markdown_path -PathType Leaf)) {
        throw "debug_title_wndproc_dispatch_scan did not write markdown_path: $($titleWndProcDispatchScanText.markdown_path)"
    }
    if (-not (Test-Path -LiteralPath $titleWndProcDispatchScanText.probe_plan_path -PathType Leaf)) {
        throw "debug_title_wndproc_dispatch_scan did not write probe_plan_path: $($titleWndProcDispatchScanText.probe_plan_path)"
    }
    if (-not (Test-Path -LiteralPath $titleWndProcDispatchScanText.probe_plan_markdown_path -PathType Leaf)) {
        throw "debug_title_wndproc_dispatch_scan did not write probe_plan_markdown_path: $($titleWndProcDispatchScanText.probe_plan_markdown_path)"
    }
    $titleWndProcDispatchProbePlanJson = Get-Content -LiteralPath $titleWndProcDispatchScanText.probe_plan_path -Raw | ConvertFrom-Json
    if ($titleWndProcDispatchProbePlanJson.profile -ne "title-wndproc-dispatch") {
        throw "debug_title_wndproc_dispatch_scan returned unexpected probe profile: $($titleWndProcDispatchProbePlanJson.profile)"
    }
    if (@($titleWndProcDispatchProbePlanJson.targets).Count -ne $titleWndProcDispatchScanText.probe_target_count) {
        throw "debug_title_wndproc_dispatch_scan probe target count did not match generated plan."
    }
    if (($titleWndProcDispatchProbePlanJson.targets | Where-Object { $_.name -like "*wm_command*" -or $_.name -like "*wm_keydown*" }).Count -lt 1) {
        throw "debug_title_wndproc_dispatch_scan should include WM_COMMAND or WM_KEYDOWN candidates."
    }

    $titleMenuDispatchProbeText = ConvertFrom-McpToolTextJson -Response $titleMenuDispatchProbe -ToolName "titleMenuDispatchProbe"
    if ($titleMenuDispatchProbeText.status -ne "plan-ready") {
        throw "debug_title_menu_dispatch_probe_run dry validation expected plan-ready, got: $($titleMenuDispatchProbeText.status)"
    }
    if ($titleMenuDispatchProbeText.allow_input) {
        throw "debug_title_menu_dispatch_probe_run dry validation must not send input."
    }
    if ($titleMenuDispatchProbeText.run_probes) {
        throw "debug_title_menu_dispatch_probe_run dry validation must not run probes."
    }
    if ($titleMenuDispatchProbeText.probe_target_count -lt 1) {
        throw "debug_title_menu_dispatch_probe_run returned no probe targets."
    }
    if (-not (Test-Path -LiteralPath $titleMenuDispatchProbeText.dispatch_report_path -PathType Leaf)) {
        throw "debug_title_menu_dispatch_probe_run did not write dispatch_report_path: $($titleMenuDispatchProbeText.dispatch_report_path)"
    }
    if (-not (Test-Path -LiteralPath $titleMenuDispatchProbeText.probe_plan_path -PathType Leaf)) {
        throw "debug_title_menu_dispatch_probe_run did not write probe_plan_path: $($titleMenuDispatchProbeText.probe_plan_path)"
    }
    $titleMenuDispatchProbeRunPlanJson = Get-Content -LiteralPath $titleMenuDispatchProbeText.probe_plan_path -Raw | ConvertFrom-Json
    if ($titleMenuDispatchProbeRunPlanJson.profile -ne "title-menu-dispatch") {
        throw "debug_title_menu_dispatch_probe_run returned unexpected probe profile: $($titleMenuDispatchProbeRunPlanJson.profile)"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $titleMenuDispatchProbeText.session_dir "title-menu-dispatch-probe-summary.json") -PathType Leaf)) {
        throw "debug_title_menu_dispatch_probe_run did not write title-menu-dispatch-probe-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $titleMenuDispatchProbeText.session_dir "title-menu-dispatch-probe-summary.md") -PathType Leaf)) {
        throw "debug_title_menu_dispatch_probe_run did not write title-menu-dispatch-probe-summary.md."
    }

    $titleMenuDispatchMatrixText = ConvertFrom-McpToolTextJson -Response $titleMenuDispatchMatrix -ToolName "titleMenuDispatchMatrix"
    if ($titleMenuDispatchMatrixText.status -ne "title-menu-dispatch-matrix-plan-ready") {
        throw "debug_title_menu_dispatch_matrix_run dry validation expected matrix-plan-ready, got: $($titleMenuDispatchMatrixText.status)"
    }
    if ($titleMenuDispatchMatrixText.allow_input) {
        throw "debug_title_menu_dispatch_matrix_run dry validation must not send input."
    }
    if ($titleMenuDispatchMatrixText.run_probes) {
        throw "debug_title_menu_dispatch_matrix_run dry validation must not run probes."
    }
    if ($titleMenuDispatchMatrixText.allow_exit_route) {
        throw "debug_title_menu_dispatch_matrix_run dry validation must not allow exit route input."
    }
    if ($titleMenuDispatchMatrixText.route_count -lt 4) {
        throw "debug_title_menu_dispatch_matrix_run should cover the four title routes."
    }
    if ($titleMenuDispatchMatrixText.shared_probe_target_count -lt 1) {
        throw "debug_title_menu_dispatch_matrix_run returned no shared probe targets."
    }
    if (-not (Test-Path -LiteralPath $titleMenuDispatchMatrixText.shared_dispatch_report_path -PathType Leaf)) {
        throw "debug_title_menu_dispatch_matrix_run did not write shared_dispatch_report_path: $($titleMenuDispatchMatrixText.shared_dispatch_report_path)"
    }
    if (-not (Test-Path -LiteralPath $titleMenuDispatchMatrixText.shared_probe_plan_path -PathType Leaf)) {
        throw "debug_title_menu_dispatch_matrix_run did not write shared_probe_plan_path: $($titleMenuDispatchMatrixText.shared_probe_plan_path)"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $titleMenuDispatchMatrixText.session_dir "title-menu-dispatch-matrix-summary.json") -PathType Leaf)) {
        throw "debug_title_menu_dispatch_matrix_run did not write title-menu-dispatch-matrix-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $titleMenuDispatchMatrixText.session_dir "title-menu-dispatch-matrix-summary.md") -PathType Leaf)) {
        throw "debug_title_menu_dispatch_matrix_run did not write title-menu-dispatch-matrix-summary.md."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $titleMenuDispatchMatrixText.session_dir "action-chain.jsonl") -PathType Leaf)) {
        throw "debug_title_menu_dispatch_matrix_run did not write action-chain.jsonl."
    }

    $r00ActorRouteText = ConvertFrom-McpToolTextJson -Response $r00ActorRoute -ToolName "r00ActorRoute"
    if ($r00ActorRouteText.status -ne "actor-route-ready") {
        throw "debug_r00_actor_route_analyze expected actor-route-ready, got: $($r00ActorRouteText.status)"
    }
    if ($r00ActorRouteText.person_id -ne 157) {
        throw "debug_r00_actor_route_analyze returned unexpected person_id: $($r00ActorRouteText.person_id)"
    }
    if ($r00ActorRouteText.actor_command_count -lt 1) {
        throw "debug_r00_actor_route_analyze returned no actor commands."
    }
    if ([string]::IsNullOrWhiteSpace($r00ActorRouteText.click_offset)) {
        throw "debug_r00_actor_route_analyze did not return click_offset."
    }
    if (-not (Test-Path -LiteralPath $r00ActorRouteText.report_path -PathType Leaf)) {
        throw "debug_r00_actor_route_analyze did not write report_path: $($r00ActorRouteText.report_path)"
    }
    if (-not (Test-Path -LiteralPath $r00ActorRouteText.markdown_path -PathType Leaf)) {
        throw "debug_r00_actor_route_analyze did not write markdown_path: $($r00ActorRouteText.markdown_path)"
    }

    $r00RuntimeInvokeCandidateText = ConvertFrom-McpToolTextJson -Response $r00RuntimeInvokeCandidate -ToolName "r00RuntimeInvokeCandidate"
    if ($r00RuntimeInvokeCandidateText.status -ne "r00-runtime-invoke-candidate-plan-ready") {
        throw "debug_r00_runtime_invoke_candidate_plan expected r00-runtime-invoke-candidate-plan-ready, got: $($r00RuntimeInvokeCandidateText.status)"
    }
    if ($r00RuntimeInvokeCandidateText.action_count -lt 3) {
        throw "debug_r00_runtime_invoke_candidate_plan returned too few R_00 actions."
    }
    if ($r00RuntimeInvokeCandidateText.handler_candidate_count -lt 1) {
        throw "debug_r00_runtime_invoke_candidate_plan returned no handler candidates."
    }
    if ($r00RuntimeInvokeCandidateText.probe_target_count -lt 1) {
        throw "debug_r00_runtime_invoke_candidate_plan returned no probe targets."
    }
    if (-not (Test-Path -LiteralPath $r00RuntimeInvokeCandidateText.report_path -PathType Leaf)) {
        throw "debug_r00_runtime_invoke_candidate_plan did not write report_path: $($r00RuntimeInvokeCandidateText.report_path)"
    }
    if (-not (Test-Path -LiteralPath $r00RuntimeInvokeCandidateText.markdown_path -PathType Leaf)) {
        throw "debug_r00_runtime_invoke_candidate_plan did not write markdown_path: $($r00RuntimeInvokeCandidateText.markdown_path)"
    }
    if (-not (Test-Path -LiteralPath $r00RuntimeInvokeCandidateText.probe_plan_path -PathType Leaf)) {
        throw "debug_r00_runtime_invoke_candidate_plan did not write probe_plan_path: $($r00RuntimeInvokeCandidateText.probe_plan_path)"
    }
    if (-not (Test-Path -LiteralPath $r00RuntimeInvokeCandidateText.probe_plan_markdown_path -PathType Leaf)) {
        throw "debug_r00_runtime_invoke_candidate_plan did not write probe_plan_markdown_path: $($r00RuntimeInvokeCandidateText.probe_plan_markdown_path)"
    }
    $r00RuntimeInvokeCandidateJson = Get-Content -Raw -Encoding UTF8 -LiteralPath $r00RuntimeInvokeCandidateText.report_path | ConvertFrom-Json
    $r00RuntimeActions = @($r00RuntimeInvokeCandidateJson.runtime_invoke_actions)
    if (($r00RuntimeActions | Where-Object { [string]::IsNullOrWhiteSpace($_.evidence_gate) }).Count -gt 0) {
        throw "debug_r00_runtime_invoke_candidate_plan emitted actions without evidence_gate."
    }
    if (($r00RuntimeActions | Where-Object { $_.requires_runtime_injection -or $_.writes_process_memory }).Count -gt 0) {
        throw "R_00 candidate plan must not require injection or process-memory writes before ABI proof."
    }
    if (($r00RuntimeActions | Where-Object { $_.candidate_address -like "002*" }).Count -gt 0) {
        throw "R_00 candidate plan treated script offsets as executable candidate addresses."
    }
    $r00RuntimeProbePlanJson = Get-Content -Raw -Encoding UTF8 -LiteralPath $r00RuntimeInvokeCandidateText.probe_plan_path | ConvertFrom-Json
    if ($r00RuntimeProbePlanJson.profile -ne "r00-runtime-handler-candidates") {
        throw "R_00 runtime candidate probe plan returned unexpected profile: $($r00RuntimeProbePlanJson.profile)"
    }
    if (@($r00RuntimeProbePlanJson.targets).Count -ne $r00RuntimeInvokeCandidateText.probe_target_count) {
        throw "R_00 runtime candidate probe target count did not match generated plan."
    }

    $r00RuntimeHandlerProbeText = ConvertFrom-McpToolTextJson -Response $r00RuntimeHandlerProbe -ToolName "r00RuntimeHandlerProbe"
    if ($r00RuntimeHandlerProbeText.status -ne "plan-ready") {
        throw "debug_r00_runtime_handler_probe_run dry validation expected plan-ready, got: $($r00RuntimeHandlerProbeText.status)"
    }
    if ($r00RuntimeHandlerProbeText.run_probes) {
        throw "debug_r00_runtime_handler_probe_run dry validation must not run probes."
    }
    if ($r00RuntimeHandlerProbeText.handler_candidate_count -lt 1) {
        throw "debug_r00_runtime_handler_probe_run returned no handler candidates."
    }
    if ($r00RuntimeHandlerProbeText.handler_probe_target_count -lt 1) {
        throw "debug_r00_runtime_handler_probe_run returned no handler probe targets."
    }
    if (-not (Test-Path -LiteralPath $r00RuntimeHandlerProbeText.candidate_report_path -PathType Leaf)) {
        throw "debug_r00_runtime_handler_probe_run did not write candidate_report_path: $($r00RuntimeHandlerProbeText.candidate_report_path)"
    }
    if (-not (Test-Path -LiteralPath $r00RuntimeHandlerProbeText.probe_plan_path -PathType Leaf)) {
        throw "debug_r00_runtime_handler_probe_run did not write probe_plan_path: $($r00RuntimeHandlerProbeText.probe_plan_path)"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $r00RuntimeHandlerProbeText.session_dir "r00-runtime-handler-probe-summary.json") -PathType Leaf)) {
        throw "debug_r00_runtime_handler_probe_run did not write r00-runtime-handler-probe-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $r00RuntimeHandlerProbeText.session_dir "r00-runtime-handler-probe-summary.md") -PathType Leaf)) {
        throw "debug_r00_runtime_handler_probe_run did not write r00-runtime-handler-probe-summary.md."
    }

    $internalEvidenceText = ConvertFrom-McpToolTextJson -Response $internalEvidence -ToolName "internalEvidence"
    if (-not (Test-Path -LiteralPath $internalEvidenceText.evidence_path -PathType Leaf)) {
        throw "debug_capture_evidence did not write evidence_path: $($internalEvidenceText.evidence_path)"
    }
    if ($internalEvidenceText.report.include_screenshot) {
        throw "debug_capture_evidence validation expected include_screenshot=false."
    }
    if ($null -ne $internalEvidenceText.report.screenshot) {
        throw "debug_capture_evidence captured a screenshot even though include_screenshot=false."
    }

    $scriptDryRunText = ConvertFrom-McpToolTextJson -Response $scriptDryRun -ToolName "scriptDryRun"
    if ($scriptDryRunText.dryRun.status -ne "dry-run") {
        throw "debug_script_run validation expected dry-run status, got: $($scriptDryRunText.dryRun.status)"
    }
    if ($scriptDryRunText.dryRun.safety -notmatch "no mouse input, screenshots") {
        throw "debug_script_run dry-run did not report the no-input/no-screenshot safety boundary."
    }

    $battleMatchText = ConvertFrom-McpToolTextJson -Response $battleMatch -ToolName "battleMatch"
    if ([string]::IsNullOrWhiteSpace($battleMatchText.status)) {
        throw "game_battle_state_match did not return a status."
    }
    if ($battleMatchText.profile -ne "yingchuan_cao_zhangliang") {
        throw "game_battle_state_match returned unexpected profile: $($battleMatchText.profile)"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $battleMatchText.session_dir "battle-state-match.json") -PathType Leaf)) {
        throw "game_battle_state_match did not write battle-state-match.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $battleMatchText.session_dir "battle-state-match.md") -PathType Leaf)) {
        throw "game_battle_state_match did not write battle-state-match.md."
    }

    $battleProfileProbeText = ConvertFrom-McpToolTextJson -Response $battleProfileProbe -ToolName "battleProfileProbe"
    if ($battleProfileProbeText.status -ne "profile-not-ready-plan-ready" -and $battleProfileProbeText.status -ne "profile-plan-ready") {
        throw "debug_battle_profile_probe_run safe validation expected plan-ready status, got: $($battleProfileProbeText.status)"
    }
    if ($battleProfileProbeText.dynamic_probes_allowed) {
        throw "debug_battle_profile_probe_run safe validation must not allow dynamic probes when run_probes=false."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $battleProfileProbeText.session_dir "battle-profile-probe-run-summary.json") -PathType Leaf)) {
        throw "debug_battle_profile_probe_run did not write battle-profile-probe-run-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $battleProfileProbeText.session_dir "battle-profile-probe-run-summary.md") -PathType Leaf)) {
        throw "debug_battle_profile_probe_run did not write battle-profile-probe-run-summary.md."
    }
    if (-not (Test-Path -LiteralPath $battleProfileProbeText.plan_path -PathType Leaf)) {
        throw "debug_battle_profile_probe_run did not write plan_path: $($battleProfileProbeText.plan_path)"
    }
    if ($battleProfileProbeText.target_count -lt 1) {
        throw "debug_battle_profile_probe_run returned no probe targets."
    }

    $liveReadinessText = ConvertFrom-McpToolTextJson -Response $liveReadiness -ToolName "liveReadiness"
    if ([string]::IsNullOrWhiteSpace($liveReadinessText.status)) {
        throw "debug_live_probe_readiness did not return a status."
    }
    if ($liveReadinessText.ready_for_dynamic_probe) {
        throw "debug_live_probe_readiness safe validation must not report ready_for_dynamic_probe when no live process/profile is expected."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $liveReadinessText.session_dir "live-probe-readiness.json") -PathType Leaf)) {
        throw "debug_live_probe_readiness did not write live-probe-readiness.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $liveReadinessText.session_dir "live-probe-readiness.md") -PathType Leaf)) {
        throw "debug_live_probe_readiness did not write live-probe-readiness.md."
    }
    if (-not (Test-Path -LiteralPath $liveReadinessText.plan_path -PathType Leaf)) {
        throw "debug_live_probe_readiness did not write plan_path: $($liveReadinessText.plan_path)"
    }
    if ($liveReadinessText.target_count -lt 1) {
        throw "debug_live_probe_readiness returned no probe targets."
    }

    $liveAutoRunText = ConvertFrom-McpToolTextJson -Response $liveAutoRun -ToolName "liveAutoRun"
    if ($liveAutoRunText.status -ne "readiness-recorded") {
        throw "debug_live_probe_auto_run safe validation expected readiness-recorded, got: $($liveAutoRunText.status)"
    }
    if ($liveAutoRunText.dynamic_probes_allowed) {
        throw "debug_live_probe_auto_run safe validation must not allow dynamic probes when run_probes=false."
    }
    if ($liveAutoRunText.continue_startup_requested) {
        throw "debug_live_probe_auto_run safe validation must not continue startup unless continue_startup=true."
    }
    if ($null -ne $liveAutoRunText.startup_continue) {
        throw "debug_live_probe_auto_run safe validation executed startup continuation unexpectedly."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $liveAutoRunText.session_dir "live-probe-auto-run-summary.json") -PathType Leaf)) {
        throw "debug_live_probe_auto_run did not write live-probe-auto-run-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $liveAutoRunText.session_dir "live-probe-auto-run-summary.md") -PathType Leaf)) {
        throw "debug_live_probe_auto_run did not write live-probe-auto-run-summary.md."
    }
    if (-not (Test-Path -LiteralPath $liveAutoRunText.plan_path -PathType Leaf)) {
        throw "debug_live_probe_auto_run did not write plan_path: $($liveAutoRunText.plan_path)"
    }
    if ($liveAutoRunText.target_count -lt 1) {
        throw "debug_live_probe_auto_run returned no probe targets."
    }

    $battleAutoStepText = ConvertFrom-McpToolTextJson -Response $battleAutoStep -ToolName "battleAutoStep"
    if ($battleAutoStepText.status -notlike "dry-run*") {
        throw "game_battle_auto_step validation expected dry-run status, got: $($battleAutoStepText.status)"
    }
    if ($battleAutoStepText.allow_input) {
        throw "game_battle_auto_step validation must not send input when allow_input=false."
    }
    if (-not (Test-Path -LiteralPath $battleAutoStepText.session_dir -PathType Container)) {
        throw "game_battle_auto_step did not write an evidence session."
    }

    $battleAutoRunText = ConvertFrom-McpToolTextJson -Response $battleAutoRun -ToolName "battleAutoRun"
    if ($battleAutoRunText.status -notlike "dry-run*") {
        throw "game_battle_auto_run validation expected dry-run status, got: $($battleAutoRunText.status)"
    }
    if ($battleAutoRunText.allow_input) {
        throw "game_battle_auto_run validation must not send input when allow_input=false."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $battleAutoRunText.session_dir "battle-auto-run-summary.json") -PathType Leaf)) {
        throw "game_battle_auto_run did not write battle-auto-run-summary.json."
    }

    $battleAutoProbeText = ConvertFrom-McpToolTextJson -Response $battleAutoProbe -ToolName "battleAutoProbe"
    if ($battleAutoProbeText.status -ne "plan-only") {
        throw "debug_battle_auto_probe_run validation expected plan-only, got: $($battleAutoProbeText.status)"
    }
    if ($battleAutoProbeText.run_probes) {
        throw "debug_battle_auto_probe_run validation must not run probes when run_probes=false."
    }
    if ($battleAutoProbeText.allow_input) {
        throw "debug_battle_auto_probe_run validation must not send input when allow_input=false."
    }
    if ($battleAutoProbeText.target_count -lt 1) {
        throw "debug_battle_auto_probe_run returned no probe targets."
    }
    if (-not (Test-Path -LiteralPath $battleAutoProbeText.plan_path -PathType Leaf)) {
        throw "debug_battle_auto_probe_run did not write plan_path: $($battleAutoProbeText.plan_path)"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $battleAutoProbeText.session_dir "battle-auto-probe-summary.json") -PathType Leaf)) {
        throw "debug_battle_auto_probe_run did not write battle-auto-probe-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $battleAutoProbeText.session_dir "battle-auto-probe-summary.md") -PathType Leaf)) {
        throw "debug_battle_auto_probe_run did not write battle-auto-probe-summary.md."
    }

    $keySequenceText = ConvertFrom-McpToolTextJson -Response $keySequence -ToolName "keySequence"
    if ($keySequenceText.status -ne "dry-run" -and $keySequenceText.status -ne "dry-run-no-process") {
        throw "game_key_sequence validation expected dry-run status, got: $($keySequenceText.status)"
    }
    if ($keySequenceText.allow_input) {
        throw "game_key_sequence validation must not send keyboard messages when allow_input=false."
    }
    if (-not (Test-Path -LiteralPath $keySequenceText.session_dir -PathType Container)) {
        throw "game_key_sequence did not write a session directory."
    }

    $transitionProbeText = ConvertFrom-McpToolTextJson -Response $transitionProbe -ToolName "transitionProbe"
    if ($transitionProbeText.status -ne "dry-run" -and $transitionProbeText.status -ne "bridge-unavailable") {
        throw "debug_transition_probe_run validation expected dry-run or bridge-unavailable, got: $($transitionProbeText.status)"
    }
    if ($transitionProbeText.allow_input) {
        throw "debug_transition_probe_run validation must not post keyboard messages when allow_input=false."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $transitionProbeText.session_dir "transition-probe-run-summary.json") -PathType Leaf)) {
        throw "debug_transition_probe_run did not write transition-probe-run-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $transitionProbeText.session_dir "transition-probe-run-summary.md") -PathType Leaf)) {
        throw "debug_transition_probe_run did not write transition-probe-run-summary.md."
    }
    if (-not (Test-Path -LiteralPath $transitionProbeText.plan_path -PathType Leaf)) {
        throw "debug_transition_probe_run did not write plan_path: $($transitionProbeText.plan_path)"
    }

    $r00RouteText = ConvertFrom-McpToolTextJson -Response $r00Route -ToolName "r00Route"
    if ($r00RouteText.status -ne "route-plan-ready") {
        throw "debug_r00_mode_route_analyze expected route-plan-ready, got: $($r00RouteText.status)"
    }
    if ($r00RouteText.sequence -ne "enter,down,down,down,down,down,enter") {
        throw "debug_r00_mode_route_analyze returned unexpected sequence: $($r00RouteText.sequence)"
    }
    if ($r00RouteText.mode_option_count -lt 3 -or $r00RouteText.config_option_count -lt 6) {
        throw "debug_r00_mode_route_analyze did not decode expected choice options."
    }
    if ([string]::IsNullOrWhiteSpace($r00RouteText.first_choice_offset) -or [string]::IsNullOrWhiteSpace($r00RouteText.second_choice_offset)) {
        throw "debug_r00_mode_route_analyze did not report both choice offsets."
    }
    if ($r00RouteText.prerequisite_person_id -ne 157 -or [string]::IsNullOrWhiteSpace($r00RouteText.prerequisite_actor_click_offset)) {
        throw "debug_r00_mode_route_analyze did not report the Xu Zijiang actor-click prerequisite."
    }
    if (-not (Test-Path -LiteralPath $r00RouteText.report_path -PathType Leaf)) {
        throw "debug_r00_mode_route_analyze did not write report_path: $($r00RouteText.report_path)"
    }
    if (-not (Test-Path -LiteralPath $r00RouteText.markdown_path -PathType Leaf)) {
        throw "debug_r00_mode_route_analyze did not write markdown_path: $($r00RouteText.markdown_path)"
    }

    $r00StartupProbeText = ConvertFrom-McpToolTextJson -Response $r00StartupProbe -ToolName "r00StartupProbe"
    if ($r00StartupProbeText.status -ne "plan-ready") {
        throw "debug_r00_startup_route_probe_run safe validation expected plan-ready, got: $($r00StartupProbeText.status)"
    }
    if ($r00StartupProbeText.allow_input) {
        throw "debug_r00_startup_route_probe_run safe validation must not send input when allow_input=false."
    }
    if ($r00StartupProbeText.run_probes) {
        throw "debug_r00_startup_route_probe_run safe validation must not run probes when run_probes=false."
    }
    if ([string]::IsNullOrWhiteSpace($r00StartupProbeText.handler_plan_path) -or -not (Test-Path -LiteralPath $r00StartupProbeText.handler_plan_path -PathType Leaf)) {
        throw "debug_r00_startup_route_probe_run did not write handler_plan_path: $($r00StartupProbeText.handler_plan_path)"
    }
    if ($r00StartupProbeText.handler_probe_target_count -lt 1) {
        throw "debug_r00_startup_route_probe_run returned no handler probe targets."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $r00StartupProbeText.session_dir "r00-startup-route-probe-summary.json") -PathType Leaf)) {
        throw "debug_r00_startup_route_probe_run did not write r00-startup-route-probe-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $r00StartupProbeText.session_dir "r00-startup-route-probe-summary.md") -PathType Leaf)) {
        throw "debug_r00_startup_route_probe_run did not write r00-startup-route-probe-summary.md."
    }

    $keyboardExplorationText = ConvertFrom-McpToolTextJson -Response $keyboardExploration -ToolName "keyboardExploration"
    if ($keyboardExplorationText.status -ne "plan-ready") {
        throw "debug_keyboard_exploration_run safe validation expected plan-ready, got: $($keyboardExplorationText.status)"
    }
    if ($keyboardExplorationText.allow_launch) {
        throw "debug_keyboard_exploration_run safe validation must not launch when allow_launch=false."
    }
    if ($keyboardExplorationText.allow_input) {
        throw "debug_keyboard_exploration_run safe validation must not send input when allow_input=false."
    }
    if ($keyboardExplorationText.sequence_count -lt 1) {
        throw "debug_keyboard_exploration_run did not preserve sequence candidates."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $keyboardExplorationText.session_dir "keyboard-exploration-summary.json") -PathType Leaf)) {
        throw "debug_keyboard_exploration_run did not write keyboard-exploration-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $keyboardExplorationText.session_dir "keyboard-exploration-summary.md") -PathType Leaf)) {
        throw "debug_keyboard_exploration_run did not write keyboard-exploration-summary.md."
    }

    $runtimeAnchorSweepText = ConvertFrom-McpToolTextJson -Response $runtimeAnchorSweep -ToolName "runtimeAnchorSweep"
    if ([string]::IsNullOrWhiteSpace($runtimeAnchorSweepText.status)) {
        throw "game_runtime_anchor_sweep did not return a status."
    }
    if ($runtimeAnchorSweepText.status -notin @("not-running", "anchors-found", "anchors-not-found")) {
        throw "game_runtime_anchor_sweep returned unexpected status: $($runtimeAnchorSweepText.status)"
    }
    if ([string]::IsNullOrWhiteSpace($runtimeAnchorSweepText.phase_inference.phase)) {
        throw "game_runtime_anchor_sweep did not return phase inference."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeAnchorSweepText.session_dir "runtime-anchor-sweep.json") -PathType Leaf)) {
        throw "game_runtime_anchor_sweep did not write runtime-anchor-sweep.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeAnchorSweepText.session_dir "runtime-anchor-sweep.md") -PathType Leaf)) {
        throw "game_runtime_anchor_sweep did not write runtime-anchor-sweep.md."
    }

    $runtimeInvokePlanText = ConvertFrom-McpToolTextJson -Response $runtimeInvokePlan -ToolName "runtimeInvokePlan"
    if ($runtimeInvokePlanText.action_count -lt 1) {
        throw "debug_runtime_invoke_plan returned no actions."
    }
    if (-not (Test-Path -LiteralPath $runtimeInvokePlanText.plan_path -PathType Leaf)) {
        throw "debug_runtime_invoke_plan did not write plan_path: $($runtimeInvokePlanText.plan_path)"
    }
    if (-not (Test-Path -LiteralPath $runtimeInvokePlanText.markdown_path -PathType Leaf)) {
        throw "debug_runtime_invoke_plan did not write markdown_path: $($runtimeInvokePlanText.markdown_path)"
    }
    $runtimeInvokePlanJson = Get-Content -Raw -Encoding UTF8 -LiteralPath $runtimeInvokePlanText.plan_path | ConvertFrom-Json
    $runtimeInvokeActions = @($runtimeInvokePlanJson.actions)
    if (($runtimeInvokeActions | Where-Object { [string]::IsNullOrWhiteSpace($_.invoke_strategy) }).Count -gt 0) {
        throw "debug_runtime_invoke_plan emitted actions without invoke_strategy."
    }
    if (($runtimeInvokeActions | Where-Object { $null -eq $_.requires_paused_debuggee }).Count -gt 0) {
        throw "debug_runtime_invoke_plan emitted actions without requires_paused_debuggee."
    }
    if (($runtimeInvokeActions | Where-Object { $_.requires_runtime_injection -and -not $_.requires_paused_debuggee }).Count -gt 0) {
        throw "runtime injection actions must require a paused debuggee."
    }
    $titleMenuRuntimeActions = @($runtimeInvokeActions | Where-Object { $_.key -like "title_*" -or $_.key -eq "settings_return" })
    if (($titleMenuRuntimeActions | Where-Object { $_.requires_runtime_injection -or $_.writes_process_memory }).Count -gt 0) {
        throw "Title menu runtime invoke actions must stay breakpoint-only before dispatcher ABI proof."
    }
    if (($titleMenuRuntimeActions | Where-Object { $_.invoke_strategy -ne "title_menu_dispatch_breakpoint_probe" }).Count -gt 0) {
        throw "Title menu runtime invoke actions must use title_menu_dispatch_breakpoint_probe."
    }
    if (($titleMenuRuntimeActions | Where-Object { @($_.breakpoints).Count -lt 1 }).Count -gt 0) {
        throw "Title menu runtime invoke actions must include dispatch breakpoint candidates."
    }
    if (($runtimeInvokeActions | Where-Object { $_.key -like "r00_*" -or $_.key -eq "xu_zijiang_actor_click" } | Where-Object { [string]::IsNullOrWhiteSpace($_.candidate_source) -or [string]::IsNullOrWhiteSpace($_.evidence_gate) }).Count -gt 0) {
        throw "R_00 runtime invoke actions must include candidate_source and evidence_gate."
    }

    $runtimeInvokeRunText = ConvertFrom-McpToolTextJson -Response $runtimeInvokeRun -ToolName "runtimeInvokeRun"
    if ($runtimeInvokeRunText.status -ne "runtime-invoke-plan-only" -and $runtimeInvokeRunText.status -ne "runtime-invoke-attempted") {
        throw "debug_runtime_invoke_run returned unexpected status: $($runtimeInvokeRunText.status)"
    }
    if ($runtimeInvokeRunText.allow_runtime_injection) {
        throw "debug_runtime_invoke_run validation must not allow runtime injection."
    }
    if (($runtimeInvokeRunText.actions | Where-Object { $_.status -eq "runtime-stub-issued" }).Count -gt 0) {
        throw "debug_runtime_invoke_run safe validation unexpectedly issued a runtime stub."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeInvokeRunText.session_dir "runtime-invoke-run.json") -PathType Leaf)) {
        throw "debug_runtime_invoke_run did not write runtime-invoke-run.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeInvokeRunText.session_dir "action-chain.jsonl") -PathType Leaf)) {
        throw "debug_runtime_invoke_run did not write action-chain.jsonl."
    }

    $menuRouteText = ConvertFrom-McpToolTextJson -Response $menuRoute -ToolName "menuRoute"
    if ($menuRouteText.status -ne "menu-route-recorded") {
        throw "debug_menu_route_run returned unexpected status: $($menuRouteText.status)"
    }
    if ($menuRouteText.route_count -lt 1) {
        throw "debug_menu_route_run returned no route steps."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $menuRouteText.session_dir "menu-route-summary.json") -PathType Leaf)) {
        throw "debug_menu_route_run did not write menu-route-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $menuRouteText.session_dir "action-chain.jsonl") -PathType Leaf)) {
        throw "debug_menu_route_run did not write action-chain.jsonl."
    }

    $addressVerifyText = ConvertFrom-McpToolTextJson -Response $addressVerify -ToolName "addressVerify"
    if ($addressVerifyText.status -ne "address-verification-plan-ready" -and $addressVerifyText.status -ne "profile-gate-blocked" -and $addressVerifyText.status -ne "address-verification-probe-attempted") {
        throw "debug_address_verify_run returned unexpected status: $($addressVerifyText.status)"
    }
    if ($addressVerifyText.run_probes) {
        throw "debug_address_verify_run validation must not run probes when run_probes=false."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $addressVerifyText.session_dir "address-verify-summary.json") -PathType Leaf)) {
        throw "debug_address_verify_run did not write address-verify-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $addressVerifyText.session_dir "action-chain.jsonl") -PathType Leaf)) {
        throw "debug_address_verify_run did not write action-chain.jsonl."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $addressVerifyText.session_dir "probe-hits.jsonl") -PathType Leaf)) {
        throw "debug_address_verify_run did not write probe-hits.jsonl."
    }

    $knowledgePromoteText = ConvertFrom-McpToolTextJson -Response $knowledgePromote -ToolName "knowledgePromote"
    if ($knowledgePromoteText.status -ne "promotion-preview" -and $knowledgePromoteText.status -ne "promotion-refused") {
        throw "debug_knowledge_promote returned unexpected status: $($knowledgePromoteText.status)"
    }
    if ($knowledgePromoteText.allow_write) {
        throw "debug_knowledge_promote validation must not write formal knowledge when allow_write=false."
    }
    if (-not (Test-Path -LiteralPath $knowledgePromoteText.report_path -PathType Leaf)) {
        throw "debug_knowledge_promote did not write report_path: $($knowledgePromoteText.report_path)"
    }

    $fullAutoText = ConvertFrom-McpToolTextJson -Response $fullAuto -ToolName "fullAuto"
    if ($fullAutoText.status -ne "full-auto-plan-ready" -and $fullAutoText.status -ne "full-auto-run-attempted") {
        throw "debug_full_auto_run returned unexpected status: $($fullAutoText.status)"
    }
    if ($fullAutoText.allow_runtime_injection) {
        throw "debug_full_auto_run validation must not allow runtime injection."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $fullAutoText.session_dir "full-auto-summary.json") -PathType Leaf)) {
        throw "debug_full_auto_run did not write full-auto-summary.json."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $fullAutoText.session_dir "action-chain.jsonl") -PathType Leaf)) {
        throw "debug_full_auto_run did not write action-chain.jsonl."
    }

    Write-Host ("GAME_DEBUG_MCP_VALIDATE_OK server={0} protocol={1} tools={2}" -f $init.result.serverInfo.name, $init.result.protocolVersion, $toolNames.Count)
    Write-Host ("GAME_DEBUG_MCP_STATE_OK contentItems={0}" -f @($state.result.content).Count)
    Write-Host ("GAME_DEBUG_MCP_PROCESS_START_OK dryRun={0} started={1}" -f $processStartText.dry_run, $processStartText.started)
    Write-Host ("GAME_DEBUG_MCP_CATALOG_OK entries={0} path={1}" -f $catalogText.report.entry_count, $catalogText.catalog_path)
    Write-Host ("GAME_DEBUG_MCP_XREF_OK targets={0} candidates={1} path={2}" -f $xrefText.report.scan.target_count, $xrefText.report.scan.breakpoint_candidate_count, $xrefText.xref_path)
    Write-Host ("GAME_DEBUG_MCP_ADDRESS_REPORT_OK targets={0} candidates={1} path={2}" -f $addressReportText.report.summary.target_count, $addressReportText.report.summary.breakpoint_candidate_count, $addressReportText.report_path)
    Write-Host ("GAME_DEBUG_MCP_ADDRESS_PROBE_PLAN_OK targets={0} path={1}" -f $addressProbePlanText.target_count, $addressProbePlanText.plan_path)
    Write-Host ("GAME_DEBUG_MCP_ADDRESS_PROBE_RUN_OK status={0} targets={1} path={2}" -f $addressProbeRunText.status, $addressProbeRunText.target_count, (Join-Path $addressProbeRunText.session_dir "address-probe-run-summary.json"))
    Write-Host ("GAME_DEBUG_MCP_AUTONOMY_OK status={0} session={1}" -f $autonomyText.status, $autonomyText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_RUNTIME_STATE_OK classification={0} confidence={1}" -f $runtimeStateText.runtime.classification, $runtimeStateText.runtime.confidence)
    Write-Host ("GAME_DEBUG_MCP_RUNTIME_WAIT_OK status={0} final={1} samples={2}" -f $runtimeWaitText.status, $runtimeWaitText.final_classification, $runtimeWaitText.sample_count)
    Write-Host ("GAME_DEBUG_MCP_RSCENE_ANCHOR_SCAN_OK status={0} anchors={1} hits={2} session={3}" -f $rsceneAnchorText.status, @($rsceneAnchorText.anchors).Count, @($rsceneAnchorText.hits).Count, $rsceneAnchorText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_RSCENE_SCRIPT_WINDOW_OK status={0} windows={1} hits={2} refs={3} session={4}" -f $rsceneScriptWindowText.status, @($rsceneScriptWindowText.windows).Count, @($rsceneScriptWindowText.window_hits).Count, @($rsceneScriptWindowText.pointer_refs).Count, $rsceneScriptWindowText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_RSCENE_HANDLER_SCAN_OK status={0} commands={1} candidates={2} targets={3} session={4}" -f $rsceneHandlerScanText.status, $rsceneHandlerScanText.command_count, $rsceneHandlerScanText.candidate_count, $rsceneHandlerScanText.probe_target_count, $rsceneHandlerScanText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_RSCENE_LOAD_TRANSITION_OK status={0} anchors={1} candidates={2} targets={3} session={4}" -f $rsceneLoadTransitionScanText.status, $rsceneLoadTransitionScanText.static_anchor_count, $rsceneLoadTransitionScanText.reference_candidate_count, $rsceneLoadTransitionScanText.probe_target_count, $rsceneLoadTransitionScanText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_RSCENE_LOAD_TRANSITION_PROBE_OK status={0} targets={1} filter={2} runProbes={3} hits={4} script={5} session={6}" -f $rsceneLoadTransitionProbeText.status, $rsceneLoadTransitionProbeText.probe_target_count, $rsceneLoadTransitionProbeText.candidate_filter, $rsceneLoadTransitionProbeText.run_probes, $rsceneLoadTransitionProbeText.hit_count, $rsceneLoadTransitionProbeText.after_script_window_status, $rsceneLoadTransitionProbeText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_TITLE_MENU_DISPATCH_SCAN_OK status={0} calls={1} functions={2} targets={3} session={4}" -f $titleMenuDispatchScanText.status, $titleMenuDispatchScanText.call_candidate_count, $titleMenuDispatchScanText.function_candidate_count, $titleMenuDispatchScanText.probe_target_count, $titleMenuDispatchScanText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_TITLE_WNDPROC_DISPATCH_SCAN_OK status={0} compares={1} functions={2} targets={3} session={4}" -f $titleWndProcDispatchScanText.status, $titleWndProcDispatchScanText.compare_candidate_count, $titleWndProcDispatchScanText.function_candidate_count, $titleWndProcDispatchScanText.probe_target_count, $titleWndProcDispatchScanText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_TITLE_MENU_DISPATCH_PROBE_OK status={0} route={1} targets={2} runProbes={3} hits={4} session={5}" -f $titleMenuDispatchProbeText.status, $titleMenuDispatchProbeText.route, $titleMenuDispatchProbeText.probe_target_count, $titleMenuDispatchProbeText.run_probes, $titleMenuDispatchProbeText.hit_count, $titleMenuDispatchProbeText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_TITLE_MENU_DISPATCH_MATRIX_OK status={0} routes={1} targets={2} runProbes={3} hits={4} session={5}" -f $titleMenuDispatchMatrixText.status, $titleMenuDispatchMatrixText.route_count, $titleMenuDispatchMatrixText.shared_probe_target_count, $titleMenuDispatchMatrixText.run_probes, $titleMenuDispatchMatrixText.hit_count, $titleMenuDispatchMatrixText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_R00_ACTOR_ROUTE_OK status={0} person={1} commands={2} click={3} latest={4} session={5}" -f $r00ActorRouteText.status, $r00ActorRouteText.person_id, $r00ActorRouteText.actor_command_count, $r00ActorRouteText.click_offset, $r00ActorRouteText.latest_probe_status, $r00ActorRouteText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_R00_RUNTIME_INVOKE_CANDIDATE_OK status={0} actions={1} candidates={2} targets={3} verifiedHits={4} session={5}" -f $r00RuntimeInvokeCandidateText.status, $r00RuntimeInvokeCandidateText.action_count, $r00RuntimeInvokeCandidateText.handler_candidate_count, $r00RuntimeInvokeCandidateText.probe_target_count, $r00RuntimeInvokeCandidateText.latest_verified_hit_count, $r00RuntimeInvokeCandidateText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_R00_RUNTIME_HANDLER_PROBE_OK status={0} candidates={1} targets={2} runProbes={3} hits={4} session={5}" -f $r00RuntimeHandlerProbeText.status, $r00RuntimeHandlerProbeText.handler_candidate_count, $r00RuntimeHandlerProbeText.handler_probe_target_count, $r00RuntimeHandlerProbeText.run_probes, $r00RuntimeHandlerProbeText.hit_count, $r00RuntimeHandlerProbeText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_INTERNAL_EVIDENCE_OK includeScreenshot={0} path={1}" -f $internalEvidenceText.report.include_screenshot, $internalEvidenceText.evidence_path)
    Write-Host ("GAME_DEBUG_MCP_SCRIPT_DRY_RUN_OK status={0} session={1}" -f $scriptDryRunText.dryRun.status, $scriptDryRunText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_BATTLE_MATCH_OK status={0} profile={1} session={2}" -f $battleMatchText.status, $battleMatchText.profile, $battleMatchText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_BATTLE_PROFILE_PROBE_OK status={0} profileStatus={1} targets={2} session={3}" -f $battleProfileProbeText.status, $battleProfileProbeText.profile_status, $battleProfileProbeText.target_count, $battleProfileProbeText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_LIVE_READINESS_OK status={0} ready={1} missing={2} targets={3} session={4}" -f $liveReadinessText.status, $liveReadinessText.ready_for_dynamic_probe, ($liveReadinessText.missing_gates -join ","), $liveReadinessText.target_count, $liveReadinessText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_LIVE_AUTO_RUN_OK status={0} ready={1} targets={2} session={3}" -f $liveAutoRunText.status, $liveAutoRunText.ready_for_dynamic_probe, $liveAutoRunText.target_count, $liveAutoRunText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_BATTLE_AUTO_STEP_OK status={0} session={1}" -f $battleAutoStepText.status, $battleAutoStepText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_BATTLE_AUTO_RUN_OK status={0} session={1}" -f $battleAutoRunText.status, $battleAutoRunText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_BATTLE_AUTO_PROBE_OK status={0} targets={1} session={2}" -f $battleAutoProbeText.status, $battleAutoProbeText.target_count, $battleAutoProbeText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_KEY_SEQUENCE_OK status={0} keys={1} session={2}" -f $keySequenceText.status, $keySequenceText.key_count, $keySequenceText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_TRANSITION_PROBE_OK status={0} targets={1} session={2}" -f $transitionProbeText.status, $transitionProbeText.target_count, $transitionProbeText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_R00_ROUTE_OK status={0} sequence={1} prerequisitePerson={2} options={3}/{4} session={5}" -f $r00RouteText.status, $r00RouteText.sequence, $r00RouteText.prerequisite_person_id, $r00RouteText.mode_option_count, $r00RouteText.config_option_count, $r00RouteText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_R00_STARTUP_PROBE_OK status={0} targets={1} allowInput={2} runProbes={3} session={4}" -f $r00StartupProbeText.status, $r00StartupProbeText.handler_probe_target_count, $r00StartupProbeText.allow_input, $r00StartupProbeText.run_probes, $r00StartupProbeText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_KEYBOARD_EXPLORATION_OK status={0} stop={1} sequences={2} final={3} text={4} script={5} session={6}" -f $keyboardExplorationText.status, $keyboardExplorationText.stop_reason, $keyboardExplorationText.sequence_count, $keyboardExplorationText.final_runtime_classification, $keyboardExplorationText.final_text_anchor_status, $keyboardExplorationText.final_script_window_status, $keyboardExplorationText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_RUNTIME_ANCHOR_SWEEP_OK status={0} phase={1} hits={2} session={3}" -f $runtimeAnchorSweepText.status, $runtimeAnchorSweepText.phase_inference.phase, $runtimeAnchorSweepText.hit_count, $runtimeAnchorSweepText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_RUNTIME_INVOKE_PLAN_OK actions={0} path={1}" -f $runtimeInvokePlanText.action_count, $runtimeInvokePlanText.plan_path)
    Write-Host ("GAME_DEBUG_MCP_RUNTIME_INVOKE_RUN_OK status={0} actions={1} session={2}" -f $runtimeInvokeRunText.status, @($runtimeInvokeRunText.actions).Count, $runtimeInvokeRunText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_MENU_ROUTE_OK status={0} routes={1} loadSave={2} session={3}" -f $menuRouteText.status, $menuRouteText.route_count, $menuRouteText.load_save_status, $menuRouteText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_ADDRESS_VERIFY_OK status={0} profile={1} runProbes={2} session={3}" -f $addressVerifyText.status, $addressVerifyText.profile_status, $addressVerifyText.run_probes, $addressVerifyText.session_dir)
    Write-Host ("GAME_DEBUG_MCP_KNOWLEDGE_PROMOTE_OK status={0} canPromote={1} path={2}" -f $knowledgePromoteText.status, $knowledgePromoteText.promotions.can_promote, $knowledgePromoteText.report_path)
    Write-Host ("GAME_DEBUG_MCP_FULL_AUTO_OK status={0} steps={1} session={2}" -f $fullAutoText.status, $fullAutoText.step_count, $fullAutoText.session_dir)
}
finally {
    if ($proc -and -not $proc.HasExited) {
        $proc.Kill()
    }

    if ($proc) {
        $proc.Dispose()
    }
}
