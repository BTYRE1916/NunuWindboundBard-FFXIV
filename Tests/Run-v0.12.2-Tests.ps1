$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
Write-Host 'Windbound v0.12.2 — progress-reset and ensemble regression tests' -ForegroundColor Magenta
& (Join-Path $PSScriptRoot 'Run-v0.12-Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Previous ensemble / Soulweaver regression tests failed.' }
dotnet run --project (Join-Path $PSScriptRoot 'PlaybackResetSmokeTest.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Playback rewind tests failed.' }
Write-Host 'Progress rewind, stop, loaded MIDI, native note release and ensemble countdown tests passed.' -ForegroundColor Green
Write-Host 'Native performance and real FFXIV ensemble still require in-game testing.' -ForegroundColor Yellow
