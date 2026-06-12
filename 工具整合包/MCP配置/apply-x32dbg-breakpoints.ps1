param(
    [string]$BaselineJson = "",
    [string]$OutputPath = "",
    [string]$HostName = "127.0.0.1",
    [int]$Port = 27042
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

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Split-Path -Parent $BaselineJson) "x32dbg-breakpoints-applied.json"
}

$baseUrl = "http://${HostName}:$Port"
$baseline = Get-Content -Raw -Encoding UTF8 -LiteralPath $BaselineJson | ConvertFrom-Json

function Invoke-X32dbgJson {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null
    )

    try {
        if ($Method -eq "POST") {
            $bodyText = if ($null -eq $Body) { "{}" } else { $Body | ConvertTo-Json -Depth 10 }
            $data = Invoke-RestMethod -Uri ($baseUrl + $Path) -Method Post -ContentType "application/json" -Body $bodyText -TimeoutSec 5
        }
        else {
            $data = Invoke-RestMethod -Uri ($baseUrl + $Path) -TimeoutSec 5
        }

        return [pscustomobject]@{ Ok = $true; Data = $data }
    }
    catch {
        $message = $_.Exception.Message
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
            $message = $_.ErrorDetails.Message
        }

        return [pscustomobject]@{ Ok = $false; Error = $message }
    }
}

$health = Invoke-X32dbgJson GET "/api/health"
$setResults = @()
foreach ($bp in $baseline.Breakpoints) {
    $result = Invoke-X32dbgJson POST "/api/breakpoints/set" @{ address = $bp.address }
    $setResults += [pscustomobject]@{
        Address = $bp.address
        Name = $bp.name
        Group = $bp.group
        Ok = $result.Ok
        Result = $result
    }
}

$report = [pscustomobject]@{
    CreatedAt = (Get-Date).ToString("O")
    SourceBaseline = (Resolve-Path -LiteralPath $BaselineJson).Path
    Health = $health
    SetResults = $setResults
    BreakpointList = Invoke-X32dbgJson GET "/api/breakpoints/list"
    ProcessInfo = Invoke-X32dbgJson GET "/api/process/info"
    Registers = Invoke-X32dbgJson GET "/api/registers/all"
    NextStep = "If ProcessInfo says No active debug session, activate the target in x32dbg and rerun this script."
}

$report | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

[pscustomobject]@{
    Output = $OutputPath
    HealthOk = $health.Ok
    Attempted = $setResults.Count
    Succeeded = @($setResults | Where-Object Ok).Count
    ProcessOk = $report.ProcessInfo.Ok
    RegistersOk = $report.Registers.Ok
} | Format-List
