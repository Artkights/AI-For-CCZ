param(
    [string]$GameRoot = "",
    [string]$OutputDir = "",
    [string]$Label = "battle-auto",
    [ValidateSet("Auto", "Process", "X32dbg")]
    [string]$ReadMode = "Auto",
    [string]$HostName = "127.0.0.1",
    [int]$Port = 27042,
    [int]$DurationSec = 600,
    [int]$PollIntervalMs = 500,
    [int]$IdleReportEverySec = 15,
    [switch]$StartGameIfMissing,
    [switch]$StartX32dbgIfMissing,
    [switch]$AutoResumePaused,
    [switch]$SaveMemoryOnEvent
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot
$targetSha256 = "84E3A1DC085AE6F9900D1E8C388A9CD6766379832DDF51BC7BDF780C6615B4A3"

function Resolve-DefaultGameRoot {
    param([string]$Root)

    $excludedPathParts = @(
        "\CCZModStudio_TestCopies\",
        "\CCZModStudio_Exports\",
        "\_CCZModStudio_Backups\",
        "\工具整合包\"
    )

    $candidates = @(Get-ChildItem -LiteralPath $Root -Recurse -Filter "Ekd5.exe" -File -ErrorAction SilentlyContinue |
        Where-Object {
            $fullName = $_.FullName
            -not ($excludedPathParts | Where-Object { $fullName.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
        })
    foreach ($candidate in $candidates) {
        try {
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidate.FullName).Hash
            if ($hash -eq $targetSha256) {
                return (Split-Path -Parent $candidate.FullName)
            }
        }
        catch {
            continue
        }
    }

    if ($candidates.Count -gt 0) {
        return (Split-Path -Parent $candidates[0].FullName)
    }

    return ""
}

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $GameRoot = Resolve-DefaultGameRoot -Root $workspaceRoot
    if ([string]::IsNullOrWhiteSpace($GameRoot)) {
        throw "Could not locate Ekd5.exe under workspace root: $workspaceRoot"
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $workspaceRoot "CCZModStudio_Reports\DebugEvidence"
}

$safeLabel = ($Label -replace "[^\w\-.]+", "-").Trim("-")
if ([string]::IsNullOrWhiteSpace($safeLabel)) { $safeLabel = "battle-auto" }

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$sessionDir = Join-Path $OutputDir ("{0}-{1}" -f $safeLabel, $stamp)
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null

$jsonlPath = Join-Path $sessionDir "events.jsonl"
$mdPath = Join-Path $sessionDir "events.md"
$summaryPath = Join-Path $sessionDir "summary.json"
$baseUrl = "http://${HostName}:$Port"

Set-Content -LiteralPath $mdPath -Encoding UTF8 -Value @(
    "# 6.5 Battle Auto Monitor",
    "",
    "- Created: $((Get-Date).ToString("O"))",
    "- ReadMode: $ReadMode",
    "- GameRoot: $GameRoot",
    "- x32dbg bridge: $baseUrl",
    ""
)

function Add-LogLine {
    param([string]$Text)
    Add-Content -LiteralPath $mdPath -Encoding UTF8 -Value $Text
}

function Write-JsonLine {
    param([object]$Object)
    ($Object | ConvertTo-Json -Depth 30 -Compress) | Add-Content -LiteralPath $jsonlPath -Encoding UTF8
}

function ConvertTo-NormalizedHexAddress {
    param([object]$Value)
    if ($null -eq $Value) { return "" }
    $text = ([string]$Value).Trim()
    $match = [regex]::Match($text, "(?i)0x[0-9a-f]+|\b[0-9a-f]{6,8}\b")
    if (-not $match.Success) { return $text.ToUpperInvariant() }
    $hex = $match.Value
    if ($hex.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        $hex = $hex.Substring(2)
    }
    $number = [Convert]::ToUInt64($hex, 16)
    return "0x" + (("{0:X}" -f $number).PadLeft(8, "0"))
}

function ConvertFrom-HexBytes {
    param([string]$Hex)
    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return New-Object byte[] 0
    }

    $parts = $Hex -split "\s+" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $bytes = New-Object byte[] $parts.Count
    for ($i = 0; $i -lt $parts.Count; $i++) {
        $bytes[$i] = [Convert]::ToByte($parts[$i], 16)
    }
    return $bytes
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
        [int]$TimeoutSec = 2
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

function Test-X32dbgBridge {
    $health = Invoke-X32dbgJson GET "/api/health" -TimeoutSec 1
    return ($health.Ok -and $health.Data.success)
}

if (-not ("CczReadProcessMemory.NativeMethods" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

namespace CczReadProcessMemory {
    public static class NativeMethods {
        [DllImport("kernel32.dll", SetLastError=true)]
        public static extern IntPtr OpenProcess(UInt32 dwDesiredAccess, bool bInheritHandle, UInt32 dwProcessId);

        [DllImport("kernel32.dll", SetLastError=true)]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, Int32 dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError=true)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
"@
}

$processHandle = [IntPtr]::Zero
$processId = 0

function Close-TargetHandle {
    if ($script:processHandle -ne [IntPtr]::Zero) {
        [void][CczReadProcessMemory.NativeMethods]::CloseHandle($script:processHandle)
        $script:processHandle = [IntPtr]::Zero
        $script:processId = 0
    }
}

function Get-TargetProcess {
    $processes = @(Get-Process -Name "Ekd5" -ErrorAction SilentlyContinue | Sort-Object StartTime -Descending)
    if ($processes.Count -eq 0) { return $null }
    return $processes[0]
}

function Open-TargetProcess {
    $target = Get-TargetProcess
    if ($null -eq $target) {
        Close-TargetHandle
        return $false
    }

    if ($script:processHandle -ne [IntPtr]::Zero -and $script:processId -eq $target.Id) {
        return $true
    }

    Close-TargetHandle
    $PROCESS_QUERY_INFORMATION = 0x0400
    $PROCESS_VM_READ = 0x0010
    $handle = [CczReadProcessMemory.NativeMethods]::OpenProcess($PROCESS_QUERY_INFORMATION -bor $PROCESS_VM_READ, $false, [uint32]$target.Id)
    if ($handle -eq [IntPtr]::Zero) {
        return $false
    }

    $script:processHandle = $handle
    $script:processId = $target.Id
    return $true
}

function Read-ProcessMemoryBytes {
    param([uint32]$Address, [int]$Size)

    if (-not (Open-TargetProcess)) {
        throw "Ekd5.exe is not running or cannot be opened for read-only memory access."
    }

    $buffer = New-Object byte[] $Size
    $bytesRead = [IntPtr]::Zero
    $ok = [CczReadProcessMemory.NativeMethods]::ReadProcessMemory($script:processHandle, [IntPtr]([int64]$Address), $buffer, $Size, [ref]$bytesRead)
    if (-not $ok) {
        $lastError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw ("ReadProcessMemory failed at {0:X8}, size {1}, error {2}" -f $Address, $Size, $lastError)
    }

    $actual = $bytesRead.ToInt32()
    if ($actual -lt $Size) {
        $trimmed = New-Object byte[] $actual
        [Array]::Copy($buffer, $trimmed, $actual)
        return $trimmed
    }

    return $buffer
}

function Read-X32dbgMemoryBytes {
    param([string]$Address, [int]$Size)

    $read = Invoke-X32dbgJson GET "/api/memory/read" @{
        address = $Address
        size = [string]$Size
    } -TimeoutSec 5

    if (-not $read.Ok -or -not $read.Data.success) {
        $errorText = if ($read.Error) { $read.Error } else { ($read | ConvertTo-Json -Depth 8 -Compress) }
        throw "x32dbg memory read failed: $errorText"
    }

    return ConvertFrom-HexBytes $read.Data.data.hex
}

function Read-MemoryRange {
    param(
        [string]$Name,
        [uint32]$Address,
        [int]$Size,
        [string]$Mode
    )

    $addressText = "0x" + ("{0:X8}" -f $Address)
    if ($Mode -eq "X32dbg") {
        return [pscustomobject]@{
            Name = $Name
            Address = $addressText
            Size = $Size
            Bytes = Read-X32dbgMemoryBytes $addressText $Size
            Mode = "X32dbg"
        }
    }

    return [pscustomobject]@{
        Name = $Name
        Address = $addressText
        Size = $Size
        Bytes = Read-ProcessMemoryBytes $Address $Size
        Mode = "Process"
    }
}

function Read-UInt16Le {
    param([byte[]]$Bytes, [int]$Offset)
    if ($Offset -lt 0 -or ($Offset + 1) -ge $Bytes.Length) { return $null }
    return [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Get-UnitRows {
    param([byte[]]$Bytes)

    $rows = @()
    $stride = 0x30
    $count = [Math]::Floor($Bytes.Length / $stride)
    for ($i = 0; $i -lt $count; $i++) {
        $o = $i * $stride
        if (($o + $stride) -gt $Bytes.Length) { break }
        $hp = Read-UInt16Le $Bytes ($o + 0x10)
        $mp = Read-UInt16Le $Bytes ($o + 0x14)
        $x = $Bytes[$o + 0x06]
        $y = $Bytes[$o + 0x07]
        $side = $Bytes[$o + 0x05]
        $hasSignal = ($hp -gt 0 -and $hp -lt 999 -and $x -lt 80 -and $y -lt 80 -and $side -le 3)
        if (-not $hasSignal) { continue }

        $rows += [pscustomobject]@{
            UnitIndex = $i
            DataIdByte = $Bytes[$o + 0x04]
            Side = $side
            X = $x
            Y = $y
            Action = $Bytes[$o + 0x0D]
            HP = $hp
            MP = $mp
            Attrs = (($Bytes[($o + 0x18)..($o + 0x1D)] | ForEach-Object { $_.ToString("X2") }) -join " ")
        }
    }
    return $rows
}

function Convert-UnitsToMap {
    param([object[]]$Rows)
    $map = @{}
    foreach ($row in $Rows) {
        $map[[string]$row.UnitIndex] = $row
    }
    return $map
}

function Compare-UnitMaps {
    param([hashtable]$Before, [hashtable]$After)

    $changes = @()
    $keys = @($Before.Keys + $After.Keys | Sort-Object -Unique)
    foreach ($key in $keys) {
        $b = $Before[$key]
        $a = $After[$key]
        if ($null -eq $b -and $null -ne $a) {
            $changes += [pscustomobject]@{ Kind = "UnitAppeared"; UnitIndex = [int]$key; Before = $null; After = $a }
            continue
        }
        if ($null -ne $b -and $null -eq $a) {
            $changes += [pscustomobject]@{ Kind = "UnitDisappeared"; UnitIndex = [int]$key; Before = $b; After = $null }
            continue
        }

        $fields = @("DataIdByte", "Side", "X", "Y", "Action", "HP", "MP", "Attrs")
        $fieldChanges = @()
        foreach ($field in $fields) {
            if ($b.$field -ne $a.$field) {
                $fieldChanges += [pscustomobject]@{
                    Field = $field
                    Before = $b.$field
                    After = $a.$field
                    Delta = if (($b.$field -is [int]) -and ($a.$field -is [int])) { $a.$field - $b.$field } else { $null }
                }
            }
        }

        if ($fieldChanges.Count -gt 0) {
            $kinds = @()
            if ($fieldChanges.Field -contains "X" -or $fieldChanges.Field -contains "Y") { $kinds += "Moved" }
            if ($fieldChanges.Field -contains "HP") { $kinds += "HpChanged" }
            if ($fieldChanges.Field -contains "MP") { $kinds += "MpChanged" }
            if ($fieldChanges.Field -contains "Action") { $kinds += "ActionChanged" }
            if ($fieldChanges.Field -contains "Attrs") { $kinds += "AttrsChanged" }
            if ($kinds.Count -eq 0) { $kinds += "UnitChanged" }

            $changes += [pscustomobject]@{
                Kind = ($kinds -join "+")
                UnitIndex = [int]$key
                Side = $a.Side
                DataIdByte = $a.DataIdByte
                Before = $b
                After = $a
                Changes = $fieldChanges
            }
        }
    }
    return $changes
}

function Get-EventSummary {
    param([object[]]$Changes)
    $parts = @()
    foreach ($change in $Changes) {
        if ($null -eq $change.After) {
            $parts += ("unit[{0}] disappeared" -f $change.UnitIndex)
            continue
        }
        if ($null -eq $change.Before) {
            $parts += ("unit[{0}] appeared side={1} hp={2} pos=({3},{4})" -f $change.UnitIndex, $change.After.Side, $change.After.HP, $change.After.X, $change.After.Y)
            continue
        }

        $bits = @()
        foreach ($fieldChange in $change.Changes) {
            if ($fieldChange.Field -eq "Action") {
                $bits += ("Action {0:X2}->0x{1:X2}" -f [int]$fieldChange.Before, [int]$fieldChange.After)
            }
            elseif ($fieldChange.Field -eq "X" -or $fieldChange.Field -eq "Y") {
                continue
            }
            else {
                $bits += ("{0} {1}->{2}" -f $fieldChange.Field, $fieldChange.Before, $fieldChange.After)
            }
        }
        if (($change.Changes.Field -contains "X") -or ($change.Changes.Field -contains "Y")) {
            $bits = @("Pos ({0},{1})->({2},{3})" -f $change.Before.X, $change.Before.Y, $change.After.X, $change.After.Y) + $bits
        }

        $parts += ("unit[{0}] side={1} data={2}: {3}" -f $change.UnitIndex, $change.After.Side, $change.After.DataIdByte, ($bits -join ", "))
    }
    return ($parts -join "; ")
}

function Save-EventMemory {
    param(
        [int]$EventIndex,
        [object[]]$Ranges
    )
    $eventDir = Join-Path $sessionDir ("event-{0:D4}" -f $EventIndex)
    New-Item -ItemType Directory -Force -Path $eventDir | Out-Null
    foreach ($range in $Ranges) {
        $plain = $range.Address -replace "^0x", ""
        $path = Join-Path $eventDir ("memory-{0}-{1}-{2}.bin" -f $range.Name, $plain, $range.Size)
        [IO.File]::WriteAllBytes($path, $range.Bytes)
    }
    return $eventDir
}

function Save-X32PausedEvidence {
    param([string]$Reason)
    $state = Invoke-X32dbgJson GET "/api/debug/state" -TimeoutSec 2
    $cip = ""
    if ($state.Ok -and $state.Data.success -and $state.Data.data.cip) {
        $cip = ConvertTo-NormalizedHexAddress $state.Data.data.cip
    }

    $report = [pscustomobject]@{
        CreatedAt = (Get-Date).ToString("O")
        Reason = $Reason
        State = $state
        Registers = Invoke-X32dbgJson GET "/api/registers/all" -TimeoutSec 2
        Process = Invoke-X32dbgJson GET "/api/process/info" -TimeoutSec 2
        Disasm = if ($cip) { Invoke-X32dbgJson GET "/api/disasm/at" @{ address = $cip; count = "24" } -TimeoutSec 3 } else { $null }
        StackTrace = Invoke-X32dbgJson GET "/api/stack/trace" @{ max_depth = "32" } -TimeoutSec 3
    }

    $path = Join-Path $sessionDir ("x32dbg-paused-{0}-{1}.json" -f (($cip -replace "^0x", ""), (Get-Date -Format "yyyyMMdd-HHmmss")))
    $report | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $path -Encoding UTF8
    return [pscustomobject]@{ Path = $path; Cip = $cip }
}

function Start-GameOrDebuggerIfNeeded {
    $target = Get-TargetProcess
    if ($null -ne $target) { return }

    if ($StartX32dbgIfMissing) {
        $startScript = Join-Path $configRoot "start-x32dbg-6.5.ps1"
        & $startScript -GameRoot $GameRoot | Out-Null
        Add-LogLine "- Started x32dbg with target because no Ekd5.exe process was found."
        return
    }

    if ($StartGameIfMissing) {
        $exePath = Join-Path $GameRoot "Ekd5.exe"
        if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
            throw "Ekd5.exe not found: $exePath"
        }
        Start-Process -FilePath (Resolve-Path -LiteralPath $exePath).Path -WorkingDirectory (Resolve-Path -LiteralPath $GameRoot).Path
        Add-LogLine "- Started Ekd5.exe directly because no process was found."
    }
}

Start-GameOrDebuggerIfNeeded

$ranges = @(
    [pscustomobject]@{ Name = "unit_array"; Address = [uint32]0x004A7B20; Size = 0x3000 },
    [pscustomobject]@{ Name = "battle_globals"; Address = [uint32]0x00490000; Size = 0x22000 },
    [pscustomobject]@{ Name = "combat_data"; Address = [uint32]0x004927F0; Size = 0x800 },
    [pscustomobject]@{ Name = "merit_candidate_00492F90"; Address = [uint32]0x00492F90; Size = 0x80 },
    [pscustomobject]@{ Name = "merit_candidate_004B1B10"; Address = [uint32]0x004B1B10; Size = 0x80 }
)

$start = Get-Date
$end = $start.AddSeconds($DurationSec)
$eventIndex = 0
$pollCount = 0
$lastIdle = Get-Date
$lastBridgeState = $null
$lastUnitMap = $null
$lastReadModeUsed = ""
$bridgeAvailable = $false
$lastX32PausedCip = ""
$errors = New-Object System.Collections.Generic.List[object]

try {
    while ((Get-Date) -lt $end) {
        $pollCount++
        $now = Get-Date

        $bridgeAvailable = Test-X32dbgBridge
        if ($bridgeAvailable -ne $lastBridgeState) {
            $lastBridgeState = $bridgeAvailable
            $bridgeText = if ($bridgeAvailable) { "online" } else { "offline" }
            Add-LogLine ("- {0}: x32dbg bridge {1}." -f $now.ToString("HH:mm:ss"), $bridgeText)
            Write-JsonLine ([pscustomobject]@{
                Type = "BridgeState"
                CreatedAt = $now.ToString("O")
                Online = $bridgeAvailable
            })
        }

        $modeUsed = $ReadMode
        if ($ReadMode -eq "Auto") {
            if ($null -ne (Get-TargetProcess)) {
                $modeUsed = "Process"
            }
            elseif ($bridgeAvailable) {
                $modeUsed = "X32dbg"
            }
            else {
                $modeUsed = "Process"
            }
        }

        if ($modeUsed -ne $lastReadModeUsed) {
            $lastReadModeUsed = $modeUsed
            Add-LogLine ("- {0}: memory read mode = {1}." -f $now.ToString("HH:mm:ss"), $modeUsed)
        }

        if ($bridgeAvailable) {
            $debugState = Invoke-X32dbgJson GET "/api/debug/state" -TimeoutSec 1
            if ($debugState.Ok -and $debugState.Data.success -and $debugState.Data.data.state -eq "paused") {
                $cip = ConvertTo-NormalizedHexAddress $debugState.Data.data.cip
                if ($cip -ne $lastX32PausedCip) {
                    $pausedEvidence = Save-X32PausedEvidence -Reason "debugger-paused-during-auto-monitor"
                    Add-LogLine ("- {0}: x32dbg paused at {1}; evidence: `{2}`." -f $now.ToString("HH:mm:ss"), $pausedEvidence.Cip, $pausedEvidence.Path)
                    Write-JsonLine ([pscustomobject]@{
                        Type = "DebuggerPaused"
                        CreatedAt = $now.ToString("O")
                        Cip = $pausedEvidence.Cip
                        EvidencePath = $pausedEvidence.Path
                    })
                    $lastX32PausedCip = $cip
                }

                if ($AutoResumePaused) {
                    [void](Invoke-X32dbgJson POST "/api/command/exec" -Body @{ command = "run" } -TimeoutSec 2)
                    Add-LogLine ("- {0}: auto-resumed x32dbg." -f $now.ToString("HH:mm:ss"))
                }
            }
            elseif ($debugState.Ok -and $debugState.Data.success -and $debugState.Data.data.state -eq "running") {
                $lastX32PausedCip = ""
            }
        }

        try {
            $captured = @()
            foreach ($range in $ranges) {
                $captured += Read-MemoryRange -Name $range.Name -Address $range.Address -Size $range.Size -Mode $modeUsed
            }

            $unitRange = $captured | Where-Object Name -eq "unit_array" | Select-Object -First 1
            $unitRows = @(Get-UnitRows -Bytes $unitRange.Bytes)
            $unitMap = Convert-UnitsToMap -Rows $unitRows

            if ($null -eq $lastUnitMap) {
                $lastUnitMap = $unitMap
                Add-LogLine ("- {0}: baseline captured, active units = {1}." -f $now.ToString("HH:mm:ss"), $unitRows.Count)
                Write-JsonLine ([pscustomobject]@{
                    Type = "Baseline"
                    CreatedAt = $now.ToString("O")
                    ReadMode = $modeUsed
                    ActiveUnitCount = $unitRows.Count
                    Units = $unitRows
                })
            }
            else {
                $changes = @(Compare-UnitMaps -Before $lastUnitMap -After $unitMap)
                if ($changes.Count -gt 0) {
                    $eventIndex++
                    $summary = Get-EventSummary -Changes $changes
                    $eventDir = ""
                    if ($SaveMemoryOnEvent) {
                        $eventDir = Save-EventMemory -EventIndex $eventIndex -Ranges $captured
                    }

                    Add-LogLine ("- {0}: event {1}: {2}" -f $now.ToString("HH:mm:ss"), $eventIndex, $summary)
                    Write-Host ("[{0}] event {1}: {2}" -f $now.ToString("HH:mm:ss"), $eventIndex, $summary)
                    Write-JsonLine ([pscustomobject]@{
                        Type = "UnitDiff"
                        EventIndex = $eventIndex
                        CreatedAt = $now.ToString("O")
                        ReadMode = $modeUsed
                        Summary = $summary
                        EventDir = $eventDir
                        Changes = $changes
                        Units = $unitRows
                    })
                    $lastUnitMap = $unitMap
                }
            }
        }
        catch {
            $err = [pscustomobject]@{
                CreatedAt = $now.ToString("O")
                Message = $_.Exception.Message
                ReadMode = $modeUsed
            }
            $errors.Add($err)
            if ($errors.Count -le 5 -or ($errors.Count % 20) -eq 0) {
                Add-LogLine ("- {0}: read failed ({1}): {2}" -f $now.ToString("HH:mm:ss"), $errors.Count, $err.Message)
            }
        }

        if (((Get-Date) - $lastIdle).TotalSeconds -ge $IdleReportEverySec) {
            $target = Get-TargetProcess
            $procText = if ($target) { "pid=$($target.Id) responding=$($target.Responding)" } else { "not-running" }
            Add-LogLine ("- {0}: heartbeat polls={1}, events={2}, process={3}, bridge={4}." -f (Get-Date).ToString("HH:mm:ss"), $pollCount, $eventIndex, $procText, $bridgeAvailable)
            $lastIdle = Get-Date
        }

        Start-Sleep -Milliseconds $PollIntervalMs
    }
}
finally {
    Close-TargetHandle
}

$summary = [pscustomobject]@{
    CreatedAt = (Get-Date).ToString("O")
    SessionDir = $sessionDir
    GameRoot = $GameRoot
    ReadMode = $ReadMode
    LastReadModeUsed = $lastReadModeUsed
    PollCount = $pollCount
    EventCount = $eventIndex
    ErrorCount = $errors.Count
    Errors = $errors
    EventsJsonl = $jsonlPath
    EventsMarkdown = $mdPath
}

$summary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Add-LogLine ""
Add-LogLine ("Summary: polls={0}, events={1}, errors={2}." -f $pollCount, $eventIndex, $errors.Count)

[pscustomobject]@{
    SessionDir = $sessionDir
    Events = $jsonlPath
    Markdown = $mdPath
    Summary = $summaryPath
    PollCount = $pollCount
    EventCount = $eventIndex
    ErrorCount = $errors.Count
    LastReadModeUsed = $lastReadModeUsed
} | Format-List
