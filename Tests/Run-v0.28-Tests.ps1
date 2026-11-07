$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windbound v0.28 - Concert Snapshot & Restore regression tests' -ForegroundColor Magenta

dotnet run --project .\ConcertSnapshotSmokeTest.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Concert Snapshot JSON regression test failed.' }

$src = Join-Path $PSScriptRoot '..\src\NunuWindboundBard'
$main = Get-Content (Join-Path $src 'MainWindow.cs') -Raw
$restore = Get-Content (Join-Path $src 'MainWindow.ConcertSnapshots.cs') -Raw
$config = Get-Content (Join-Path $src 'Configuration.cs') -Raw
$plugin = Get-Content (Join-Path $src 'Plugin.cs') -Raw
if (-not $main.Contains('DrawConcertSnapshots()')) { throw 'Concert Snapshot UI not registered.' }
if (-not $main.Contains('if (!suppressSnapshotSync) OnNativeConductorSongLoaded()')) { throw 'Restore may broadcast conductor file messages.' }
if (-not $restore.Contains('outputArmed = false')) { throw 'Restore must always disarm output.' }
if (-not $restore.Contains('SavedPlaylistLibrary.NewProfile')) { throw 'Restore must create a new playlist.' }
if (-not $restore.Contains('CaptureConcertSnapshot("Before restore')) { throw 'Restore safety backup missing.' }
if (-not $config.Contains('ConcertSnapshots')) { throw 'Snapshot persistence missing.' }
if (-not $plugin.Contains('if (Config.Version < 30)')) { throw 'Configuration migration missing.' }
Write-Host 'PASS: migration, locally scoped restore, source integration.' -ForegroundColor Green
Write-Host 'Real Windows/Dalamud compilation and in-game testing still required.' -ForegroundColor Yellow
