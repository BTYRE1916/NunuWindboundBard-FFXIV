from pathlib import Path
import xml.etree.ElementTree as ET
r=Path(__file__).resolve().parent.parent
s=r/'src/NunuWindboundBard'
def has(filename,*phrases):
 text=(s/filename).read_text(encoding='utf8')
 for phrase in phrases:
  assert phrase in text, f'Missing {phrase!r} in {filename}'
 print('PASS',filename)

has('Core/GamePartyCueProtocol.cs','NativeSetTracks','NativeTracksReady','NativeTracksFailed','NativeStopAndUnequip','if (p.Length == 6 && p[1].Equals("tracks"','if (p.Length == 3 && p[1].Equals("stow"')
has('Core/GamePartyChatSync.cs','GamePartyCueKind.NativeSetTracks','GamePartyCueKind.NativeStopUnequip','GamePartyCueKind.NativeTracksReady','GamePartyCueKind.NativeTracksFailed')
has('Core/OneClickEnsembleCue.cs','bool explicitMap = false','if (assignments.Count == 0 && !explicitMap)')
has('MainWindow.NativeLeaderSync.cs','SetNativePartyTracks','ReceiveNativeTracks','ReceiveNativeStopAndUnequip',
    'NativeConductorStopAndUnequip','NativeLeaderProgress(nativeLeaderTracks, "Tracks")',
    'GamePartyCueProtocol.NativeTracksReady','plugin.Instruments.RequestUnequip()',
    'plugin.Player.IsStopPending','nativeConductorTrackMapExplicit = true')
has('MainWindow.GameEnsemble.cs','GamePartyCueKind.NativeSetTracks','GamePartyCueKind.NativeStopUnequip',
    'GamePartyCueKind.NativeTracksReady','GamePartyCueKind.NativeTracksFailed')
has('MainWindow.cs','SET PARTY TRACKS — send to every Bard','nativeConductorTrackMapExplicit',
    'NativeConductorStopAndUnequip();','StopPlayback()')
has('Core/InstrumentSwitcher.cs','public void RequestUnequip()','case Phase.RequestUnequip:','case Phase.WaitForUnequip:',
    'TryClosePerformanceAddon(out exitRoute)','CurrentId == 0')
has('Configuration.cs','public int Version { get; set; } = 20;','public bool NativeEnsembleAutoAccept { get; set; } = true;')
has('Plugin.cs','if (Config.Version < 20)','Windbound Bard v0.19 loaded')
ET.parse(s/'NunuWindboundBard.csproj')
assert '<Version>0.19.0.0</Version>' in (s/'NunuWindboundBard.csproj').read_text()
ET.parse(r/'Tests/NativePartyTracksAndStopTests.csproj')
print('PASS: native leader file/track/instrument sync, STOP/unequip, default Auto-Accept, project XML')
