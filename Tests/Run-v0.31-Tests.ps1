$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.31 - Visual Stage Designer / Bard Dots tests' -ForegroundColor Magenta
dotnet run --project .\VisualStageDesignerSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Visual dot-to-world stage editor tests FAILED.' }
& (Join-Path $PSScriptRoot 'Run-v0.30-Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Existing Stage Formation + Concert Director tests FAILED.' }
Write-Host 'Stage diagram geometry + existing ensemble regression tests passed.' -ForegroundColor Green
Write-Host 'Game movement and interactive ImGui drawing still require live Dalamud testing.' -ForegroundColor Yellow
