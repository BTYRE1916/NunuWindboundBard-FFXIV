$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.18.2 - party sender identity and ensemble sync regression' -ForegroundColor Magenta

dotnet run --project PartyChatSenderMatcherTests.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Party sender identity tests failed.' }
dotnet run --project NativeLeaderSyncSmokeTest.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Conductor protocol tests failed.' }
dotnet run --project GameEnsembleStartGateTests.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Native START regression tests failed.' }

Write-Host 'PASS: identity matcher and existing native ensemble protocols.' -ForegroundColor Green
Write-Host 'You must still rebuild the Dalamud plugin and test with the party in-game.' -ForegroundColor Yellow
