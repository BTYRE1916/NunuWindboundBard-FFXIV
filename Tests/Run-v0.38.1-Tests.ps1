# Run locally on Windows. Does not connect to FFXIV or GitHub.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
python (Join-Path $PSScriptRoot 'Check-v0.38.1-Emote-Library.py')
if ($LASTEXITCODE -ne 0) { throw 'v0.38.1 source checks failed' }
dotnet run --project (Join-Path $PSScriptRoot 'PianoAcademyShowmasterSmokeTest.csproj')
if ($LASTEXITCODE -ne 0) { throw 'v0.38.1 C# smoke test failed' }
Write-Host 'PASS: Windbound Showmaster v0.38.1 offline checks.'
