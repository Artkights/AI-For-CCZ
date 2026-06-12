param(
    [string]$InputDir = "",
    [string]$OutputDir = "",
    [string[]]$HitJson = @(),
    [string]$PlanJson = "",
    [string]$GameplayEventLabel = "",
    [string]$OperatorNote = "",
    [string]$TargetExeSha256 = "84E3A1DC085AE6F9900D1E8C388A9CD6766379832DDF51BC7BDF780C6615B4A3",
    [string]$KnowledgeDraftDir = "",
    [switch]$SkipKnowledgeDraft
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot
$evidenceRoot = Join-Path $workspaceRoot "CCZModStudio_Reports\DebugEvidence"
if ([string]::IsNullOrWhiteSpace($KnowledgeDraftDir)) {
    $KnowledgeDraftDir = Join-Path $workspaceRoot "CCZModStudio_Notes\DebugKnowledgeDrafts"
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

function Find-LatestWatchpointRunDir {
    if (-not (Test-Path -LiteralPath $evidenceRoot -PathType Container)) { return "" }

    $withHits = Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File -Filter "merit-watchpoint-hit-*.json" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($withHits) {
        return (Split-Path -Parent $withHits.FullName)
    }

    $latestSummary = Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File -Filter "merit-watchpoint-run-summary.json" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($latestSummary) {
        return (Split-Path -Parent $latestSummary.FullName)
    }

    return ""
}

function Find-RunSummary {
    param([string]$Directory)

    if ([string]::IsNullOrWhiteSpace($Directory) -or -not (Test-Path -LiteralPath $Directory -PathType Container)) {
        return $null
    }

    $summary = Get-ChildItem -LiteralPath $Directory -File -Filter "merit-watchpoint-run-summary.json" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($summary) {
        return (Get-Content -Raw -Encoding UTF8 -LiteralPath $summary.FullName | ConvertFrom-Json)
    }

    return $null
}

function Find-LatestPlan {
    if (-not (Test-Path -LiteralPath $evidenceRoot -PathType Container)) { return "" }

    $latest = Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File -Filter "merit-watchpoint-plan.json" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($latest) { return $latest.FullName }
    return ""
}

function Get-ApiSuccess {
    param([object]$Node)

    if ($null -eq $Node) { return $false }
    if ($Node.PSObject.Properties.Name -contains "Ok") {
        return [bool]$Node.Ok
    }
    if ($Node.PSObject.Properties.Name -contains "success") {
        return [bool]$Node.success
    }
    return $false
}

function Get-ApiData {
    param([object]$Node)

    if ($null -eq $Node) { return $null }
    if ($Node.PSObject.Properties.Name -contains "Data") {
        if ($Node.Data -and ($Node.Data.PSObject.Properties.Name -contains "data")) {
            return $Node.Data.data
        }
        return $Node.Data
    }
    if ($Node.PSObject.Properties.Name -contains "data") {
        return $Node.data
    }
    return $Node
}

function Get-RegisterMap {
    param([object]$RegistersNode)

    $data = Get-ApiData -Node $RegistersNode
    $map = [ordered]@{}
    if ($null -eq $data) { return $map }

    foreach ($name in @("eip", "cip", "eax", "ebx", "ecx", "edx", "esi", "edi", "esp", "ebp", "eflags")) {
        if ($data.PSObject.Properties.Name -contains $name) {
            $map[$name.ToUpperInvariant()] = [string]$data.$name
        }
    }

    return $map
}

function Get-FirstDisasmLine {
    param([object]$DisasmNode)

    $data = Get-ApiData -Node $DisasmNode
    if ($null -eq $data) { return "" }

    if ($data -is [array] -and $data.Count -gt 0) {
        $first = $data[0]
    }
    elseif ($data.PSObject.Properties.Name -contains "instructions" -and $data.instructions.Count -gt 0) {
        $first = $data.instructions[0]
    }
    elseif ($data.PSObject.Properties.Name -contains "items" -and $data.items.Count -gt 0) {
        $first = $data.items[0]
    }
    else {
        $first = $data
    }

    if ($null -eq $first) { return "" }
    foreach ($prop in @("text", "instruction", "disasm", "cmd", "command")) {
        if ($first.PSObject.Properties.Name -contains $prop) {
            return [string]$first.$prop
        }
    }

    return (($first | ConvertTo-Json -Depth 6 -Compress) -replace "\|", "/")
}

function Convert-HitToRow {
    param(
        [object]$Hit,
        [string]$Path
    )

    $candidateRows = New-Object System.Collections.Generic.List[object]
    foreach ($memory in @($Hit.CandidateMemory)) {
        $candidate = $memory.Candidate
        $candidateRows.Add([pscustomobject]@{
            Address = ConvertTo-NormalizedHexAddress $memory.CandidateAddress
            ReadAddress = ConvertTo-NormalizedHexAddress $memory.ReadAddress
            Size = $memory.Size
            Score = if ($candidate -and ($candidate.PSObject.Properties.Name -contains "Score")) { $candidate.Score } else { "" }
            Type = if ($candidate -and ($candidate.PSObject.Properties.Name -contains "Type")) { $candidate.Type } else { "" }
            Range = if ($candidate -and ($candidate.PSObject.Properties.Name -contains "Range")) { $candidate.Range } else { "" }
            Reasons = if ($candidate -and ($candidate.PSObject.Properties.Name -contains "Reasons")) { (@($candidate.Reasons) -join "; ") } else { "" }
            ReadOk = Get-ApiSuccess -Node $memory.Read
        })
    }

    $registerMap = Get-RegisterMap -RegistersNode $Hit.Registers
    $cip = ConvertTo-NormalizedHexAddress $Hit.Cip
    if ([string]::IsNullOrWhiteSpace($cip)) {
        $stateData = Get-ApiData -Node $Hit.State
        if ($stateData -and ($stateData.PSObject.Properties.Name -contains "cip")) {
            $cip = ConvertTo-NormalizedHexAddress $stateData.cip
        }
    }

    $classification = "dynamic-hit-needs-human-correlation"
    if ([string]::IsNullOrWhiteSpace($cip)) {
        $classification = "hit-without-cip-unusable"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($GameplayEventLabel)) {
        $classification = "dynamic-hit-with-gameplay-label"
    }

    return [pscustomobject]@{
        Path = $Path
        CreatedAt = $Hit.CreatedAt
        Cip = $cip
        StopReason = $Hit.StopReason
        Classification = $classification
        GameplayEventLabel = $GameplayEventLabel
        OperatorNote = $OperatorNote
        FirstDisasmLine = Get-FirstDisasmLine -DisasmNode $Hit.DisasmAtCip
        Registers = $registerMap
        CandidateMemory = $candidateRows
        CandidateAddresses = @($candidateRows | ForEach-Object { $_.Address } | Sort-Object -Unique)
    }
}

if ([string]::IsNullOrWhiteSpace($InputDir)) {
    $InputDir = Find-LatestWatchpointRunDir
}

if ([string]::IsNullOrWhiteSpace($InputDir) -or -not (Test-Path -LiteralPath $InputDir -PathType Container)) {
    throw "No watchpoint run directory found. Run run-6.5-merit-watchpoint-experiment.ps1 first, or pass -InputDir."
}

$InputDir = (Resolve-Path -LiteralPath $InputDir).Path
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = $InputDir
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

if ($HitJson.Count -eq 0) {
    $HitJson = @(Get-ChildItem -LiteralPath $InputDir -File -Filter "merit-watchpoint-hit-*.json" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "^merit-watchpoint-hit-\d{8}-\d{6}\.json$" } |
        Sort-Object LastWriteTime |
        ForEach-Object { $_.FullName })
}

if ([string]::IsNullOrWhiteSpace($PlanJson)) {
    $runSummary = Find-RunSummary -Directory $InputDir
    if ($runSummary -and ($runSummary.PSObject.Properties.Name -contains "SourcePlan") -and -not [string]::IsNullOrWhiteSpace($runSummary.SourcePlan)) {
        $PlanJson = $runSummary.SourcePlan
    }
    else {
        $PlanJson = Find-LatestPlan
    }
}

$plan = $null
if (-not [string]::IsNullOrWhiteSpace($PlanJson) -and (Test-Path -LiteralPath $PlanJson -PathType Leaf)) {
    $PlanJson = (Resolve-Path -LiteralPath $PlanJson).Path
    $plan = Get-Content -Raw -Encoding UTF8 -LiteralPath $PlanJson | ConvertFrom-Json
}

$hitRows = New-Object System.Collections.Generic.List[object]
foreach ($path in $HitJson) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $resolved = (Resolve-Path -LiteralPath $path).Path
    $hit = Get-Content -Raw -Encoding UTF8 -LiteralPath $resolved | ConvertFrom-Json
    $hitRows.Add((Convert-HitToRow -Hit $hit -Path $resolved))
}

