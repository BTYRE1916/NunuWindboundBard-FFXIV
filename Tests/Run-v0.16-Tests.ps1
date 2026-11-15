$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.16 - Smart Playlist tests' -ForegroundColor Magenta

dotnet run --project SmartPlaylistSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Smart Playlist test failed.' }

Write-Host 'Verifying source references and playlist migration...' -ForegroundColor Cyan
$root = Join-Path $PSScriptRoot '..\src\NunuWindboundBard'
$main = Get-Content (Join-Path $root 'MainWindow.cs') -Raw
$ui = Get-Content (Join-Path $root 'MainWindow.SmartPlaylists.cs') -Raw
$config = Get-Content (Join-Path $root 'Configuration.cs') -Raw
if (-not $main.Contains('DrawSmartPlaylistControls()')) { throw 'Smart list is not hooked into the main player.' }
if (-not $main.Contains('AdvancePlaylist(action.PlayAfterLoad)')) { throw 'Manual/auto transitions are not differentiated.' }
if (-not $ui.Contains('SetDragDropPayload')) { throw 'Missing drag-and-drop support.' }
if (-not $config.Contains('SmartPlaylistsById')) { throw 'Missing per-profile persistent state.' }
Write-Host 'Smart Playlist source checks PASS' -ForegroundColor Green
