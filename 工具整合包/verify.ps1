param(
    [switch]$RunWriteSmoke,
    [switch]$SkipMcpValidation,
    [switch]$StopStaleMcp,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host ""
    Write-Host "== $Name ==" -ForegroundColor Cyan
    & $Action
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "External command failed with exit code ${exitCode}: $FilePath $($Arguments -join ' ')"
    }
}

function Find-FirstFile {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Filter
    )

    $file = Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $Filter |
        Sort-Object FullName |
        Select-Object -First 1

    if (-not $file) {
        throw "Could not find $Filter under $Root."
    }

    $file.FullName
}

function Test-IsIgnoredPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$KnowledgeRoot = ""
    )

    if ($Path -match '\\bin\\|\\obj\\|\\.git\\|\\.vs\\|\\90-[^\\]*\\') {
        return $true
    }

    if ($KnowledgeRoot) {
        $rootWithSlash = $KnowledgeRoot.TrimEnd('\') + '\'
        if ($Path.StartsWith($rootWithSlash, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Find-KnowledgeRoot {
    param([Parameter(Mandatory = $true)][string]$Root)

    $archiveDir = Get-ChildItem -LiteralPath $Root -Recurse -Directory |
        Where-Object { $_.Name -like '90-*' } |
        Sort-Object FullName |
        Select-Object -First 1

    if (-not $archiveDir) {
        throw "Could not find knowledge archive directory under $Root."
    }

    $archiveDir.Parent.FullName
}

function Get-OldKnowledgeEntryNames {
    param([Parameter(Mandatory = $true)][string]$Root)

    $indexes = Get-ChildItem -LiteralPath $Root -Recurse -File -Filter "*.md" |
        Where-Object {
            $_.DirectoryName -match '\\90-[^\\]*$' -and
            $_.Name -notlike '*README*'
        } |
        Sort-Object FullName

    foreach ($index in $indexes) {
        $content = Get-Content -LiteralPath $index.FullName -Encoding UTF8 -ErrorAction Stop
        $names = foreach ($line in $content) {
            $matches = [regex]::Matches($line, '`([^`]+\.md)`')
            foreach ($match in $matches) {
                $name = $match.Groups[1].Value
                if ($name -notmatch '/' -and $name -notmatch '\\') {
                    $name
                }
            }
        }

        $oldNames = $names |
            Where-Object { $_ -ne 'README.md' } |
            Sort-Object -Unique

        if ($oldNames) {
            return $oldNames
        }
    }

    throw "Could not find old knowledge-base entry names from migration indexes under $Root."
}

function Get-TextFilesForReferenceScan {
    param([Parameter(Mandatory = $true)][string]$Root)

    $paths = git -C $Root ls-files -co --exclude-standard -- "*.cs" "*.md" "*.ps1"
    if ($LASTEXITCODE -eq 0 -and $paths) {
        foreach ($path in $paths) {
            $fullPath = Join-Path $Root $path
            if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
                Get-Item -LiteralPath $fullPath
            }
        }

        return
    }

    Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.Extension -in @(".cs", ".md", ".ps1") }
}

function Assert-NoGeneratedSourcePollution {
    param(
        [Parameter(Mandatory = $true)][string]$ToolRoot,
        [Parameter(Mandatory = $true)][string]$Configuration
    )

    $projects = Get-ChildItem -LiteralPath $ToolRoot -Recurse -File -Filter "*.csproj" |
        Where-Object { $_.FullName -notmatch '\\artifacts\\|\\bin\\|\\obj\\' } |
        Sort-Object FullName

    $polluted = New-Object System.Collections.Generic.List[string]
    foreach ($project in $projects) {
        $output = & dotnet msbuild $project.FullName -nologo -p:Configuration=$Configuration -p:UseAppHost=false -getItem:Compile 2>&1
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            $output | ForEach-Object { Write-Host $_ }
            throw "Could not evaluate Compile items for $($project.FullName). dotnet msbuild exited with $exitCode."
        }

        $jsonText = ($output -join [Environment]::NewLine)
        $jsonStart = $jsonText.IndexOf('{')
        if ($jsonStart -lt 0) {
            throw "Could not find JSON Compile item output for $($project.FullName)."
        }
        $evaluated = $jsonText.Substring($jsonStart) | ConvertFrom-Json
        foreach ($item in @($evaluated.Items.Compile)) {
            $identity = [string]$item.Identity
            $fullPath = [string]$item.FullPath
            $relative = $fullPath
            if ($relative.StartsWith($ToolRoot, [StringComparison]::OrdinalIgnoreCase)) {
                $relative = $relative.Substring($ToolRoot.Length).TrimStart('\')
            }

            $isGeneratedSource =
                $identity -match '(^|\\)artifacts\\' -and
                ($identity -match '\.NETCoreApp,Version=.*AssemblyAttributes\.cs$' -or
                 $identity -match '\.AssemblyInfo\.cs$' -or
                 $identity -match '\.GlobalUsings\.g\.cs$')

            if ($isGeneratedSource) {
                $polluted.Add("$($project.FullName) <- $relative")
            }
        }
    }

    if ($polluted.Count -gt 0) {
        $polluted | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
        throw "Generated source pollution detected in MSBuild Compile items. Exclude artifacts/** before building."
    }

    Write-Host "Generated source pollution scan passed: no artifacts generated sources are compiled."
}

function Get-StaleMcpDotnetProcesses {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    $escapedRoot = [regex]::Escape($ToolRoot)
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine -match $escapedRoot -and
            ($_.CommandLine -match 'CCZModStudio\.McpServer\.dll' -or $_.CommandLine -match 'CCZModStudio\.GameDebugMcpServer\.dll')
        } |
        Sort-Object ProcessId
}

function Assert-StaleMcpProcesses {
    param(
        [Parameter(Mandatory = $true)][string]$ToolRoot,
        [switch]$Stop
    )

    $processes = @(Get-StaleMcpDotnetProcesses -ToolRoot $ToolRoot)
    if ($processes.Count -eq 0) {
        return
    }

    foreach ($process in $processes) {
        Write-Host ("Stale MCP dotnet process PID={0}: {1}" -f $process.ProcessId, $process.CommandLine) -ForegroundColor Yellow
    }

    if (-not $Stop) {
        Write-Host "Pass -StopStaleMcp to terminate these current-workspace MCP processes before building." -ForegroundColor Yellow
        return
    }

    foreach ($process in $processes) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
    }
    Write-Host ("Stopped {0} stale MCP process(es)." -f $processes.Count)
}

