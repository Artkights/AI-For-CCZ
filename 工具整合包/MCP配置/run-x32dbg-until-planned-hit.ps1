param(
    [string]$BaselineJson = "",
    [string]$OutputDir = "",
    [string]$HostName = "127.0.0.1",
    [int]$Port = 27042,
    [int]$MaxRuns = 8,
    [int]$PollsPerRun = 120,
    [int]$PollIntervalMs = 250,
    [int]$DisasmCount = 24,
    [int]$FunctionInstructionCount = 64,
    [int]$StackSize = 128,
    [int]$MemorySize = 64,
    [string[]]$DisableAddress = @(),
    [switch]$DisableCurrentPlannedHit,
    [switch]$ContinueAfterUnplannedStop,
    [switch]$UseTraceRun,
    [switch]$CaptureCurrentPaused
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if ([string]::IsNullOrWhiteSpace($BaselineJson)) {
    $evidenceRoot = Join-Path $workspaceRoot "CCZModStudio_Reports\DebugEvidence"
    $latest = Get-ChildItem -LiteralPath $evidenceRoot -Directory |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $latest) {
        throw "No debug baseline directory was found: $evidenceRoot"
    }

    $BaselineJson = Join-Path $latest.FullName "breakpoints-6.5-baseline.json"
}

if (-not (Test-Path -LiteralPath $BaselineJson -PathType Leaf)) {
    throw "Baseline JSON not found: $BaselineJson"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Split-Path -Parent $BaselineJson
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$baseUrl = "http://${HostName}:$Port"
$baseline = Get-Content -Raw -Encoding UTF8 -LiteralPath $BaselineJson | ConvertFrom-Json

function Normalize-X32Address {
    param([object]$Value)

    if ($null -eq $Value) { return "" }
    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return "" }

    $match = [regex]::Match($text, "(?i)0x[0-9a-f]+|(?<=\.)[0-9a-f]{6,8}\b|\b[0-9a-f]{6,8}\b")
    if (-not $match.Success) { return $text.ToUpperInvariant() }

    $hex = $match.Value
    if ($hex.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        $hex = $hex.Substring(2)
    }

    try {
        $number = [Convert]::ToUInt64($hex, 16)
        if ($hex.Length -le 8) {
            return (("{0:X}" -f $number).PadLeft(8, "0"))
        }

        return ("{0:X}" -f $number)
    }
    catch {
        return $text.ToUpperInvariant()
    }
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
    return Invoke-X32dbgJson GET "/api/debug/state"
}

function Get-X32dbgRegisters {
    return Invoke-X32dbgJson GET "/api/registers/all"
}

function Expand-X32AddressArguments {
    param([string[]]$Values)

    $addresses = New-Object System.Collections.Generic.List[string]
    foreach ($value in $Values) {
        if ([string]::IsNullOrWhiteSpace($value)) { continue }
        $matches = [regex]::Matches($value, "(?i)0x[0-9a-f]+|\b[0-9a-f]{6,8}\b")
        foreach ($match in $matches) {
            $addresses.Add((Normalize-X32Address $match.Value))
        }
    }

    return $addresses
}

function Get-PlannedBreakpoint {
    param([string]$Address)

    $normalized = Normalize-X32Address $Address
    if ($plannedByAddress.ContainsKey($normalized)) {
        return $plannedByAddress[$normalized]
    }

    return $null
}

