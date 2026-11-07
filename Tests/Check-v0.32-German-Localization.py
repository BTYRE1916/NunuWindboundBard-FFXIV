"""Source-only (no SDK required) localization coverage and safety checks.

Run: py Tests/Check-v0.32-German-Localization.py
This is NOT a C# compilation or an in-game FFXIV test.
"""
from pathlib import Path
from collections import defaultdict
import json, re, sys, xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
src = root / 'src' / 'NunuWindboundBard'
fr = (src/'Localization.cs').read_text(encoding='utf-8')
de = (src/'Localization.German.cs').read_text(encoding='utf-8')
main = (src/'MainWindow.cs').read_text(encoding='utf-8')
config = (src/'Configuration.cs').read_text(encoding='utf-8')

keys = re.compile(r'^\s*\["((?:[^"\\]|\\.)*)"\]\s*=\s*"((?:[^"\\]|\\.)*)",', re.M)
french = dict(keys.findall(fr))
german = dict(keys.findall(de))
assert len(french) == 942, f'French key count changed: {len(french)}'
assert len(german) == 951, f'German key count: {len(german)}'
assert set(french).issubset(german), 'German translations missing English keys from the French source'
assert all(v.strip() for v in german.values()), 'Empty German strings'
assert all('##' not in v for v in german.values()), 'ImGui IDs must not be translated or recreated'
assert '"de" => "de"' in fr, 'German locale is not selected'
assert 'Deutsch##lang-de' in main, 'Missing German entry in language selector'
assert 'language is "fr" or "de"' in main, 'German locale not persisted'
assert 'GermanVisible' in fr and 'GermanFragments' in fr, 'German translation engine missing'
assert 'Loc.SetLanguage(Config.Language)' in (src/'Plugin.cs').read_text(encoding='utf-8'), 'Saved client locale not restored'
assert '<Version>0.32.0.0</Version>' in (src/'NunuWindboundBard.csproj').read_text(), 'Wrong plugin version'
assert 'public string Language { get; set; } = "en";' in config, 'Existing default English changed'

# Look for single-literal UI calls. Technical octave symbols intentionally stay unchanged.
visible = {k.split('##')[0] for k in german}
unknown = []
for file in src.glob('*.cs'):
    if file.name.startswith('Localization'):
        continue
    for match in re.finditer(r'Loc\.T\(\s*"((?:[^"\\]|\\.)*)"\s*\)', file.read_text(encoding='utf-8')):
        label = match.group(1)
        if label.split('##')[0] not in visible and label not in {'+12##liveTranspose','-12##liveTranspose'}:
            unknown.append((file.name,label))
assert not unknown, f'Untranslated direct UI labels: {unknown[:20]}'

# Check the dictionary-visible values agree, including widget labels that differ only in their hidden ID.
per_visible = defaultdict(set)
for k, v in german.items():
    per_visible[k.split('##')[0]].add(v)
assert all(len(s) == 1 for s in per_visible.values()), 'Same visible English label has conflicting German translations'

# Assembly projects remain valid.
for project in root.rglob('*.csproj'):
    ET.parse(project)

print(f'PASS: {len(french)} inherited strings translated, {len(german)-len(french)} extra strings localized.')
print('PASS: all current single-literal UI labels have a catalogue entry or are symbolic octave values.')
print('PASS: German picker, English default, settings persistence, stable ImGui IDs, project XML.')
print('SOURCE-ONLY VALIDATION: C# build and FFXIV playback still require an installed SDK and client.')
