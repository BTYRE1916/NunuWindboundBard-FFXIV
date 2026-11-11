#!/usr/bin/env python3
"""Source-only validation for v0.40.1 OBS title overlay; not a replacement for dotnet/FFXIV."""
from pathlib import Path
import hashlib
import json
root=Path(__file__).resolve().parents[1]
src=root/'src/NunuWindboundBard'
def read(path): return path.read_text(encoding='utf-8-sig')
def check(ok,descr):
    if not ok: raise AssertionError(descr)
    print('PASS:',descr)
client=read(src/'Core/ObsRecordingClient.cs')
ui=read(src/'MainWindow.ConcertCapture.cs')
filebridge=read(src/'Core/ObsSongTitleFileBridge.cs')
plugin=read(src/'Plugin.cs')
main=read(src/'MainWindow.cs')
config=read(src/'Configuration.cs')
model=read(src/'Core/ObsSongTitleOverlay.cs')
check('<Version>0.40.1.0</Version>' in read(src/'NunuWindboundBard.csproj'),'v0.40.1 version metadata')
check(json.loads(read(root/'Plugin.json'))[0]['AssemblyVersion']=='0.40.1.0','plugin manifest version')
check('PollConcertCaptureSongTitle();' in plugin and 'SetSongTitle(' in ui,'framework polls the loaded MIDI title outside the UI')
check(client.index('await UpdateSongTitleAsync(') < client.index('await RefreshAsync('),'OBS overlay update precedes optional recorder status polling')
check('catch (ObsRequestException ex)' in client and 'GetReplayBufferStatus' in client,'optional recording status failures are isolated')
check('GetInputList' in client and 'CreateInput' in client and 'GetCurrentProgramScene' in client,'source discovery and explicit CreateInput supported')
check('GetInputKindList' in client and 'text_gdiplus_v2' in client and 'text_gdiplus_v3' in client,'Windows GDI+ input kinds detected before creation')
check('read_from_file' in client and 'ObsTitleFileModeException' in client,'Read from file diagnosed distinctly')
check('"SetInputSettings"' in client and 'overlay = true' in client,'existing OBS font and layout preserved')
check('ObsSongTitleFileBridge' in ui and 'File.WriteAllTextAsync' in filebridge and 'Task.Run(WriteLoopAsync)' in filebridge,'file bridge writes asynchronously')
check('Plugin.PluginInterface.GetPluginConfigDirectory()' in ui and 'Windbound-NowPlaying.txt' in filebridge,'file output stays local to plugin config folder')
check('SHOW TEST TITLE (10 seconds)' in ui and 'TEST TITLE' in ui,'test caption available without an active song')
check('COPY TITLE DIAGNOSTICS' in ui and 'sourceName' in ui,'copyable title diagnostics')
check('Hide title between songs' in ui and 'Local MIDI output:' in ui,'silent-conductor caveat visible')
check('text_ft2_source_v2' in model,'accept OBS FreeType2 v2 input kind')
check('ObsTitleFileModeException' in client and 'OBS text field updated' in client,'OBS UI status distinguishes accepted data from visibility')
check('obsTitleFile.Dispose()' in main,'file bridge disposed on plugin unload')
check('StartRecord' in client and 'StopRecord' in client and 'StartStream' in client,'recording controls retained')
for f in ['Core/PlaybackEngine.cs','Core/InstrumentSwitcher.cs','Core/GamePartyCueProtocol.cs','MainWindow.StageFormations.cs','MainWindow.Showmaster.cs','MainWindow.PianoAcademy.cs']:
    actual=hashlib.sha256((src/f).read_bytes()).hexdigest()
    expected={'Core/PlaybackEngine.cs': '77c6c69dcbb8cd6a7be09565cea91ae372f6fd7139d789458aef024275db007d', 'Core/InstrumentSwitcher.cs': '9c3aac7b1474bcb21b3668718a5d758beb2a35fd79326d3e3ea6e0e1f288c6a3', 'Core/GamePartyCueProtocol.cs': 'b0117ac48e022b8dc7d29daceb5dc96a173202d42e67bd9a6c65eeb3031a9b9e', 'MainWindow.StageFormations.cs': '77934efa70441f49114db331f07b47a12d2ee08992ae8d2182a24a5e9b171873', 'MainWindow.Showmaster.cs': '659e08b19d684d058e5ac5ba8aa4271efbb0faee90f578582d7e135a2e7b6237', 'MainWindow.PianoAcademy.cs': '87b19980416cef0524b57ed3dbcc02d41157e735c3d2a3441ee347b028d7c808'}[f]
    check(actual==expected, f'previous core file unchanged: {f}')
print('All v0.40.1 source checks passed. Verify the DLL with .NET 10 + Dalamud and then OBS live.')
