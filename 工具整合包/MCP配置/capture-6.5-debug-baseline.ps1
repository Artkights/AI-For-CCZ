param(
    [string]$GameRoot = "",
    [string]$OutputRoot = "",
    [int]$ByteCount = 16
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $GameRoot = Join-Path $workspaceRoot ([IO.Path]::Combine("base", "6.5-unencrypted"))
    throw "GameRoot is required when the default localized path cannot be expressed safely. Pass -GameRoot explicitly."
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $workspaceRoot "CCZModStudio_Reports\DebugEvidence"
}

$exePath = Join-Path $GameRoot "Ekd5.exe"
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Ekd5.exe not found: $exePath"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

function Read-UInt16At {
    param([byte[]]$Bytes, [int]$Offset)
    return [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Read-UInt32At {
    param([byte[]]$Bytes, [int]$Offset)
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function ConvertTo-HexBytes {
    param([byte[]]$Bytes)
    if ($Bytes.Count -eq 0) { return "" }
    return (($Bytes | ForEach-Object { $_.ToString("X2") }) -join " ")
}

function Read-PeMap {
    param([string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    $peOffset = [int](Read-UInt32At $bytes 0x3C)
    $signature = Read-UInt32At $bytes $peOffset
    if ($signature -ne 0x00004550) { throw "Not a PE file: $Path" }

    $sectionCount = Read-UInt16At $bytes ($peOffset + 6)
    $optionalHeaderSize = Read-UInt16At $bytes ($peOffset + 20)
    $optionalHeaderStart = $peOffset + 24
    $magic = Read-UInt16At $bytes $optionalHeaderStart
    if ($magic -eq 0x10B) {
        $imageBase = Read-UInt32At $bytes ($optionalHeaderStart + 28)
    }
    elseif ($magic -eq 0x20B) {
        throw "64-bit PE is not expected for Ekd5.exe."
    }
    else {
        throw ("Unknown PE optional header magic: {0:X}" -f $magic)
    }

    $sections = @()
    $sectionStart = $optionalHeaderStart + $optionalHeaderSize
    for ($i = 0; $i -lt $sectionCount; $i++) {
        $base = $sectionStart + 40 * $i
        $nameBytes = $bytes[$base..($base + 7)]
        $name = [Text.Encoding]::ASCII.GetString($nameBytes).TrimEnd([char]0)
        $sections += [pscustomobject]@{
            Name = $name
            VirtualSize = Read-UInt32At $bytes ($base + 8)
            VirtualAddress = Read-UInt32At $bytes ($base + 12)
            RawSize = Read-UInt32At $bytes ($base + 16)
            RawPointer = Read-UInt32At $bytes ($base + 20)
        }
    }

    return [pscustomobject]@{
        Bytes = $bytes
        ImageBase = [uint32]$imageBase
        Sections = $sections
    }
}

function Convert-VaToFileOffset {
    param(
        [pscustomobject]$Pe,
        [uint32]$VirtualAddress
    )

    if ($VirtualAddress -lt $Pe.ImageBase) {
        throw ("VA {0:X} is below image base {1:X}." -f $VirtualAddress, $Pe.ImageBase)
    }

    $rva = [uint32]($VirtualAddress - $Pe.ImageBase)
    foreach ($section in $Pe.Sections) {
        $size = [Math]::Max([uint32]$section.VirtualSize, [uint32]$section.RawSize)
        if ($rva -ge $section.VirtualAddress -and $rva -lt ($section.VirtualAddress + $size)) {
            return [int64]($section.RawPointer + ($rva - $section.VirtualAddress))
        }
    }

    throw ("Cannot map VA {0:X} to a file offset." -f $VirtualAddress)
}

function Read-BytesAtVa {
    param(
        [pscustomobject]$Pe,
        [uint32]$VirtualAddress,
        [int]$Count
    )

    $offset = Convert-VaToFileOffset -Pe $Pe -VirtualAddress $VirtualAddress
    if ($offset + $Count -gt $Pe.Bytes.LongLength) {
        throw ("Read out of range: {0:X} + {1}" -f $offset, $Count)
    }

    $buffer = New-Object byte[] $Count
    [Array]::Copy($Pe.Bytes, [int]$offset, $buffer, 0, $Count)
    return [pscustomobject]@{
        FileOffset = $offset
        Bytes = $buffer
    }
}

function Get-RunSummary {
    param([byte[]]$Bytes)

    $counts = @{}
    foreach ($b in $Bytes) {
        $key = "0x" + $b.ToString("X2")
        if (-not $counts.ContainsKey($key)) { $counts[$key] = 0 }
        $counts[$key]++
    }

    $dominant = $counts.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 1
    $allNop = $Bytes.Count -gt 0 -and ($Bytes | Where-Object { $_ -ne 0x90 }).Count -eq 0
    $allZero = $Bytes.Count -gt 0 -and ($Bytes | Where-Object { $_ -ne 0x00 }).Count -eq 0

    return [pscustomobject]@{
        AllNop = $allNop
        AllZero = $allZero
        DominantByte = if ($dominant) { $dominant.Name } else { "" }
        DominantCount = if ($dominant) { $dominant.Value } else { 0 }
    }
}

$targets = @(
    @{ Group = "core-effect-chain"; Name = "core_engine"; Address = 0x4101D9 },
    @{ Group = "core-effect-chain"; Name = "get_effect_value"; Address = 0x413009 },
    @{ Group = "core-effect-chain"; Name = "dual_channel_check"; Address = 0x41301E },
    @{ Group = "core-effect-chain"; Name = "ability_check_wrapper"; Address = 0x42518F },
    @{ Group = "unit-combat"; Name = "get_unit_ptr"; Address = 0x4061F9 },
    @{ Group = "unit-combat"; Name = "get_char_data_ptr"; Address = 0x484002 },
    @{ Group = "unit-combat"; Name = "get_unit_hp"; Address = 0x41B500 },
    @{ Group = "unit-combat"; Name = "get_max_mp"; Address = 0x40728F },
    @{ Group = "unit-combat"; Name = "generic_event_dispatch"; Address = 0x450986 },
    @{ Group = "high-value-hook"; Name = "mp_recover_on_hit_hook"; Address = 0x418335 },
    @{ Group = "high-value-hook"; Name = "strategy_reduce_check_hook"; Address = 0x43C242 },
    @{ Group = "high-value-hook"; Name = "strategy_charge_hook"; Address = 0x43C2B0 },
    @{ Group = "high-value-hook"; Name = "strategy_group_power_hook"; Address = 0x43C2B5 },
    @{ Group = "high-value-hook"; Name = "strategy_floor_cap_hook"; Address = 0x43C2D5 },
    @{ Group = "high-value-hook"; Name = "strategy_random_status_hook"; Address = 0x4259AF },
    @{ Group = "high-value-hook"; Name = "strategy_money_on_hit_hook"; Address = 0x4259B4 },
    @{ Group = "high-value-hook"; Name = "overbearing_on_crit_hook"; Address = 0x472D92 }
)

$caves = @(
    @{ Name = "D-pre/D-main-cave"; Start = 0x4528FC; End = 0x452F05 },
    @{ Name = "strategy-post-damage-cave"; Start = 0x41A5A7; End = 0x41A6DB },
    @{ Name = "strategy-damage-cave-cluster"; Start = 0x43D0D6; End = 0x43D44E },
    @{ Name = "large-cave-E-baseline"; Start = 0x601A9A; End = 0x602500 }
)

$pe = Read-PeMap -Path $exePath
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exePath).Hash
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$sessionId = "debug-baseline-$stamp"
$sessionDir = Join-Path $OutputRoot $sessionId
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null

$addressRows = @()
foreach ($target in $targets) {
    $va = [uint32]$target.Address
    try {
        $read = Read-BytesAtVa -Pe $pe -VirtualAddress $va -Count $ByteCount
        $addressRows += [pscustomobject]@{
            Group = $target.Group
            Name = $target.Name
            VirtualAddress = ("{0:X6}" -f $va)
            FileOffset = ("{0:X}" -f $read.FileOffset)
            ByteCount = $ByteCount
            OriginalBytes = ConvertTo-HexBytes $read.Bytes
            EvidenceLevel = "static-byte-read"
            DynamicStatus = "pending-x32dbg-breakpoint"
            Status = "mapped"
        }
    }
    catch {
        $addressRows += [pscustomobject]@{
            Group = $target.Group
            Name = $target.Name
            VirtualAddress = ("{0:X6}" -f $va)
            FileOffset = "-"
            ByteCount = $ByteCount
            OriginalBytes = ""
            EvidenceLevel = "static-map-failed"
            DynamicStatus = "blocked-unmapped-va"
            Status = $_.Exception.Message
        }
    }
}

$caveRows = @()
foreach ($cave in $caves) {
    $start = [uint32]$cave.Start
    $end = [uint32]$cave.End
    $count = [int]($end - $start + 1)
    try {
        $read = Read-BytesAtVa -Pe $pe -VirtualAddress $start -Count $count
        $summary = Get-RunSummary -Bytes $read.Bytes
        $sampleCount = [Math]::Min(32, $read.Bytes.Count)
        $sample = New-Object byte[] $sampleCount
        [Array]::Copy($read.Bytes, 0, $sample, 0, $sampleCount)
        $caveRows += [pscustomobject]@{
            Name = $cave.Name
            Start = ("{0:X6}" -f $start)
            End = ("{0:X6}" -f $end)
            FileOffset = ("{0:X}" -f $read.FileOffset)
            Length = $count
            AllNop = $summary.AllNop
            AllZero = $summary.AllZero
            DominantByte = $summary.DominantByte
            DominantCount = $summary.DominantCount
            FirstBytes = ConvertTo-HexBytes $sample
            EvidenceLevel = "static-cave-scan"
            Status = "mapped"
        }
    }
    catch {
        $caveRows += [pscustomobject]@{
            Name = $cave.Name
            Start = ("{0:X6}" -f $start)
            End = ("{0:X6}" -f $end)
            FileOffset = "-"
            Length = $count
            AllNop = $false
            AllZero = $false
            DominantByte = ""
            DominantCount = 0
            FirstBytes = ""
            EvidenceLevel = "static-map-failed"
            Status = $_.Exception.Message
        }
    }
}

$breakpoints = $targets | ForEach-Object {
    [pscustomobject]@{
        name = $_.Name
        group = $_.Group
        address = ("{0:X6}" -f [uint32]$_.Address)
        type = "software"
        plannedEvidence = @("registers", "stack", "nearby_memory", "callstack", "disassembly")
        status = "pending_dynamic_trigger"
    }
}

$x32dbgPath = Join-Path $workspaceRoot "CCZModStudio_Exports\DebugTools\x64dbg\release\x32\x32dbg.exe"
$pluginPath = Join-Path $workspaceRoot "CCZModStudio_Exports\DebugTools\x64dbg\release\x32\plugins\x64dbg_mcp.dp32"

$json = [pscustomobject]@{
    SessionId = $sessionId
    CreatedAt = (Get-Date).ToString("O")
    GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
    ExePath = (Resolve-Path -LiteralPath $exePath).Path
    ExeSha256 = $hash
    ImageBase = ("{0:X}" -f $pe.ImageBase)
    X32dbgPath = if (Test-Path -LiteralPath $x32dbgPath) { (Resolve-Path -LiteralPath $x32dbgPath).Path } else { $x32dbgPath }
    X64dbgMcpPluginPath = if (Test-Path -LiteralPath $pluginPath) { (Resolve-Path -LiteralPath $pluginPath).Path } else { $pluginPath }
    AddressRows = $addressRows
    CaveRows = $caveRows
    Breakpoints = $breakpoints
}

$jsonPath = Join-Path $sessionDir "breakpoints-6.5-baseline.json"
$mdPath = Join-Path $sessionDir "debug-session-6.5-baseline.md"
$asmPath = Join-Path $sessionDir "disasm-6.5-byte-baseline.asm"

$json | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$md = New-Object System.Collections.Generic.List[string]
$md.Add("# 6.5 x32dbg Static Debug Baseline")
$md.Add("")
$md.Add("- Session: $sessionId")
$md.Add("- Target: $exePath")
$md.Add("- SHA256: $hash")
$md.Add("- ImageBase: $("{0:X}" -f $pe.ImageBase)")
$md.Add("- x32dbg: $x32dbgPath")
$md.Add("- MCP plugin: $pluginPath")
$md.Add("- Note: This report only covers PE mapping, original byte reads, and code cave scans. Runtime semantics still require x32dbg breakpoint evidence.")
$md.Add("")
$md.Add("## Address Bytes")
$md.Add("")
$md.Add("| Group | Name | VA | File offset | Original bytes | Dynamic status | Map status |")
$md.Add("|---|---|---:|---:|---|---|---|")
foreach ($row in $addressRows) {
    $md.Add("| $($row.Group) | $($row.Name) | ``$($row.VirtualAddress)`` | ``$($row.FileOffset)`` | ``$($row.OriginalBytes)`` | $($row.DynamicStatus) | $($row.Status) |")
}
$md.Add("")
$md.Add("## Code Cave Scan")
$md.Add("")
$md.Add("| Name | Range | File offset | Length | All NOP | Dominant byte | First 32 bytes | Status |")
$md.Add("|---|---|---:|---:|---:|---|---|---|")
foreach ($row in $caveRows) {
    $range = "``$($row.Start)``-``$($row.End)``"
    $md.Add("| $($row.Name) | $range | ``$($row.FileOffset)`` | $($row.Length) | $($row.AllNop) | $($row.DominantByte) x $($row.DominantCount) | ``$($row.FirstBytes)`` | $($row.Status) |")
}
$md.Add("")
$md.Add("## Dynamic Breakpoint Batch")
$md.Add("")
$md.Add("- Open the target in x32dbg, then set software breakpoints from the address field in breakpoints-6.5-baseline.json.")
$md.Add("- For each hit, capture registers, stack, call stack, nearby memory, and current function disassembly.")
$md.Add("- Verify each address in at least two trigger scenes. If a breakpoint is unstable or semantically mismatched, keep it as pending evidence.")
$md.Add("")
[IO.File]::WriteAllLines($mdPath, $md, [Text.UTF8Encoding]::new($true))

$asm = New-Object System.Collections.Generic.List[string]
$asm.Add("; 6.5 x32dbg byte baseline")
$asm.Add("; This is a byte-level anchor file, not a full disassembly.")
$asm.Add("; SHA256: $hash")
$asm.Add("")
foreach ($row in $addressRows) {
    $asm.Add(("; {0} / {1}" -f $row.Group, $row.Name))
    $asm.Add(("{0}: db {1}" -f $row.VirtualAddress, ($row.OriginalBytes -replace " ", ", 0x" -replace "^", "0x")))
    $asm.Add("")
}
[IO.File]::WriteAllLines($asmPath, $asm, [Text.UTF8Encoding]::new($true))

[pscustomobject]@{
    SessionDir = $sessionDir
    Markdown = $mdPath
    Json = $jsonPath
    Asm = $asmPath
    ExeSha256 = $hash
    AddressCount = $addressRows.Count
    CaveCount = $caveRows.Count
} | Format-List
