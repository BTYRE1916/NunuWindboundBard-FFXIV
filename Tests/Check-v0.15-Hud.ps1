# Safe source-level checks for the new Performance HUD. Actual playback still
# requires a Dalamud/FFXIV test and a Windows .NET 10 build.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $root 'src\NunuWindboundBard'
$plugin = Get-Content (Join-Path $projectDir 'Plugin.cs') -Raw
$main = Get-Content (Join-Path $projectDir 'MainWindow.Hud.cs') -Raw
$hud = Get-Content (Join-Path $projectDir 'PerformanceHudWindow.cs') -Raw
$settings = Get-Content (Join-Path $projectDir 'Configuration.cs') -Raw
$version = Get-Content (Join-Path $projectDir 'NunuWindboundBard.csproj') -Raw

function Assert([bool] $condition, [string] $label) {
    if (-not $condition) { throw "FAIL: $label" }
    Write-Host "PASS: $label" -ForegroundColor Green
}
Assert ($plugin.Contains('windows.AddWindow(hud)')) 'HUD registered with Dalamud WindowSystem'
Assert ($plugin.Contains('/windbound hud')) 'HUD command is available'
Assert ($plugin.Contains('main.PollSongTransitions()')) 'Background song transitions preserved'
Assert ($plugin.Contains('Player.Update(performing && expandedKeyboard)')) 'Original native output remains on Framework.Update'
Assert ($main.Contains('if (HudIsSolo) StartSong();')) 'HUD solo Play uses existing song start'
Assert ($main.Contains('else CueEnsemble();')) 'HUD conductor Play uses existing ensemble cue'
Assert ($main.Contains('if (HudIsFollower)')) 'Follower does not start playback independently'
Assert ($main.Contains('HudSetOutputArmed')) 'ARM is explicitly controlled'
Assert ($hud.Contains('Truncate(playerName')) 'Client/character identity visible'
Assert ($hud.Contains('HudReadyFollowers')) 'Conductor readiness counts visible'
Assert ($hud.Contains('BeginPopupContextItem')) 'Right-click reset on progress bar'
Assert ($settings.Contains('public bool HudVisible')) 'HUD visibility persisted'
Assert ($settings.Contains('HudCompact')) 'Compact mode persisted'
Assert ($version.Contains('0.15.0.0')) 'Plugin version incremented'
Write-Host 'Source-level HUD checks passed. Now build with Build-Plugin.ps1 and test in FFXIV.' -ForegroundColor Cyan
