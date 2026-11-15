$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
Write-Host 'Windbound v0.11.1: MIDI export, same-pitch overlaps, source safety, tempo, playlist checks' -ForegroundColor Magenta
$midi = Join-Path $PSScriptRoot '..\Samples\Soulweaver-Quartet-Demo.mid'
dotnet run --project (Join-Path $PSScriptRoot 'SoulweaverSmokeTest.csproj') -c Release -- $midi
if ($LASTEXITCODE -ne 0) { throw 'Soulweaver core test failed.' }
dotnet run --project (Join-Path $PSScriptRoot 'PlaylistProfilesSmokeTest.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Saved playlist regression test failed.' }
Write-Host 'Soulweaver export and playlist tests passed.' -ForegroundColor Green
Write-Host 'Also run Tests\Run-v0.10-Tests.ps1 to check existing playlist and playback logic.'
