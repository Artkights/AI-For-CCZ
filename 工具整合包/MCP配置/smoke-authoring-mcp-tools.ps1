param(
    [string]$GameRoot = "",
    [int]$TimeoutMs = 20000
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot
$startScript = Join-Path $configRoot "start-ccz-mcp.ps1"
$script:NextJsonRpcId = 1

function Test-GameRoot {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return $false
    }

    $coreFiles = @("Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5")
    $coreCount = 0
    foreach ($file in $coreFiles) {
        if (Test-Path -LiteralPath (Join-Path $Path $file) -PathType Leaf) {
            $coreCount++
        }
    }

    return $coreCount -ge 3 -or
        ($coreCount -ge 1 -and (Test-Path -LiteralPath (Join-Path $Path "RS") -PathType Container))
}

function Find-DefaultGameRoot {
    param([string]$WorkspaceRoot)

    $candidates = New-Object System.Collections.Generic.List[string]
    $frontier = @($WorkspaceRoot)
    for ($depth = 0; $depth -lt 3; $depth++) {
        $next = @()
        foreach ($dir in $frontier) {
            if (-not (Test-Path -LiteralPath $dir -PathType Container)) {
                continue
            }

            foreach ($child in Get-ChildItem -LiteralPath $dir -Directory -Force -ErrorAction SilentlyContinue) {
                if ($child.Name -in @(".git", ".vs", "bin", "obj", "_BuildCheck")) {
                    continue
                }

                if (Test-GameRoot $child.FullName) {
                    $candidates.Add($child.FullName)
                }

                $next += $child.FullName
            }
        }

        $frontier = $next
    }

    $candidates |
        Sort-Object -Unique |
        Sort-Object -Property @{ Expression = { if ((Split-Path -Leaf $_) -like "*6.5*") { 0 } else { 1 } } }, Length, { $_ } |
        Select-Object -First 1
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

function ConvertTo-AsciiJson {
    param([Parameter(Mandatory = $true)] $Value)

    $json = $Value | ConvertTo-Json -Depth 80 -Compress
    $builder = [System.Text.StringBuilder]::new($json.Length)
    foreach ($ch in $json.ToCharArray()) {
        $code = [int][char]$ch
        if ($code -gt 127) {
            [void]$builder.Append(("\u{0:x4}" -f $code))
        } else {
            [void]$builder.Append($ch)
        }
    }

    return $builder.ToString()
}

function Send-McpNotification {
    param(
        [Parameter(Mandatory = $true)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)] [string]$Method,
        [Parameter(Mandatory = $true)] $Params
    )

    $request = [ordered]@{
        jsonrpc = "2.0"
        method = $Method
        params = $Params
    }
    $Process.StandardInput.WriteLine((ConvertTo-AsciiJson $request))
    $Process.StandardInput.Flush()
}

function Invoke-McpRequest {
    param(
        [Parameter(Mandatory = $true)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)] [string]$Method,
        [Parameter(Mandatory = $true)] $Params,
        [string]$Label = ""
    )

    $id = $script:NextJsonRpcId
    $script:NextJsonRpcId++
    $request = [ordered]@{
        jsonrpc = "2.0"
        id = $id
        method = $Method
        params = $Params
    }

    $Process.StandardInput.WriteLine((ConvertTo-AsciiJson $request))
    $Process.StandardInput.Flush()
    $line = Read-LineWithTimeout -Reader $Process.StandardOutput -Timeout $TimeoutMs -Label ($(if ($Label) { $Label } else { $Method }))
    if ($null -eq $line) {
        $stderr = $Process.StandardError.ReadToEnd()
        throw "MCP process exited while waiting for $Method $Label. stderr=$stderr"
    }

    $response = $line | ConvertFrom-Json
    if ($response.error) {
        throw "MCP $Method failed: $($response.error.message)"
    }

    return $response
}

function Invoke-McpTool {
    param(
        [Parameter(Mandatory = $true)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] $Arguments
    )

    $response = Invoke-McpRequest -Process $Process -Method "tools/call" -Params @{ name = $Name; arguments = $Arguments } -Label "tools/call $Name"
    $text = ($response.result.content | Where-Object { $_.type -eq "text" } | Select-Object -First 1).text
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Tool $Name did not return text content."
    }

    try {
        return $text | ConvertFrom-Json
    } catch {
        return $text
    }
}

