"""Windbound v0.18 source-level checks; not a substitute for a dotnet build."""
from pathlib import Path
from zipfile import ZipFile
import xml.etree.ElementTree as ET
import sys

root = Path(__file__).resolve().parents[1]
source = root/'src'/'NunuWindboundBard'
project = (source/'NunuWindboundBard.csproj').read_text()
plugin = (source/'Plugin.cs').read_text()
settings = (source/'Configuration.cs').read_text()
ui = (source/'MainWindow.GameEnsemble.cs').read_text()
leader = (source/'MainWindow.NativeLeaderSync.cs').read_text()
main = (source/'MainWindow.cs').read_text()
chat = (source/'Core/GamePartyChatSync.cs').read_text()
protocol = (source/'Core/GamePartyCueProtocol.cs').read_text()
perf = (source/'Core/GameEnsembleSync.cs').read_text()

checks = {
    'version is 0.18.1.0': '<Version>0.18.1.0</Version>' in project,
    'native file parser avoids C# CS0136': 'out int fileIndex' in protocol and
        'GamePartyCueKind.NativeFile, fileIndex' in protocol,
    'configuration migration to v19': 'if (Config.Version < 19)' in plugin and 'Version { get; set; } = 19' in settings,
    'auto accept default on new installs': 'NativeEnsembleAutoAccept { get; set; } = true;' in settings and
        'if (isFirstInstall)' in plugin and 'Config.NativeEnsembleAutoAccept = true;' in plugin,
    'opt-out remains possible': 'cfg.NativeEnsembleAutoAccept = autoAccept;' in ui,
    'native conductor sync default true': 'NativeLeaderSyncEnabled { get; set; } = true;' in settings,
    'leader selection sends after successful load': 'OnNativeConductorSongLoaded();' in main and
        'BroadcastNativeFile()' in leader,
    'conductor instrument SET button': 'SET PARTY INSTRUMENTS' in leader and 'SetNativePartyInstruments();' in leader,
    'follower auto loads exact matching MIDI': 'GamePartyCueProtocol.GetHash(path)' in leader and
        'LoadFromUser(path)' in leader,
    'follower auto switches instrument': 'plugin.Instruments.Request(selected)' in leader,
    'multi client ack and watchdog': 'NativeFileReady' in protocol and 'NativeFileFailed' in protocol and
        'NativeLeaderSession' in leader and '40)' in leader,
    'conductor receives replies': 'Config.EnsembleMode == (int)EnsembleRole.Conductor' in plugin and
        'OnNativeLeaderReply(cue, envelope.SenderContentId)' in ui,
    'native ReadyCheck gated on prepared selections': 'NativeLeaderSyncIsReady(out' in ui and
        'NativeLeaderSyncIsReady(out string reason)' in leader,
    'native START still only FFXIV': 'StartSongCore(decision.StartQpc)' in ui and 'TryConsumeStart' in ui and
        'OnNativeStart' in perf,
    'no party-chat PLAY accidentally restored': 'Ignore obsolete chat-timed PLAY' in ui,
    'ImGui API15 drag-drop CS1503 fixed': 'BitConverter.GetBytes(from), ImGuiCond.Once' in
        (source/'MainWindow.SmartPlaylists.cs').read_text(),
    'all src csproj XML valid': all(ET.parse(f) is not None for f in root.rglob('*.csproj')),
}
for label, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + label)
if not all(checks.values()):
    sys.exit(1)
print(f'PASS: all {len(checks)} v0.18 static source checks. Compile and test on Windows/Dalamud next.')