$runSummary = Find-RunSummary -Directory $InputDir
$candidateRows = @()
if ($plan) {
    foreach ($batch in @($plan.Batches)) {
        foreach ($candidate in @($batch.Candidates)) {
            $candidateRows += [pscustomobject]@{
                BatchIndex = $batch.BatchIndex
                Address = ConvertTo-NormalizedHexAddress $candidate.Address
                Size = $candidate.HardwareSize
                Score = $candidate.Score
                Confidence = $candidate.Confidence
                Range = $candidate.Range
                Reasons = (@($candidate.Reasons) -join "; ")
            }
        }
    }
}

$status = if ($hitRows.Count -gt 0) {
    if ([string]::IsNullOrWhiteSpace($GameplayEventLabel)) { "hit-captured-needs-gameplay-label" } else { "hit-captured-with-gameplay-label" }
}
elseif ($runSummary -and $runSummary.BridgeAvailable) {
    "no-hit-captured"
}
else {
    "no-hit-bridge-offline-or-dryrun"
}

$report = [pscustomobject]@{
    CreatedAt = (Get-Date).ToString("O")
    InputDir = $InputDir
    OutputDir = $OutputDir
    SourcePlan = if ($PlanJson) { $PlanJson } else { "" }
    TargetExeSha256 = $TargetExeSha256
    GameplayEventLabel = $GameplayEventLabel
    OperatorNote = $OperatorNote
    Status = $status
    HitCount = $hitRows.Count
    Hits = $hitRows
    PlanCandidates = $candidateRows
    RunSummary = $runSummary
    KnowledgeDraft = ""
    KnowledgeBoundary = "Do not promote a merit candidate to verified unless a hit has CIP, disassembly/register evidence, and a human/gameplay label tying it to one narrow merit event."
}

