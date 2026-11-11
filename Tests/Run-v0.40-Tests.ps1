param([switch]$Build)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    py -3 .\Tests\Check-v0.40-Concert-Capture.py
    if ($LASTEXITCODE -ne 0) { throw 'v0.40 source validation failed.' }
    dotnet run --project .\Tests\ObsCaptureProtocolSmokeTest.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'OBS protocol test failed.' }
    if ($Build) {
        dotnet build .\NunuWindboundBard.sln -c Release -p:Platform=x64
        if ($LASTEXITCODE -ne 0) { throw 'Plugin compilation failed.' }
    }
} finally { Pop-Location }
