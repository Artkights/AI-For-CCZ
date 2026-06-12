param(
    [string]$BeforeSnapshot = "",
    [string]$AfterSnapshot = "",
    [string]$EventJsonl = "",
    [switch]$UseLatestEventLog,
    [string]$OutputRoot = "",
    [string]$Label = "auto-locate",
    [int]$MaxCandidates = 200,
    [int]$MaxDiffsPerRange = 20000,
    [int]$HardwareSlots = 4,
    [switch]$NoKnownMeritSeeds,
    [switch]$ApplyFirstBatch,
    [string]$HostName = "127.0.0.1",
    [int]$Port = 27042
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $workspaceRoot "CCZModStudio_Reports\DebugEvidence"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$safeLabel = ($Label -replace "[^0-9A-Za-z_.-]", "_").Trim("_")
if ([string]::IsNullOrWhiteSpace($safeLabel)) { $safeLabel = "auto-locate" }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$sessionDir = Join-Path $OutputRoot ("{0}-{1}" -f $safeLabel, $stamp)
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null

$baseUrl = "http://${HostName}:$Port"
$unitArrayBase = [uint32]0x004A7B20
$unitStride = 0x30

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
    param([object]$Value)

    $normalized = ConvertTo-NormalizedHexAddress $Value
    if ([string]::IsNullOrWhiteSpace($normalized) -or -not $normalized.StartsWith("0x")) {
        throw "Cannot parse address: $Value"
    }

    return [Convert]::ToUInt32($normalized.Substring(2), 16)
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

function Find-LatestEventJsonl {
    $root = Join-Path $workspaceRoot "CCZModStudio_Reports\DebugEvidence"
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { return "" }

    $latest = Get-ChildItem -LiteralPath $root -Recurse -Filter "events.jsonl" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($latest) { return $latest.FullName }
    return ""
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

function Get-RangeMap {
    param([object]$Manifest)

    $map = @{}
    foreach ($range in $Manifest.Ranges) {
        if ($range.Name) {
            $map[$range.Name] = $range
        }
    }

    return $map
}

function Get-RangeBytesFromManifestRange {
    param([object]$Range)

    if (-not $Range.Ok) { return New-Object byte[] 0 }
    if ([string]::IsNullOrWhiteSpace($Range.BinaryPath)) { return New-Object byte[] 0 }
    if (-not (Test-Path -LiteralPath $Range.BinaryPath -PathType Leaf)) { return New-Object byte[] 0 }
    return [IO.File]::ReadAllBytes($Range.BinaryPath)
}

$script:candidateMap = @{}

function Add-Candidate {
    param(
        [object]$Address,
        [int]$Size,
        [string]$Type,
        [int]$Score,
        [string]$Range = "",
        [int]$Offset = -1,
        [object]$Before = $null,
        [object]$After = $null,
        [object]$Delta = $null,
        [string]$Reason,
        [string]$Source,
        [string]$Confidence = "candidate"
    )

    $normalized = ConvertTo-NormalizedHexAddress $Address
    if ([string]::IsNullOrWhiteSpace($normalized) -or -not $normalized.StartsWith("0x")) { return }

    if ($Size -le 0) { $Size = 1 }
    $key = "{0}|{1}|{2}" -f $normalized, $Size, $Type
    if (-not $script:candidateMap.ContainsKey($key)) {
        $script:candidateMap[$key] = [ordered]@{
            Address = $normalized
            Size = $Size
            HardwareSize = if ($Size -le 1) { 1 } elseif ($Size -le 2) { 2 } elseif ($Size -le 4) { 4 } else { 1 }
            Type = $Type
            Score = 0
            Confidence = $Confidence
            Range = $Range
            Offset = if ($Offset -ge 0) { ("0x{0:X}" -f $Offset) } else { "" }
            Reasons = New-Object System.Collections.Generic.List[string]
            Evidence = New-Object System.Collections.Generic.List[object]
        }
    }

    $candidate = $script:candidateMap[$key]
    $candidate.Score = [int]$candidate.Score + $Score
    if (-not $candidate.Reasons.Contains($Reason)) {
        $candidate.Reasons.Add($Reason)
    }
    if ([string]::IsNullOrWhiteSpace($candidate.Range) -and -not [string]::IsNullOrWhiteSpace($Range)) {
        $candidate.Range = $Range
    }
    if ([string]::IsNullOrWhiteSpace($candidate.Offset) -and $Offset -ge 0) {
        $candidate.Offset = "0x{0:X}" -f $Offset
    }

    $candidate.Evidence.Add([pscustomobject]@{
        Source = $Source
        Range = $Range
        Offset = if ($Offset -ge 0) { ("0x{0:X}" -f $Offset) } else { "" }
        Before = if ($null -ne $Before) { [string]$Before } else { "" }
        After = if ($null -ne $After) { [string]$After } else { "" }
        Delta = if ($null -ne $Delta) { [string]$Delta } else { "" }
        Reason = $Reason
    })
}

function Get-RangeBaseScore {
    param([string]$RangeName)

    if ($RangeName -like "battle_globals*") { return 5 }
    if ($RangeName -like "char_data*") { return 4 }
    if ($RangeName -like "unit_array*") { return 4 }
    if ($RangeName -like "runtime_heap*") { return -2 }
    if ($RangeName -like "stack_*" -or $RangeName -like "frame_*") { return -3 }
    return 0
}

function Get-InterestingDeltaScore {
    param([int64]$Delta)

    switch ($Delta) {
        1 { return 12 }
        -1 { return 8 }
        2 { return 6 }
        -2 { return 5 }
        5 { return 8 }
        -5 { return 5 }
        10 { return 4 }
        -10 { return 4 }
        default { return 0 }
    }
}

function Test-NearKnownMeritSeed {
    param([uint32]$Address)

    foreach ($seed in @(0x00492F9C, 0x00496E79, 0x004B1B1A)) {
        $distance = [Math]::Abs([int64]$Address - [int64]$seed)
        if ($distance -le 0x20) { return $true }
    }

    return $false
}

function Add-KnownMeritSeeds {
    if ($NoKnownMeritSeeds) { return }

    foreach ($seed in @(
        @{ Address = "0x00492F9C"; Note = "physical sample +1 candidate in battle_globals" },
        @{ Address = "0x00496E79"; Note = "physical sample +1 candidate in battle_globals" },
        @{ Address = "0x004B1B1A"; Note = "physical sample +1 candidate outside initial broad range" }
    )) {
        Add-Candidate -Address $seed.Address -Size 1 -Type "Byte" -Score 22 -Range "known-merit-seed" -Reason $seed.Note -Source "prior-snapshot-diff" -Confidence "needs-write-breakpoint"
    }
}

function Analyze-SnapshotDiff {
    param(
        [string]$BeforePath,
        [string]$AfterPath
    )

    if ([string]::IsNullOrWhiteSpace($BeforePath) -or [string]::IsNullOrWhiteSpace($AfterPath)) { return $null }

    $beforeManifest = Read-SnapshotManifest -Path $BeforePath
    $afterManifest = Read-SnapshotManifest -Path $AfterPath
    $beforeMap = Get-RangeMap -Manifest $beforeManifest

    $rangeReports = New-Object System.Collections.Generic.List[object]
    foreach ($afterRange in $afterManifest.Ranges) {
        if (-not $afterRange.Ok) { continue }
        if (-not $beforeMap.ContainsKey($afterRange.Name)) { continue }
        $beforeRange = $beforeMap[$afterRange.Name]
        if (-not $beforeRange.Ok) { continue }

        $beforeBytes = Get-RangeBytesFromManifestRange -Range $beforeRange
        $afterBytes = Get-RangeBytesFromManifestRange -Range $afterRange
        if ($beforeBytes.Length -eq 0 -or $afterBytes.Length -eq 0) { continue }

        $max = [Math]::Min($beforeBytes.Length, $afterBytes.Length)
        $baseAddress = ConvertTo-UInt32Address $afterRange.Address
        $rangeBaseScore = Get-RangeBaseScore -RangeName $afterRange.Name
        $diffCount = 0
        $typedSeen = @{}

        for ($i = 0; $i -lt $max; $i++) {
            if ($beforeBytes[$i] -eq $afterBytes[$i]) { continue }
            $diffCount++
            if ($diffCount -gt $MaxDiffsPerRange) { break }

            $address = [uint32]($baseAddress + [uint32]$i)
            $delta = [int]$afterBytes[$i] - [int]$beforeBytes[$i]
            $score = 1 + $rangeBaseScore + (Get-InterestingDeltaScore -Delta $delta)
            if (Test-NearKnownMeritSeed -Address $address) { $score += 15 }
            $reason = if ((Get-InterestingDeltaScore -Delta $delta) -gt 0) { "interesting byte delta $delta" } else { "byte changed" }

            Add-Candidate -Address $address -Size 1 -Type "Byte" -Score $score -Range $afterRange.Name -Offset $i -Before ("0x{0:X2}" -f $beforeBytes[$i]) -After ("0x{0:X2}" -f $afterBytes[$i]) -Delta $delta -Reason $reason -Source "snapshot-diff" -Confidence "candidate"

            foreach ($size in @(2, 4)) {
                for ($start = $i - $size + 1; $start -le $i; $start++) {
                    if ($start -lt 0 -or ($start + $size) -gt $max) { continue }
                    $typedKey = "{0}|{1}" -f $start, $size
                    if ($typedSeen.ContainsKey($typedKey)) { continue }
                    $typedSeen[$typedKey] = $true

                    if ($size -eq 2) {
                        $beforeValue = Read-UInt16Le -Bytes $beforeBytes -Offset $start
                        $afterValue = Read-UInt16Le -Bytes $afterBytes -Offset $start
                        $type = "UInt16LE"
                    }
                    else {
                        $beforeValue = Read-UInt32Le -Bytes $beforeBytes -Offset $start
                        $afterValue = Read-UInt32Le -Bytes $afterBytes -Offset $start
                        $type = "UInt32LE"
                    }

                    if ($null -eq $beforeValue -or $null -eq $afterValue -or $beforeValue -eq $afterValue) { continue }
                    $typedDelta = [int64]$afterValue - [int64]$beforeValue
                    $typedDeltaScore = Get-InterestingDeltaScore -Delta $typedDelta
                    if ($typedDeltaScore -le 0 -and -not (Test-NearKnownMeritSeed -Address ([uint32]($baseAddress + [uint32]$start)))) { continue }

                    $typedScore = 6 + $rangeBaseScore + $typedDeltaScore
                    if (Test-NearKnownMeritSeed -Address ([uint32]($baseAddress + [uint32]$start))) { $typedScore += 15 }
                    Add-Candidate -Address ([uint32]($baseAddress + [uint32]$start)) -Size $size -Type $type -Score $typedScore -Range $afterRange.Name -Offset $start -Before $beforeValue -After $afterValue -Delta $typedDelta -Reason ("interesting {0} delta {1}" -f $type, $typedDelta) -Source "snapshot-diff" -Confidence "candidate"
                }
            }
        }

        $rangeReports.Add([pscustomobject]@{
            Name = $afterRange.Name
            Address = $afterRange.Address
            Size = $afterRange.Size
            ComparedBytes = $max
            DiffCountScanned = [Math]::Min($diffCount, $MaxDiffsPerRange)
            HitDiffLimit = ($diffCount -gt $MaxDiffsPerRange)
        })
    }

    return [pscustomobject]@{
        Before = $beforeManifest.SnapshotDir
        After = $afterManifest.SnapshotDir
        Ranges = $rangeReports
    }
}

function Convert-AttrStringToBytes {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return New-Object byte[] 0 }
    $parts = $Text -split "\s+" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $bytes = New-Object byte[] $parts.Count
    for ($i = 0; $i -lt $parts.Count; $i++) {
        $bytes[$i] = [Convert]::ToByte($parts[$i], 16)
    }
    return $bytes
}

function Analyze-EventJsonl {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "events.jsonl not found: $Path"
    }

    $fieldMap = @{
        DataIdByte = @{ Offset = 0x04; Size = 1; Score = 4; Type = "Byte"; Reason = "unit data-id byte changed" }
        Side = @{ Offset = 0x05; Size = 1; Score = 4; Type = "Byte"; Reason = "unit side byte changed" }
        X = @{ Offset = 0x06; Size = 1; Score = 6; Type = "Byte"; Reason = "unit x coordinate changed" }
        Y = @{ Offset = 0x07; Size = 1; Score = 6; Type = "Byte"; Reason = "unit y coordinate changed" }
        Action = @{ Offset = 0x0D; Size = 1; Score = 14; Type = "Byte"; Reason = "unit action-state byte changed" }
        HP = @{ Offset = 0x10; Size = 2; Score = 18; Type = "UInt16LE"; Reason = "unit HP changed" }
        MP = @{ Offset = 0x14; Size = 2; Score = 10; Type = "UInt16LE"; Reason = "unit MP changed" }
    }

    $unitDiffCount = 0
    $changeCount = 0
    $eventRange = New-Object System.Collections.Generic.List[int]
    $lines = Get-Content -LiteralPath $Path -Encoding UTF8
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $ev = $line | ConvertFrom-Json
        if ($ev.Type -ne "UnitDiff") { continue }
        $unitDiffCount++
        if ($ev.EventIndex) { $eventRange.Add([int]$ev.EventIndex) }

        foreach ($change in $ev.Changes) {
            if ($null -eq $change.UnitIndex) { continue }
            $unitIndex = [int]$change.UnitIndex
            $unitBase = [uint32]($unitArrayBase + [uint32]($unitIndex * $unitStride))

            if ($change.Before -and $change.After -and $change.Changes) {
                foreach ($fieldChange in $change.Changes) {
                    $field = [string]$fieldChange.Field
                    if ($fieldMap.ContainsKey($field)) {
                        $meta = $fieldMap[$field]
                        $address = [uint32]($unitBase + [uint32]$meta.Offset)
                        $before = $fieldChange.Before
                        $after = $fieldChange.After
                        $delta = $null
                        try { $delta = [int64]$after - [int64]$before } catch { $delta = "" }
                        Add-Candidate -Address $address -Size ([int]$meta.Size) -Type ([string]$meta.Type) -Score ([int]$meta.Score) -Range "unit_array_004A7B20" -Offset ($unitIndex * $unitStride + [int]$meta.Offset) -Before $before -After $after -Delta $delta -Reason ([string]$meta.Reason) -Source ("event-jsonl event {0} unit[{1}]" -f $ev.EventIndex, $unitIndex) -Confidence "observed-unit-field"
                        $changeCount++
                    }
                    elseif ($field -eq "Attrs") {
                        $beforeBytes = Convert-AttrStringToBytes ([string]$fieldChange.Before)
                        $afterBytes = Convert-AttrStringToBytes ([string]$fieldChange.After)
                        $limit = [Math]::Min($beforeBytes.Length, $afterBytes.Length)
                        for ($j = 0; $j -lt $limit; $j++) {
                            if ($beforeBytes[$j] -eq $afterBytes[$j]) { continue }
                            $address = [uint32]($unitBase + [uint32](0x18 + $j))
                            $delta = [int]$afterBytes[$j] - [int]$beforeBytes[$j]
                            Add-Candidate -Address $address -Size 1 -Type "Byte" -Score 9 -Range "unit_array_004A7B20" -Offset ($unitIndex * $unitStride + 0x18 + $j) -Before ("0x{0:X2}" -f $beforeBytes[$j]) -After ("0x{0:X2}" -f $afterBytes[$j]) -Delta $delta -Reason "unit Attrs byte changed" -Source ("event-jsonl event {0} unit[{1}]" -f $ev.EventIndex, $unitIndex) -Confidence "observed-unit-field"
                            $changeCount++
                        }
                    }
                }
            }
            elseif ($change.Kind -eq "UnitAppeared" -and $change.After) {
                $hpAddress = [uint32]($unitBase + 0x10)
                Add-Candidate -Address $hpAddress -Size 2 -Type "UInt16LE" -Score 3 -Range "unit_array_004A7B20" -Offset ($unitIndex * $unitStride + 0x10) -Before "" -After $change.After.HP -Reason "unit appeared with HP field" -Source ("event-jsonl event {0} unit[{1}]" -f $ev.EventIndex, $unitIndex) -Confidence "observed-unit-field"
            }
        }
    }

    return [pscustomobject]@{
        Path = (Resolve-Path -LiteralPath $Path).Path
        UnitDiffEventCount = $unitDiffCount
        FieldChangeCount = $changeCount
        FirstEvent = if ($eventRange.Count -gt 0) { ($eventRange | Measure-Object -Minimum).Minimum } else { $null }
        LastEvent = if ($eventRange.Count -gt 0) { ($eventRange | Measure-Object -Maximum).Maximum } else { $null }
    }
}

