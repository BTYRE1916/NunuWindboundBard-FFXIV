$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
& (Join-Path $PSScriptRoot 'Run-v0.25-Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Stage and setlist tests failed.' }
dotnet run --project MidiArrangementInspectorSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'MIDI Inspector tests failed.' }
Write-Host 'PASS: Concert Director, Setlist Automation, Retry and MIDI Arrangement Inspector.' -ForegroundColor Green
Write-Host 'Live native game and multi-account testing are still required.' -ForegroundColor Yellow
