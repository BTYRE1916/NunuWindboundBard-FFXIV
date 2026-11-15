$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$files = @{
  'Core/GameEnsembleSync.cs' = @('NetworkEnsembleStart', 'OnNativeStart', 'TryConsumeStart', 'PerformanceMetronome')
  'Core/GamePartyCueProtocol.cs' = @('GetHash', 'SHA256.HashData', 'TryParse', 'GamePartyCueKind.Stop')
  'Core/GamePartyChatSync.cs' = @('XivChatType.Party', 'PartyLeaderIndex', 'ProcessChatBoxEntry')
  'Plugin.cs' = @('main.PollGameEnsemble()', 'main.PollGamePartyChat()', 'GameEnsemble.Dispose()', 'PartyChat.Dispose()')
  'MainWindow.GameEnsemble.cs' = @('Use FFXIV Ensemble Sync', 'PollGameEnsemble()', 'PollGamePartyChat()', 'StartSongCore(decision.StartQpc)', 'GetGameEnsembleReadiness', 'gameStartGate.Capture')
}
foreach ($name in $files.Keys) {
  $path = Join-Path $root "src\NunuWindboundBard\$name"
  if (-not (Test-Path $path)) { throw "Missing $name" }
  $contents = Get-Content -Raw $path
  foreach ($s in $files[$name]) {
    if (-not $contents.Contains($s)) { throw "Missing $s from $name" }
  }
}
$source = Get-Content -Raw (Join-Path $root 'src\NunuWindboundBard\MainWindow.cs')
if (-not $source.Contains('if (cfg.GameEnsembleEnabled)')) { throw 'Game Ensemble mode not routed into cue branch.' }
if (-not $source.Contains('if (cfg.GameEnsembleEnabled || cfg.MultiDeviceEnabled')) { throw 'Legacy ensemble modes are not isolated.' }
Write-Host 'PASS: Game Ensemble source wiring is in place (v0.12.1).' -ForegroundColor Green
Write-Host 'Note: live FFXIV hook and Ready Check behaviour require a real game test.' -ForegroundColor Yellow