function Assert-RuntimeBoundary {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    $runtimeProject = Join-Path $ToolRoot "CCZModStudio.Runtime\CCZModStudio.Runtime.csproj"
    $runtimeProjectText = Get-Content -LiteralPath $runtimeProject -Raw -Encoding UTF8
    if ($runtimeProjectText -match '<UseWindowsForms>\s*true\s*</UseWindowsForms>') {
        throw "Runtime boundary violation: CCZModStudio.Runtime must not enable UseWindowsForms."
    }

    $sourceRoots = @(
        (Join-Path $ToolRoot "CCZModStudio\Core"),
        (Join-Path $ToolRoot "CCZModStudio\Formats"),
        (Join-Path $ToolRoot "CCZModStudio\Models")
    )
    $forbiddenPattern = 'System\.Windows\.Forms|Windows\.Forms|MessageBox|Control\.DefaultFont|Application\.|Form\b|DataGridView\b|TreeView\b|ListView\b|ComboBox\b|OpenFileDialog\b|SaveFileDialog\b|FolderBrowserDialog\b|Clipboard\b'
    $matches = foreach ($root in $sourceRoots) {
        Get-ChildItem -LiteralPath $root -Recurse -File -Filter "*.cs" |
            Select-String -Pattern $forbiddenPattern -Encoding UTF8 -ErrorAction SilentlyContinue
    }

    if ($matches) {
        $matches | ForEach-Object {
            Write-Host ("{0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim()) -ForegroundColor Yellow
        }
        throw "Runtime boundary violation: Core/Formats/Models must not reference WinForms UI APIs."
    }

    Write-Host "Runtime boundary scan passed: no WinForms references in Core/Formats/Models."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $PSScriptRoot "CCZModStudio.sln"
$smokeProject = Join-Path $PSScriptRoot "CCZModStudio.SmokeTests\CCZModStudio.SmokeTests.csproj"
$smokeDll = Join-Path $PSScriptRoot "CCZModStudio.SmokeTests\bin\$Configuration\net8.0-windows\CCZModStudio.SmokeTests.dll"
$mcpValidation = Find-FirstFile -Root $PSScriptRoot -Filter "validate-mcp-config.ps1"
$gameDebugMcpValidation = Find-FirstFile -Root $PSScriptRoot -Filter "validate-ccz-game-debug-mcp.ps1"

Invoke-Step "Generated source pollution scan" {
    Assert-NoGeneratedSourcePollution -ToolRoot $PSScriptRoot -Configuration $Configuration
}

Invoke-Step "Stale MCP process scan" {
    Assert-StaleMcpProcesses -ToolRoot $PSScriptRoot -Stop:$StopStaleMcp
}

Invoke-Step "Build solution" {
    Invoke-External -FilePath dotnet -Arguments @("build", $solution, "-c", $Configuration, "-v:minimal", "-p:UseAppHost=false")
}

Invoke-Step "Runtime boundary scan" {
    Assert-RuntimeBoundary -ToolRoot $PSScriptRoot
}

Invoke-Step "R/S read smoke" {
    if (-not (Test-Path -LiteralPath $smokeDll -PathType Leaf)) {
        throw "Smoke test DLL was not built: $smokeDll"
    }

    Invoke-External -FilePath dotnet -Arguments @($smokeDll, "--rs-smoke")
}

Invoke-Step "Legacy MFC dialog smoke" {
    Invoke-External -FilePath dotnet -Arguments @($smokeDll, "--legacy-mfc-dialog-smoke")
}

Invoke-Step "Global settings dialog smoke" {
    Invoke-External -FilePath dotnet -Arguments @($smokeDll, "--global-settings-dialog-smoke")
}

if ($RunWriteSmoke) {
    Invoke-Step "R/S write smoke on test copy" {
        Invoke-External -FilePath dotnet -Arguments @($smokeDll, "--rs-write-smoke")
    }
}

if (-not $SkipMcpValidation) {
    Invoke-Step "MCP stdio validation" {
        Invoke-External -FilePath powershell -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $mcpValidation)
    }

    Invoke-Step "Game debug MCP stdio validation" {
        Invoke-External -FilePath powershell -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $gameDebugMcpValidation)
    }
}

Invoke-Step "Knowledge migration reference scan" {
    $oldEntryNames = Get-OldKnowledgeEntryNames -Root $PSScriptRoot
    $oldEntryPattern = ($oldEntryNames | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $knowledgeRoot = Find-KnowledgeRoot -Root $PSScriptRoot
    $files = Get-TextFilesForReferenceScan -Root $repoRoot |
        Where-Object { -not (Test-IsIgnoredPath -Path $_.FullName -KnowledgeRoot $knowledgeRoot) }

    $matches = foreach ($file in $files) {
        Select-String -Path $file.FullName -Pattern $oldEntryPattern -Encoding UTF8 -ErrorAction SilentlyContinue
    }

    if ($matches) {
        $matches | ForEach-Object {
            Write-Host ("{0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim()) -ForegroundColor Yellow
        }
        throw "Found active references to old flat knowledge-base Markdown paths."
    }

    Write-Host ("No active old flat knowledge-base Markdown references found. Checked {0} old entry names." -f $oldEntryNames.Count)
}

Write-Host ""
Write-Host "VERIFY_OK" -ForegroundColor Green
