$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.13 - one-click cross-PC conductor tests' -ForegroundColor Magenta
dotnet run --project .\OneClickEnsembleSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'One-click conductor smoke test failed.' }
dotnet run --project .\GamePartyCueProtocolTests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Legacy party cue compatibility failed.' }
dotnet run --project .\GameEnsembleStartGateTests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Native FFXIV Ensemble regression test failed.' }
Write-Host 'PASS: cross-PC One-Click and native fallback checks.' -ForegroundColor Green
