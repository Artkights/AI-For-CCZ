param(
    [string]$GameRoot = "",
    [string]$OutputDirectory = "",
    [string]$Configuration = "Debug",
    [switch]$IncludeGameDebug,
    [switch]$IncludeX64dbg,
    [string]$X64dbgHost = "127.0.0.1",
    [int]$X64dbgPort = 27042,
    [string]$X64dbgToken = "",
    [switch]$Build
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot
$startScript = Join-Path $configRoot "start-ccz-mcp.ps1"
$gameDebugStartScript = Join-Path $configRoot "start-ccz-game-debug-mcp.ps1"
$serverProject = Join-Path $toolRoot "CCZModStudio.McpServer\CCZModStudio.McpServer.csproj"
$serverDll = Join-Path $toolRoot "CCZModStudio.McpServer\bin\$Configuration\net8.0-windows\CCZModStudio.McpServer.dll"
$gameDebugProject = Join-Path $toolRoot "CCZModStudio.GameDebugMcpServer\CCZModStudio.GameDebugMcpServer.csproj"
$gameDebugDll = Join-Path $toolRoot "CCZModStudio.GameDebugMcpServer\bin\$Configuration\net8.0-windows\CCZModStudio.GameDebugMcpServer.dll"

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

function Get-GameRootScore {
    param(
        [string]$Path,
        [string]$WorkspaceRoot
    )

    $score = 0
    $name = Split-Path -Leaf $Path
    $parentName = Split-Path -Leaf (Split-Path -Parent $Path)
    $unencryptedNamePart = -join ([char[]](0x672A, 0x52A0, 0x5BC6))
    $enhancedNamePart = -join ([char[]](0x52A0, 0x5F3A, 0x7248))

    if ($name -like "*6.5*") { $score += 60 }
    if ($name -like "*6.4*") { $score -= 40 }
    if ($name.Contains($unencryptedNamePart)) { $score += 80 }
    if ($name.Contains($enhancedNamePart)) { $score += 20 }
    if (Test-Path -LiteralPath (Join-Path $Path "RS") -PathType Container) { $score += 20 }
    if (Test-Path -LiteralPath (Join-Path $Path "Map") -PathType Container) { $score += 10 }
    if (Test-Path -LiteralPath (Join-Path $Path "_CCZModStudio_TestCopy.txt") -PathType Leaf) { $score -= 100 }
    if ($parentName -eq "CCZModStudio_TestCopies") { $score -= 100 }

    $relativeLength = $Path.Length - $WorkspaceRoot.Length
    return [pscustomobject]@{
        Path = $Path
        Score = $score
        RelativeLength = $relativeLength
    }
}

function Find-DefaultGameRoot {
    param([string]$WorkspaceRoot)

    $candidatePaths = New-Object System.Collections.Generic.List[string]
    if (Test-GameRoot $WorkspaceRoot) {
        $candidatePaths.Add($WorkspaceRoot)
    }

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
                    $candidatePaths.Add($child.FullName)
                }

                $next += $child.FullName
            }
        }

        $frontier = $next
    }

    $candidatePaths |
        Sort-Object -Unique |
        ForEach-Object { Get-GameRootScore -Path $_ -WorkspaceRoot $WorkspaceRoot } |
        Sort-Object -Property @{ Expression = "Score"; Descending = $true }, @{ Expression = "RelativeLength"; Ascending = $true }, @{ Expression = "Path"; Ascending = $true } |
        Select-Object -First 1 -ExpandProperty Path
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $configRoot "_generated"
}

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $GameRoot = Find-DefaultGameRoot -WorkspaceRoot $workspaceRoot
}

if (-not (Test-Path -LiteralPath $startScript)) {
    throw "Start script not found: $startScript"
}

if ($Build -or -not (Test-Path -LiteralPath $serverDll)) {
    dotnet build $serverProject -c $Configuration -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "MCP server build failed with exit code $LASTEXITCODE."
    }
}

if ($IncludeGameDebug -and ($Build -or -not (Test-Path -LiteralPath $gameDebugDll))) {
    dotnet build $gameDebugProject -c $Configuration -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Game debug MCP server build failed with exit code $LASTEXITCODE."
    }
}

