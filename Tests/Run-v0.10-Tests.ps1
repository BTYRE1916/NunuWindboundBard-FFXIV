$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound Bard v0.10: Auto Next / manual MIDI transitions' -ForegroundColor Cyan
& .\Check-SongFlow-Source.ps1
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Source check failed' }
dotnet run --project SongFlowCoordinatorTests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Song flow tests failed' }
Write-Host 'PASS: framework-driven song transition tests. Build plugin & verify in FFXIV.' -ForegroundColor Green
Write-Host 'Optional v0.9 networking regression tests: .\Run-v0.9-Tests.ps1'
