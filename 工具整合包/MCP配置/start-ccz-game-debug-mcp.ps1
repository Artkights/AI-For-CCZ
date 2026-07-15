param(
    [string]$GameRoot = "",
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$DeveloperBuild
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot
$project = Join-Path $toolRoot "CCZModStudio.GameDebugMcpServer\CCZModStudio.GameDebugMcpServer.csproj"
$serverOutput = Join-Path $toolRoot "CCZModStudio.GameDebugMcpServer\bin\$Configuration\net8.0-windows"
$serverDll = Join-Path $serverOutput "CCZModStudio.GameDebugMcpServer.dll"
$serverRuntimeConfig = Join-Path $serverOutput "CCZModStudio.GameDebugMcpServer.runtimeconfig.json"
$serverDeps = Join-Path $serverOutput "CCZModStudio.GameDebugMcpServer.deps.json"

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Game debug MCP project not found: $project"
}

function Test-ServerOutput {
    param([string]$OutputDirectory)

    return (Test-Path -LiteralPath (Join-Path $OutputDirectory "CCZModStudio.GameDebugMcpServer.dll") -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $OutputDirectory "CCZModStudio.GameDebugMcpServer.runtimeconfig.json") -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $OutputDirectory "CCZModStudio.GameDebugMcpServer.deps.json") -PathType Leaf)
}

function Test-ServerOutputCurrent {
    param([string]$OutputDirectory)

    if (-not (Test-ServerOutput $OutputDirectory)) {
        return $false
    }

    $dllPath = Join-Path $OutputDirectory "CCZModStudio.GameDebugMcpServer.dll"
    $dllTime = (Get-Item -LiteralPath $dllPath).LastWriteTimeUtc
    $sourceRoot = Split-Path -Parent $project
    $newerSource = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Include *.cs, *.csproj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" -and $_.LastWriteTimeUtc -gt $dllTime } |
        Select-Object -First 1

    return $null -eq $newerSource
}

function Set-ServerOutput {
    param([string]$OutputDirectory)

    $script:serverOutput = $OutputDirectory
    $script:serverDll = Join-Path $OutputDirectory "CCZModStudio.GameDebugMcpServer.dll"
    $script:serverRuntimeConfig = Join-Path $OutputDirectory "CCZModStudio.GameDebugMcpServer.runtimeconfig.json"
    $script:serverDeps = Join-Path $OutputDirectory "CCZModStudio.GameDebugMcpServer.deps.json"
}

function Invoke-ServerBuild {
    param(
        [string]$OutputDirectory,
        [string]$IntermediateDirectory
    )

    New-Item -ItemType Directory -Force -Path $OutputDirectory, $IntermediateDirectory | Out-Null
    $outputArg = "/p:OutputPath=$OutputDirectory\"
    $intermediateArg = "/p:IntermediateOutputPath=$IntermediateDirectory\"
    $buildOutput = & dotnet build $project -c $Configuration -v:minimal $outputArg $intermediateArg 2>&1
    $buildExitCode = $LASTEXITCODE
    foreach ($line in $buildOutput) {
        [Console]::Error.WriteLine($line)
    }

    if ($buildExitCode -ne 0) {
        throw "Game debug MCP server build failed with exit code $buildExitCode."
    }
}

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
    param([string]$Root)

    $candidates = New-Object System.Collections.Generic.List[string]
    $frontier = @($Root)
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