$jsonPath = Join-Path $OutputDir "merit-watchpoint-hit-summary.json"
$report | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$mdPath = Join-Path $OutputDir "merit-watchpoint-hit-summary.md"
$md = New-Object System.Collections.Generic.List[string]
$md.Add("# 6.5 Merit Watchpoint Hit Summary")
$md.Add("")
$md.Add("## Summary")
$md.Add("")
$md.Add("- Created: $($report.CreatedAt)")
$md.Add('- Input: `' + $InputDir + '`')
$md.Add('- Plan: `' + $(if ($PlanJson) { $PlanJson } else { "not found" }) + '`')
$md.Add('- Target EXE SHA256: `' + $TargetExeSha256 + '`')
$md.Add('- Status: `' + $status + '`')
$md.Add('- Hit count: `' + $hitRows.Count + '`')
$eventText = if ([string]::IsNullOrWhiteSpace($GameplayEventLabel)) { "not supplied" } else { $GameplayEventLabel }
$md.Add('- Gameplay event label: `' + $eventText + '`')
if (-not [string]::IsNullOrWhiteSpace($OperatorNote)) {
    $md.Add('- Operator note: ' + ($OperatorNote -replace "\|", "/"))
}
$md.Add("")

if ($candidateRows.Count -gt 0) {
    $md.Add("## Planned Candidates")
    $md.Add("")
    $md.Add("| Batch | Address | Size | Score | Confidence | Reasons |")
    $md.Add("|---:|---|---:|---:|---|---|")
    foreach ($candidate in @($candidateRows | Select-Object -First 16)) {
        $reasons = ([string]$candidate.Reasons) -replace "\|", "/"
        $md.Add(('| {0} | `{1}` | {2} | {3} | `{4}` | {5} |' -f $candidate.BatchIndex, $candidate.Address, $candidate.Size, $candidate.Score, $candidate.Confidence, $reasons))
    }
    $md.Add("")
}

