$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.14 - automatic cross-PC party instrument tests' -ForegroundColor Magenta
& (Join-Path $PSScriptRoot 'Run-v0.13-Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'v0.13 regression tests failed.' }
dotnet run --project .\PartyInstrumentPlanSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Party instruments smoke test failed.' }
Write-Host 'PASS: instrument plan + backwards-compatible one-click ensemble protocol.' -ForegroundColor Green
