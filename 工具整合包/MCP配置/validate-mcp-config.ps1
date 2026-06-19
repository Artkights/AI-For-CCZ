param(
    [string]$GameRoot = "",
    [int]$TimeoutMs = 10000,
    [int]$MinimumToolCount = 65
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
        ($coreCount -ge 1 -and (Test-Path -LiteralPath (Join-Path $Path "RS") -PathType Container)) -or
        (Test-Path -LiteralPath (Join-Path $Path "_CCZModStudio_TestCopy.txt") -PathType Leaf)
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

    $init = $initLine | ConvertFrom-Json
    $tools = $toolsLine | ConvertFrom-Json
    $resources = $resourcesLine | ConvertFrom-Json
    $resourceTemplates = $resourceTemplatesLine | ConvertFrom-Json
    $prompts = $promptsLine | ConvertFrom-Json
    $effectSchema = $effectSchemaLine | ConvertFrom-Json
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

    $toolNames = @($tools.result.tools | ForEach-Object { $_.name })
    if ($toolNames.Count -lt $MinimumToolCount) {
        throw "Expected at least $MinimumToolCount tools, got $($toolNames.Count)."
    }

    $requiredTools = @(
        "detect_project",
        "list_tables",
        "read_table",
        "write_table_rows",
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
        "list_ccz_image_asset_presets",
        "build_ccz_image_prompt",
        "prepare_ccz_generated_image",
        "draw_ccz_image_asset",
        "list_e5_image_entries",
        "preview_e5_image_replace",
        "preview_e5_image_batch_replace",
        "replace_e5_image_entry",
        "replace_e5_image_batch",
        "preview_r_image_raw_replace",
        "replace_r_image_raw",
        "preview_s_image_raw_replace",
        "replace_s_image_raw",
        "preview_e5_role_raw_normalize",
        "normalize_e5_role_raw",
        "preview_dll_icon_replace",
        "replace_dll_icon",
        "preview_clear_dll_icon",
        "clear_dll_icon",
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
        "promote_test_copy_mod",
        "validate_mod_package",
        "export_mod_report",
        "compile_scenario_patch",
        "preview_scenario_patch",
        "apply_scenario_patch",
        "apply_scenario_patch_aggressive",
        "list_effect_templates",
        "build_effect_package_from_template",
        "preview_effect_patch",
        "apply_effect_patch",
        "read_effect_resource",
        "read_effect_prompt"
    )
    $missing = @($requiredTools | Where-Object { $toolNames -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Missing required tools: $($missing -join ', ')"
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
