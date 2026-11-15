$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.23 — persistent performer lineups' -ForegroundColor Magenta
& (Join-Path $PSScriptRoot 'Run-v0.22.2-Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Previously working MIDI/natively synchronised ensemble regression tests failed.' }
dotnet run --project .\PartyTrackLineupSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Reusable octet assignment tests failed.' }
$root = Join-Path $PSScriptRoot '..\src\NunuWindboundBard'
$main = Get-Content (Join-Path $root 'MainWindow.cs') -Raw
$bridge = Get-Content (Join-Path $root 'MainWindow.ReusablePartyLineup.cs') -Raw
$config = Get-Content (Join-Path $root 'Configuration.cs') -Raw
$native = Get-Content (Join-Path $root 'MainWindow.NativeLeaderSync.cs') -Raw
if (-not $main.Contains('LoadPartyAssignmentsForFile();')) { throw 'Missing automatic restore on MIDI selection.' }
if (-not $main.Contains('RememberCurrentPartyLineup();')) { throw 'Missing default-lineup update on manual assignment.' }
if (-not $bridge.Contains('SavedPlaylistLibrary.GetActive(cfg).Id')) { throw 'Missing playlist-scoped lineup.' }
if (-not $config.Contains('KeepPartyAssignmentsAcrossSongs { get; set; } = true;')) { throw 'Default carry-over not enabled.' }
if (-not $native.Contains('OneClickEnsembleCue.EncodeAssignments(lanes.Select')) { throw 'Native conductor parts no longer being transmitted.' }
Write-Host 'PASS: reusable octet lineup, native ensemble transfer, and existing MIDI regression tests.' -ForegroundColor Green
Write-Host 'Compile against the latest Dalamud SDK and test with two game clients before publishing.' -ForegroundColor Yellow
