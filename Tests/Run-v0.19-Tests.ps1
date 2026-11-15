$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.19 - conductor-controlled tracks and STOP+unequip' -ForegroundColor Magenta

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 10 SDK to run these tests and build Windbound.'
}
$tests = @(
    'NativePartyTracksAndStopTests.csproj',
    'NativeLeaderSyncSmokeTest.csproj',
    'GameEnsembleStartGateTests.csproj',
    'PartyChatSenderMatcherTests.csproj',
    'PartyInstrumentPlanSmokeTest.csproj'
)
foreach ($test in $tests) {
    dotnet run --project (Join-Path $PSScriptRoot $test) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Regression failed: $test" }
}
Write-Host 'PASS: track map protocol, native start gate, party sender verification and instrument preparation.' -ForegroundColor Green
Write-Host 'A real multi-client FFXIV test is still necessary for native Performance close.' -ForegroundColor Yellow
