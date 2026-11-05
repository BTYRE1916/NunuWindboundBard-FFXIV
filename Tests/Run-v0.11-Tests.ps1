$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
Write-Host 'Windbound v0.11: MIDI arrangement, note-count, source safety, tempo and export tests' -ForegroundColor Magenta
$midi = Join-Path $PSScriptRoot '..\Samples\Soulweaver-Quartet-Demo.mid'
dotnet run --project (Join-Path $PSScriptRoot 'SoulweaverSmokeTest.csproj') -c Release -- $midi
if ($LASTEXITCODE -ne 0) { throw 'Soulweaver core test failed.' }
Write-Host 'All Soulweaver tests passed.' -ForegroundColor Green
Write-Host 'Also run Tests\Run-v0.10-Tests.ps1 to check existing playlist and playback logic.'