function Convert-CandidateMapToRows {
    $rows = foreach ($entry in $script:candidateMap.GetEnumerator()) {
        $c = $entry.Value
        $reasonRows = @()
        foreach ($reason in $c.Reasons) {
            $reasonRows += [string]$reason
        }

        $evidenceRows = @()
        foreach ($evidence in $c.Evidence) {
            $evidenceRows += $evidence
        }

        [pscustomobject][ordered]@{
            Address = $c.Address
            Size = $c.Size
            HardwareSize = $c.HardwareSize
            Type = $c.Type
            Score = [int]$c.Score
            Confidence = $c.Confidence
            Range = $c.Range
            Offset = $c.Offset
            Reasons = $reasonRows
            Evidence = $evidenceRows
        }
    }

    return @($rows | Sort-Object -Property @{ Expression = "Score"; Descending = $true }, Address | Select-Object -First $MaxCandidates)
}

function New-WatchpointPlan {
    param([object[]]$Candidates)

    $batches = New-Object System.Collections.Generic.List[object]
    $batchIndex = 1
    for ($i = 0; $i -lt $Candidates.Count; $i += $HardwareSlots) {
        $batchCandidates = @($Candidates | Select-Object -Skip $i -First $HardwareSlots)
        if ($batchCandidates.Count -eq 0) { continue }

        $commands = @()
        $mcpActions = @()
        foreach ($candidate in $batchCandidates) {
            $plain = ($candidate.Address -replace "^0x", "")
            $hwSize = [int]$candidate.HardwareSize
            $commands += ("bphws {0},w,{1}" -f $plain, $hwSize)
            $mcpActions += [pscustomobject]@{
                action = [pscustomobject]@{
                    action = "set_hardware"
                    address = $candidate.Address
                    type = "w"
                    size = [string]$hwSize
                }
            }
        }

        $batches.Add([pscustomobject]@{
            BatchIndex = $batchIndex
            CandidateCount = $batchCandidates.Count
            Candidates = $batchCandidates
            X32dbgCommands = $commands
            X64dbgMcpActions = $mcpActions
            CaptureHint = "When the game breaks on this batch, run run-x32dbg-until-planned-hit.ps1 -CaptureCurrentPaused to export registers, stack, disasm, and memory."
        })
        $batchIndex++
    }

    return [pscustomobject]@{
        CreatedAt = (Get-Date).ToString("O")
        HardwareSlots = $HardwareSlots
        CandidateCount = $Candidates.Count
        Batches = $batches
    }
}

