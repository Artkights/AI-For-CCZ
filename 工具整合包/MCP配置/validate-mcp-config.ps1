param(
    [string]$GameRoot = "",
    [int]$TimeoutMs = 10000,
    [int]$MinimumToolCount = 0
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
    $initialize = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"ccz-mcp-validate","version":"1.0.0"}}}'
    $initialized = '{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}'
    $toolsList = '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
    $resourcesList = '{"jsonrpc":"2.0","id":3,"method":"resources/list","params":{}}'
    $resourceTemplatesList = '{"jsonrpc":"2.0","id":4,"method":"resources/templates/list","params":{}}'
    $promptsList = '{"jsonrpc":"2.0","id":5,"method":"prompts/list","params":{}}'
    $effectSchemaRead = '{"jsonrpc":"2.0","id":6,"method":"resources/read","params":{"uri":"ccz://effects/schema"}}'
    $manifestRead = '{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"read_mcp_capability_manifest","arguments":{}}}'

    $proc.StandardInput.WriteLine($initialize)
    $proc.StandardInput.Flush()
    $initLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "initialize"

    $proc.StandardInput.WriteLine($initialized)
    $proc.StandardInput.Flush()
    $proc.StandardInput.WriteLine($toolsList)
    $proc.StandardInput.Flush()
    $toolsLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/list"
    $proc.StandardInput.WriteLine($resourcesList)
    $proc.StandardInput.Flush()
    $resourcesLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "resources/list"
    $proc.StandardInput.WriteLine($resourceTemplatesList)
    $proc.StandardInput.Flush()
    $resourceTemplatesLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "resources/templates/list"
    $proc.StandardInput.WriteLine($promptsList)
    $proc.StandardInput.Flush()
    $promptsLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "prompts/list"
    $proc.StandardInput.WriteLine($effectSchemaRead)
    $proc.StandardInput.Flush()
    $effectSchemaLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "resources/read ccz://effects/schema"
    $proc.StandardInput.WriteLine($manifestRead)
    $proc.StandardInput.Flush()
    $manifestLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/call read_mcp_capability_manifest"

    $init = $initLine | ConvertFrom-Json
    $tools = $toolsLine | ConvertFrom-Json
    $resources = $resourcesLine | ConvertFrom-Json
    $resourceTemplates = $resourceTemplatesLine | ConvertFrom-Json
    $prompts = $promptsLine | ConvertFrom-Json
    $effectSchema = $effectSchemaLine | ConvertFrom-Json
    $manifestResponse = $manifestLine | ConvertFrom-Json
    if ($init.error) {
        throw "MCP initialize failed: $($init.error.message)"
    }

    if ($tools.error) {
        throw "MCP tools/list failed: $($tools.error.message)"
    }
    if ($resources.error) {
        throw "MCP resources/list failed: $($resources.error.message)"
    }
    if ($resourceTemplates.error) {
        throw "MCP resources/templates/list failed: $($resourceTemplates.error.message)"
    }
    if ($prompts.error) {
        throw "MCP prompts/list failed: $($prompts.error.message)"
    }
    if ($effectSchema.error) {
        throw "MCP resources/read ccz://effects/schema failed: $($effectSchema.error.message)"
    }
    if ($manifestResponse.error) {
        throw "MCP tools/call read_mcp_capability_manifest failed: $($manifestResponse.error.message)"
    }

    $toolNames = @($tools.result.tools | ForEach-Object { $_.name })
    if ($MinimumToolCount -gt 0 -and $toolNames.Count -lt $MinimumToolCount) {
        throw "Expected at least $MinimumToolCount tools, got $($toolNames.Count)."
    }

    $manifestText = ($manifestResponse.result.content | Where-Object { $_.type -eq "text" } | Select-Object -First 1).text
    if ([string]::IsNullOrWhiteSpace($manifestText)) {
        throw "Capability manifest did not return text content."
    }
    $manifest = $manifestText | ConvertFrom-Json
    if ([int]$manifest.ToolCount -ne $toolNames.Count) {
        throw "Capability manifest tool count mismatch: manifest=$($manifest.ToolCount), tools/list=$($toolNames.Count)."
    }
    $manifestTools = @($manifest.Tools)
    $manifestMissingTools = @($toolNames | Where-Object { $manifestTools -notcontains $_ })
    if ($manifestMissingTools.Count -gt 0) {
        throw "Capability manifest is missing tools/list entries: $($manifestMissingTools -join ', ')"
    }

    $requiredTools = @(
        "read_mcp_capability_manifest",
        "detect_project",
        "list_tables",
        "read_table",
        "write_table_rows",
        "read_table_schema",
        "read_table_derived_display",
        "export_table_csv",
        "preview_import_table_csv",
        "apply_import_table_csv",
        "list_scenario_files",
        "read_scenario_commands",
        "list_battlefield_unit_status_targets",
        "read_battlefield_unit_status",
        "write_battlefield_unit_status",
        "search_knowledge_entries",
        "list_hexzmap_blocks",
        "read_hexzmap_block",
        "preview_resource_replace",
        "list_image_resources",
        "list_image_resource_entries",
        "export_image_resource_preview",
        "export_bmp_assets",
        "list_ccz_image_asset_presets",
        "build_ccz_image_prompt",
        "prepare_ccz_generated_image",
        "draw_ccz_image_asset",
        "draw_and_replace_ccz_image_asset",
        "build_rs_pixel_character_design",
        "create_rs_pixel_edit_workspace",
        "build_rs_pixel_edit_plan",
        "apply_rs_pixel_frame_edits",
        "export_rs_pixel_contact_sheets",
        "validate_rs_pixel_edit_workspace",
        "list_e5_image_entries",
        "preview_e5_image_replace",
        "preview_e5_image_batch_replace",
        "replace_e5_image_entry",
        "replace_e5_image_batch",
        "preview_r_image_raw_replace",
        "replace_r_image_raw",
        "preview_s_image_raw_replace",
        "replace_s_image_raw",
        "preview_job_s_image_raw_batch_replace",
        "replace_job_s_image_raw_batch_replace",
        "replace_job_s_image_raw_batch",
        "preview_e5_role_raw_normalize",
        "normalize_e5_role_raw",
        "preview_item_icon_batch_import",
        "replace_item_icon_batch_import",
        "preview_strategy_icon_batch_import",
        "replace_strategy_icon_batch_import",
        "preview_role_face_batch_import",
        "replace_role_face_batch_import",
        "read_role_editor",
        "preview_write_roles",
        "write_roles",
        "read_role_texts",
        "preview_write_role_texts",
        "write_role_texts",
        "find_free_image_assignment_ids",
        "preview_image_assignment_update",
        "write_image_assignment_update",
        "preview_dll_icon_replace",
        "replace_dll_icon",
        "preview_clear_dll_icon",
        "clear_dll_icon",
        "read_editable_image_target",
        "preview_editable_image_write",
        "write_editable_image",
        "list_portrait_frames",
        "preview_apply_portrait_frame",
        "apply_portrait_frame",
        "list_effects",
        "read_effect",
        "export_effect_package",
        "preview_effect_package",
        "apply_effect_package",
        "analyze_mod_request",
        "compile_mod_package",
        "analyze_standalone_scenario_request",
        "compile_standalone_scenario_package",
        "list_available_slots",
        "preview_mod_package",
        "apply_mod_package",
        "auto_make_mod",
        "auto_validate_mod",
        "validate_mod_package",
        "export_mod_report",
        "compile_scenario_patch",
        "preview_scenario_patch",
        "apply_scenario_patch",
        "apply_scenario_patch_aggressive",
        "apply_scenario_text_import",
        "publish_rscene_draft_to_scenario",
        "list_effect_templates",
        "build_effect_package_from_template",
        "preview_effect_patch",
        "apply_effect_patch",
        "scan_installed_effects",
        "scan_effect_id_locations",
        "read_native_effect_definition",
        "preview_native_effect_update",
        "apply_native_effect_update",
        "create_extended_binding_probe_plan",
        "import_extended_binding_evidence",
        "preflight_composite_effect",
        "preview_modular_effect",
        "apply_modular_effect",
        "read_effect_capabilities",
        "list_map_drafts",
        "read_map_draft",
        "save_map_draft",
        "preview_map_canvas",
        "export_map_canvas_jpeg",
        "publish_map_canvas_to_map_image",
        "publish_map_workbench_bundle",
        "list_material_assets",
        "migrate_material_library_preview",
        "migrate_material_library",
        "analyze_material_driven_terrain",
        "derive_material_terrain_cells",
        "generate_terrain_driven_map",
        "beautify_terrain_map_preview",
        "preview_extract_map_materials",
        "extract_map_materials",
        "preview_terrain_beautify_filter",
        "apply_terrain_beautify_to_draft",
        "read_shop_editor",
        "preview_write_shop_rows",
        "write_shop_rows",
        "read_global_settings",
        "preview_write_global_settings",
        "write_global_settings",
        "parse_scenario_text_import",
        "read_scenario_text_import_template",
        "export_scenario_texts",
        "scan_script_variables",
        "read_script_variable_snapshot",
        "read_rscene_draft",
        "save_rscene_draft",
        "list_rscene_command_candidates",
        "list_battlefield_deployment_slots",
        "preview_battlefield_deployment_write",
        "write_battlefield_deployment",
        "inspect_eex_entries",
        "compare_eex_archives",
        "export_table_annotations",
        "diagnose_qinger66_project",
        "audit_qinger66_items",
        "list_legacy_mfc_dialogs",
        "read_legacy_mfc_dialog",
        "read_scenario_reference_checklist",
        "list_item_effect_catalog",
        "save_item_effect_catalog",
        "write_item_effect_name66_slot",
        "read_equipment_type_profile",
        "read_job_settings",
        "preview_job_settings",
        "write_job_settings",
        "read_accessory_job_groups",
        "preview_accessory_job_groups",
        "write_accessory_job_groups",
        "preview_attack_area",
        "preview_strategy_animation",
        "read_effect_resource",
        "read_effect_prompt"
    )
    $missing = @($requiredTools | Where-Object { $toolNames -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Missing required tools: $($missing -join ', ')"
    }

    $forbiddenTools = @(
        "promote_test_copy_mod",
        "create_test_copy",
        "diff_test_copy",
        "create_release_copy"
    )
    $presentForbiddenTools = @($forbiddenTools | Where-Object { $toolNames -contains $_ })
    if ($presentForbiddenTools.Count -gt 0) {
        throw "Obsolete production tools are still exposed: $($presentForbiddenTools -join ', ')"
    }

    $resourceUris = @($resources.result.resources | ForEach-Object { $_.uri })
    $resourceTemplateUris = @($resourceTemplates.result.resourceTemplates | ForEach-Object { $_.uriTemplate })
    $promptNames = @($prompts.result.prompts | ForEach-Object { $_.name })

    $requiredResources = @(
        "ccz://effects/schema",
        "ccz://effects/templates",
        "ccz://effects/manifests",
        "ccz://knowledge/effects"
    )
    $missingResources = @($requiredResources | Where-Object { $resourceUris -notcontains $_ })
    if ($missingResources.Count -gt 0) {
        throw "Missing required resources: $($missingResources -join ', ')"
    }

    if ($resourceTemplateUris -notcontains "ccz://effects/catalog/{domain}") {
        throw "Missing required resource template: ccz://effects/catalog/{domain}"
    }

    $requiredPrompts = @("make_ccz_effect", "import_ccz_effect", "delete_ccz_effect")
    $missingPrompts = @($requiredPrompts | Where-Object { $promptNames -notcontains $_ })
    if ($missingPrompts.Count -gt 0) {
        throw "Missing required prompts: $($missingPrompts -join ', ')"
    }

    $effectSchemaText = ($effectSchema.result.contents | ForEach-Object { $_.text }) -join "`n"
    if ($effectSchemaText -notlike "*EffectPackage*") {
        throw "Effect schema resource did not contain EffectPackage."
    }

    Write-Host ("MCP_VALIDATE_OK server={0} protocol={1} tools={2}" -f $init.result.serverInfo.name, $init.result.protocolVersion, $toolNames.Count)
    Write-Host ("MCP_MANIFEST_OK tools={0} groups={1} document={2}" -f $manifest.ToolCount, @($manifest.Groups.PSObject.Properties).Count, $manifest.DocumentVersion)
    Write-Host ("MCP_VALIDATE_SAMPLE " + (($toolNames | Select-Object -First 10) -join ","))
    Write-Host ("EFFECT_MCP_SMOKE_OK resources={0} templates={1} prompts={2}" -f $resourceUris.Count, $resourceTemplateUris.Count, $promptNames.Count)
} finally {
    if ($proc -and -not $proc.HasExited) {
        $proc.Kill()
    }

    if ($proc) {
        $proc.Dispose()
    }
}