function Save-X32dbgHit {
    param(
        [string]$Address,
        [string]$HitKind,
        [string]$StopReason
    )

    $normalized = Normalize-X32Address $Address
    $planned = Get-PlannedBreakpoint -Address $normalized
    $registers = Get-X32dbgRegisters
    $esp = $null
    if ($registers.Ok -and $registers.Data.success -and $registers.Data.data) {
        $esp = $registers.Data.data.esp
    }

    $report = [pscustomobject]@{
        CreatedAt = (Get-Date).ToString("O")
        Address = $normalized
        Name = if ($planned) { $planned.name } else { "" }
        Group = if ($planned) { $planned.group } else { "" }
        HitKind = $HitKind
        StopReason = $StopReason
        IsPlannedBreakpoint = [bool]$planned
        SourceBaseline = (Resolve-Path -LiteralPath $BaselineJson).Path
        DebugState = Get-X32dbgState
        Process = Invoke-X32dbgJson GET "/api/process/info"
        Registers = $registers
        Breakpoint = Invoke-X32dbgJson GET "/api/breakpoints/get" @{ address = $normalized }
        Disasm = Invoke-X32dbgJson GET "/api/disasm/at" @{ address = $normalized; count = [string]$DisasmCount }
        FunctionDisasm = Invoke-X32dbgJson GET "/api/disasm/function" @{ address = $normalized; max_instructions = [string]$FunctionInstructionCount }
        StackTrace = Invoke-X32dbgJson GET "/api/stack/trace" @{ max_depth = "32" }
        StackRead = if ($esp) { Invoke-X32dbgJson GET "/api/stack/read" @{ address = $esp; size = [string]$StackSize } } else { [pscustomobject]@{ Ok = $false; Error = "ESP was not available." } }
        Memory = Invoke-X32dbgJson GET "/api/memory/read" @{ address = $normalized; size = [string]$MemorySize }
    }

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $plain = $normalized -replace "^0x", ""
    $path = Join-Path $OutputDir ("x32dbg-hit-{0}-{1}.json" -f $plain, $stamp)
    $report | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $path -Encoding UTF8

    return [pscustomobject]@{
        Path = $path
        Address = $normalized
        Name = $report.Name
        Group = $report.Group
        IsPlannedBreakpoint = $report.IsPlannedBreakpoint
    }
}

$plannedByAddress = @{}
foreach ($bp in $baseline.Breakpoints) {
    $plannedByAddress[(Normalize-X32Address $bp.address)] = $bp
}

$events = New-Object System.Collections.Generic.List[object]
$disabled = New-Object System.Collections.Generic.List[object]
$hit = $null
$ignorePausedAt = ""

$health = Invoke-X32dbgJson GET "/api/health"
if (-not $health.Ok -or -not $health.Data.success) {
    throw "x32dbg MCP bridge is not healthy at $baseUrl."
}

if ($CaptureCurrentPaused) {
    $state = Get-X32dbgState
    if ($state.Ok -and $state.Data.success -and $state.Data.data.state -eq "paused") {
        $cip = Normalize-X32Address $state.Data.data.cip
        $hitKind = if ($plannedByAddress.ContainsKey($cip)) { "planned-breakpoint" } else { "unplanned-stop" }
        $hit = Save-X32dbgHit -Address $cip -HitKind $hitKind -StopReason "capture-current-paused-state"
    }
    else {
        throw "The debugger is not paused; cannot capture current paused state."
    }
}

if ($DisableCurrentPlannedHit) {
    $state = Get-X32dbgState
    if ($state.Ok -and $state.Data.success) {
        $cip = Normalize-X32Address $state.Data.data.cip
        if ($plannedByAddress.ContainsKey($cip)) {
            $result = Invoke-X32dbgJson POST "/api/breakpoints/disable" -Body @{ address = $cip }
            $disabled.Add([pscustomobject]@{
                Address = $cip
                Reason = "DisableCurrentPlannedHit"
                Result = $result
            })
            $ignorePausedAt = $cip
        }
    }
}

foreach ($address in (Expand-X32AddressArguments -Values $DisableAddress)) {
    $normalized = Normalize-X32Address $address
    $result = Invoke-X32dbgJson POST "/api/breakpoints/disable" -Body @{ address = $normalized }
    $disabled.Add([pscustomobject]@{
        Address = $normalized
        Reason = "DisableAddress"
        Result = $result
    })
}

