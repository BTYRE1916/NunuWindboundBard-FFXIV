"""Windbound v0.39 offline regression checks. Requires Python 3.10+ only.
This is NOT a .NET compile, UI rendering test or live Dalamud test.
"""
from pathlib import Path
from hashlib import sha256
import json
import re
import sys

root = Path(__file__).resolve().parent.parent
src = root / 'src' / 'NunuWindboundBard'
read = lambda name: (src / name).read_text(encoding='utf-8-sig')
main = read('MainWindow.cs')
nav = read('MainWindow.StageTheme.cs')
style = read('WindboundStageStyle.cs')
cfg = read('Configuration.cs')
plugin = read('Plugin.cs')
academy = read('MainWindow.PianoAcademy.cs')
show = read('MainWindow.Showmaster.cs')
hud = read('PerformanceHudWindow.cs')

checks = 0
def check(condition, message):
    global checks
    assert condition, message
    checks += 1

check('<Version>0.39.0.0</Version>' in read('NunuWindboundBard.csproj'), 'version mismatch')
manifest = json.loads((root / 'Plugin.json').read_text(encoding='utf-8-sig'))
check(manifest[0]['AssemblyVersion'] == '0.39.0.0', 'manifest version mismatch')
check('public int Version { get; set; } = 37' in cfg, 'migration config version')
for opt in ('DarkStageThemeEnabled', 'StageUiMotionEnabled', 'StageUiAccent'):
    check(opt in cfg, f'missing {opt} user setting')
    check('Config.' + opt in plugin, f'{opt} migration missing')

check('WindboundStageStyle.Push(cfg.StageUiAccent)' in main, 'main theme not active')
check('finally' in main[main.index('    public override void Draw()'): main.index('    private void SetLanguage')], 'main theme cleanup missing')
check('DrawMemberPortraitCaptureWindow();' in main, 'portrait editor missing')
check('WindboundStageStyle.Push(cfg.StageUiAccent)' in hud, 'HUD theme not active')
check('finally { if (styled) WindboundStageStyle.Pop(); }' in hud, 'HUD scope not cleaned up')
check('WindboundStageStyle.Push(cfg.StageUiAccent)' in academy, 'Academy floating view not themed')
check('finally { if (styled) WindboundStageStyle.Pop(); }' in academy, 'Academy theme scope missing')
check('WindboundStageStyle.Chip' in show and 'WindboundStageStyle.Chip' in academy,
      'status badges missing from showcase modules')

for tab in ('PERFORMANCE', 'BAND', 'STUDIO', 'SHOW', 'TOOLS', 'SETUP'):
    check(nav.count(f'ImGui.BeginTabItem(Loc.T("{tab}"))') == 1, f'category {tab} missing / duplicated')
for section in ('Songs & Playback', 'Band Portraits', 'Piano Academy', 'Showmaster',
                'Audio Preview', 'Ensemble', 'Stage Formations', 'FFXIV Optimiser',
                'Concert Snapshots', 'MIDI Inspector', 'Soulweaver Studio', 'Instruments',
                'Live MIDI Piano', 'Guitar Tones', 'Direct Output & Test', 'About / Setup'):
    # The SHOW category embeds Showmaster directly, not a nested tab.
    if section == 'Showmaster':
        check('DrawShowmaster();' in nav, f'{section} renderer missing')
    else:
        check(nav.count(f'ImGui.BeginTabItem(Loc.T("{section}"))') == 1,
              f'{section} missing / duplicated')

# Important: no UI change is permitted to call PlaybackEngine.Update, party
# START or ARM. The complete original core library is retained in this archive.
check('DrawPianoAcademy();' in nav and 'DrawShowmaster();' in nav, 'tutorial/showcase route missing')
check('DrawBandPortraitsTab();' in nav and nav.index('DrawBandPortraitsTab();') < nav.index('DrawEnsemble();'),
      'portrait tab not easy to find')
