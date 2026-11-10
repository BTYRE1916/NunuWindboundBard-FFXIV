# v0.37.2 source and (when .NET is available) portrait cache checks.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
python (Join-Path $PSScriptRoot 'Check-v0.37.2-Band-Portraits-Tab.py')
if ($LASTEXITCODE -ne 0) { throw 'Band Portraits source checks failed.' }
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet run --project (Join-Path $PSScriptRoot 'MemberPortraitFilesSmokeTest.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Portrait cache smoke test failed.' }
} else {
    Write-Host 'Skipped .NET portrait-cache test: dotnet is not installed.'
}
Write-Host 'Source checks complete. Visual Studio + FFXIV still required for the plugin/game test.'
