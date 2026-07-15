param(
    [string]$GameRoot = "",
    [int]$TimeoutMs = 60000
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot
$startScript = Join-Path $configRoot "start-ccz-mcp.ps1"

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

    return $coreCount -ge 3 -and (Test-Path -LiteralPath (Join-Path $Path "RS") -PathType Container)
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

function New-McpRequest {
    param(
        [Parameter(Mandatory = $true)] [int]$Id,
        [Parameter(Mandatory = $true)] [string]$Method,
        [hashtable]$Params = @{}
    )

    return @{
        jsonrpc = "2.0"
        id = $Id
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 80 -Compress
}

function Invoke-McpTool {
    param(
        [Parameter(Mandatory = $true)] [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)] [string]$Name,
        [hashtable]$Arguments = @{},
        [switch]$ExpectError
    )

    $script:nextId++
    $payload = New-McpRequest -Id $script:nextId -Method "tools/call" -Params @{
        name = $Name
        arguments = $Arguments
    }
    $Process.StandardInput.WriteLine($payload)
    $Process.StandardInput.Flush()
    $line = Read-LineWithTimeout -Reader $Process.StandardOutput -Timeout $TimeoutMs -Label "tools/call $Name"
    $response = $line | ConvertFrom-Json

    if ($response.error) {
        if ($ExpectError) {
            return @{
                Error = $response.error
                Text = $response.error.message
            }
        }

        throw "tools/call $Name failed: $($response.error.message)"
    }

    $text = ""
    if ($response.result.content -and $response.result.content.Count -gt 0) {
        $text = [string]$response.result.content[0].text
    }

    $isToolError = $false
    if ($response.result.PSObject.Properties.Name -contains "isError") {
        $isToolError = [bool]$response.result.isError
    }

    if ($isToolError) {
        if ($ExpectError) {
            return @{
                Error = $response.result
                Text = $text
            }
        }

        throw "tools/call $Name returned tool error: $text"
    }

    if ($ExpectError) {
        throw "tools/call $Name was expected to fail but succeeded."
    }

    if ([string]::IsNullOrWhiteSpace($text)) {
        return @{}
    }

    return $text | ConvertFrom-Json
}

function Assert-Truthy {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
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

$resolvedStartScript = (Resolve-Path -LiteralPath $startScript).Path
$resolvedGameRoot = (Resolve-Path -LiteralPath $GameRoot).Path

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
$script:nextId = 1
try {
    $initialize = New-McpRequest -Id 1 -Method "initialize" -Params @{
        protocolVersion = "2025-06-18"
        capabilities = @{}
        clientInfo = @{
            name = "ccz-production-tools-smoke"
            version = "1.0.0"
        }
    }
    $proc.StandardInput.WriteLine($initialize)
    $proc.StandardInput.Flush()
    $initLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "initialize"
    $init = $initLine | ConvertFrom-Json
    if ($init.error) {
        throw "MCP initialize failed: $($init.error.message)"
    }

    $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}')
    $proc.StandardInput.Flush()

    $script:nextId++
    $toolsPayload = New-McpRequest -Id $script:nextId -Method "tools/list"
    $proc.StandardInput.WriteLine($toolsPayload)
    $proc.StandardInput.Flush()
    $toolsLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/list"
    $tools = $toolsLine | ConvertFrom-Json
    if ($tools.error) {
        throw "MCP tools/list failed: $($tools.error.message)"
    }

    $toolNames = @($tools.result.tools | ForEach-Object { $_.name })
    $requiredDirectTools = @(
        "apply_mod_package",
        "auto_make_mod",
        "apply_scenario_patch_aggressive",
        "apply_scenario_text_import",
        "publish_rscene_draft_to_scenario",
        "publish_map_workbench_bundle",
        "draw_and_replace_ccz_image_asset"
    )
    $requiredEffectTools = @(
        "scan_installed_effects",
        "scan_effect_id_locations",
        "read_native_effect_definition",
        "preview_native_effect_update",
        "create_extended_binding_probe_plan",
        "import_effect_evidence_bundle",
        "read_hook_execution_contract",
        "preflight_composite_effect",
        "apply_composite_effect",
        "read_modular_effect",
        "preview_update_modular_effect",
        "apply_update_modular_effect",
        "preview_toggle_modular_effect",
        "apply_toggle_modular_effect",
        "preview_repair_modular_effect",
        "apply_repair_modular_effect",
        "preview_remove_modular_effect",
        "apply_remove_modular_effect",
        "read_effect_capabilities"
    )
    $requiredDirectTools += $requiredEffectTools
    $missingDirectTools = @($requiredDirectTools | Where-Object { $toolNames -notcontains $_ })
    if ($missingDirectTools.Count -gt 0) {
        throw "Missing direct-production tools: $($missingDirectTools -join ', ')"
    }

    $obsoleteTools = @("promote_test_copy_mod", "create_test_copy", "diff_test_copy", "create_release_copy")
    $presentObsoleteTools = @($obsoleteTools | Where-Object { $toolNames -contains $_ })
    if ($presentObsoleteTools.Count -gt 0) {
        throw "Obsolete test-copy tools are still exposed: $($presentObsoleteTools -join ', ')"
    }

    $draftId = "mcp_smoke_" + (Get-Date -Format "yyyyMMdd_HHmmss")
    $mapDraft = Invoke-McpTool -Process $proc -Name "save_map_draft" -Arguments @{
        request = @{
            draft_id = $draftId
            grid_width = 4
            grid_height = 4
            material_root = ""
            auto_generate_map_from_terrain = $false
            beautify_generated_map = $false
        }
    }
    Assert-Truthy ($mapDraft.draft.draftId -eq $draftId) "save_map_draft did not return the expected draft id."

    $drafts = Invoke-McpTool -Process $proc -Name "list_map_drafts" -Arguments @{ limit = 5 }
    Assert-Truthy ($drafts.count -ge 1) "list_map_drafts returned no drafts after saving a smoke draft."

    $readDraft = Invoke-McpTool -Process $proc -Name "read_map_draft" -Arguments @{ draft_id = $draftId }
    Assert-Truthy ($readDraft.draft.draftId -eq $draftId) "read_map_draft did not return the smoke draft."

    $previewMap = Invoke-McpTool -Process $proc -Name "preview_map_canvas" -Arguments @{ draft_id = $draftId; show_grid = $true }
    Assert-Truthy ((Test-Path -LiteralPath ([string]$previewMap.outputPath) -PathType Leaf)) "preview_map_canvas did not create a preview image."

    $jpegMap = Invoke-McpTool -Process $proc -Name "export_map_canvas_jpeg" -Arguments @{ draft_id = $draftId }
    Assert-Truthy ((Test-Path -LiteralPath ([string]$jpegMap.outputPath) -PathType Leaf)) "export_map_canvas_jpeg did not create a JPEG."

    $materials = Invoke-McpTool -Process $proc -Name "list_material_assets" -Arguments @{ limit = 5 }
    Assert-Truthy ($materials.totalAssets -ge 1) "list_material_assets returned no material assets."
    $materialRoot = [string]$materials.materialRoot

    $migrationPreview = Invoke-McpTool -Process $proc -Name "migrate_material_library_preview" -Arguments @{ old_root = $materialRoot }
    Assert-Truthy ($migrationPreview.sourceAssetCount -ge 1) "migrate_material_library_preview returned no source assets."

    $terrainAnalysis = Invoke-McpTool -Process $proc -Name "analyze_material_driven_terrain" -Arguments @{ draft_id = $draftId }
    Assert-Truthy ($null -ne $terrainAnalysis.diagnostics) "analyze_material_driven_terrain did not return diagnostics."

    $terrainCells = Invoke-McpTool -Process $proc -Name "derive_material_terrain_cells" -Arguments @{ draft_id = $draftId; save_draft = $false }
    Assert-Truthy ($terrainCells.cells.Count -eq 16) "derive_material_terrain_cells did not return the expected 4x4 cells."

    $generatedMap = Invoke-McpTool -Process $proc -Name "generate_terrain_driven_map" -Arguments @{ draft_id = $draftId; save_draft = $false }
    Assert-Truthy ((Test-Path -LiteralPath ([string]$generatedMap.previewPath) -PathType Leaf)) "generate_terrain_driven_map did not create a preview."

    $beautifiedMap = Invoke-McpTool -Process $proc -Name "beautify_terrain_map_preview" -Arguments @{ draft_id = $draftId }
    Assert-Truthy ((Test-Path -LiteralPath ([string]$beautifiedMap.outputPath) -PathType Leaf)) "beautify_terrain_map_preview did not create a preview."

    $shop = Invoke-McpTool -Process $proc -Name "read_shop_editor" -Arguments @{ limit = 3 }
    Assert-Truthy ($shop.returnedRows -ge 1) "read_shop_editor returned no rows."

    $firstShopRow = $shop.rows[0]
    $shopPreview = Invoke-McpTool -Process $proc -Name "preview_write_shop_rows" -Arguments @{
        updates = @(@{
            row_id = [int]$firstShopRow.ID
            campaign_name = "MCP Smoke"
            shop_values = @{}
        })
    }
    Assert-Truthy ($shopPreview.preview.changeCount -ge 1) "preview_write_shop_rows did not report a change preview."

    $settings = Invoke-McpTool -Process $proc -Name "read_global_settings" -Arguments @{ limit = 2 }
    Assert-Truthy ($settings.gameTitle -ne $null) "read_global_settings did not return game title evidence."

    $settingsPreview = Invoke-McpTool -Process $proc -Name "preview_write_global_settings" -Arguments @{ update = @{ game_title = [string]$settings.gameTitle.title } }
    Assert-Truthy ($settingsPreview.changes.changeCount -ge 1) "preview_write_global_settings did not report a title preview."

    $importTemplate = Invoke-McpTool -Process $proc -Name "read_scenario_text_import_template"
    Assert-Truthy ([string]$importTemplate.template -like "*@narration*") "read_scenario_text_import_template did not return the template text."

    $importParse = Invoke-McpTool -Process $proc -Name "parse_scenario_text_import" -Arguments @{ input = "@narration`nSmoke text"; scenario_kind = "R" }
    Assert-Truthy ($importParse.commandCount -ge 1) "parse_scenario_text_import did not parse the narration smoke sample."

    $scenarioExport = Invoke-McpTool -Process $proc -Name "export_scenario_texts" -Arguments @{ relative_path = "RS/R_00.eex"; format = "csv" }
    Assert-Truthy ((Test-Path -LiteralPath ([string]$scenarioExport.outputPath) -PathType Leaf)) "export_scenario_texts did not create an export file."

    $variableScan = Invoke-McpTool -Process $proc -Name "scan_script_variables" -Arguments @{ relative_path = "RS/R_00.eex"; limit = 5 }
    Assert-Truthy ($variableScan.totalSummaries -ge 0) "scan_script_variables did not return a summary payload."

    $variableSnapshot = Invoke-McpTool -Process $proc -Name "read_script_variable_snapshot" -Arguments @{ relative_path = "RS/R_00.eex"; addresses = @(0, 1) }
    Assert-Truthy ($variableSnapshot.addresses.Count -eq 2) "read_script_variable_snapshot did not return requested addresses."

    $rSceneDraft = Invoke-McpTool -Process $proc -Name "save_rscene_draft" -Arguments @{
        request = @{
            scenario_file_name = "R_00.eex"
            background_image_number = 0
            grid_size = 16
            actors = @()
        }
    }
    Assert-Truthy ($rSceneDraft.draft -ne $null) "save_rscene_draft did not return a draft."

    $readRSceneDraft = Invoke-McpTool -Process $proc -Name "read_rscene_draft" -Arguments @{ scenario_file_name = "R_00.eex" }
    Assert-Truthy ($readRSceneDraft.draft -ne $null) "read_rscene_draft did not return the saved draft."

    $rSceneCandidates = Invoke-McpTool -Process $proc -Name "list_rscene_command_candidates" -Arguments @{ relative_path = "RS/R_00.eex"; limit = 5 }
    Assert-Truthy ($rSceneCandidates.candidateCount -ge 0) "list_rscene_command_candidates did not return a candidate payload."

    $deploymentSlots = Invoke-McpTool -Process $proc -Name "list_battlefield_deployment_slots" -Arguments @{ relative_path = "RS/S_00.eex"; limit = 5 }
    Assert-Truthy ($deploymentSlots.slotCount -ge 0) "list_battlefield_deployment_slots did not return a slot payload."

    $deploymentPreview = Invoke-McpTool -Process $proc -Name "preview_battlefield_deployment_write" -Arguments @{
        relative_path = "RS/S_00.eex"
        updates = @(@{
            target_key = "mcp-smoke-non-writable"
            person_id = 0
            name = "MCP Smoke"
            grid_x = 0
            grid_y = 0
            ai_mode = ""
            direction = ""
            hidden = $false
            faction = ""
            source = "MCP"
            placement_note = "preview only"
        })
    }
    Assert-Truthy ($deploymentPreview.requestedPlacementCount -eq 1) "preview_battlefield_deployment_write did not report the requested placement."

    $eex = Invoke-McpTool -Process $proc -Name "inspect_eex_entries" -Arguments @{ relative_path = "RS/R_00.eex"; limit = 5 }
    Assert-Truthy (($eex.PSObject.Properties.Name -contains "rows")) "inspect_eex_entries did not return an entry payload."

    $eexCompare = Invoke-McpTool -Process $proc -Name "compare_eex_archives" -Arguments @{ relative_path = "RS/R_00.eex"; max_peers = 2 }
    Assert-Truthy ($eexCompare.result -ne $null) "compare_eex_archives did not return comparisons."

    $tables = Invoke-McpTool -Process $proc -Name "list_tables"
    $firstTable = $tables.tables | Select-Object -First 1
    Assert-Truthy ($firstTable -ne $null) "list_tables returned no table definitions for annotation export."
    $annotations = Invoke-McpTool -Process $proc -Name "export_table_annotations" -Arguments @{ table_name = [string]$firstTable.tableName }
    Assert-Truthy ((Test-Path -LiteralPath ([string]$annotations.outputPath) -PathType Leaf)) "export_table_annotations did not create an export file."

    $catalog = Invoke-McpTool -Process $proc -Name "list_item_effect_catalog"
    Assert-Truthy ($catalog.count -ge 0) "list_item_effect_catalog did not return a catalog payload."

    $equipmentProfile = Invoke-McpTool -Process $proc -Name "read_equipment_type_profile"
    Assert-Truthy ($equipmentProfile.profile -ne $null) "read_equipment_type_profile did not return a profile."

    $attackPreview = Invoke-McpTool -Process $proc -Name "preview_attack_area" -Arguments @{ column_name = "AttackRange"; field_value = 0; canvas_size = 192 }
    Assert-Truthy ($attackPreview.message -ne $null) "preview_attack_area did not return a preview message."

    $strategyPreview = Invoke-McpTool -Process $proc -Name "preview_strategy_animation" -Arguments @{ animation_value = 0; kind = "small_meff"; canvas_size = 160 }
    Assert-Truthy ($strategyPreview.frameCount -ge 0) "preview_strategy_animation did not return frame metadata."

    Write-Host ("MCP_PRODUCTION_TOOLS_SMOKE_OK server={0} protocol={1} draft={2} materials={3}" -f $init.result.serverInfo.name, $init.result.protocolVersion, $draftId, $materials.totalAssets)
} finally {
    if ($proc -and -not $proc.HasExited) {
        $proc.Kill()
    }

    if ($proc) {
        $proc.Dispose()
    }
}
