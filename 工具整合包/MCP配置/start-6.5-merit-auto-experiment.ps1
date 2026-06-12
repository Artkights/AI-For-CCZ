param(
    [string]$PlanJson = "",
    [string]$OutputRoot = "",
    [string]$GameRoot = "",
    [string]$X32dbgPath = "",
    [string]$HostName = "127.0.0.1",
    [int]$Port = 27042,
    [int]$BatchIndex = 1,
    [int]$BridgeWaitSeconds = 15,
    [int]$BridgePollIntervalMs = 500,
    [switch]$StartX32dbg,
    [switch]$HiddenX32dbg,
    [switch]$RunExperiment,
    [switch]$ApplyOnly,
    [switch]$DryRun,
    [switch]$ClearHardwareFirst,
    [switch]$SkipHitSummary,
    [switch]$SkipKnowledgeDraft,
    [string]$KnowledgeDraftDir = "",
    [string]$GameplayEventLabel = "",
    [string]$OperatorNote = ""
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $workspaceRoot "CCZModStudio_Reports\DebugEvidence"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$sessionDir = Join-Path $OutputRoot ("merit-auto-experiment-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null

function Find-LatestPlan {
    $latest = Get-ChildItem -LiteralPath $OutputRoot -Recurse -File -Filter "merit-watchpoint-plan.json" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($latest) { return $latest.FullName }
    return ""
}

function Invoke-JsonHealth {
    param([string]$BaseUrl)

    try {
        $data = Invoke-RestMethod -Uri ($BaseUrl + "/api/health") -TimeoutSec 2
        return [pscustomobject]@{ Ok = $true; Data = $data; Error = "" }
    }
    catch {
        return [pscustomobject]@{ Ok = $false; Data = $null; Error = $_.Exception.Message }
    }
}

function Get-ProcessSummary {
    $rows = @()
    foreach ($name in @("Ekd5", "x32dbg", "x64dbg")) {
        $rows += @(Get-Process -Name $name -ErrorAction SilentlyContinue | Select-Object ProcessName, Id, Responding, StartTime)
    }
    return $rows
}

function Wait-JsonHealth {
    param(
        [string]$BaseUrl,
        [int]$TimeoutSeconds,
        [int]$PollIntervalMs
    )

    $deadline = (Get-Date).AddSeconds([Math]::Max(0, $TimeoutSeconds))
    $attempt = 0
    $last = Invoke-JsonHealth -BaseUrl $BaseUrl
    while (-not $last.Ok -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds ([Math]::Max(100, $PollIntervalMs))
        $attempt++
        $last = Invoke-JsonHealth -BaseUrl $BaseUrl
    }

    return [pscustomobject]@{
        Attempts = $attempt + 1
        Health = $last
    }
}

if ([string]::IsNullOrWhiteSpace($PlanJson)) {
    $PlanJson = Find-LatestPlan
}

$planExists = -not [string]::IsNullOrWhiteSpace($PlanJson) -and (Test-Path -LiteralPath $PlanJson -PathType Leaf)
$plan = $null
$candidateRows = @()
if ($planExists) {
    $PlanJson = (Resolve-Path -LiteralPath $PlanJson).Path
    $plan = Get-Content -Raw -Encoding UTF8 -LiteralPath $PlanJson | ConvertFrom-Json
    $batch = @($plan.Batches | Where-Object { [int]$_.BatchIndex -eq $BatchIndex } | Select-Object -First 1)
    if ($batch) {
        $candidateRows = @($batch.Candidates)
    }
}

$baseUrl = "http://${HostName}:$Port"
$preHealth = Invoke-JsonHealth -BaseUrl $baseUrl
$preProcesses = Get-ProcessSummary
$startResult = $null

if ($StartX32dbg) {
    $args = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $configRoot "start-x32dbg-6.5.ps1")
    )
    if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
        $args += @("-GameRoot", $GameRoot)
    }
    if (-not [string]::IsNullOrWhiteSpace($X32dbgPath)) {
        $args += @("-X32dbgPath", $X32dbgPath)
    }
    if ($HiddenX32dbg) {
        $args += "-Hidden"
    }

    $startOut = & powershell @args 2>&1
    $startResult = [pscustomobject]@{
        Args = $args
        Output = @($startOut | ForEach-Object { [string]$_ })
    }
}

$waitResult = if ($StartX32dbg) { Wait-JsonHealth -BaseUrl $baseUrl -TimeoutSeconds $BridgeWaitSeconds -PollIntervalMs $BridgePollIntervalMs } else { $null }
$postHealth = if ($waitResult) { $waitResult.Health } else { Invoke-JsonHealth -BaseUrl $baseUrl }
$postProcesses = Get-ProcessSummary
$experimentResult = $null
$hitSummaryResult = $null

