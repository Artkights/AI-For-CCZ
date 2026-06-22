param(
    [string]$GameRoot = "",
    [int]$TimeoutMs = 30000,
    [int]$MinimumToolCount = 65
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot
$startScript = Join-Path $configRoot "start-ccz-game-debug-mcp.ps1"

function Find-DefaultGameRoot {
    param([string]$WorkspaceRoot)

    $candidate = Join-Path $WorkspaceRoot "基底\加强版6.5未加密版"
    if (Test-Path -LiteralPath $candidate -PathType Container) {
        return $candidate
    }

    $exe = Get-ChildItem -LiteralPath $WorkspaceRoot -Recurse -File -Filter "Ekd5.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\|\\.git\\|\\CCZModStudio_Exports\\|\\CCZModStudio_Reports\\' } |
        Sort-Object FullName |
        Select-Object -First 1
    if ($exe) {
        return $exe.DirectoryName
    }

    return ""
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

function Get-WorkspaceGameDebugDotnetProcessIds {
    param([string]$ToolRoot)

    $escapedRoot = [regex]::Escape($ToolRoot)
    @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine -match $escapedRoot -and
            $_.CommandLine -match 'CCZModStudio\.GameDebugMcpServer\.dll'
        } |
        ForEach-Object { [int]$_.ProcessId })
}

if (-not (Test-Path -LiteralPath $startScript -PathType Leaf)) {
    throw "Game debug MCP start script not found: $startScript"
}

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $GameRoot = Find-DefaultGameRoot -WorkspaceRoot $workspaceRoot
}

$resolvedStartScript = (Resolve-Path -LiteralPath $startScript).Path
$resolvedGameRoot = ""
if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
    if (-not (Test-Path -LiteralPath $GameRoot -PathType Container)) {
        throw "Game root was not found: $GameRoot"
    }

    $resolvedGameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
}

$hashText = ""
if (-not [string]::IsNullOrWhiteSpace($resolvedGameRoot)) {
    $exe = Join-Path $resolvedGameRoot "Ekd5.exe"
    if (Test-Path -LiteralPath $exe -PathType Leaf) {
        $hashText = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    }
}

$argsList = @(
    "-NoLogo",
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    "`"$resolvedStartScript`"",
    "-NoBuild"
)

if (-not [string]::IsNullOrWhiteSpace($resolvedGameRoot)) {
    $argsList += @("-GameRoot", "`"$resolvedGameRoot`"")
}

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "powershell.exe"
$psi.Arguments = $argsList -join " "
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

$existingGameDebugDotnetPids = @(Get-WorkspaceGameDebugDotnetProcessIds -ToolRoot $toolRoot)
$proc = [System.Diagnostics.Process]::Start($psi)
try {
    $initialize = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"ccz-game-debug-tools-validate","version":"1.0.0"}}}'
    $initialized = '{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}'
    $toolsList = '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'

    $proc.StandardInput.WriteLine($initialize)
    $proc.StandardInput.Flush()
    $initLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "initialize"

    $proc.StandardInput.WriteLine($initialized)
    $proc.StandardInput.Flush()
    $proc.StandardInput.WriteLine($toolsList)
    $proc.StandardInput.Flush()
    $toolsLine = Read-LineWithTimeout -Reader $proc.StandardOutput -Timeout $TimeoutMs -Label "tools/list"

    $init = $initLine | ConvertFrom-Json
    $tools = $toolsLine | ConvertFrom-Json
    if ($init.error) {
        throw "GameDebug MCP initialize failed: $($init.error.message)"
    }

    if ($tools.error) {
        throw "GameDebug MCP tools/list failed: $($tools.error.message)"
    }

    $toolNames = @($tools.result.tools | ForEach-Object { $_.name })
    if ($toolNames.Count -lt $MinimumToolCount) {
        throw "Expected at least $MinimumToolCount GameDebug tools, got $($toolNames.Count)."
    }

    $requiredTools = @(
        "debug_session_state",
        "game_process_start",
        "debug_function_catalog",
        "debug_static_xref_scan",
        "debug_address_report",
        "debug_runtime_invoke_plan",
        "game_runtime_state_classify",
        "game_key_sequence",
        "debug_full_auto_run"
    )
    $missing = @($requiredTools | Where-Object { $toolNames -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Missing required GameDebug tools: $($missing -join ', ')"
    }

    Write-Host ("GAME_DEBUG_MCP_TOOLS_OK server={0} protocol={1} tools={2}" -f $init.result.serverInfo.name, $init.result.protocolVersion, $toolNames.Count)
    if (-not [string]::IsNullOrWhiteSpace($resolvedGameRoot)) {
        Write-Host ("GAME_DEBUG_MCP_GAME_ROOT {0}" -f $resolvedGameRoot)
    }
    if (-not [string]::IsNullOrWhiteSpace($hashText)) {
        Write-Host ("GAME_DEBUG_MCP_EKD5_SHA256 {0}" -f $hashText)
    }
} finally {
    if ($proc -and -not $proc.HasExited) {
        $proc.Kill()
    }

    if ($proc) {
        $proc.Dispose()
    }

    $remainingGameDebugDotnetPids = @(Get-WorkspaceGameDebugDotnetProcessIds -ToolRoot $toolRoot)
    $newGameDebugDotnetPids = @($remainingGameDebugDotnetPids | Where-Object { $existingGameDebugDotnetPids -notcontains $_ })
    foreach ($pidValue in $newGameDebugDotnetPids) {
        Stop-Process -Id $pidValue -Force -ErrorAction SilentlyContinue
    }
}
