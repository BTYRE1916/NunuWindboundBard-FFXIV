"""Offline source wiring safety checks for v0.40.2. Not a substitute for a C# compile or OBS runtime test."""
from pathlib import Path
import re
root = Path(__file__).resolve().parents[1]
cs = root / 'src' / 'NunuWindboundBard'

def read(p): return (cs / p).read_text(encoding='utf-8-sig')

def check(predicate, name):
    assert predicate, name
    print('PASS:', name)

client = read('Core/ObsRecordingClient.cs')
ui = read('MainWindow.ConcertCapture.cs')
hud = read('PerformanceHudWindow.cs')
transport = read('Core/ObsRecordTransport.cs')
check((cs / 'Core/ObsRecordTransport.cs').is_file(), 'shared recording transport helper present')
check(ui.count('DrawObsRecordingTransport(obs, state, pink, amber, green);') == 1,
      'always-visible transport called once')
for name in ['START RECORDING  [REC]', 'PAUSE  [||]', 'PLAY / RESUME  [>]', 'STOP & SAVE...']:
    check(ui.count(name) == 1, f'Concert Capture always-visible {name} control')
for name in ['REC##hudObsRecord', 'PAUSE##hudObsPause', 'PLAY / RESUME##hudObsResume']:
    check(hud.count(name) == 1, f'HUD has {name} control')
for m in ['CanStart', 'CanPause', 'CanResume', 'CanStop']:
    check(f'ObsRecordTransport.{m}(state)' in ui and f'ObsRecordTransport.{m}(capture)' in hud,
          f'{m} guard shared by HUD and main window') if m != 'CanStop' else check(f'ObsRecordTransport.{m}(state)' in ui, 'Stop guard main window')
check('ImGui.BeginDisabled' in ui and 'ImGui.BeginDisabled' in hud,
      'unavailable controls are visibly disabled')
check('SnapshotAfterAcceptedCommand(s, requestType)' in client,
      'acknowledged OBS commands update snapshot without waiting for poll')
check('"PauseRecord" when state.Recording => state with { Paused = true }' in client and
      '"ResumeRecord" when state.Recording => state with { Paused = false }' in client,
      'recording pause and resume states stay distinct')
rec = client.index('var rec = await RequestAsync(ws, "GetRecordStatus", cancel)')
record_update = client.index('Update(s => s with {\n            Recording = active,', rec)
replay = client.index('GetReplayBufferStatus',rec)
check(rec < record_update < replay, 'recording status updated before optional replay buffer check')
check('catch (ObsRequestException) { /* Replay buffer may be disabled in OBS. */ }' in client,
      'optional replay buffer error does not block recording controls')
check('obs.Command("StopRecord")' in ui and 'obsConfirmRecordStop' in ui,
      'stop recording retains confirmation requirement')
check('ImGui.SmallButton("Capture settings##hudObsOpen")' in hud,
      'HUD always provides access to full capture settings')
check('new Uri($"ws://127.0.0.1:{port}")' in client and 'StartStream' in client,
      'OBS WebSocket remains localhost-only and streaming separate')
check('<Version>0.40.2.0</Version>' in read('NunuWindboundBard.csproj') and
      '"AssemblyVersion": "0.40.2.0"' in (root / 'Plugin.json').read_text(),
      'project version and manifest updated')
check((root / 'Tests/ObsRecorderTransportSmokeTest.csproj').is_file(), 'C# smoke tests provided')
print('ALL SOURCE TRANSPORT CHECKS PASSED (still requires Windows compile and OBS verification).')