if ($hitRows.Count -eq 0) {
    $md.Add("## Result")
    $md.Add("")
    $md.Add("- No `merit-watchpoint-hit-*.json` file was found in this run directory.")
    if ($runSummary) {
        $md.Add('- Bridge available in run summary: `' + [bool]$runSummary.BridgeAvailable + '`')
        $md.Add('- DryRun: `' + [bool]$runSummary.DryRun + '`')
        $md.Add('- ApplyOnly: `' + [bool]$runSummary.ApplyOnly + '`')
    }
    $md.Add('- Current conclusion remains `needs-write-breakpoint`; no knowledge-base fact should be promoted.')
}
else {
    $md.Add("## Hits")
    $md.Add("")
    foreach ($hit in $hitRows) {
        $md.Add("### " + $hit.Cip)
        $md.Add("")
        $md.Add('- Source: `' + $hit.Path + '`')
        $md.Add('- Stop reason: `' + $hit.StopReason + '`')
        $md.Add('- Classification: `' + $hit.Classification + '`')
        if (-not [string]::IsNullOrWhiteSpace($hit.FirstDisasmLine)) {
            $md.Add('- First disasm: `' + ($hit.FirstDisasmLine -replace "`r?`n", " ") + '`')
        }
        $md.Add('- Candidate addresses: `' + ((@($hit.CandidateAddresses) -join ", ")) + '`')
        if ($hit.Registers.Count -gt 0) {
            $registerText = (@($hit.Registers.GetEnumerator() | ForEach-Object { "{0}={1}" -f $_.Key, $_.Value }) -join "; ")
            $md.Add('- Registers: `' + $registerText + '`')
        }
        $md.Add("")
        $md.Add("| Candidate | Read address | Read OK | Type | Range | Reasons |")
        $md.Add("|---|---|---:|---|---|---|")
        foreach ($candidateMemory in $hit.CandidateMemory) {
            $reasons = ([string]$candidateMemory.Reasons) -replace "\|", "/"
            $md.Add(('| `{0}` | `{1}` | {2} | `{3}` | `{4}` | {5} |' -f $candidateMemory.Address, $candidateMemory.ReadAddress, $candidateMemory.ReadOk, $candidateMemory.Type, $candidateMemory.Range, $reasons))
        }
        $md.Add("")
    }
}

$md.Add("")
$md.Add("## Knowledge Base Decision")
$md.Add("")
if ($hitRows.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($GameplayEventLabel)) {
    $md.Add('- Evidence may be written as `dynamic-hit-with-gameplay-label`, but still requires review before upgrading to `verified`.')
    $md.Add("- Promote only the writer function/address actually shown by CIP/disassembly, not every candidate in the batch.")
}
elseif ($hitRows.Count -gt 0) {
    $md.Add('- A debugger hit exists, but no gameplay event label was supplied; keep it below `verified` until the operator ties it to one narrow in-game action.')
}
else {
    $md.Add('- No hit exists; keep all merit candidates as `needs-write-breakpoint`.')
}
$md.Add("- Required fields for promotion: EXE hash, VA, original candidate value, write breakpoint hit, CIP, disassembly, registers/stack, candidate memory, and one narrow UI/gameplay event.")

Set-Content -LiteralPath $mdPath -Encoding UTF8 -Value $md

