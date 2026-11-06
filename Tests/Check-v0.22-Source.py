from pathlib import Path
import xml.etree.ElementTree as ET
import json
import re

root = Path(__file__).resolve().parents[1]
source = root / 'src/NunuWindboundBard'
checks = {
    'reader captures Program Change': 'if (type == 0xC0)' in (source/'Core/MidiFileReader.cs').read_text(),
    'reader captures FF04 name': 'meta == 0x04' in (source/'Core/MidiFileReader.cs').read_text(),
    'reader tracks per-channel MIDI metadata': 'LaneInstruments = laneInstruments' in (source/'Core/MidiFileReader.cs').read_text(),
    'per-lane octave tag parser': 'TryGetLaneOctave' in (source/'Core/MidiTrackTags.cs').read_text(),
    'preserve manual octave edits': 'MarkOctaveManual(lane.Key)' in (source/'MainWindow.cs').read_text(),
    'apply file octave on MIDI load': 'ApplyInitialOctaveShifts(candidate, full)' in (source/'MainWindow.cs').read_text(),
    'infer instrument by assigned party parts': 'MidiInstrumentInference.ForParty(song, partyAssignments)' in (source/'MainWindow.MidiMetadata.cs').read_text(),
    'leader instrument SET uses inference': 'var choices = GetPartyInstrumentChoices();' in (source/'MainWindow.NativeLeaderSync.cs').read_text(),
    'manual instruments saved independently': 'cfg.PartyInstrumentsByFile[loaded] = chosen = new(' in (source/'MainWindow.PartyInstruments.cs').read_text(),
    'version and migration': '<Version>0.22.2.0</Version>' in (source/'NunuWindboundBard.csproj').read_text() and 'Config.Version < 23' in (source/'Plugin.cs').read_text(),
    'auto-inference default on': 'AutoDetectMidiTrackInstruments { get; set; } = true' in (source/'Configuration.cs').read_text(),
    'auto-octave default on': 'AutoReadMidiTrackOctaves { get; set; } = true' in (source/'Configuration.cs').read_text(),
    'sample file present': (root/'Samples/Windbound-MIDI-Instrument-and-Octave-Tags.mid').exists(),
}
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ': ' + name)
assert all(checks.values())
for f in list(root.rglob('*.csproj')):
    ET.parse(f)
translation = json.loads((root/'Assets/i18n/fr-FR.json').read_text())
assert translation['Read #+12 / #-12 octave tags on MIDI tracks'].startswith('Lire')
loc = (source/'Localization.cs').read_text()
for key in ['MIDI INSTRUMENTS & OCTAVE TAGS — automatic detection', 'Read #+12 / #-12 octave tags on MIDI tracks']:
    assert f'[{json.dumps(key, ensure_ascii=False)}]' in loc
print('PASS: projects XML and French translation.')
