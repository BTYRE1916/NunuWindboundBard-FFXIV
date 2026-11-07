$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
& (Join-Path $PSScriptRoot 'Run-v0.24-Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Stage Manager regression tests failed.' }
dotnet run --project ConcertSetlistSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Concert Setlist tests failed.' }
Write-Host 'PASS: Concert Stage + Setlist + targeted retry protocol.' -ForegroundColor Green