function Select-MeritFocusedCandidates {
    param([object[]]$Candidates)

    $scored = foreach ($candidate in $Candidates) {
        $reasonText = (($candidate.Reasons | ForEach-Object { [string]$_ }) -join "; ")
        $range = [string]$candidate.Range
        $include = $false
        $priority = [int]$candidate.Score

        if ($range -eq "known-merit-seed" -or $reasonText -match "physical sample") {
            $include = $true
            $priority += 1000
        }

        if ($range -notlike "unit_array*" -and $reasonText -match "delta 1\b") {
            $include = $true
            $priority += 300
        }

        if ($range -like "battle_globals*") {
            $priority += 100
        }

        if ($candidate.Type -eq "Byte") {
            $priority += 80
        }
        elseif ($candidate.Type -eq "UInt16LE") {
            $priority += 30
        }

        if ($reasonText -match "interesting byte delta 1") {
            $priority += 80
        }
        elseif ($reasonText -match "interesting UInt16LE delta 1|interesting UInt32LE delta 1") {
            $priority += 40
        }

        if ($range -like "runtime_heap*" -or $range -like "stack_*" -or $range -like "frame_*") {
            $priority -= 250
        }

        if ($include) {
            [pscustomobject]@{
                Candidate = $candidate
                Priority = $priority
            }
        }
    }

    $selected = New-Object System.Collections.Generic.List[object]
    $seen = @{}
    foreach ($row in @($scored | Sort-Object -Property @{ Expression = "Priority"; Descending = $true }, @{ Expression = { $_.Candidate.Address } })) {
        $address = [string]$row.Candidate.Address
        if ($seen.ContainsKey($address)) { continue }
        $seen[$address] = $true
        $selected.Add($row.Candidate)
    }

    return @($selected | Select-Object -First ([Math]::Min($MaxCandidates, 64)))
}