if (-not $DeveloperBuild) {
    $activeRoot = Join-Path $toolRoot "CCZModStudio_Releases\Active"
    $activeOutput = Join-Path $activeRoot "servers\gamedebug"
    $activeManifest = Join-Path $activeRoot "effect-release-manifest.json"
    if (-not (Test-ServerOutput $activeOutput) -or -not (Test-Path -LiteralPath $activeManifest -PathType Leaf)) {
        throw "Active v7 release is missing. Run publish-effect-release.ps1 and activate-pending-effect-release.ps1, or pass -DeveloperBuild explicitly."
    }
    $manifest = Get-Content -LiteralPath $activeManifest -Raw -Encoding utf8 | ConvertFrom-Json
    if ($manifest.EffectCapabilitySchemaVersion -ne "effect-authoring-7.0" -or $manifest.BuildChannel -ne "ccz65-open-authoring-v7") {
        throw "Active release does not provide effect-authoring-7.0."
    }
    foreach ($component in $manifest.Components) {
        $componentPath = Join-Path $activeRoot ([string]$component.RelativePath)
        if (-not (Test-Path -LiteralPath $componentPath -PathType Leaf) -or
            (Get-FileHash -LiteralPath $componentPath -Algorithm SHA256).Hash -ne [string]$component.Sha256) {
            throw "Active release component failed SHA validation: $($component.ComponentId)"
        }
    }
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ccz-game-debug-mcp-active-" + [guid]::NewGuid().ToString("N"))
    $tempOutput = Join-Path $tempRoot "bin"
    New-Item -ItemType Directory -Force -Path $tempOutput | Out-Null
    Get-ChildItem -LiteralPath $activeOutput -Force | Copy-Item -Destination $tempOutput -Recurse -Force
    Copy-Item -LiteralPath $activeManifest -Destination (Join-Path $tempRoot "effect-release-manifest.json") -Force
    $env:CCZ_EFFECT_RELEASE_MANIFEST = $activeManifest
    Set-ServerOutput $tempOutput
} elseif (-not $NoBuild -and -not (Test-ServerOutputCurrent $serverOutput)) {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ccz-game-debug-mcp-developer-" + [guid]::NewGuid().ToString("N"))
    $tempOutput = Join-Path $tempRoot "bin"
    $tempIntermediate = Join-Path $tempRoot "obj"
    Invoke-ServerBuild -OutputDirectory $tempOutput -IntermediateDirectory $tempIntermediate
    Set-ServerOutput $tempOutput
}

if (-not (Test-Path -LiteralPath $serverDll -PathType Leaf)) {
    throw "Game debug MCP server dll was not found after build: $serverDll"
}
if (-not (Test-Path -LiteralPath $serverRuntimeConfig -PathType Leaf)) {
    throw "Game debug MCP server runtimeconfig was not found after build: $serverRuntimeConfig"
}
if (-not (Test-Path -LiteralPath $serverDeps -PathType Leaf)) {
    throw "Game debug MCP server deps file was not found after build: $serverDeps"
}

$env:CCZMODSTUDIO_TOOL_ROOT = (Resolve-Path -LiteralPath $toolRoot).Path
if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
    $env:CCZGAME_DEBUG_GAME_ROOT = (Resolve-Path -LiteralPath $GameRoot).Path
    $env:CCZMODSTUDIO_GAME_ROOT = $env:CCZGAME_DEBUG_GAME_ROOT
} elseif ([string]::IsNullOrWhiteSpace($env:CCZGAME_DEBUG_GAME_ROOT) -and [string]::IsNullOrWhiteSpace($env:CCZMODSTUDIO_GAME_ROOT)) {
    $defaultGameRoot = Find-DefaultGameRoot -Root $workspaceRoot
    if (-not [string]::IsNullOrWhiteSpace($defaultGameRoot)) {
        $env:CCZGAME_DEBUG_GAME_ROOT = (Resolve-Path -LiteralPath $defaultGameRoot).Path
        $env:CCZMODSTUDIO_GAME_ROOT = $env:CCZGAME_DEBUG_GAME_ROOT
    }
} elseif ([string]::IsNullOrWhiteSpace($env:CCZGAME_DEBUG_GAME_ROOT)) {
    $env:CCZGAME_DEBUG_GAME_ROOT = $env:CCZMODSTUDIO_GAME_ROOT
} elseif ([string]::IsNullOrWhiteSpace($env:CCZMODSTUDIO_GAME_ROOT)) {
    $env:CCZMODSTUDIO_GAME_ROOT = $env:CCZGAME_DEBUG_GAME_ROOT
}

Set-Location $toolRoot
dotnet $serverDll
