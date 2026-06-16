param(
    [string]$GameRoot = "",
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$toolRoot = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $toolRoot "CCZModStudio.McpServer\CCZModStudio.McpServer.csproj"
$serverOutput = Join-Path $toolRoot "CCZModStudio.McpServer\bin\$Configuration\net8.0-windows"
$serverDll = Join-Path $serverOutput "CCZModStudio.McpServer.dll"
$serverRuntimeConfig = Join-Path $serverOutput "CCZModStudio.McpServer.runtimeconfig.json"
$serverDeps = Join-Path $serverOutput "CCZModStudio.McpServer.deps.json"

function Test-ServerOutput {
    param([string]$OutputDirectory)

    return (Test-Path -LiteralPath (Join-Path $OutputDirectory "CCZModStudio.McpServer.dll") -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $OutputDirectory "CCZModStudio.McpServer.runtimeconfig.json") -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $OutputDirectory "CCZModStudio.McpServer.deps.json") -PathType Leaf)
}

function Set-ServerOutput {
    param([string]$OutputDirectory)

    $script:serverOutput = $OutputDirectory
    $script:serverDll = Join-Path $OutputDirectory "CCZModStudio.McpServer.dll"
    $script:serverRuntimeConfig = Join-Path $OutputDirectory "CCZModStudio.McpServer.runtimeconfig.json"
    $script:serverDeps = Join-Path $OutputDirectory "CCZModStudio.McpServer.deps.json"
}

function Invoke-ServerBuild {
    param(
        [string]$OutputDirectory,
        [string]$IntermediateDirectory
    )

    New-Item -ItemType Directory -Force -Path $OutputDirectory, $IntermediateDirectory | Out-Null
    $outputArg = "/p:OutputPath=$OutputDirectory\"
    $intermediateArg = "/p:IntermediateOutputPath=$IntermediateDirectory\"
    $buildOutput = & dotnet build $serverProject -c $Configuration -v:minimal $outputArg $intermediateArg 2>&1
    $buildExitCode = $LASTEXITCODE
    foreach ($line in $buildOutput) {
        [Console]::Error.WriteLine($line)
    }

    if ($buildExitCode -ne 0) {
        throw "MCP server build failed with exit code $buildExitCode."
    }
}

if (-not (Test-ServerOutput $serverOutput)) {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ccz-mcp-server-" + [guid]::NewGuid().ToString("N"))
    $tempOutput = Join-Path $tempRoot "bin"
    $tempIntermediate = Join-Path $tempRoot "obj"
    Invoke-ServerBuild -OutputDirectory $tempOutput -IntermediateDirectory $tempIntermediate
    Set-ServerOutput $tempOutput
}

if (-not (Test-Path -LiteralPath $serverDll)) {
    throw "MCP server dll was not found after build: $serverDll"
}
if (-not (Test-Path -LiteralPath $serverRuntimeConfig)) {
    throw "MCP server runtimeconfig was not found after build: $serverRuntimeConfig"
}
if (-not (Test-Path -LiteralPath $serverDeps)) {
    throw "MCP server deps file was not found after build: $serverDeps"
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
