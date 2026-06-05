param(
    [string]$GameRoot = "",
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$toolRoot = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $toolRoot "CCZModStudio.McpServer\CCZModStudio.McpServer.csproj"
$serverDll = Join-Path $toolRoot "CCZModStudio.McpServer\bin\$Configuration\net8.0-windows\CCZModStudio.McpServer.dll"

if (-not (Test-Path -LiteralPath $serverDll)) {
    $buildOutput = & dotnet build $serverProject -c $Configuration -v:minimal 2>&1
    $buildExitCode = $LASTEXITCODE
    foreach ($line in $buildOutput) {
        [Console]::Error.WriteLine($line)
    }

    if ($buildExitCode -ne 0) {
        throw "MCP server build failed with exit code $buildExitCode."
    }
}

if (-not (Test-Path -LiteralPath $serverDll)) {
    throw "MCP server dll was not found after build: $serverDll"
}

if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
    $resolvedGameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
    $env:CCZMODSTUDIO_GAME_ROOT = $resolvedGameRoot
} elseif ([string]::IsNullOrWhiteSpace($env:CCZMODSTUDIO_GAME_ROOT)) {
    $defaultGameRoot = Join-Path (Split-Path -Parent $toolRoot) "基底\加强版6.5未加密版"
    if (Test-Path -LiteralPath $defaultGameRoot) {
        $env:CCZMODSTUDIO_GAME_ROOT = (Resolve-Path -LiteralPath $defaultGameRoot).Path
    }
}

Set-Location -LiteralPath $toolRoot
& dotnet $serverDll