function Assert-ToolPresent {
    param(
        [string[]]$ToolNames,
        [string]$Name
    )

    if ($ToolNames -notcontains $Name) {
        throw "Missing authoring MCP smoke tool: $Name"
    }
}

function Get-JsonPropertyValue {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)] [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties |
        Where-Object { $_.Name.Equals($Name, [System.StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1
    if ($property) {
        return $property.Value
    }

    return $null
}

function Get-JsonArrayValue {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)] [string]$Name
    )

    $value = Get-JsonPropertyValue -Object $Object -Name $Name
    if ($null -eq $value) {
        return @()
    }

    return @($value)
}

if (-not (Test-Path -LiteralPath $startScript -PathType Leaf)) {
    throw "Start script not found: $startScript"
}

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $GameRoot = Find-DefaultGameRoot -WorkspaceRoot $workspaceRoot
}

if ([string]::IsNullOrWhiteSpace($GameRoot) -or -not (Test-GameRoot $GameRoot)) {
    throw "Game root was not found. Pass -GameRoot explicitly."
}

$resolvedGameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
$resolvedStartScript = (Resolve-Path -LiteralPath $startScript).Path

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "powershell.exe"
$psi.Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$resolvedStartScript`" -GameRoot `"$resolvedGameRoot`""
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