function Write-WatchpointTextPlan {
    param(
        [object]$Plan,
        [string]$Path
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# x32dbg hardware write breakpoint plan")
    $lines.Add("# Generated: $((Get-Date).ToString("O"))")
    $lines.Add("# Use one batch at a time. Hardware breakpoint slots are limited.")
    $lines.Add("# Default command syntax used here: bphws ADDRESS,w,SIZE")
    $lines.Add("")

    foreach ($batch in $Plan.Batches) {
        $lines.Add(("## Batch {0}" -f $batch.BatchIndex))
        foreach ($candidate in $batch.Candidates) {
            $lines.Add(("# {0} size={1} score={2} range={3} reasons={4}" -f $candidate.Address, $candidate.HardwareSize, $candidate.Score, $candidate.Range, (($candidate.Reasons | Select-Object -First 3) -join "; ")))
        }
        foreach ($command in $batch.X32dbgCommands) {
            $lines.Add($command)
        }
        $lines.Add("")
    }

    Set-Content -LiteralPath $Path -Encoding UTF8 -Value $lines
}

function Apply-FirstBatch {
    param([object]$Plan)

    if (-not $ApplyFirstBatch) { return $null }
    if (-not $Plan.Batches -or $Plan.Batches.Count -eq 0) { return $null }

    $health = Invoke-X32dbgJson GET "/api/health" -TimeoutSec 3
    $results = New-Object System.Collections.Generic.List[object]
    foreach ($command in $Plan.Batches[0].X32dbgCommands) {
        $result = Invoke-X32dbgJson POST "/api/command/exec" -Body @{ command = $command } -TimeoutSec 5
        $results.Add([pscustomobject]@{
            Command = $command
            Result = $result
        })
    }

    return [pscustomobject]@{
        Health = $health
        AppliedBatch = 1
        Results = $results
        Note = "If x32dbg rejects bphws syntax, use watchpoint-plan.json X64dbgMcpActions through the x64dbg MCP tool."
    }
}

if ($UseLatestEventLog -and [string]::IsNullOrWhiteSpace($EventJsonl)) {
    $EventJsonl = Find-LatestEventJsonl
}

Add-KnownMeritSeeds
$snapshotReport = Analyze-SnapshotDiff -BeforePath $BeforeSnapshot -AfterPath $AfterSnapshot
$eventReport = Analyze-EventJsonl -Path $EventJsonl
$candidates = Convert-CandidateMapToRows
$plan = New-WatchpointPlan -Candidates $candidates
$meritCandidates = Select-MeritFocusedCandidates -Candidates $candidates
$meritPlan = New-WatchpointPlan -Candidates $meritCandidates
$applyReport = Apply-FirstBatch -Plan $plan

$candidatesPath = Join-Path $sessionDir "auto-locate-candidates.json"
$planPath = Join-Path $sessionDir "watchpoint-plan.json"
$planTextPath = Join-Path $sessionDir "x32dbg-watchpoint-plan.txt"
$meritPlanPath = Join-Path $sessionDir "merit-watchpoint-plan.json"
$meritPlanTextPath = Join-Path $sessionDir "x32dbg-merit-watchpoint-plan.txt"
$summaryPath = Join-Path $sessionDir "auto-locate-summary.md"

$report = [pscustomobject]@{
    CreatedAt = (Get-Date).ToString("O")
    WorkspaceRoot = $workspaceRoot
    SessionDir = $sessionDir
    BeforeSnapshot = $BeforeSnapshot
    AfterSnapshot = $AfterSnapshot
    EventJsonl = $EventJsonl
    SnapshotReport = $snapshotReport
    EventReport = $eventReport
    CandidateCount = $candidates.Count
    MeritCandidateCount = $meritCandidates.Count
    Candidates = $candidates
    MeritFocusedCandidates = $meritCandidates
    Safety = "Read-only analysis by default. ApplyFirstBatch only sets debugger hardware breakpoints; it does not write game files or process memory."
}

$report | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $candidatesPath -Encoding UTF8
$plan | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $planPath -Encoding UTF8
$meritPlan | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $meritPlanPath -Encoding UTF8
Write-WatchpointTextPlan -Plan $plan -Path $planTextPath
Write-WatchpointTextPlan -Plan $meritPlan -Path $meritPlanTextPath
if ($applyReport) {
    $applyReport | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $sessionDir "apply-first-batch-result.json") -Encoding UTF8
}

