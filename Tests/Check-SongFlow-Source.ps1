# Source-only regression checks. The C# test project is the executable behavioural check.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$window = Get-Content (Join-Path $root 'src\NunuWindboundBard\MainWindow.cs') -Raw
$plugin = Get-Content (Join-Path $root 'src\NunuWindboundBard\Plugin.cs') -Raw
$player = Get-Content (Join-Path $root 'src\NunuWindboundBard\Core\PlaybackEngine.cs') -Raw
foreach ($needle in @('public void PollSongTransitions()', 'songFlow.QueueSelection(', 'songFlow.QueueNext()', 'songFlow.MarkStarted()', 'songFlow.Cancel()', 'AdvancePlaylist()', 'Instrument preparation timed out', 'private bool Load(')) {
    if (!$window.Contains($needle)) { throw "Missing transition feature: $needle" }
}
if (!$player.Contains('public bool IsStopPending')) { throw 'Stop-pending handshake missing.' }
if (!$plugin.Contains('main.PollSongTransitions();')) { throw 'Missing framework poll.' }
if ($plugin.IndexOf('main.PollSongTransitions();') -lt $plugin.IndexOf('Player.Update(performing && expandedKeyboard);')) { throw 'Poll must follow Player.Update.' }
if ($window.Contains('private bool pendingNext;') -or $window.Contains('private bool wasPlaying;')) { throw 'Legacy UI-only Auto Next is still present.' }
Write-Host 'PASS: Auto Next independent of ImGui Draw, queued stop/selection, and bounded instrument wait.' -ForegroundColor Green
