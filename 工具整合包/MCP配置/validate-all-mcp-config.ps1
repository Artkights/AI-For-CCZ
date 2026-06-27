param(
    [string]$GameRoot = "",
    [int]$TimeoutMs = 60000,
    [int]$MainMinimumToolCount = 124,
    [int]$GameDebugMinimumToolCount = 65,
    [switch]$SkipProductionToolSmoke,
    [switch]$RunStrictGameDebugSmoke
)

$ErrorActionPreference = "Stop"

$configRoot = $PSScriptRoot
$mainValidator = Join-Path $configRoot "validate-mcp-config.ps1"
$productionToolValidator = Join-Path $configRoot "validate-mcp-production-tools.ps1"
$gameDebugToolsValidator = Join-Path $configRoot "validate-ccz-game-debug-tools.ps1"
$gameDebugStrictValidator = Join-Path $configRoot "validate-ccz-game-debug-mcp.ps1"

if (-not (Test-Path -LiteralPath $mainValidator -PathType Leaf)) {
    throw "Main MCP validator not found: $mainValidator"
}

if (-not (Test-Path -LiteralPath $gameDebugToolsValidator -PathType Leaf)) {
    throw "GameDebug MCP tools validator not found: $gameDebugToolsValidator"
}

if (-not $SkipProductionToolSmoke -and -not (Test-Path -LiteralPath $productionToolValidator -PathType Leaf)) {
    throw "MCP production tool smoke validator not found: $productionToolValidator"
}

if ($RunStrictGameDebugSmoke -and -not (Test-Path -LiteralPath $gameDebugStrictValidator -PathType Leaf)) {
    throw "Strict GameDebug MCP validator not found: $gameDebugStrictValidator"
}

Write-Host "VALIDATE_ALL_MCP_START main_min=$MainMinimumToolCount gamedebug_min=$GameDebugMinimumToolCount production_smoke=$(-not $SkipProductionToolSmoke) strict_gamedebug=$RunStrictGameDebugSmoke"

& $mainValidator -GameRoot $GameRoot -TimeoutMs $TimeoutMs -MinimumToolCount $MainMinimumToolCount
if (-not $SkipProductionToolSmoke) {
    & $productionToolValidator -GameRoot $GameRoot -TimeoutMs $TimeoutMs
}
& $gameDebugToolsValidator -GameRoot $GameRoot -TimeoutMs $TimeoutMs -MinimumToolCount $GameDebugMinimumToolCount

if ($RunStrictGameDebugSmoke) {
    & $gameDebugStrictValidator -GameRoot $GameRoot -TimeoutMs $TimeoutMs -MinimumToolCount $GameDebugMinimumToolCount
}

Write-Host "VALIDATE_ALL_MCP_OK"