check('DrawStageAppearanceSettings();' in nav, 'appearance settings unreachable')
check('cfg.StageUiMotionEnabled' in nav, 'reduced motion missing')
check('ImGui.PopStyleVar(VarCount)' in style and 'ImGui.PopStyleColor(ColorCount)' in style,
      'unpaired style scope')
style_push = style.split('    public static void Push(int accentPreference)', 1)[1].split('    public static void Pop()', 1)[0]
check(style_push.count('ImGui.PushStyleColor(') == int(re.search(r'ColorCount = (\d+)', style).group(1)),
      'theme colour push/pop counts mismatch')
check(style_push.count('ImGui.PushStyleVar(') == int(re.search(r'VarCount = (\d+)', style).group(1)),
      'theme variable push/pop counts mismatch')
check('showmaster.Start(' in show and 'ShowmasterStartV2(' in show,
      'v0.38.1 group routine logic missing')
check('academy.Advance(' in academy and 'OnAcademyMidi' in academy,
      'Piano Academy practice engine missing')
check('StartSong()' in main and 'StopPlayback()' in main and 'NextSong()' in main,
      'playback controls no longer routed')
check('RequestUnequip()' in show, 'Showmaster native stow missing')

# Avoid accidental edits to performance, protocol, instrument switching, and
# safety-critical routines relative to the frozen v0.38.1 source archive.
# SHA-256 values were computed on the original package, not after editing.
critical = {
    'Core/PlaybackEngine.cs': '77c6c69dcbb8cd6a7be09565cea91ae372f6fd7139d789458aef024275db007d',
    'Core/GamePartyCueProtocol.cs': 'b0117ac48e022b8dc7d29daceb5dc96a173202d42e67bd9a6c65eeb3031a9b9e',
    'Core/InstrumentSwitcher.cs': '9c3aac7b1474bcb21b3668718a5d758beb2a35fd79326d3e3ea6e0e1f288c6a3',
}
for name, expected_sha in critical.items():
    path = src / name
    check(path.is_file(), f'missing performance-critical source: {name}')
    check(sha256(path.read_bytes()).hexdigest() == expected_sha,
          f'performance-critical source was modified: {name}')

# Lightweight C# scope balancing for changed files, excluding strings/comments.
def check_braces(text, filename):
    state = 'code'; stack = []
    i = 0
    while i < len(text):
        c = text[i]; nextc = text[i+1] if i+1 < len(text) else ''
        if state == 'line':
            if c == '\n': state = 'code'
        elif state == 'block':
            if c == '*' and nextc == '/': state = 'code'; i += 1
        elif state == 'string':
            if c == '\\': i += 1
            elif c == '"': state = 'code'
        elif state == 'verbatim':
            if c == '"' and nextc == '"': i += 1
            elif c == '"': state = 'code'
        elif state == 'char':
            if c == '\\': i += 1
            elif c == "'": state = 'code'
        elif state == 'code':
            if c == '/' and nextc == '/': state='line'; i+=1
            elif c == '/' and nextc == '*': state='block'; i+=1
            elif c == '@' and nextc == '"': state='verbatim'; i+=1
            elif c == '"': state='string'
            elif c == "'": state='char'
            elif c in '({[': stack.append(c)
            elif c in ')}]':
                assert stack and stack.pop() == {')':'(', '}':'{', ']':'['}[c], f'Unbalanced {filename} at character {i}'
        i += 1
    check(not stack and state not in ('string','char','block','verbatim'),
          f'C# brace/string/comment mismatch: {filename}; {stack[-4:]} state={state}')

for f in ('MainWindow.cs','MainWindow.StageTheme.cs','WindboundStageStyle.cs',
          'MainWindow.PianoAcademy.cs','MainWindow.Showmaster.cs',
          'PerformanceHudWindow.cs','Configuration.cs','Plugin.cs'):
    check_braces(read(f), f)

print(f'PASS: Windbound Bard v0.39 Dark Stage source audit ({checks} checks).')
print('NOTE: A real .NET/Dalamud compile and an FFXIV in-game visual test are still required.')
