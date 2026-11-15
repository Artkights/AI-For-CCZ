param(
    [switch] $SkipSmoke,
    [switch] $SkipInstall,
    [switch] $Offline,
    [string] $Wheelhouse
)

$ErrorActionPreference = "Stop"
# `$PSScriptRoot` was added after the PowerShell 2.0 shipped with Windows 7.
# Resolve the script directory through the older invocation metadata instead.
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $scriptRoot

# The Python entry point is the canonical build.  Keeping this wrapper small
# avoids PowerShell 5-only syntax on a stock Windows 7 host.
$arguments = @()
if ($SkipSmoke) { $arguments += "--skip-smoke" }
if ($SkipInstall) { $arguments += "--skip-install" }
if ($Offline) { $arguments += "--offline" }
if (-not [String]::IsNullOrEmpty($Wheelhouse)) {
    $arguments += @("--wheelhouse", $Wheelhouse)
}
& python build_release.py @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Win7 release build failed with exit code $LASTEXITCODE"
}
exit 0
