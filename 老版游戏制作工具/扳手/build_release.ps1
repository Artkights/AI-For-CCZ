$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$buildTemp = $env:BANSHOU_BUILD_TEMP
if ([string]::IsNullOrWhiteSpace($buildTemp)) {
    $buildTemp = Join-Path $PSScriptRoot "build-temp"
}
$buildTemp = [System.IO.Path]::GetFullPath($buildTemp)
New-Item -ItemType Directory -Path $buildTemp -Force | Out-Null
$env:TEMP = $buildTemp
$env:TMP = $buildTemp
$env:PIP_CACHE_DIR = Join-Path $buildTemp "pip-cache"
$env:PYINSTALLER_CONFIG_DIR = Join-Path $buildTemp "pyinstaller-cache"
Write-Host "Build temporary directory: $buildTemp"

function Assert-LastExitCode([string] $Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE"
    }
}

python -c "import struct,sys; sys.exit(0 if struct.calcsize('P') == 4 else 1)"
Assert-LastExitCode "32-bit Python check"

python -m pip install -r requirements-dev-32.txt
Assert-LastExitCode "Dependency installation"

python -m py_compile main.py mywindow.py hook.py engine_profile.py engine_layout.py semantic_resolver.py process_memory.py capability_evidence.py evidence_collector.py
Assert-LastExitCode "Syntax compilation"

$previousQpaPlatform = $env:QT_QPA_PLATFORM
try {
    $env:QT_QPA_PLATFORM = "offscreen"
    python -m unittest discover -s tests -q
    Assert-LastExitCode "Test suite"
}
finally {
    $env:QT_QPA_PLATFORM = $previousQpaPlatform
}

python -m PyInstaller --noconfirm --clean banshou66.spec
Assert-LastExitCode "PyInstaller build"

$artifacts = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "dist") -Filter "*.exe")
if ($artifacts.Count -ne 1) {
    throw "Expected exactly one release executable, found $($artifacts.Count)"
}

$artifact = $artifacts[0]
$archiveListing = python -m PyInstaller.utils.cliutils.archive_viewer -l $artifact.FullName
Assert-LastExitCode "PyInstaller archive inspection"
if (-not ($archiveListing -match "capstone")) {
    throw "Packaged executable does not contain the Capstone runtime"
}
$previousQpaPlatform = $env:QT_QPA_PLATFORM
$smokeProcesses = @()
try {
    $env:QT_QPA_PLATFORM = "offscreen"
    Start-Process -FilePath $artifact.FullName -WindowStyle Hidden | Out-Null
    Start-Sleep -Seconds 8
    $smokeProcesses = @(Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $artifact.FullName })
    if ($smokeProcesses.Count -eq 0) {
        throw "Packaged executable did not remain running"
    }
    $exceptionWindows = @($smokeProcesses | Where-Object {
        $_.MainWindowTitle -like "*Unhandled exception*" -or
        $_.MainWindowTitle -like "*Failed to execute script*"
    })
    if ($exceptionWindows.Count -ne 0) {
        throw "Packaged executable opened an unhandled-exception window"
    }
}
finally {
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $cleanupProcesses = @(Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $artifact.FullName })
        if ($cleanupProcesses.Count -eq 0) {
            break
        }
        foreach ($process in $cleanupProcesses) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 300
    }
    $remainingSmokeProcesses = @(Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $artifact.FullName })
    $env:QT_QPA_PLATFORM = $previousQpaPlatform
    if ($remainingSmokeProcesses.Count -ne 0) {
        throw "Packaged smoke-test processes did not exit"
    }
}

$digest = (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256).Hash
$checksumPath = "$($artifact.FullName).sha256"
$releaseFileName = "6.6$([char]0x6273)$([char]0x624B).exe"
if ($artifact.Name -ne $releaseFileName) {
    throw "Unexpected release filename: $($artifact.Name)"
}
[System.IO.File]::WriteAllText(
    $checksumPath,
    "$digest *$releaseFileName`r`n",
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Build complete: $($artifact.FullName)"
Write-Host "SHA256: $digest"
