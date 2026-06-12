param(
    [string]$GameRoot = "",
    [string]$Configuration = "Debug",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$project = Join-Path $toolRoot "CCZModStudio.GameDebugMcpServer\CCZModStudio.GameDebugMcpServer.csproj"
$serverDll = Join-Path $toolRoot "CCZModStudio.GameDebugMcpServer\bin\$Configuration\net8.0-windows\CCZModStudio.GameDebugMcpServer.dll"

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Game debug MCP project not found: $project"
}

if (-not $NoBuild) {
    $buildOutput = & dotnet build $project -c $Configuration -v:minimal 2>&1
    $buildExitCode = $LASTEXITCODE
    foreach ($line in $buildOutput) {
        [Console]::Error.WriteLine($line)
    }

    if ($buildExitCode -ne 0) {
        throw "Game debug MCP server build failed with exit code $buildExitCode."
    }
}

if (-not (Test-Path -LiteralPath $serverDll -PathType Leaf)) {
    throw "Game debug MCP server dll was not found after build: $serverDll"
}

$env:CCZMODSTUDIO_TOOL_ROOT = (Resolve-Path -LiteralPath $toolRoot).Path
if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
    $env:CCZGAME_DEBUG_GAME_ROOT = (Resolve-Path -LiteralPath $GameRoot).Path
    $env:CCZMODSTUDIO_GAME_ROOT = $env:CCZGAME_DEBUG_GAME_ROOT
}

Set-Location $toolRoot
dotnet $serverDll