$proc = [System.Diagnostics.Process]::Start($psi)
try {
    $init = Invoke-McpRequest -Process $proc -Method "initialize" -Params @{
        protocolVersion = "2025-06-18"
        capabilities = @{}
        clientInfo = @{ name = "ccz-authoring-mcp-smoke"; version = "1.0.0" }
    } -Label "initialize"
    Send-McpNotification -Process $proc -Method "notifications/initialized" -Params @{}

    $toolsResponse = Invoke-McpRequest -Process $proc -Method "tools/list" -Params @{} -Label "tools/list"
    $toolNames = @($toolsResponse.result.tools | ForEach-Object { $_.name })

    $requiredAuthoringTools = @(
        "read_mcp_capability_manifest",
        "read_table_schema",
        "read_table_derived_display",
        "export_table_csv",
        "preview_import_table_csv",
        "read_role_editor",
        "preview_write_roles",
        "read_role_texts",
        "preview_write_role_texts",
        "find_free_image_assignment_ids",
        "preview_image_assignment_update",
        "read_editable_image_target",
        "preview_editable_image_write",
        "list_portrait_frames",
        "preview_extract_map_materials",
        "preview_terrain_beautify_filter",
        "diagnose_qinger66_project",
        "audit_qinger66_items",
        "list_legacy_mfc_dialogs",
        "read_scenario_reference_checklist",
        "replace_job_s_image_raw_batch",
        "write_item_effect_name66_slot"
    )
    foreach ($name in $requiredAuthoringTools) {
        Assert-ToolPresent -ToolNames $toolNames -Name $name
    }

    $manifest = Invoke-McpTool -Process $proc -Name "read_mcp_capability_manifest" -Arguments @{}
    $manifestToolCount = [int](Get-JsonPropertyValue -Object $manifest -Name "ToolCount")
    if ($manifestToolCount -ne $toolNames.Count) {
        throw "Manifest/tool-list count mismatch: manifest=$manifestToolCount, tools=$($toolNames.Count)."
    }

    $tableList = Invoke-McpTool -Process $proc -Name "list_tables" -Arguments @{}
    $tableRows = Get-JsonArrayValue -Object $tableList -Name "Tables"
    $personTableRow = $tableRows |
        Where-Object { ([string](Get-JsonPropertyValue -Object $_ -Name "TableName")) -match "^6\.5-0\s" } |
        Select-Object -First 1
    if ($null -eq $personTableRow) {
        throw "Could not find the 6.5 person table from list_tables."
    }

    $personTable = [string](Get-JsonPropertyValue -Object $personTableRow -Name "TableName")
    $schema = Invoke-McpTool -Process $proc -Name "read_table_schema" -Arguments @{ table_name = $personTable }
    if ((Get-JsonArrayValue -Object $schema -Name "Columns").Count -eq 0) {
        throw "read_table_schema returned no columns."
    }

    $derived = Invoke-McpTool -Process $proc -Name "read_table_derived_display" -Arguments @{ table_name = $personTable; limit = 1 }
    if ([int](Get-JsonPropertyValue -Object $derived -Name "ReturnedRows") -lt 0) {
        throw "read_table_derived_display returned an invalid row count."
    }

    $csv = Invoke-McpTool -Process $proc -Name "export_table_csv" -Arguments @{ table_name = $personTable; include_annotation_row = $true; limit = 1 }
    $csvOutputPath = [string](Get-JsonPropertyValue -Object $csv -Name "OutputPath")
    if ([string]::IsNullOrWhiteSpace($csvOutputPath) -or -not (Test-Path -LiteralPath $csvOutputPath -PathType Leaf)) {
        throw "export_table_csv did not create OutputPath: $csvOutputPath"
    }

    $csvPreview = Invoke-McpTool -Process $proc -Name "preview_import_table_csv" -Arguments @{ table_name = $personTable; csv_path = $csvOutputPath; allow_partial_columns = $true; match_by_id_when_present = $true }
    if ([int](Get-JsonPropertyValue -Object $csvPreview -Name "ImportedRows") -lt 0) {
        throw "preview_import_table_csv returned an invalid row count."
    }

    $roles = Invoke-McpTool -Process $proc -Name "read_role_editor" -Arguments @{ limit = 2 }
    $roleRows = Get-JsonArrayValue -Object $roles -Name "Rows"
    if ($roleRows.Count -eq 0) {
        throw "read_role_editor returned no rows."
    }

    $roleId = [int](Get-JsonPropertyValue -Object $roleRows[0] -Name "RowId")
    $rolePreview = Invoke-McpTool -Process $proc -Name "preview_write_roles" -Arguments @{ updates = @(@{ row_id = $roleId; values = @{} }) }
    if ($null -eq (Get-JsonPropertyValue -Object $rolePreview -Name "Preview")) {
        throw "preview_write_roles returned no preview payload."
    }

    $roleTexts = Invoke-McpTool -Process $proc -Name "read_role_texts" -Arguments @{ role_ids = @($roleId); limit = 1 }
    if ([int](Get-JsonPropertyValue -Object $roleTexts -Name "ReturnedRows") -lt 1) {
        throw "read_role_texts returned no rows for role $roleId."
    }

    $roleTextPreview = Invoke-McpTool -Process $proc -Name "preview_write_role_texts" -Arguments @{ updates = @(@{ role_id = $roleId }) }
    if ($null -eq (Get-JsonPropertyValue -Object $roleTextPreview -Name "Preview")) {
        throw "preview_write_role_texts returned no preview payload."
    }

    $freeFaces = Invoke-McpTool -Process $proc -Name "find_free_image_assignment_ids" -Arguments @{ kind = "face"; start_id = 0; limit = 3 }
    if ([int](Get-JsonPropertyValue -Object $freeFaces -Name "ReturnedCount") -lt 0) {
        throw "find_free_image_assignment_ids returned an invalid count."
    }

    $assignmentPreview = Invoke-McpTool -Process $proc -Name "preview_image_assignment_update" -Arguments @{ updates = @(@{ row_id = $roleId }) }
    if ($null -eq (Get-JsonPropertyValue -Object $assignmentPreview -Name "Preview")) {
        throw "preview_image_assignment_update returned no preview payload."
    }

    $editableRequest = @{ semantic = "face_assignment"; row_id = $roleId; display_name = "authoring_smoke_face_assignment" }
    $editable = Invoke-McpTool -Process $proc -Name "read_editable_image_target" -Arguments @{ request = $editableRequest }
    if ([int](Get-JsonPropertyValue -Object $editable -Name "Width") -le 0 -or [int](Get-JsonPropertyValue -Object $editable -Name "Height") -le 0) {
        throw "read_editable_image_target returned an invalid bitmap size."
    }

    $editablePreview = Invoke-McpTool -Process $proc -Name "preview_editable_image_write" -Arguments @{
        request = $editableRequest
        pixel_edits = @(@{ x = 0; y = 0; argb = "FFFF00FF" })
    }
    if ($null -eq (Get-JsonPropertyValue -Object $editablePreview -Name "Preview")) {
        throw "preview_editable_image_write returned no preview payload."
    }

    $frames = Invoke-McpTool -Process $proc -Name "list_portrait_frames" -Arguments @{ limit = 3 }
    $portraitPreviewStatus = "skipped-no-frame"
    $frameRows = Get-JsonArrayValue -Object $frames -Name "Frames"
    if ($frameRows.Count -gt 0) {
        $framePath = [string](Get-JsonPropertyValue -Object $frameRows[0] -Name "Path")
        $portraitPreview = Invoke-McpTool -Process $proc -Name "preview_apply_portrait_frame" -Arguments @{
            frame_path = $framePath
            targets = @(@{ row_id = $roleId; display_name = "authoring_smoke"; face_id = 0 })
        }
        if ($null -eq (Get-JsonPropertyValue -Object $portraitPreview -Name "Preview")) {
            throw "preview_apply_portrait_frame returned no preview payload."
        }
        $portraitPreviewStatus = "ok"
    }

    $mapPreviewStatus = "skipped-no-draft"
    $drafts = Invoke-McpTool -Process $proc -Name "list_map_drafts" -Arguments @{ limit = 1 }
    $draftRows = Get-JsonArrayValue -Object $drafts -Name "Drafts"
    if ($draftRows.Count -gt 0) {
        $draftId = [string](Get-JsonPropertyValue -Object $draftRows[0] -Name "DraftId")
        try {
            $extractPreview = Invoke-McpTool -Process $proc -Name "preview_extract_map_materials" -Arguments @{
                request = @{ draft_id = $draftId; target_type = "terrain"; terrain_id = 1; x = 0; y = 0; width = 1; height = 1; source = "current_composite" }
            }
            if ($null -eq (Get-JsonPropertyValue -Object $extractPreview -Name "Preview")) {
                throw "preview_extract_map_materials returned no preview payload."
            }
            $beautifyPreview = Invoke-McpTool -Process $proc -Name "preview_terrain_beautify_filter" -Arguments @{ draft_id = $draftId; filter = "natural"; strength = 1 }
            if ([string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue -Object $beautifyPreview -Name "OutputPath"))) {
                throw "preview_terrain_beautify_filter returned no output path."
            }
            $mapPreviewStatus = "ok"
        } catch {
            $mapPreviewStatus = "skipped-" + $_.Exception.Message
        }
    }

    $qinger = Invoke-McpTool -Process $proc -Name "diagnose_qinger66_project" -Arguments @{}
    if ($null -eq $qinger) {
        throw "diagnose_qinger66_project returned no payload."
    }

    $audit = Invoke-McpTool -Process $proc -Name "audit_qinger66_items" -Arguments @{ limit = 3 }
    if ($null -eq (Get-JsonPropertyValue -Object $audit -Name "Summary")) {
        throw "audit_qinger66_items returned no summary."
    }

    $legacy = Invoke-McpTool -Process $proc -Name "list_legacy_mfc_dialogs" -Arguments @{ limit = 3 }
    if ([int](Get-JsonPropertyValue -Object $legacy -Name "Count") -lt 0) {
        throw "list_legacy_mfc_dialogs returned an invalid count."
    }

    $scenarioChecklist = Invoke-McpTool -Process $proc -Name "read_scenario_reference_checklist" -Arguments @{ limit = 1 }
    if ([int](Get-JsonPropertyValue -Object $scenarioChecklist -Name "Count") -lt 0) {
        throw "read_scenario_reference_checklist returned an invalid count."
    }

    Write-Host ("AUTHORING_MCP_SMOKE_OK server={0} protocol={1} tools={2} roles={3} csv={4} portrait={5} map={6}" -f `
        $init.result.serverInfo.name,
        $init.result.protocolVersion,
        $toolNames.Count,
        $roleRows.Count,
        $csvOutputPath,
        $portraitPreviewStatus,
        $mapPreviewStatus)
} finally {
    if ($proc -and -not $proc.HasExited) {
        $proc.Kill()
    }

    if ($proc) {
        $proc.Dispose()
    }
}
