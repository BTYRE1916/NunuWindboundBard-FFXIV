#!/usr/bin/env python3
"""Static source integration checks (not a substitute for dotnet build)."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[1] / 'src' / 'NunuWindboundBard'
text = (root / 'Core' / 'GamePartyChatSync.cs').read_text()
helper = (root / 'Core' / 'PartyChatSenderMatcher.cs').read_text()
ui = (root / 'MainWindow.NativeLeaderSync.cs').read_text()
plugin = (root / 'Plugin.cs').read_text()
checks = {
    'World-linked chat identity': 'PlayerPayload' in text and 'playerLink.World.RowId' in text,
    'World suffix fallback': 'character + "@" + world' in helper,
    'Unique FFXIV party membership required': 'matching.Length != 1' in helper,
    'Actual party leader enforced': 'senderId != leader.ContentId' in text,
    'All commands retained as data': 'pending.Enqueue(new PartyCueEnvelope(cue, senderId))' in text,
    'Receiver even when hidden by another plugin': '!ReceiveEnabled' in text and 'message.IsHandled' not in text,
    'Instrument native command dispatched by follower': 'ReceiveNativeInstruments(cue);' in (root/'MainWindow.GameEnsemble.cs').read_text(),
    'Framework processes native commands': 'main.PollGamePartyChat();' in plugin and 'main.PollNativeConductorSync();' in plugin,
    'Follower copy diagnostics': 'Copy party-command report' in ui,
    'No alt-tab keystroke changes': 'SendInput' not in text,
    'Version bump': '<Version>0.18.2.0</Version>' in (root/'NunuWindboundBard.csproj').read_text(),
}
for name, yes in checks.items():
    print(('PASS' if yes else 'FAIL') + ': ' + name)
assert all(checks.values())
ET.parse(root/'NunuWindboundBard.csproj')
ET.parse(root.parents[1]/'Tests'/'PartyChatSenderMatcherTests.csproj')
print('PASS: project XML and all source invariants')