if ($RunExperiment) {
    if (-not $planExists) {
        throw "Cannot run experiment because merit-watchpoint-plan.json was not found."
    }

    $experimentOutput = Join-Path $sessionDir "watchpoint-run"
    $args = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $configRoot "run-6.5-merit-watchpoint-experiment.ps1"),
        "-PlanJson", $PlanJson,
        "-OutputDir", $experimentOutput,
        "-HostName", $HostName,
        "-Port", [string]$Port,
        "-BatchIndex", [string]$BatchIndex
    )
    if ($ApplyOnly) { $args += "-ApplyOnly" }
    if ($DryRun) { $args += "-DryRun" }
    if ($ClearHardwareFirst) { $args += "-ClearHardwareFirst" }

    $out = & powershell @args 2>&1
    $experimentResult = [pscustomobject]@{
        Args = $args
        Output = @($out | ForEach-Object { [string]$_ })
    }

    if (-not $SkipHitSummary) {
        $summaryArgs = @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", (Join-Path $configRoot "summarize-6.5-merit-watchpoint-hits.ps1"),
            "-InputDir", $experimentOutput,
            "-PlanJson", $PlanJson
        )
        if (-not [string]::IsNullOrWhiteSpace($GameplayEventLabel)) {
            $summaryArgs += @("-GameplayEventLabel", $GameplayEventLabel)
        }
        if (-not [string]::IsNullOrWhiteSpace($OperatorNote)) {
            $summaryArgs += @("-OperatorNote", $OperatorNote)
        }
        if (-not [string]::IsNullOrWhiteSpace($KnowledgeDraftDir)) {
            $summaryArgs += @("-KnowledgeDraftDir", $KnowledgeDraftDir)
        }
        if ($SkipKnowledgeDraft) {
            $summaryArgs += "-SkipKnowledgeDraft"
        }

        $summaryOut = & powershell @summaryArgs 2>&1
        $hitSummaryResult = [pscustomobject]@{
            Args = $summaryArgs
            Output = @($summaryOut | ForEach-Object { [string]$_ })
        }
    }
}

$summary = [pscustomobject]@{
    CreatedAt = (Get-Date).ToString("O")
    SessionDir = $sessionDir
    PlanJson = if ($planExists) { $PlanJson } else { "" }
    PlanExists = $planExists
    BatchIndex = $BatchIndex
    CandidateCount = $candidateRows.Count
    Candidates = $candidateRows
    BaseUrl = $baseUrl
    PreHealth = $preHealth
    PostHealth = $postHealth
    PreProcesses = $preProcesses
    PostProcesses = $postProcesses
    StartX32dbg = [bool]$StartX32dbg
    StartResult = $startResult
    BridgeWaitResult = $waitResult
    RunExperiment = [bool]$RunExperiment
    ExperimentResult = $experimentResult
    HitSummaryResult = $hitSummaryResult
    NextStep = if ($postHealth.Ok -and -not $RunExperiment) { "Run again with -RunExperiment -ClearHardwareFirst when the game is at a reproducible merit event." } elseif (-not $postHealth.Ok) { "Start x32dbg with MCP bridge, then rerun with -RunExperiment." } else { "Inspect watchpoint-run output if an experiment was run." }
}

$jsonPath = Join-Path $sessionDir "merit-auto-experiment-summary.json"
$summary | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$mdPath = Join-Path $sessionDir "merit-auto-experiment-summary.md"
$md = New-Object System.Collections.Generic.List[string]
$md.Add("# 6.5 Merit Auto Experiment Preflight")
$md.Add("")
$md.Add("## Summary")
$md.Add("")
$md.Add("- Created: $($summary.CreatedAt)")
$md.Add('- Session: `' + $sessionDir + '`')
$planText = if ($planExists) { $PlanJson } else { "not found" }
$md.Add('- Plan: `' + $planText + '`')
$md.Add('- Bridge before: `' + $preHealth.Ok + '`')
$md.Add('- Bridge after: `' + $postHealth.Ok + '`')
$md.Add('- Candidate count: `' + $candidateRows.Count + '`')
$md.Add('- Run experiment: `' + [bool]$RunExperiment + '`')
$md.Add('- Hit summary: `' + [bool]($null -ne $hitSummaryResult) + '`')
$md.Add("")
$md.Add("## Candidates")
$md.Add("")
$md.Add("| Address | Size | Score | Reasons |")
$md.Add("|---|---:|---:|---|")
foreach ($candidate in $candidateRows) {
    $reasons = (($candidate.Reasons | Select-Object -First 3) -join "; ") -replace "\|", "/"
    $md.Add(('| `{0}` | {1} | {2} | {3} |' -f $candidate.Address, $candidate.HardwareSize, $candidate.Score, $reasons))
}
$md.Add("")
$md.Add("## Next")
$md.Add("")
$md.Add("- " + $summary.NextStep)
Set-Content -LiteralPath $mdPath -Encoding UTF8 -Value $md

[pscustomobject]@{
    Summary = $jsonPath
    Markdown = $mdPath
    PlanExists = $planExists
    BridgeBefore = $preHealth.Ok
    BridgeAfter = $postHealth.Ok
    CandidateCount = $candidateRows.Count
    RanExperiment = [bool]$RunExperiment
} | ConvertTo-Json -Depth 20
