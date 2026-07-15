param(
    [string]$ReleaseRoot = ""
)

$ErrorActionPreference = "Stop"
$toolRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    $ReleaseRoot = Join-Path $toolRoot "CCZModStudio_Releases"
}
$releaseRootFull = [System.IO.Path]::GetFullPath($ReleaseRoot)
$pendingPointer = Join-Path $releaseRootFull "pending-effect-release.json"
$activeRoot = Join-Path $releaseRootFull "Active"

function Get-CczRunningProcesses {
    $localizedDesktopProcess = -join (0x666E, 0x7F57, 0x5DE5, 0x5177, 0x6574, 0x5408, 0x5305 | ForEach-Object { [char]$_ })
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -in @("CCZModStudio", $localizedDesktopProcess, "CCZModStudio.McpServer", "CCZModStudio.GameDebugMcpServer")
    })
    $dotnetServers = @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue | Where-Object {
        $_.CommandLine -match "CCZModStudio\.(McpServer|GameDebugMcpServer)\.dll"
    })
    return @($running) + @($dotnetServers)
}

if (Test-Path -LiteralPath $pendingPointer -PathType Leaf) {
    if ((Get-CczRunningProcesses).Count -eq 0) {
        & (Join-Path $PSScriptRoot "activate-pending-effect-release.ps1") -ReleaseRoot $releaseRootFull
        if ($LASTEXITCODE -ne 0) { throw "Pending release activation failed." }
    } elseif (-not (Test-Path -LiteralPath $activeRoot -PathType Container)) {
        throw "A pending release exists, but desktop/MCP/GameDebug is running and no Active release is available. Close those processes and retry."
    }
}

$manifestPath = Join-Path $activeRoot "effect-release-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Active release is missing. Publish and activate effect-authoring-7.0 first."
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($manifest.SchemaVersion -ne "effect-release-manifest-v1" -or
    $manifest.EffectCapabilitySchemaVersion -ne "effect-authoring-7.0" -or
    $manifest.BuildChannel -ne "ccz65-open-authoring-v7" -or
    [string]$manifest.BuildIdentity -notlike "7.0.0+open-authoring-v7*") {
    throw "Active release identity is not the required v7 open-authoring build."
}
foreach ($component in $manifest.Components) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $activeRoot ([string]$component.RelativePath)))
    $prefix = $activeRoot.TrimEnd('\') + '\'
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne [string]$component.Sha256) {
        throw "Active release component failed validation: $($component.ComponentId)"
    }
}

$desktopExe = @(
    (Join-Path $activeRoot ((-join (0x666E, 0x7F57, 0x5DE5, 0x5177, 0x6574, 0x5408, 0x5305 | ForEach-Object { [char]$_ })) + ".exe")),
    (Join-Path $activeRoot "CCZModStudio.exe")
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $desktopExe) { throw "Active desktop launcher was not found." }
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($desktopExe)
if ($version.ProductVersion -notlike "7.0.0+open-authoring-v7*") {
    throw "Active desktop executable has an unexpected build identity: $($version.ProductVersion)"
}
$env:CCZ_EFFECT_RELEASE_MANIFEST = $manifestPath
Start-Process -FilePath $desktopExe -WorkingDirectory $activeRoot
Write-Output "CCZ_STUDIO_STARTED $desktopExe"