for ($run = 0; $run -lt $MaxRuns -and $null -eq $hit; $run++) {
    $before = Get-X32dbgState
    $events.Add([pscustomobject]@{
        Run = $run
        Tick = -1
        Event = "before-run"
        State = if ($before.Ok -and $before.Data.success) { $before.Data.data.state } else { "" }
        Cip = if ($before.Ok -and $before.Data.success) { Normalize-X32Address $before.Data.data.cip } else { "" }
        Raw = $before
    })

    if ($UseTraceRun) {
        $runResult = Invoke-X32dbgJson POST "/api/trace/run" -Body @{ party = "0" } -TimeoutSec 3
    }
    else {
        $runResult = Invoke-X32dbgJson POST "/api/command/exec" -Body @{ command = "run" } -TimeoutSec 3
    }

    $events.Add([pscustomobject]@{
        Run = $run
        Tick = -1
        Event = if ($UseTraceRun) { "trace-run" } else { "command-run" }
        State = ""
        Cip = ""
        Raw = $runResult
    })

    for ($tick = 0; $tick -lt $PollsPerRun -and $null -eq $hit; $tick++) {
        Start-Sleep -Milliseconds $PollIntervalMs
        $state = Get-X32dbgState
        $cip = if ($state.Ok -and $state.Data.success) { Normalize-X32Address $state.Data.data.cip } else { "" }
        $debugState = if ($state.Ok -and $state.Data.success) { $state.Data.data.state } else { "" }

        $events.Add([pscustomobject]@{
            Run = $run
            Tick = $tick
            Event = "poll"
            State = $debugState
            Cip = $cip
            Module = if ($state.Ok -and $state.Data.success) { $state.Data.data.module } else { "" }
            Raw = $state
        })

        if ($debugState -eq "paused") {
            if ($ignorePausedAt -and $cip -eq $ignorePausedAt) {
                $events.Add([pscustomobject]@{
                    Run = $run
                    Tick = $tick
                    Event = "ignored-old-paused-cip"
                    State = $debugState
                    Cip = $cip
                })
                continue
            }

            $ignorePausedAt = ""
            if ($plannedByAddress.ContainsKey($cip)) {
                $hit = Save-X32dbgHit -Address $cip -HitKind "planned-breakpoint" -StopReason "debugger-paused-at-planned-address"
                break
            }

            $unplanned = Save-X32dbgHit -Address $cip -HitKind "unplanned-stop" -StopReason "debugger-paused-outside-planned-address"
            $events.Add([pscustomobject]@{
                Run = $run
                Tick = $tick
                Event = "unplanned-stop-saved"
                State = $debugState
                Cip = $cip
                Output = $unplanned.Path
            })

            if (-not $ContinueAfterUnplannedStop) {
                $hit = $unplanned
                break
            }

            break
        }
    }
}

$summary = [pscustomobject]@{
    CreatedAt = (Get-Date).ToString("O")
    SourceBaseline = (Resolve-Path -LiteralPath $BaselineJson).Path
    BaseUrl = $baseUrl
    MaxRuns = $MaxRuns
    PollsPerRun = $PollsPerRun
    PollIntervalMs = $PollIntervalMs
    DisabledBreakpoints = $disabled
    PlannedAddresses = $plannedByAddress.Keys | Sort-Object
    Hit = $hit
    FinalState = Get-X32dbgState
    FinalRegisters = Get-X32dbgRegisters
    Events = $events
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$summaryPath = Join-Path $OutputDir ("x32dbg-run-until-planned-hit-{0}.json" -f $stamp)
$summary | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

[pscustomobject]@{
    Summary = $summaryPath
    HitPath = if ($hit) { $hit.Path } else { "" }
    HitAddress = if ($hit) { $hit.Address } else { "" }
    HitName = if ($hit) { $hit.Name } else { "" }
    IsPlannedBreakpoint = if ($hit) { $hit.IsPlannedBreakpoint } else { $false }
    Disabled = $disabled.Count
    FinalState = if ($summary.FinalState.Ok -and $summary.FinalState.Data.success) { $summary.FinalState.Data.data.state } else { "" }
    FinalCip = if ($summary.FinalState.Ok -and $summary.FinalState.Data.success) { Normalize-X32Address $summary.FinalState.Data.data.cip } else { "" }
} | Format-List
