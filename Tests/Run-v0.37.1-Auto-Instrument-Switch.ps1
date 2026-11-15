# Run this from a Windows PowerShell prompt in the project folder.
$ErrorActionPreference = 'Stop'
$test = Join-Path $PSScriptRoot 'Check-v0.37.1-Auto-Instrument-Switch.py'
if (Get-Command py -ErrorAction SilentlyContinue) {
    & py -3 $test
} elseif (Get-Command python -ErrorAction SilentlyContinue) {
    & python $test
} else { throw 'Python 3 is required for source checks.' }
if ($LASTEXITCODE -ne 0) { throw "Instrument switch check failed with code $LASTEXITCODE" }
Write-Host 'Now rebuild NunuWindboundBard.sln (x64 Release) and test inside FFXIV.'
