$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.32 — German, French and English UI tests' -ForegroundColor Magenta
py .\Check-v0.32-German-Localization.py
if ($LASTEXITCODE -ne 0) { throw 'Static German coverage check failed.' }

dotnet run --project .\LocalizationSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'French/English regression suite failed.' }

dotnet run --project .\GermanLocalizationSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'German translation coverage/IDs suite failed.' }

& .\Run-v0.31.2-Checks.ps1
if ($LASTEXITCODE -ne 0) { throw 'Last known stable playback source checks failed.' }
Write-Host 'PASS: English, French, German and playback source checks' -ForegroundColor Green
Write-Host 'Build v0.32 with ..\Build-Plugin.ps1 and test UI inside FFXIV' -ForegroundColor Yellow
