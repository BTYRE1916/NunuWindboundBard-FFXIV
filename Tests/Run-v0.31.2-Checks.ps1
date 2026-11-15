$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
Write-Host 'Windbound v0.31.2 C# definite-assignment source checks' -ForegroundColor Magenta
py (Join-Path $PSScriptRoot 'Check-v0.31.2-CSharp-Definite-Assignment.py')
if ($LASTEXITCODE -ne 0) { throw 'Compiler-hotfix source check failed.' }
py (Join-Path $PSScriptRoot 'Check-v0.31.1-Playback-Recovery.py')
if ($LASTEXITCODE -ne 0) { throw 'Playback recovery source regression check failed.' }
Write-Host 'Source checks passed. Run dotnet build on Windows to verify actual compilation.' -ForegroundColor Green
