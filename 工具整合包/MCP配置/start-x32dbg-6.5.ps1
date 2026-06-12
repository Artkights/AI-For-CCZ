param(
    [string]$GameRoot = "",
    [string]$X32dbgPath = "",
    [switch]$Hidden
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$toolRoot = Split-Path -Parent $configRoot
$workspaceRoot = Split-Path -Parent $toolRoot
$targetSha256 = "84E3A1DC085AE6F9900D1E8C388A9CD6766379832DDF51BC7BDF780C6615B4A3"

function Resolve-DefaultGameRoot {
    param([string]$Root)

    $excludedPathParts = @(
        "\CCZModStudio_TestCopies\",
        "\CCZModStudio_Exports\",
        "\_CCZModStudio_Backups\",
        "\工具整合包\"
    )

    $candidates = @(Get-ChildItem -LiteralPath $Root -Recurse -Filter "Ekd5.exe" -File -ErrorAction SilentlyContinue |
        Where-Object {
            $fullName = $_.FullName
            -not ($excludedPathParts | Where-Object { $fullName.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
        })
    foreach ($candidate in $candidates) {
        try {
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidate.FullName).Hash
            if ($hash -eq $targetSha256) {
                return (Split-Path -Parent $candidate.FullName)
            }
        }
        catch {
            continue
        }
    }

    if ($candidates.Count -gt 0) {
        return (Split-Path -Parent $candidates[0].FullName)
    }

    return ""
}

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $GameRoot = Resolve-DefaultGameRoot -Root $workspaceRoot
    if ([string]::IsNullOrWhiteSpace($GameRoot)) {
        throw "Could not locate Ekd5.exe under workspace root: $workspaceRoot"
    }
}

if ([string]::IsNullOrWhiteSpace($X32dbgPath)) {
    $X32dbgPath = Join-Path $workspaceRoot "CCZModStudio_Exports\DebugTools\x64dbg\release\x32\x32dbg.exe"
}

$exePath = Join-Path $GameRoot "Ekd5.exe"
$pluginPath = Join-Path (Split-Path -Parent $X32dbgPath) "plugins\x64dbg_mcp.dp32"

if (-not (Test-Path -LiteralPath $X32dbgPath -PathType Leaf)) {
    throw "x32dbg.exe not found: $X32dbgPath"
}

if (-not (Test-Path -LiteralPath $pluginPath -PathType Leaf)) {
    throw "x64dbg MCP plugin not found: $pluginPath"
}

if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Ekd5.exe not found: $exePath"
}

$resolvedGameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
$resolvedExePath = (Resolve-Path -LiteralPath $exePath).Path
$resolvedX32dbgPath = (Resolve-Path -LiteralPath $X32dbgPath).Path
$resolvedPluginPath = (Resolve-Path -LiteralPath $pluginPath).Path

$windowStyle = if ($Hidden) { "Hidden" } else { "Normal" }
Start-Process -FilePath $resolvedX32dbgPath -ArgumentList "`"$resolvedExePath`"" -WorkingDirectory $resolvedGameRoot -WindowStyle $windowStyle

[pscustomobject]@{
    X32dbg = $resolvedX32dbgPath
    Plugin = $resolvedPluginPath
    Target = $resolvedExePath
    GameRoot = $resolvedGameRoot
} | Format-List
