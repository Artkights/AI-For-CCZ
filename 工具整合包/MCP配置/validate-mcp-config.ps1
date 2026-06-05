param(
    [string]$GameRoot = "",
    [int]$TimeoutMs = 10000,
    [int]$MinimumToolCount = 32
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
        [Parameter(Mandatory = $true)] [int]$Timeout
    )

    $task = $Reader.ReadLineAsync()
    if (-not $task.Wait($Timeout)) {
        throw "Timed out waiting for MCP response."
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

    $proc.StandardInput.WriteLine($initialize)
    $proc.StandardInput.Flush()
    $initLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs

    $proc.StandardInput.WriteLine($initialized)
    $proc.StandardInput.Flush()
    $proc.StandardInput.WriteLine($toolsList)
    $proc.StandardInput.Flush()
    $toolsLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs

    $init = $initLine | ConvertFrom-Json
    $tools = $toolsLine | ConvertFrom-Json
    if ($init.error) {
        throw "MCP initialize failed: $($init.error.message)"
    }

    if ($tools.error) {
        throw "MCP tools/list failed: $($tools.error.message)"
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
        "search_knowledge_entries",
        "list_hexzmap_blocks",
        "read_hexzmap_block",
        "list_project_resources",
        "run_resource_diagnostics",
        "list_e5_image_entries",
        "replace_e5_image_entry",
        "list_creator_notes",
        "upsert_creator_note"
    )
    $missing = @($requiredTools | Where-Object { $toolNames -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Missing required tools: $($missing -join ', ')"
    }

    Write-Host ("MCP_VALIDATE_OK server={0} protocol={1} tools={2}" -f $init.result.serverInfo.name, $init.result.protocolVersion, $toolNames.Count)
    Write-Host ("MCP_VALIDATE_SAMPLE " + (($toolNames | Select-Object -First 10) -join ","))
} finally {
    if ($proc -and -not $proc.HasExited) {
        $proc.Kill()
    }

    if ($proc) {
        $proc.Dispose()
    }
}
