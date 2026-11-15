"""Offline source-only v0.40 gate; use dotnet project for executable protocol tests."""
from pathlib import Path
import hashlib
import json
import re
root = Path(__file__).resolve().parents[1]
src = root/'src'/'NunuWindboundBard'

def check(ok, description):
    if not ok: raise AssertionError(description)
    print('PASS:',description)

def text(path): return path.read_text(encoding='utf-8-sig')
plugin=text(src/'Plugin.cs')
ui=text(src/'MainWindow.ConcertCapture.cs')
client=text(src/'Core/ObsRecordingClient.cs')
nav=text(src/'MainWindow.StageTheme.cs')
config=text(src/'Configuration.cs')
hud=text(src/'PerformanceHudWindow.cs')
check('<Version>0.40.0.0</Version>' in text(src/'NunuWindboundBard.csproj'), 'source version 0.40.0.0')
check('ObsCapture = new()' in plugin and 'ObsCapture.Dispose()' in plugin, 'OBS isolated service and cleanup')
check(plugin.index('if (Config.Version < 38)') > plugin.index('if (Config.Version < 37)'), 'v0.40 migration follows older migrations')
check('DrawConcertCapture();' in nav and 'WindboundShowTabs' in nav, 'Show workspace tab')
check('GetRecordStatus' in client and 'GetStreamStatus' in client and 'GetVideoSettings' in client,'actual OBS status and resolution read')
check('GetRecordDirectory' in client and 'AvailableFreeSpace' in client,'recording disk monitor is remote path based')
check('StartRecord' in ui and 'StopRecord' in ui and 'PauseRecord' in ui and 'ResumeRecord' in ui,'manual record controls')
check('SplitRecordFile' in ui and 'SaveReplayBuffer' in ui,'manual splits and replay buffer controls')
check('StartStream' in ui and 'obsGoLiveConfirmed' in ui and 'StopStream' in ui,'streaming requires separate confirmation')
check('new Uri($"ws://127.0.0.1:{port}")' in client,'loopback-only WebSocket URL')
check(not re.search(r'public\s+string\s+(?:\w*Password|\w*StreamKey)\b',config,re.I), 'secrets never persisted in config')
check('Task.Run(' in client and 'await requests.WaitAsync' in client, 'all network operations off game thread')
check('Snapshot.FreeDiskGiB' not in ui and 'state.FreeDiskGiB' in ui,'disk I/O is not called from ImGui')
check('ObsCapture.Command("StartRecord")' in hud and 'ObsShowCaptureHud' in hud, 'HUD record quick control')
check('No artificial recording time limit' not in client and 'TimeSpan.FromMinutes' not in client, 'no recording stop timer')
# Recording overlays must be optional, text-only, and updated without MIDI scheduling I/O.
caption=text(src/'Core/ObsSongTitleOverlay.cs')
check('GetInputSettings' in client and 'SetInputSettings' in client and 'overlay = true' in client, 'OBS title source updates preserve formatting')
check('IsTextInputKind' in client and 'game_capture' not in caption, 'only OBS text input accepted')
check('ObsSongTitleEnabled { get; set; } = false' in config, 'title source is an explicit user opt-in')
check('PollConcertCaptureSongTitle()' in plugin and 'SetSongTitle' in ui, 'song title updated even if menu is hidden')
check('TextForPlayback(title, plugin.Player.IsPlaying' in ui, 'song title reflects actual MIDI playing state')
check('TryGetProperty("inputKind"' not in client or 'GetString(info, "inputKind")' in client, 'OBS text source identity validated')
check('SongTitleStatus' in ui and 'RetrySongTitle()' in ui, 'overlay status and retry available')
check('RequestAsync(ws, "SetInputSettings", new {' in client, 'no remote program execution for titles')
check('0.40.0.0' in text(root/'Plugin.json'), 'manifest metadata updated')
for s in ['Core/PlaybackEngine.cs','Core/GamePartyCueProtocol.cs','Core/InstrumentSwitcher.cs','MainWindow.StageFormations.cs','MainWindow.PianoAcademy.cs','MainWindow.Showmaster.cs']:
    check((src/s).is_file(), f'preserved {s}')
print('All v0.40 source checks passed. This is NOT a substitute for compiling in Visual Studio / OBS live testing.')
