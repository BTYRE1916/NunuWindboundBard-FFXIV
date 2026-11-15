$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound Concert Director v0.24 preflight / party status tests' -ForegroundColor Magenta
dotnet run --project ConcertStageHealthSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Concert stage tests failed.' }
Write-Host 'Review Tests/Run-v0.23-Tests.ps1 for previously established MIDI and party-protocol regressions.' -ForegroundColor Yellow
Write-Host 'Live FFXIV native START and chat replies require a real multi-client ensemble test.' -ForegroundColor Yellow
