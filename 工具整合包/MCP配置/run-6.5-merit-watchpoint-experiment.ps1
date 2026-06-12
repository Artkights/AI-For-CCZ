param(
    [string]$PlanJson = "",
    [string]$OutputDir = "",
    [string]$HostName = "127.0.0.1",
    [int]$Port = 27042,
    [int]$BatchIndex = 1,
    [int]$Polls = 240,
    [int]$PollIntervalMs = 250,
    [int]$DisasmCount = 32,
    [int]$StackSize = 160,
    [int]$MemoryAroundCandidate = 16,
    [switch]$ApplyOnly,
    [switch]$NoRun,
    [switch]$DryRun,
    [switch]$ClearHardwareFirst,
    [switch]$ContinueAfterUnplannedStop
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot

if ([string]::IsNullOrWhiteSpace($PlanJson)) {
    $evidenceRoot = Join-Path $workspaceRoot "CCZModStudio_Reports\DebugEvidence"
    $latest = Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File -Filter "merit-watchpoint-plan.json" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $latest) {
        throw "No merit-watchpoint-plan.json found under $evidenceRoot. Run auto-locate-6.5-addresses.ps1 first."
    }

    $PlanJson = $latest.FullName
}

if (-not (Test-Path -LiteralPath $PlanJson -PathType Leaf)) {
    throw "Plan JSON not found: $PlanJson"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $base = Split-Path -Parent $PlanJson
    $OutputDir = Join-Path $base ("merit-watchpoint-run-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$baseUrl = "http://${HostName}:$Port"
$plan = Get-Content -Raw -Encoding UTF8 -LiteralPath $PlanJson | ConvertFrom-Json
$batch = @($plan.Batches | Where-Object { [int]$_.BatchIndex -eq $BatchIndex } | Select-Object -First 1)
if (-not $batch) {
    throw "Batch $BatchIndex not found in $PlanJson"
}

function ConvertTo-NormalizedHexAddress {
    param([object]$Value)

    if ($null -eq $Value) { return "" }
    if ($Value -is [byte] -or
        $Value -is [sbyte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int] -or
        $Value -is [uint32] -or
        $Value -is [long] -or
        $Value -is [uint64]) {
        return "0x" + (("{0:X}" -f ([uint64]$Value)).PadLeft(8, "0"))
    }

    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return "" }
    $match = [regex]::Match($text, "(?i)0x[0-9a-f]+|(?<=\.)[0-9a-f]{6,8}\b|\b[0-9a-f]{6,8}\b")
    if (-not $match.Success) { return $text.ToUpperInvariant() }

    $hex = $match.Value
    if ($hex.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        $hex = $hex.Substring(2)
    }

    $number = [Convert]::ToUInt64($hex, 16)
    return "0x" + (("{0:X}" -f $number).PadLeft(8, "0"))
}

function ConvertTo-UInt32Address {
    param([object]$Value)

    $normalized = ConvertTo-NormalizedHexAddress $Value
    if ([string]::IsNullOrWhiteSpace($normalized) -or -not $normalized.StartsWith("0x")) {
        throw "Cannot parse address: $Value"
    }

    return [Convert]::ToUInt32($normalized.Substring(2), 16)
}

function New-QueryString {
    param([hashtable]$Query)

    if ($null -eq $Query -or $Query.Count -eq 0) { return "" }
    $parts = foreach ($key in $Query.Keys) {
        if ($null -ne $Query[$key]) {
            "{0}={1}" -f [Uri]::EscapeDataString([string]$key), [Uri]::EscapeDataString([string]$Query[$key])
        }
    }

    if (-not $parts) { return "" }
    return "?" + ($parts -join "&")
}

function Invoke-X32dbgJson {
    param(
        [ValidateSet("GET", "POST")]
        [string]$Method,
        [string]$Path,
        [hashtable]$Query = $null,
        [object]$Body = $null,
        [int]$TimeoutSec = 5
    )

    $uri = $baseUrl + $Path + (New-QueryString -Query $Query)
    try {
        if ($Method -eq "POST") {
            $bodyText = if ($null -eq $Body) { "{}" } else { $Body | ConvertTo-Json -Depth 20 }
            $data = Invoke-RestMethod -Uri $uri -Method Post -ContentType "application/json" -Body $bodyText -TimeoutSec $TimeoutSec
        }
        else {
            $data = Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec $TimeoutSec
        }

        return [pscustomobject]@{
            Ok = $true
            Method = $Method
            Path = $Path
            Query = $Query
            Data = $data
        }
    }
    catch {
        $message = $_.Exception.Message
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
            $message = $_.ErrorDetails.Message
        }

        return [pscustomobject]@{
            Ok = $false
            Method = $Method
            Path = $Path
            Query = $Query
            Error = $message
        }
    }
}

