# Local developer sanity tests, no contact with the game or any repository.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Write-Host 'Source checks:'
python (Join-Path $PSScriptRoot 'Check-v0.38-PianoAcademy-Showmaster.py')
if ($LASTEXITCODE -ne 0) { throw 'Source checks failed' }
Write-Host 'Pure core C# smoke test:'
dotnet run --project (Join-Path $PSScriptRoot 'PianoAcademyShowmasterSmokeTest.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Core smoke test failed' }
Write-Host 'PASS — v0.38 offline tests. Build the plugin separately with Dalamud.NET.Sdk.'