$knowledgeDraftPath = ""
if (-not $SkipKnowledgeDraft) {
    New-Item -ItemType Directory -Force -Path $KnowledgeDraftDir | Out-Null
    $knowledgeDraftPath = Join-Path $KnowledgeDraftDir ("merit-knowledge-draft-{0}.md" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

    $draft = New-Object System.Collections.Generic.List[string]
    $draft.Add("# Merit Watchpoint Knowledge Draft")
    $draft.Add("")
    $draft.Add("## Status")
    $draft.Add("")
    $draft.Add("- Created: " + $report.CreatedAt)
    $draft.Add("- Target EXE SHA256: " + $TargetExeSha256)
    $draft.Add("- Source plan: " + $(if ($PlanJson) { $PlanJson } else { "not found" }))
    $draft.Add("- Source summary: " + $mdPath)
    $draft.Add("- Status: " + $status)
    $draft.Add("- Hit count: " + $hitRows.Count)
    $draft.Add("")

    if ($hitRows.Count -eq 0) {
        $draft.Add("No merit-watchpoint-hit-YYYYMMDD-HHMMSS.json was captured. This draft is only automation evidence and must not be promoted to verified knowledge.")
        $draft.Add("")
        $draft.Add("Allowed pending-note wording:")
        $draft.Add("")
        $draft.Add("- Merit candidate addresses still require write-breakpoint closure: 0x00492F9C, 0x00496E79, 0x004B1B1A, 0x00492C20.")
        $draft.Add("- Next run must keep x32dbg bridge online, place the game before one narrow merit event, and capture CIP, disassembly, registers, candidate memory, and UI merit change.")
    }
    else {
        $draft.Add("Write-breakpoint hits were captured. This is still a draft; review before moving any statement into the knowledge base.")
        $draft.Add("")
        foreach ($hit in $hitRows) {
            $draft.Add("### Hit " + $hit.Cip)
            $draft.Add("")
            $draft.Add("- Evidence class: " + $hit.Classification)
            $draft.Add("- Gameplay event label: " + $(if ([string]::IsNullOrWhiteSpace($hit.GameplayEventLabel)) { "not supplied" } else { $hit.GameplayEventLabel }))
            $draft.Add("- Source: " + $hit.Path)
            if (-not [string]::IsNullOrWhiteSpace($hit.FirstDisasmLine)) {
                $draft.Add("- First disasm: " + ($hit.FirstDisasmLine -replace '\r?\n', " "))
            }
            $draft.Add("- Candidate addresses: " + ((@($hit.CandidateAddresses) -join ", ")))
            $draft.Add("")
            $draft.Add("Review checklist:")
            $draft.Add("")
            $draft.Add("- [ ] CIP/disassembly explains the candidate write.")
            $draft.Add("- [ ] Registers/stack connect the write to a battle unit or merit field.")
            $draft.Add("- [ ] Gameplay label is one narrow event and matches the UI merit change.")
            $draft.Add("- [ ] Unrelated UI/flow changes do not cause the same hit, or the false-positive source is explained.")
            $draft.Add("")
        }
    }

    $draft.Add("")
    $draft.Add("## Suggested Knowledge Targets")
    $draft.Add("")
    $draft.Add("- tools knowledge base / 03 mechanisms / merit mode: write only reviewed merit rules and field/writer conclusions.")
    $draft.Add("- tools knowledge base / 00 pending list: keep unclosed addresses, trigger conditions, and evidence gaps.")
    $draft.Add("- tools knowledge base / 04 function index: write only when the CIP function semantics are clear.")
    Set-Content -LiteralPath $knowledgeDraftPath -Encoding UTF8 -Value $draft
}

$report.KnowledgeDraft = $knowledgeDraftPath
$report | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

[pscustomobject]@{
    Summary = $jsonPath
    Markdown = $mdPath
    KnowledgeDraft = $knowledgeDraftPath
    InputDir = $InputDir
    Status = $status
    HitCount = $hitRows.Count
    PlanCandidateCount = $candidateRows.Count
} | ConvertTo-Json -Depth 20