function Get-X32dbgState {
    return Invoke-X32dbgJson GET "/api/debug/state" -TimeoutSec 3
}

function Get-X32dbgRegisters {
    return Invoke-X32dbgJson GET "/api/registers/all" -TimeoutSec 3
}

function Invoke-X32dbgCommand {
    param([string]$Command, [int]$TimeoutSec = 5)
    return Invoke-X32dbgJson POST "/api/command/exec" -Body @{ command = $Command } -TimeoutSec $TimeoutSec
}

function Get-CipFromState {
    param([object]$State)

    if ($State.Ok -and $State.Data.success -and $State.Data.data) {
        return ConvertTo-NormalizedHexAddress $State.Data.data.cip
    }

    return ""
}

function Get-StateText {
    param([object]$State)

    if ($State.Ok -and $State.Data.success -and $State.Data.data) {
        return [string]$State.Data.data.state
    }

    return ""
}

function Get-CandidateMemoryReads {
    param([object[]]$Candidates)

    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($candidate in $Candidates) {
        $address = ConvertTo-NormalizedHexAddress $candidate.Address
        $size = [Math]::Max([int]$candidate.HardwareSize, [int]$MemoryAroundCandidate)
        $base = [uint32]((ConvertTo-UInt32Address $address) - [uint32]([Math]::Min(8, $MemoryAroundCandidate)))
        $read = Invoke-X32dbgJson GET "/api/memory/read" @{
            address = ("0x{0:X8}" -f $base)
            size = [string]$size
        } -TimeoutSec 5
        $rows.Add([pscustomobject]@{
            CandidateAddress = $address
            ReadAddress = ("0x{0:X8}" -f $base)
            Size = $size
            Candidate = $candidate
            Read = $read
        })
    }

    return $rows
}

