param(
    [string]$Configuration = "Release",
    [string]$ReleaseRoot = ""
)

$ErrorActionPreference = "Stop"
$toolRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    $ReleaseRoot = Join-Path $toolRoot "CCZModStudio_Releases"
}
$releaseRootFull = [System.IO.Path]::GetFullPath($ReleaseRoot)
$stamp = Get-Date -Format "yyyyMMdd-HHmmss-fff"
$workRoot = Join-Path $releaseRootFull (".staging-effect-" + $stamp)
$desktopBuild = Join-Path $workRoot "desktop-build"
$mcpBuild = Join-Path $workRoot "mcp-build"
$gameDebugBuild = Join-Path $workRoot "gamedebug-build"
$assembled = Join-Path $workRoot "release"
$pendingRoot = Join-Path $releaseRootFull "Pending"
$releaseId = "CCZModStudio-effect-v7-" + $stamp
$pendingRelease = Join-Path $pendingRoot $releaseId

function Invoke-DotNetPublish {
    param([string]$Project, [string]$Output)
    $isolatedBuild = $Output + ".build" + [System.IO.Path]::DirectorySeparatorChar
    & dotnet restore $Project --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed: $Project" }
    # Project references must not share one MSBuildProjectExtensionsPath: the
    # referenced Runtime assets would overwrite the MCP project's package graph.
    & dotnet publish $Project -c $Configuration -o $Output --no-restore --nologo `
        "/p:BaseOutputPath=$isolatedBuild"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $Project" }
}

function Get-Component {
    param([string]$Id, [string]$NameZh, [string]$Root, [string]$RelativePath)
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "发布组件缺失：$path" }
    $item = Get-Item -LiteralPath $path
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($item.FullName)
    [ordered]@{
        ComponentId = $Id
        DisplayNameZh = $NameZh
        RelativePath = $RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        Length = $item.Length
        Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        FileVersion = $version.FileVersion
        BuildIdentity = $version.ProductVersion
    }
}

function Copy-DirectoryContent {
    param([string]$Source, [string]$Destination)
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
}

function Get-CompatibleRelativePath {
    param([string]$Root, [string]$Path)
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    $relative = ([Uri]$rootFull).MakeRelativeUri([Uri]$pathFull).ToString()
    return [Uri]::UnescapeDataString($relative).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

New-Item -ItemType Directory -Force -Path $releaseRootFull, $workRoot, $assembled | Out-Null
try {
    Invoke-DotNetPublish (Join-Path $toolRoot "CCZModStudio\CCZModStudio.csproj") $desktopBuild
    Invoke-DotNetPublish (Join-Path $toolRoot "CCZModStudio.McpServer\CCZModStudio.McpServer.csproj") $mcpBuild
    Invoke-DotNetPublish (Join-Path $toolRoot "CCZModStudio.GameDebugMcpServer\CCZModStudio.GameDebugMcpServer.csproj") $gameDebugBuild

    Copy-DirectoryContent $desktopBuild $assembled
    Copy-DirectoryContent $mcpBuild (Join-Path $assembled "servers\mcp")
    Copy-DirectoryContent $gameDebugBuild (Join-Path $assembled "servers\gamedebug")

    $desktopDll = Get-ChildItem -LiteralPath $assembled -Recurse -Filter "CCZModStudio.dll" -File |
        Where-Object { $_.FullName -notmatch "\\servers\\" } | Select-Object -First 1
    if (-not $desktopDll) { throw "暂存发布中找不到桌面 Core 程序集。" }
    $desktopRelative = Get-CompatibleRelativePath $assembled $desktopDll.FullName
    $components = @(
        Get-Component "desktop-core" "桌面/Core" $assembled $desktopRelative
        Get-Component "runtime" "Runtime" $assembled "servers\mcp\CCZModStudio.Runtime.dll"
        Get-Component "mcp" "MCP" $assembled "servers\mcp\CCZModStudio.McpServer.dll"
        Get-Component "gamedebug" "GameDebug" $assembled "servers\gamedebug\CCZModStudio.GameDebugMcpServer.dll"
    )
    $buildIdentity = $components[0].BuildIdentity
    $mismatch = $components | Where-Object { $_.BuildIdentity -ne $buildIdentity }
    if ($mismatch) {
        throw "桌面、Runtime、MCP 和 GameDebug 构建标识不一致：$((($components | ForEach-Object { $_.ComponentId + '=' + $_.BuildIdentity })) -join '; ')"
    }
    $manifest = [ordered]@{
        SchemaVersion = "effect-release-manifest-v1"
        EffectCapabilitySchemaVersion = "effect-authoring-7.0"
        BuildChannel = "ccz65-open-authoring-v7"
        BuildIdentity = $buildIdentity
        CreatedAtUtc = [DateTime]::UtcNow.ToString("O")
        Components = $components
    }
    $manifestPath = Join-Path $assembled "effect-release-manifest.json"
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    foreach ($component in $components) {
        $path = Join-Path $assembled $component.RelativePath
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $component.Sha256) {
            throw "发布组件在清单生成后发生变化：$($component.ComponentId)"
        }
    }

    New-Item -ItemType Directory -Force -Path $pendingRoot | Out-Null
    if (Test-Path -LiteralPath $pendingRelease) { throw "待切换目录已存在：$pendingRelease" }
    Move-Item -LiteralPath $assembled -Destination $pendingRelease
    $pointer = [ordered]@{
        SchemaVersion = "effect-pending-release-v1"
        ReleaseId = $releaseId
        PendingPath = $pendingRelease
        ManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $pendingRelease "effect-release-manifest.json") -Algorithm SHA256).Hash
        CreatedAtUtc = [DateTime]::UtcNow.ToString("O")
    }
    $pointer | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $releaseRootFull "pending-effect-release.json") -Encoding utf8
    Write-Output "EFFECT_RELEASE_STAGED $pendingRelease"
}
finally {
    if (Test-Path -LiteralPath $workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force }
}
