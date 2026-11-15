param([switch]$Build)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    if (Get-Command py -ErrorAction SilentlyContinue) {
        & py -3 (Join-Path $PSScriptRoot 'Check-v0.39-Ui-Overhaul.py')
    } elseif (Get-Command python -ErrorAction SilentlyContinue) {
        & python (Join-Path $PSScriptRoot 'Check-v0.39-Ui-Overhaul.py')
    } else { throw 'Python 3 is required for the offline source checks.' }
    if ($LASTEXITCODE -ne 0) { throw 'v0.39 source tests failed.' }
    if ($Build) {
        if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK not installed or not on PATH.' }
        & dotnet build '.\src\NunuWindboundBard\NunuWindboundBard.csproj' -c Release -p:Platform=x64
        if ($LASTEXITCODE -ne 0) { throw 'Dalamud plugin build failed. See compiler diagnostics above.' }
    } else {
        Write-Host 'Source-only checks finished. Use -Build to compile using your local Windows/.NET/Dalamud SDK.'
    }
} finally { Pop-Location }