function Save-HitEvidence {
    param(
        [string]$StopReason,
        [object]$State,
        [object[]]$Candidates
    )

    $cip = Get-CipFromState -State $State
    $registers = Get-X32dbgRegisters
    $esp = ""
    if ($registers.Ok -and $registers.Data.success -and $registers.Data.data) {
        $esp = $registers.Data.data.esp
    }

    $report = [pscustomobject]@{
        CreatedAt = (Get-Date).ToString("O")
        StopReason = $StopReason
        SourcePlan = (Resolve-Path -LiteralPath $PlanJson).Path
        BatchIndex = $BatchIndex
        Cip = $cip
        State = $State
        Process = Invoke-X32dbgJson GET "/api/process/info" -TimeoutSec 5
        Registers = $registers
        DisasmAtCip = if ($cip) { Invoke-X32dbgJson GET "/api/disasm/at" @{ address = $cip; count = [string]$DisasmCount } -TimeoutSec 5 } else { $null }
        FunctionAtCip = if ($cip) { Invoke-X32dbgJson GET "/api/disasm/function" @{ address = $cip; max_instructions = "96" } -TimeoutSec 8 } else { $null }
        StackTrace = Invoke-X32dbgJson GET "/api/stack/trace" @{ max_depth = "48" } -TimeoutSec 5
        StackRead = if ($esp) { Invoke-X32dbgJson GET "/api/stack/read" @{ address = $esp; size = [string]$StackSize } -TimeoutSec 5 } else { [pscustomobject]@{ Ok = $false; Error = "ESP unavailable." } }
        CandidateMemory = Get-CandidateMemoryReads -Candidates $Candidates
    }

    $path = Join-Path $OutputDir ("merit-watchpoint-hit-{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
    $report | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $path -Encoding UTF8
    return [pscustomobject]@{
        Path = $path
        Cip = $cip
        StopReason = $StopReason
    }
}

function Get-CorrectedWatchpointCommands {
    param(
        [object]$Batch,
        [object[]]$Candidates
    )

    $rows = New-Object System.Collections.Generic.List[object]
    $original = @($Batch.X32dbgCommands)
    for ($i = 0; $i -lt $Candidates.Count; $i++) {
        $candidate = $Candidates[$i]
        $address = ConvertTo-NormalizedHexAddress $candidate.Address
        $plain = $address -replace "^0x", ""
        $size = if ($candidate.HardwareSize) { [int]$candidate.HardwareSize } else { 1 }
        if ($size -le 0) { $size = 1 }
        if ($size -gt 4) { $size = 4 }

        $corrected = "bphws {0},w,{1}" -f $plain, $size
        $originalCommand = if ($i -lt $original.Count) { [string]$original[$i] } else { "" }
        $rows.Add([pscustomobject]@{
            Address = $address
            Size = $size
            OriginalCommand = $originalCommand
            Command = $corrected
            Corrected = ($originalCommand -ne $corrected)
            Note = if ($originalCommand -match "(?i)^bphwc\b") { "Original command used bphwc, which deletes hardware breakpoints; corrected to bphws." } else { "" }
        })
    }

    return $rows
}

$events = New-Object System.Collections.Generic.List[object]
$commandsApplied = New-Object System.Collections.Generic.List[object]
$candidates = @($batch.Candidates)
$watchpointCommands = Get-CorrectedWatchpointCommands -Batch $batch -Candidates $candidates

$health = Invoke-X32dbgJson GET "/api/health" -TimeoutSec 3
$bridgeAvailable = $health.Ok -and $health.Data -and $health.Data.success
$initialState = if ($bridgeAvailable -and -not $DryRun) { Get-X32dbgState } else { [pscustomobject]@{ Ok = $false; Error = if ($DryRun) { "DryRun" } else { "x32dbg bridge health check failed." } } }

if ($ClearHardwareFirst) {
    $cmd = "bphwc"
    $commandsApplied.Add([pscustomobject]@{
        Command = $cmd
        Purpose = "clear-all-hardware-watchpoints"
        Result = if ($bridgeAvailable -and -not $DryRun) { Invoke-X32dbgCommand -Command $cmd -TimeoutSec 5 } else { [pscustomobject]@{ Ok = $false; Error = if ($DryRun) { "DryRun" } else { "x32dbg bridge unavailable." } } }
    })
}

foreach ($commandRow in $watchpointCommands) {
    $command = [string]$commandRow.Command
    $commandsApplied.Add([pscustomobject]@{
        Command = $command
        OriginalCommand = $commandRow.OriginalCommand
        Corrected = $commandRow.Corrected
        CorrectionNote = $commandRow.Note
        Purpose = "set-hardware-watchpoint"
        Result = if ($bridgeAvailable -and -not $DryRun) { Invoke-X32dbgCommand -Command $command -TimeoutSec 5 } else { [pscustomobject]@{ Ok = $false; Error = if ($DryRun) { "DryRun" } else { "x32dbg bridge unavailable." } } }
    })
}

$afterApplyState = if ($bridgeAvailable -and -not $DryRun) { Get-X32dbgState } else { [pscustomobject]@{ Ok = $false; Error = if ($DryRun) { "DryRun" } else { "x32dbg bridge unavailable." } } }
$preRunCandidateMemory = if ($bridgeAvailable -and -not $DryRun) { Get-CandidateMemoryReads -Candidates $candidates } else { @() }
$hit = $null

if (-not $ApplyOnly -and $bridgeAvailable -and -not $DryRun) {
    if (-not $NoRun) {
        $runResult = Invoke-X32dbgCommand -Command "run" -TimeoutSec 3
        $events.Add([pscustomobject]@{
            Tick = -1
            Event = "run-command"
            State = ""
            Cip = ""
            Raw = $runResult
        })
    }

    for ($tick = 0; $tick -lt $Polls -and $null -eq $hit; $tick++) {
        Start-Sleep -Milliseconds $PollIntervalMs
        $state = Get-X32dbgState
        $stateText = Get-StateText -State $state
        $cip = Get-CipFromState -State $state
        $events.Add([pscustomobject]@{
            Tick = $tick
            Event = "poll"
            State = $stateText
            Cip = $cip
            Raw = $state
        })

        if ($stateText -eq "paused") {
            $hit = Save-HitEvidence -StopReason "debugger-paused-after-watchpoint-batch" -State $state -Candidates $candidates
            if (-not $ContinueAfterUnplannedStop) {
                break
            }
        }
    }
}

$summary = [pscustomobject]@{
    CreatedAt = (Get-Date).ToString("O")
    SourcePlan = (Resolve-Path -LiteralPath $PlanJson).Path
    OutputDir = $OutputDir
    BaseUrl = $baseUrl
    BatchIndex = $BatchIndex
    Health = $health
    InitialState = $initialState
    AfterApplyState = $afterApplyState
    CommandsApplied = $commandsApplied
    WatchpointCommands = $watchpointCommands
    Candidates = $candidates
    PreRunCandidateMemory = $preRunCandidateMemory
    ApplyOnly = [bool]$ApplyOnly
    NoRun = [bool]$NoRun
    DryRun = [bool]$DryRun
    BridgeAvailable = [bool]$bridgeAvailable
    Hit = $hit
    FinalState = if ($bridgeAvailable -and -not $DryRun) { Get-X32dbgState } else { [pscustomobject]@{ Ok = $false; Error = if ($DryRun) { "DryRun" } else { "x32dbg bridge unavailable." } } }
    FinalRegisters = if ($bridgeAvailable -and -not $DryRun) { Get-X32dbgRegisters } else { [pscustomobject]@{ Ok = $false; Error = if ($DryRun) { "DryRun" } else { "x32dbg bridge unavailable." } } }
    Events = $events
    Safety = "Sets debugger hardware watchpoints only. Does not write game files or game memory."
}

$summaryPath = Join-Path $OutputDir "merit-watchpoint-run-summary.json"
$summary | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$mdPath = Join-Path $OutputDir "merit-watchpoint-run-summary.md"
$md = New-Object System.Collections.Generic.List[string]
$md.Add("# 6.5 Merit Watchpoint Experiment")
$md.Add("")
$md.Add("## Summary")
$md.Add("")
$md.Add("- Created: $((Get-Date).ToString("O"))")
$md.Add('- Source plan: `' + (Resolve-Path -LiteralPath $PlanJson).Path + '`')
$md.Add('- Batch: `' + $BatchIndex + '`')
$md.Add('- Output: `' + $OutputDir + '`')
$md.Add('- Health OK: `' + $health.Ok + '`')
$md.Add('- Applied commands: `' + $commandsApplied.Count + '`')
$md.Add('- Corrected commands: `' + (@($watchpointCommands | Where-Object Corrected).Count) + '`')
$hitPathText = if ($hit) { $hit.Path } else { "" }
$md.Add('- Hit path: `' + $hitPathText + '`')
$md.Add("")
$md.Add("## Candidates")
$md.Add("")
$md.Add("| Address | Size | Score | Reasons |")
$md.Add("|---|---:|---:|---|")
foreach ($candidate in $candidates) {
    $reasons = (($candidate.Reasons | Select-Object -First 3) -join "; ") -replace "\|", "/"
    $md.Add(('| `{0}` | {1} | {2} | {3} |' -f $candidate.Address, $candidate.HardwareSize, $candidate.Score, $reasons))
}
$md.Add("")
$md.Add("## Commands")
$md.Add("")
$md.Add("| Command | Original | Corrected |")
$md.Add("|---|---|---:|")
foreach ($row in $watchpointCommands) {
    $md.Add(('| `{0}` | `{1}` | {2} |' -f $row.Command, $row.OriginalCommand, $row.Corrected))
}
$md.Add("")
$md.Add("## Next")
$md.Add("")
$md.Add("- If no hit was captured, keep the batch installed and trigger one narrow merit event in game.")
$md.Add("- If a hit was captured, inspect `merit-watchpoint-hit-*.json` for CIP, disassembly, stack, registers, and candidate memory.")
$md.Add("- Do not promote any candidate to verified until the hit is tied to a specific UI/gameplay merit event.")
Set-Content -LiteralPath $mdPath -Encoding UTF8 -Value $md

[pscustomobject]@{
    Summary = $summaryPath
    Markdown = $mdPath
    OutputDir = $OutputDir
    HealthOk = $health.Ok
    AppliedCommands = $commandsApplied.Count
    HitPath = if ($hit) { $hit.Path } else { "" }
    FinalState = if ($summary.FinalState.Ok -and $summary.FinalState.Data.success) { $summary.FinalState.Data.data.state } else { "" }
    FinalCip = if ($summary.FinalState.Ok -and $summary.FinalState.Data.success) { ConvertTo-NormalizedHexAddress $summary.FinalState.Data.data.cip } else { "" }
} | ConvertTo-Json -Depth 20