$top = @($candidates | Select-Object -First ([Math]::Min(20, $candidates.Count)))
$meritTop = @($meritCandidates | Select-Object -First ([Math]::Min(20, $meritCandidates.Count)))
$md = New-Object System.Collections.Generic.List[string]
$md.Add("# 6.5 Automatic Address Locator")
$md.Add("")
$md.Add("## Summary")
$md.Add("")
$md.Add("- Created: $((Get-Date).ToString("O"))")
$eventLogText = if ($EventJsonl) { $EventJsonl } else { "not provided" }
$beforeSnapshotText = if ($BeforeSnapshot) { $BeforeSnapshot } else { "not provided" }
$afterSnapshotText = if ($AfterSnapshot) { $AfterSnapshot } else { "not provided" }
$md.Add('- Session: `' + $sessionDir + '`')
$md.Add('- Event log: `' + $eventLogText + '`')
$md.Add('- Before snapshot: `' + $beforeSnapshotText + '`')
$md.Add('- After snapshot: `' + $afterSnapshotText + '`')
$md.Add('- Candidate count: `' + $candidates.Count + '`')
$md.Add('- Merit-focused candidate count: `' + $meritCandidates.Count + '`')
$md.Add("- Safety: read-only by default; no game file or process memory writes.")
$md.Add("")
$md.Add("## Top Candidates")
$md.Add("")
$md.Add("| Rank | Address | Size | Type | Score | Range | Confidence | Reasons |")
$md.Add("|---:|---|---:|---|---:|---|---|---|")
$rank = 1
foreach ($candidate in $top) {
    $reasons = (($candidate.Reasons | Select-Object -First 3) -join "; ") -replace "\|", "/"
    $md.Add(('| {0} | `{1}` | {2} | `{3}` | {4} | `{5}` | `{6}` | {7} |' -f $rank, $candidate.Address, $candidate.HardwareSize, $candidate.Type, $candidate.Score, $candidate.Range, $candidate.Confidence, $reasons))
    $rank++
}
$md.Add("")
$md.Add("## Merit-Focused Candidates")
$md.Add("")
$md.Add("| Rank | Address | Size | Type | Score | Range | Confidence | Reasons |")
$md.Add("|---:|---|---:|---|---:|---|---|---|")
$rank = 1
foreach ($candidate in $meritTop) {
    $reasons = (($candidate.Reasons | Select-Object -First 3) -join "; ") -replace "\|", "/"
    $md.Add(('| {0} | `{1}` | {2} | `{3}` | {4} | `{5}` | `{6}` | {7} |' -f $rank, $candidate.Address, $candidate.HardwareSize, $candidate.Type, $candidate.Score, $candidate.Range, $candidate.Confidence, $reasons))
    $rank++
}
$md.Add("")
$md.Add("## Outputs")
$md.Add("")
$md.Add('- Candidates JSON: `auto-locate-candidates.json`')
$md.Add('- Watchpoint JSON plan: `watchpoint-plan.json`')
$md.Add('- x32dbg command plan: `x32dbg-watchpoint-plan.txt`')
$md.Add('- Merit-focused watchpoint JSON plan: `merit-watchpoint-plan.json`')
$md.Add('- Merit-focused x32dbg command plan: `x32dbg-merit-watchpoint-plan.txt`')
$md.Add("")
$md.Add("## How To Use")
$md.Add("")
$md.Add("1. Start x32dbg with the 6.5 base and keep the game at a reproducible state.")
$md.Add('2. For merit work, apply one batch at a time from `x32dbg-merit-watchpoint-plan.txt`; for general unit-state work, use `x32dbg-watchpoint-plan.txt`.')
$md.Add("3. Trigger one narrow game event, such as one physical attack, one strategy, or one defeat.")
$md.Add('4. When x32dbg breaks, run `run-x32dbg-until-planned-hit.ps1 -CaptureCurrentPaused` to export registers, stack, disassembly, and memory.')
$md.Add("5. Promote a candidate to verified only after the same address/function is hit by the expected event and not by unrelated UI or flow changes.")
$md.Add("")
$md.Add("## Boundaries")
$md.Add("")
$md.Add("- Event-log unit fields are verified memory changes, not necessarily merit fields.")
$md.Add("- Known merit seed addresses are still candidates until write-breakpoint evidence identifies the writer and event semantics.")
$md.Add("- Hardware breakpoint slots are limited; use batches instead of setting every candidate at once.")

Set-Content -LiteralPath $summaryPath -Encoding UTF8 -Value $md

[pscustomobject]@{
    SessionDir = $sessionDir
    Summary = $summaryPath
    Candidates = $candidatesPath
    WatchpointPlan = $planPath
    X32dbgPlan = $planTextPath
    MeritWatchpointPlan = $meritPlanPath
    MeritX32dbgPlan = $meritPlanTextPath
    CandidateCount = $candidates.Count
    MeritCandidateCount = $meritCandidates.Count
    EventJsonl = $EventJsonl
    AppliedFirstBatch = [bool]$applyReport
} | ConvertTo-Json -Depth 20
