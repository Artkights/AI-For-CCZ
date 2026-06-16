param(
    [string]$OutputDir = "",
    [string]$Label = "snapshot",
    [string]$HostName = "127.0.0.1",
    [int]$Port = 27042,
    [string[]]$ExtraRange = @(),
    [string]$DiffFrom = "",
    [int]$MaxDiffsPerRange = 2000
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $evidenceRoot = Join-Path $workspaceRoot "CCZModStudio_Reports\DebugEvidence"
    $latest = Get-ChildItem -LiteralPath $evidenceRoot -Directory |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $latest) {
        throw "No debug evidence directory was found: $evidenceRoot"
    }

    $OutputDir = $latest.FullName
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$baseUrl = "http://${HostName}:$Port"

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

function ConvertTo-UInt32Address {
    param([string]$Address)
    $normalized = ConvertTo-NormalizedHexAddress $Address
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
        [int]$TimeoutSec = 15
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

function Read-UInt16Le {
    param([byte[]]$Bytes, [int]$Offset)
    if ($Offset -lt 0 -or ($Offset + 1) -ge $Bytes.Length) { return $null }
    return [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Read-UInt32Le {
    param([byte[]]$Bytes, [int]$Offset)
    if ($Offset -lt 0 -or ($Offset + 3) -ge $Bytes.Length) { return $null }
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Parse-ExtraRange {
    param([string]$Text)

    $parts = $Text -split "[:,=]", 3
    if ($parts.Count -lt 2) {
        throw "ExtraRange must be NAME:ADDRESS:SIZE or ADDRESS:SIZE: $Text"
    }

    if ($parts.Count -eq 2) {
        $name = "extra_" + ((ConvertTo-NormalizedHexAddress $parts[0]) -replace "^0x", "")
        $address = $parts[0]
        $sizeText = $parts[1]
    }
    else {
        $name = $parts[0]
        $address = $parts[1]
        $sizeText = $parts[2]
    }

    $size = if ($sizeText.Trim().StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        [Convert]::ToInt32($sizeText.Trim().Substring(2), 16)
    }
    else {
        [Convert]::ToInt32($sizeText.Trim(), 10)
    }

    return [pscustomobject]@{
        Name = $name
        Address = ConvertTo-NormalizedHexAddress $address
        Size = $size
        Kind = "extra"
    }
}

function Get-RangeBytes {
    param([pscustomobject]$Range)

    $read = Invoke-X32dbgJson GET "/api/memory/read" @{
        address = $Range.Address
        size = [string]$Range.Size
    } -TimeoutSec 20

    if (-not $read.Ok -or -not $read.Data.success) {
        return [pscustomobject]@{
            Range = $Range
            Ok = $false
            Error = if ($read.Error) { $read.Error } else { ($read | ConvertTo-Json -Depth 10) }
            Bytes = New-Object byte[] 0
        }
    }

    return [pscustomobject]@{
        Range = $Range
        Ok = $true
        Error = ""
        Bytes = ConvertFrom-HexBytes $read.Data.data.hex
        ApiData = $read.Data.data
    }
}

function Export-RangeBinary {
    param(
        [pscustomobject]$Range,
        [byte[]]$Bytes,
        [string]$SnapshotDir
    )

    $plainAddress = (ConvertTo-NormalizedHexAddress $Range.Address) -replace "^0x", ""
    $path = Join-Path $SnapshotDir ("memory-{0}-{1}-{2}.bin" -f $Range.Name, $plainAddress, $Range.Size)
    [IO.File]::WriteAllBytes($path, $Bytes)
    return $path
}

function Get-UnitSummary {
    param([byte[]]$Bytes)

    $rows = @()
    $stride = 0x30
    $count = [Math]::Floor($Bytes.Length / $stride)
    for ($i = 0; $i -lt $count; $i++) {
        $offset = $i * $stride
        $entry = $Bytes[$offset..([Math]::Min($offset + $stride - 1, $Bytes.Length - 1))]
        $hasSignal = ($entry | Where-Object { $_ -ne 0 }).Count -gt 4
        if (-not $hasSignal) { continue }

        $rows += [pscustomobject]@{
            UnitIndex = $i
            DataIdByte = $Bytes[$offset + 0x0C]
            CurrentHp = Read-UInt32Le $Bytes ($offset + 0x10)
            MaxHp = Read-UInt32Le $Bytes ($offset + 0x14)
            StatusWord = Read-UInt16Le $Bytes ($offset + 0x1E)
            MpLikeByte = $Bytes[$offset + 0x1F]
            AbilityBytes_0x21_0x26 = (($Bytes[($offset + 0x21)..($offset + 0x26)] | ForEach-Object { $_.ToString("X2") }) -join " ")
            Raw = (($entry | ForEach-Object { $_.ToString("X2") }) -join " ")
        }
    }

    return $rows
}

function Get-CharSummary {
    param([byte[]]$Bytes)

    $rows = @()
    $stride = 0x61
    $count = [Math]::Min([Math]::Floor($Bytes.Length / $stride), 1024)
    for ($i = 0; $i -lt $count; $i++) {
        $offset = $i * $stride
        $entry = $Bytes[$offset..([Math]::Min($offset + $stride - 1, $Bytes.Length - 1))]
        $nonzero = ($entry | Where-Object { $_ -ne 0 }).Count
        if ($nonzero -eq 0) { continue }

        $rows += [pscustomobject]@{
            DataId = $i
            NonZeroBytes = $nonzero
            First16 = (($entry | Select-Object -First 16 | ForEach-Object { $_.ToString("X2") }) -join " ")
            Last16 = (($entry | Select-Object -Last 16 | ForEach-Object { $_.ToString("X2") }) -join " ")
        }
    }

    return $rows
}

function Get-DiffRows {
    param(
        [byte[]]$Before,
        [byte[]]$After,
        [uint32]$BaseAddress,
        [string]$RangeName,
        [int]$Limit
    )

    $max = [Math]::Min($Before.Length, $After.Length)
    $rows = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $max; $i++) {
        if ($Before[$i] -eq $After[$i]) { continue }
        $rows.Add([pscustomobject]@{
            Range = $RangeName
            Offset = ("{0:X}" -f $i)
            Address = ("{0:X8}" -f ($BaseAddress + [uint32]$i))
            Before = ("{0:X2}" -f $Before[$i])
            After = ("{0:X2}" -f $After[$i])
            DeltaSigned = ([int]$After[$i] - [int]$Before[$i])
        })
        if ($rows.Count -ge $Limit) { break }
    }

    return $rows
}

function Read-SnapshotManifest {
    param([string]$Path)

    $manifestPath = $Path
    if (Test-Path -LiteralPath $Path -PathType Container) {
        $manifestPath = Join-Path $Path "manifest.json"
    }

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Snapshot manifest not found: $Path"
    }

    return Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
}

$state = Invoke-X32dbgJson GET "/api/debug/state"
$registers = Invoke-X32dbgJson GET "/api/registers/all"
$process = Invoke-X32dbgJson GET "/api/process/info"

$dynamicRanges = @()
if ($registers.Ok -and $registers.Data.success -and $registers.Data.data) {
    $reg = $registers.Data.data
    foreach ($pair in @(
        @{ Name = "ctx_ecx_minus_0x100"; Address = [uint32]((ConvertTo-UInt32Address $reg.ecx) - 0x100); Size = 0x800 },
        @{ Name = "stack_esp"; Address = [uint32](ConvertTo-UInt32Address $reg.esp); Size = 0x400 },
        @{ Name = "frame_ebp_minus_0x100"; Address = [uint32]((ConvertTo-UInt32Address $reg.ebp) - 0x100); Size = 0x400 }
    )) {
        $dynamicRanges += [pscustomobject]@{
            Name = $pair.Name
            Address = ("{0:X8}" -f $pair.Address)
            Size = [int]$pair.Size
            Kind = "dynamic-register"
        }
    }
}

$ranges = @(
    [pscustomobject]@{ Name = "char_data_004A3E77"; Address = "004A3E77"; Size = 0x3CA0; Kind = "known-global-first-160-records" },
    [pscustomobject]@{ Name = "unit_array_004A7B20"; Address = "004A7B20"; Size = 0x3000; Kind = "known-global" },
    [pscustomobject]@{ Name = "battle_globals_00490000"; Address = "00490000"; Size = 0x22000; Kind = "broad-global" },
    [pscustomobject]@{ Name = "runtime_heap_02E90000"; Address = "02E90000"; Size = 0x30000; Kind = "observed-runtime" }
) + $dynamicRanges

foreach ($extra in $ExtraRange) {
    if ([string]::IsNullOrWhiteSpace($extra)) { continue }
    $ranges += Parse-ExtraRange $extra
}

$safeLabel = ($Label -replace "[^0-9A-Za-z_.-]", "_").Trim("_")
if ([string]::IsNullOrWhiteSpace($safeLabel)) { $safeLabel = "snapshot" }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$snapshotDir = Join-Path $OutputDir ("merit-snapshot-{0}-{1}" -f $safeLabel, $stamp)
New-Item -ItemType Directory -Force -Path $snapshotDir | Out-Null

$rangeReports = @()
foreach ($range in $ranges) {
    $capture = Get-RangeBytes -Range $range
    $binaryPath = ""
    $unitSummary = @()
    $charSummary = @()
    if ($capture.Ok) {
        $binaryPath = Export-RangeBinary -Range $range -Bytes $capture.Bytes -SnapshotDir $snapshotDir
        if ($range.Name -eq "unit_array_004A7B20") {
            $unitSummary = Get-UnitSummary -Bytes $capture.Bytes
        }
        elseif ($range.Name -eq "char_data_004A3E77") {
            $charSummary = Get-CharSummary -Bytes $capture.Bytes
        }
    }

    $rangeReports += [pscustomobject]@{
        Name = $range.Name
        Kind = $range.Kind
        Address = ConvertTo-NormalizedHexAddress $range.Address
        Size = $range.Size
        Ok = $capture.Ok
        Error = $capture.Error
        BinaryPath = $binaryPath
        ByteCount = $capture.Bytes.Length
        UnitSummary = $unitSummary
        CharSummary = $charSummary
    }
}

$manifest = [pscustomobject]@{
    CreatedAt = (Get-Date).ToString("O")
    Label = $Label
    SnapshotDir = $snapshotDir
    BaseUrl = $baseUrl
    State = $state
    Registers = $registers
    Process = $process
    Ranges = $rangeReports
}

$manifestPath = Join-Path $snapshotDir "manifest.json"
$manifest | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

if (-not [string]::IsNullOrWhiteSpace($DiffFrom)) {
    $beforeManifest = Read-SnapshotManifest -Path $DiffFrom
    $beforeByName = @{}
    foreach ($range in $beforeManifest.Ranges) {
        $beforeByName[$range.Name] = $range
    }

    $diffReports = @()
    foreach ($afterRange in $rangeReports) {
        if (-not $afterRange.Ok -or -not $beforeByName.ContainsKey($afterRange.Name)) { continue }
        $beforeRange = $beforeByName[$afterRange.Name]
        if (-not $beforeRange.Ok) { continue }
        if (-not (Test-Path -LiteralPath $beforeRange.BinaryPath -PathType Leaf)) { continue }
        if (-not (Test-Path -LiteralPath $afterRange.BinaryPath -PathType Leaf)) { continue }

        $beforeBytes = [IO.File]::ReadAllBytes($beforeRange.BinaryPath)
        $afterBytes = [IO.File]::ReadAllBytes($afterRange.BinaryPath)
        $baseAddress = ConvertTo-UInt32Address $afterRange.Address
        $rows = Get-DiffRows -Before $beforeBytes -After $afterBytes -BaseAddress $baseAddress -RangeName $afterRange.Name -Limit $MaxDiffsPerRange
        $diffReports += [pscustomobject]@{
            Name = $afterRange.Name
            Address = $afterRange.Address
            Size = $afterRange.Size
            ComparedBytes = [Math]::Min($beforeBytes.Length, $afterBytes.Length)
            DiffCountReported = $rows.Count
            DiffRows = $rows
        }
    }

    $diff = [pscustomobject]@{
        CreatedAt = (Get-Date).ToString("O")
        Before = $beforeManifest.SnapshotDir
        After = $snapshotDir
        MaxDiffsPerRange = $MaxDiffsPerRange
        Ranges = $diffReports
    }

    $diffPath = Join-Path $snapshotDir "diff-from-before.json"
    $diff | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath $diffPath -Encoding UTF8
}

[pscustomobject]@{
    SnapshotDir = $snapshotDir
    ManifestPath = $manifestPath
    RangeCount = $rangeReports.Count
    FailedRanges = @($rangeReports | Where-Object { -not $_.Ok } | Select-Object -ExpandProperty Name)
} | ConvertTo-Json -Depth 20
