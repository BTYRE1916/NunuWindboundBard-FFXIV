$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.20 - USB/Live MIDI keyboard tests' -ForegroundColor Magenta

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET 10 SDK first.' }
dotnet run --project .\LiveMidiNoteRouterTests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Live MIDI router regression failed.' }

& (Join-Path $PSScriptRoot 'Run-v0.19-Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'v0.19 conductor/party native tests failed.' }
Write-Host 'PASS: MIDI hardware mapping and retained ensemble protocol tests.' -ForegroundColor Green
Write-Host 'Hardware winmm and native instrument=0 unequip still require Windows/FFXIV validation.' -ForegroundColor Yellow
