$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound Bard v0.18 — Native Conductor File and Instrument Sync' -ForegroundColor Magenta
if (Get-Command py -ErrorAction SilentlyContinue) {
    & py -3 (Join-Path $PSScriptRoot 'Check-v0.18-NativeLeaderSync.py')
    if ($LASTEXITCODE -ne 0) { throw 'v0.18 source checks failed.' }
} elseif (Get-Command python -ErrorAction SilentlyContinue) {
    & python (Join-Path $PSScriptRoot 'Check-v0.18-NativeLeaderSync.py')
    if ($LASTEXITCODE -ne 0) { throw 'v0.18 source checks failed.' }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Warning 'Install .NET 10 SDK to run C# protocol tests and Build-Plugin.ps1.'
    exit 0
}
$projects = @('NativeLeaderSyncSmokeTest.csproj', 'GameEnsembleStartGateTests.csproj',
              'PartyInstrumentPlanSmokeTest.csproj', 'SmartPlaylistSmokeTest.csproj')
foreach ($test in $projects) {
    dotnet run --project (Join-Path $PSScriptRoot $test) -c Release
    if ($LASTEXITCODE -ne 0) { throw "$test failed." }
}
Write-Host 'PASS: MIDI selection, instrument SET, native start gate, shuffle playlists and protocol.' -ForegroundColor Green
Write-Host 'Test on two real Dalamud/FFXIV clients before publishing the plugin.' -ForegroundColor Yellow
