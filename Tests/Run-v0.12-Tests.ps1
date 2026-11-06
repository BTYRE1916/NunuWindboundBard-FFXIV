$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
Write-Host 'Windbound v0.12.1 — ensemble start gate, party cues and playback regression tests' -ForegroundColor Magenta
dotnet run --project (Join-Path $PSScriptRoot 'GamePartyCueProtocolTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Party command protocol test failed.' }
dotnet run --project (Join-Path $PSScriptRoot 'GameEnsembleStartGateTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Game Ensemble start gate test failed.' }
& (Join-Path $PSScriptRoot 'Run-v0.11-Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Previous Soulweaver smoke tests failed.' }
& (Join-Path $PSScriptRoot 'Check-Game-Ensemble-Source.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Game Ensemble source checks failed.' }
Write-Host 'Game Ensemble protocol tests and existing Soulweaver regression tests passed.' -ForegroundColor Green
Write-Host 'Note: native FFXIV ensemble start hook can ONLY be tested in game.' -ForegroundColor Yellow
