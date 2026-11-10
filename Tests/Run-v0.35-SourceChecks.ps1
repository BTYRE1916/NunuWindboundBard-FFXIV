$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
Write-Host 'Windbound Bard v0.35 - source integration checks' -ForegroundColor Magenta
py .\Tests\Check-v0.35-Solo-Stage-and-AutoFacing.py
if ($LASTEXITCODE -ne 0) { throw 'v0.35 source checks failed' }
Write-Host 'Source checks passed. Rebuild in Visual Studio and test live in FFXIV.' -ForegroundColor Green
