param(
    [string]$GameRoot = "",
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$toolRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $toolRoot
$serverProject = Join-Path $toolRoot "CCZModStudio.McpServer\CCZModStudio.McpServer.csproj"
$runtimeProject = Join-Path $toolRoot "CCZModStudio.Runtime\CCZModStudio.Runtime.csproj"
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

function Test-ServerOutputCurrent {
    param([string]$OutputDirectory)

    if (-not (Test-ServerOutput $OutputDirectory)) {
        return $false
    }

    $dllPath = Join-Path $OutputDirectory "CCZModStudio.McpServer.dll"
    $dllTime = (Get-Item -LiteralPath $dllPath).LastWriteTimeUtc
    $sourceRoots = @(
        (Split-Path -Parent $serverProject),
        (Split-Path -Parent $runtimeProject)
    )

    foreach ($root in $sourceRoots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }

        $newerSource = Get-ChildItem -LiteralPath $root -Recurse -File -Include *.cs, *.csproj -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" -and $_.LastWriteTimeUtc -gt $dllTime } |
            Select-Object -First 1
        if ($newerSource) {
            return $false
        }
    }

    return $true
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

if (-not (Test-ServerOutputCurrent $serverOutput)) {
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
    $env:CCZMODSTUDIO_GAME_ROOT = (Resolve-Path -LiteralPath $GameRoot).Path
} elseif ([string]::IsNullOrWhiteSpace($env:CCZMODSTUDIO_GAME_ROOT)) {
    $defaultGameRoot = Find-DefaultGameRoot -Root $workspaceRoot
    if ([string]::IsNullOrWhiteSpace($defaultGameRoot)) {
        throw "Game root was not found. Pass -GameRoot explicitly."
    }

    $env:CCZMODSTUDIO_GAME_ROOT = (Resolve-Path -LiteralPath $defaultGameRoot).Path
}

Set-Location -LiteralPath $toolRoot
& dotnet $serverDll
