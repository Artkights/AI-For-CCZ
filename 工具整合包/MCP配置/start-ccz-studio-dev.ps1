[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$DryRun,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ApplicationArguments = @()
)

$ErrorActionPreference = "Stop"
$toolRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $toolRoot "CCZModStudio\CCZModStudio.csproj"

if (-not $DryRun -and -not $NoBuild) {
    & dotnet build $projectPath -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "CCZModStudio developer build failed with exit code $LASTEXITCODE."
    }
}

$outputRoot = Join-Path $toolRoot "CCZModStudio\bin\$Configuration\net8.0-windows\win-x64"
$localizedLauncherName = -join (0x666E, 0x7F57, 0x5DE5, 0x5177, 0x6574, 0x5408, 0x5305 | ForEach-Object { [char]$_ })
$executableName = if ($Configuration -eq "Release") { $localizedLauncherName + ".exe" } else { "CCZModStudio.exe" }
$executablePath = Join-Path $outputRoot $executableName
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Developer executable was not found: $executablePath. Build $Configuration first or omit -NoBuild."
}

$workingDirectory = Split-Path -Parent $executablePath
$arguments = @("--developer-build") + @($ApplicationArguments)
Write-Output "CCZ_STUDIO_DEVELOPER_TARGET $executablePath"
Write-Output "CCZ_STUDIO_DEVELOPER_WORKING_DIRECTORY $workingDirectory"
Write-Output "CCZ_STUDIO_DEVELOPER_ARGUMENTS $(($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' ')"

if ($DryRun) {
    Write-Output "CCZ_STUDIO_DEVELOPER_DRY_RUN_OK"
    return
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $executablePath
$startInfo.WorkingDirectory = $workingDirectory
$startInfo.UseShellExecute = $false
foreach ($argument in $arguments) {
    $startInfo.ArgumentList.Add($argument)
}

$process = [System.Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) {
    throw "Failed to start the CCZModStudio developer build."
}

Write-Output "CCZ_STUDIO_DEVELOPER_STARTED pid=$($process.Id)"
