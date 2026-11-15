$ErrorActionPreference = 'Stop'
Write-Host 'Checking Windbound v0.17 native-ensemble source...' -ForegroundColor Cyan
$scriptPath = Join-Path $PSScriptRoot 'Check-v0.17-Native-Ensemble.py'
$hasPython = $false
if (Get-Command py -ErrorAction SilentlyContinue) { & py -3 $scriptPath; $hasPython = $true }
elseif (Get-Command python -ErrorAction SilentlyContinue) { & python $scriptPath; $hasPython = $true }
else { Write-Warning 'Python 3 not found; skipping source structural test.' }
if ($hasPython -and $LASTEXITCODE -ne 0) { throw 'Native source checks failed.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Warning 'dotnet SDK not found; skipping C# native start-gate tests.'
    exit 0
}
Write-Host 'Running native start-gate C# tests...' -ForegroundColor Cyan
dotnet run --project (Join-Path $PSScriptRoot 'GameEnsembleStartGateTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Native start-gate tests failed.' }
Write-Host 'Checks finished. In-game FFXIV/Dalamud ensemble testing is still essential.' -ForegroundColor Green
