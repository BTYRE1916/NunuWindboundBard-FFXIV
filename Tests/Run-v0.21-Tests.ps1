$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound Bard v0.21 — French localisation regression tests' -ForegroundColor Magenta
dotnet run --project LocalizationSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'French/English translation tests failed.' }
Write-Host 'French UI labels, runtime statuses, hidden ImGui IDs and English fallback verified.' -ForegroundColor Green
Write-Host 'Now build the complete plugin with Build-Plugin.ps1 and test inside FFXIV.' -ForegroundColor Yellow