$resolvedToolRoot = (Resolve-Path -LiteralPath $toolRoot).Path
$resolvedStartScript = (Resolve-Path -LiteralPath $startScript).Path
$resolvedGameDebugStartScript = if (Test-Path -LiteralPath $gameDebugStartScript) { (Resolve-Path -LiteralPath $gameDebugStartScript).Path } else { $gameDebugStartScript }
$resolvedServerDll = if (Test-Path -LiteralPath $serverDll) { (Resolve-Path -LiteralPath $serverDll).Path } else { $serverDll }
$resolvedGameDebugDll = if (Test-Path -LiteralPath $gameDebugDll) { (Resolve-Path -LiteralPath $gameDebugDll).Path } else { $gameDebugDll }
$resolvedGameRoot = if (-not [string]::IsNullOrWhiteSpace($GameRoot) -and (Test-Path -LiteralPath $GameRoot)) {
    (Resolve-Path -LiteralPath $GameRoot).Path
} else {
    $GameRoot
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function ConvertTo-JsonFile {
    param(
        [Parameter(Mandatory = $true)] [object]$Value,
        [Parameter(Mandatory = $true)] [string]$Path
    )

    $json = $Value | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
}

function To-TomlLiteral {
    param([string]$Value)

    if ($null -eq $Value) { return "''" }
    return "'" + ($Value -replace "'", "''") + "'"
}

$commonCommand = "powershell.exe"
$commonArgs = @(
    "-NoLogo",
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    $resolvedStartScript
)

if (-not [string]::IsNullOrWhiteSpace($resolvedGameRoot)) {
    $commonArgs += @("-GameRoot", $resolvedGameRoot)
}

$envMap = [ordered]@{
    CCZMODSTUDIO_TOOL_ROOT = $resolvedToolRoot
}

if (-not [string]::IsNullOrWhiteSpace($resolvedGameRoot)) {
    $envMap["CCZMODSTUDIO_GAME_ROOT"] = $resolvedGameRoot
}

$serverMap = [ordered]@{
    cczmodstudio = [ordered]@{
        command = $commonCommand
        args = $commonArgs
        env = $envMap
    }
}

if ($IncludeGameDebug) {
    if (-not (Test-Path -LiteralPath $gameDebugStartScript -PathType Leaf)) {
        throw "Game debug start script not found: $gameDebugStartScript"
    }

    $gameDebugArgs = @(
        "-NoLogo",
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $resolvedGameDebugStartScript
    )
    if (-not [string]::IsNullOrWhiteSpace($resolvedGameRoot)) {
        $gameDebugArgs += @("-GameRoot", $resolvedGameRoot)
    }

    $gameDebugEnv = [ordered]@{
        CCZMODSTUDIO_TOOL_ROOT = $resolvedToolRoot
        X64DBG_MCP_HOST = $X64dbgHost
        X64DBG_MCP_PORT = $X64dbgPort.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }
    if (-not [string]::IsNullOrWhiteSpace($resolvedGameRoot)) {
        $gameDebugEnv["CCZGAME_DEBUG_GAME_ROOT"] = $resolvedGameRoot
        $gameDebugEnv["CCZMODSTUDIO_GAME_ROOT"] = $resolvedGameRoot
    }
    if (-not [string]::IsNullOrWhiteSpace($X64dbgToken)) {
        $gameDebugEnv["X64DBG_MCP_TOKEN"] = $X64dbgToken
    }

    $serverMap["cczgame_debug"] = [ordered]@{
        command = "powershell.exe"
        args = $gameDebugArgs
        env = $gameDebugEnv
    }
}

if ($IncludeX64dbg) {
    $x64dbgEnv = [ordered]@{
        X64DBG_MCP_HOST = $X64dbgHost
        X64DBG_MCP_PORT = $X64dbgPort.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }

    if (-not [string]::IsNullOrWhiteSpace($X64dbgToken)) {
        $x64dbgEnv["X64DBG_MCP_TOKEN"] = $X64dbgToken
    }

    $serverMap["x64dbg"] = [ordered]@{
        command = "cmd"
        args = @("/c", "npx", "-y", "x64dbg-mcp-server")
        env = $x64dbgEnv
    }
}

$mcpServers = [ordered]@{
    mcpServers = $serverMap
}

$genericJsonPath = Join-Path $OutputDirectory "mcp-servers.json"
$claudeJsonPath = Join-Path $OutputDirectory "claude_desktop_config.json"
$codexTomlPath = Join-Path $OutputDirectory "codex-config-snippet.toml"
$directJsonPath = Join-Path $OutputDirectory "mcp-servers-direct-dotnet.json"

ConvertTo-JsonFile -Value $mcpServers -Path $genericJsonPath
ConvertTo-JsonFile -Value $mcpServers -Path $claudeJsonPath

$toml = @()
$toml += "[mcp_servers.cczmodstudio]"
$toml += "command = " + (To-TomlLiteral $commonCommand)
$toml += "args = ["
for ($i = 0; $i -lt $commonArgs.Count; $i++) {
    $suffix = if ($i -lt $commonArgs.Count - 1) { "," } else { "" }
    $toml += "  " + (To-TomlLiteral $commonArgs[$i]) + $suffix
}
$toml += "]"
$toml += "startup_timeout_sec = 120"
$toml += ""
$toml += "[mcp_servers.cczmodstudio.env]"
foreach ($key in $envMap.Keys) {
    $toml += "$key = " + (To-TomlLiteral $envMap[$key])
}
if ($IncludeX64dbg) {
    $toml += ""
    $toml += "[mcp_servers.x64dbg]"
    $toml += "command = 'cmd'"
    $toml += "args = ['/c', 'npx', '-y', 'x64dbg-mcp-server']"
    $toml += "startup_timeout_sec = 120"
    $toml += ""
    $toml += "[mcp_servers.x64dbg.env]"
    $toml += "X64DBG_MCP_HOST = " + (To-TomlLiteral $X64dbgHost)
    $toml += "X64DBG_MCP_PORT = " + (To-TomlLiteral $X64dbgPort.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    if (-not [string]::IsNullOrWhiteSpace($X64dbgToken)) {
        $toml += "X64DBG_MCP_TOKEN = " + (To-TomlLiteral $X64dbgToken)
    }
}
if ($IncludeGameDebug) {
    $toml += ""
    $toml += "[mcp_servers.cczgame_debug]"
    $toml += "command = 'powershell.exe'"
    $toml += "args = ["
    $gameDebugTomlArgs = $serverMap["cczgame_debug"].args
    for ($i = 0; $i -lt $gameDebugTomlArgs.Count; $i++) {
        $suffix = if ($i -lt $gameDebugTomlArgs.Count - 1) { "," } else { "" }
        $toml += "  " + (To-TomlLiteral $gameDebugTomlArgs[$i]) + $suffix
    }
    $toml += "]"
    $toml += "startup_timeout_sec = 120"
    $toml += ""
    $toml += "[mcp_servers.cczgame_debug.env]"
    foreach ($key in $serverMap["cczgame_debug"].env.Keys) {
        $toml += "$key = " + (To-TomlLiteral $serverMap["cczgame_debug"].env[$key])
    }
}
Set-Content -LiteralPath $codexTomlPath -Value ($toml -join [Environment]::NewLine) -Encoding UTF8

$direct = [ordered]@{
    mcpServers = [ordered]@{
        cczmodstudio = [ordered]@{
            command = "dotnet"
            args = @($resolvedServerDll)
            env = $envMap
        }
    }
}
if ($IncludeX64dbg) {
    $direct.mcpServers["x64dbg"] = $serverMap["x64dbg"]
}
if ($IncludeGameDebug) {
    $direct.mcpServers["cczgame_debug"] = [ordered]@{
        command = "dotnet"
        args = @($resolvedGameDebugDll)
        env = $serverMap["cczgame_debug"].env
    }
}
ConvertTo-JsonFile -Value $direct -Path $directJsonPath

$summaryPath = Join-Path $OutputDirectory "README.generated.md"
$summary = @"
# Generated CCZModStudio MCP Config

- Tool root: $resolvedToolRoot
- Server dll: $resolvedServerDll
- Game debug dll: $resolvedGameDebugDll
- Game root: $resolvedGameRoot
- Start script: $resolvedStartScript
- Game debug start script: $resolvedGameDebugStartScript

Generated files:

- codex-config-snippet.toml: paste into Codex config.toml or project .codex/config.toml.
- mcp-servers.json: generic mcpServers JSON for clients that accept stdio MCP JSON.
- claude_desktop_config.json: Claude Desktop-compatible JSON shape.
- mcp-servers-direct-dotnet.json: direct dotnet <dll> fallback when the client supports cwd/env reliably.

Restart the MCP client after applying the config.

Game debug MCP included: $IncludeGameDebug
X64dbg MCP included: $IncludeX64dbg
X64dbg endpoint: ${X64dbgHost}:$X64dbgPort
"@
Set-Content -LiteralPath $summaryPath -Value $summary -Encoding UTF8

Write-Host "Generated MCP config files under: $OutputDirectory"
Write-Host "Codex snippet: $codexTomlPath"
Write-Host "Generic JSON: $genericJsonPath"
