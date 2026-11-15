$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound Bard v0.30 - Stage Formations smoke tests' -ForegroundColor Magenta
dotnet run --project .\StageFormationsSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Stage formations logic / party protocol tests FAILED.' }
& (Join-Path $PSScriptRoot 'Run-v0.29-Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Original Concert Director/ARM regression tests FAILED.' }
Write-Host 'Stage formations and existing ensemble regression checks passed.' -ForegroundColor Green
Write-Host 'Still requires in-game testing with installed vnavmesh on each client.' -ForegroundColor Yellow
