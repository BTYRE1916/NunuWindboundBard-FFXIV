$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound Bard v0.22: MIDI instrument and octave metadata tests' -ForegroundColor Magenta
dotnet run --project .\MidiTrackMetadataSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'MIDI instrument/octave metadata test failed.' }
dotnet run --project .\NativePartyTracksAndStopTests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Native track/STOP regression failed.' }
dotnet run --project .\LocalizationSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'English/French translation regression failed.' }
Write-Host 'Metadata tests, native track protocol and localisation PASS.' -ForegroundColor Green
