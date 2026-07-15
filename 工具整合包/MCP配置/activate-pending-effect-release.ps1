param(
    [string]$ReleaseRoot = ""
)

$ErrorActionPreference = "Stop"
$toolRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    $ReleaseRoot = Join-Path $toolRoot "CCZModStudio_Releases"
}
$releaseRootFull = [System.IO.Path]::GetFullPath($ReleaseRoot)
$pointerPath = Join-Path $releaseRootFull "pending-effect-release.json"
if (-not (Test-Path -LiteralPath $pointerPath -PathType Leaf)) { throw "没有待切换的特效发布。" }

$localizedDesktopProcess = -join (0x666E, 0x7F57, 0x5DE5, 0x5177, 0x6574, 0x5408, 0x5305 | ForEach-Object { [char]$_ })
$running = Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -in @("CCZModStudio", $localizedDesktopProcess, "CCZModStudio.McpServer", "CCZModStudio.GameDebugMcpServer")
}
$dotnetServers = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -match "CCZModStudio\.(McpServer|GameDebugMcpServer)\.dll"
}
if ($dotnetServers) { $running = @($running) + @($dotnetServers) }
if ($running) { throw "桌面、MCP 或 GameDebug 仍在运行，禁止替换发布目录。" }

$pointer = Get-Content -LiteralPath $pointerPath -Raw -Encoding utf8 | ConvertFrom-Json
$pending = [System.IO.Path]::GetFullPath([string]$pointer.PendingPath)
$pendingPrefix = (Join-Path $releaseRootFull "Pending").TrimEnd('\') + '\'
if (-not $pending.StartsWith($pendingPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not (Test-Path -LiteralPath $pending -PathType Container)) {
    throw "待切换目录不属于当前 ReleaseRoot。"
}
$manifestPath = Join-Path $pending "effect-release-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
    (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne [string]$pointer.ManifestSha256) {
    throw "待切换发布清单缺失或摘要不一致。"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($manifest.SchemaVersion -ne "effect-release-manifest-v1" -or
    $manifest.EffectCapabilitySchemaVersion -ne "effect-authoring-7.0" -or
    $manifest.BuildChannel -ne "ccz65-open-authoring-v7" -or
    [string]$manifest.BuildIdentity -notlike "7.0.0+open-authoring-v7*" -or
    $manifest.Components.Count -lt 4) {
    throw "待切换发布清单不完整。"
}
foreach ($component in $manifest.Components) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $pending ([string]$component.RelativePath)))
    $prefix = $pending.TrimEnd('\') + '\'
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne [string]$component.Sha256) {
        throw "待切换发布组件不完整：$($component.ComponentId)"
    }
}

$active = Join-Path $releaseRootFull "Active"
$previousRoot = Join-Path $releaseRootFull "Previous"
New-Item -ItemType Directory -Force -Path $previousRoot | Out-Null
if (Test-Path -LiteralPath $active) {
    $previous = Join-Path $previousRoot ("Active-" + (Get-Date -Format "yyyyMMdd-HHmmss-fff"))
    Move-Item -LiteralPath $active -Destination $previous
}
try {
    Move-Item -LiteralPath $pending -Destination $active
}
catch {
    $lastPrevious = Get-ChildItem -LiteralPath $previousRoot -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($lastPrevious -and -not (Test-Path -LiteralPath $active)) {
        Move-Item -LiteralPath $lastPrevious.FullName -Destination $active
    }
    throw
}
Remove-Item -LiteralPath $pointerPath -Force
Write-Output "EFFECT_RELEASE_ACTIVATED $active"
