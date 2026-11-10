"""v0.37.2 deterministic source regression check. Run with Python 3."""
from pathlib import Path
import re
root = Path(__file__).resolve().parents[1]
main = (root/'src/NunuWindboundBard/MainWindow.cs').read_text(encoding='utf-8-sig')
portrait = (root/'src/NunuWindboundBard/MainWindow.MemberPortraits.cs').read_text(encoding='utf-8-sig')
config = (root/'src/NunuWindboundBard/Configuration.cs').read_text(encoding='utf-8-sig')
assert main.count('BeginTabItem(Loc.T("Band Portraits"))') == 1, 'missing or duplicate Band Portraits tab'
assert main.index('BeginTabItem(Loc.T("Band Portraits"))') < main.index('BeginTabItem(Loc.T("Ensemble"))'), 'tab must be easy to discover'
assert main.count('DrawMemberPortraitCaptureWindow();') == 1, 'editor needs exactly one main-window draw call'
assert 'DrawBandPortraitsTab();' in main, 'new tab not wired'
assign = main[main.index('private void DrawPartyAssignmentControls('):main.index('    // Poll on Dalamud', main.index('private void DrawPartyAssignmentControls('))]
assert 'if (role == EnsembleRole.Solo) return;' in assign, 'track-assignment behavior unexpectedly changed'
assert 'DrawMemberPortraitManager(choices);' not in assign, 'old role-gated manager must not remain'
assert 'DrawMemberPortraitThumbnail(' in assign, 'track thumbnails disappeared'
new_tab = portrait[portrait.index('private void DrawBandPortraitsTab()'):portrait.index('private void BeginMemberPortraitCapture(')]
assert 'EnsembleRole' not in new_tab, 'new tab must be role-independent'
assert 'PartyRoster.GetChoices(Plugin.Objects, Plugin.Party)' in new_tab, 'solo and party roster path lost'
assert 'DrawMemberPortraitManager(roster);' in new_tab, 'portrait list not reached'
assert 'ImGui.TreeNode' not in new_tab, 'new page should not hide list inside collapsible tree'
assert 'CAPTURE##WBPortrait' in new_tab and 'Remove##WBPortrait' in new_tab
assert 'TryGetMemberPortrait' in portrait and 'SaveMemberPortraitAsync' in portrait, 'portrait feature replaced'
assert 'MemberPortraitFileByContentId' in config, 'existing portrait cache lost'
roster = (root/'src/NunuWindboundBard/Core/PartyRoster.cs').read_text(encoding='utf-8-sig')
assert 'if (localId != 0 && choices.All(x => x.ContentId != localId))' in roster, 'no local-player fallback for Solo'
assert '0.37.2.0' in (root/'src/NunuWindboundBard/NunuWindboundBard.csproj').read_text(encoding='utf-8-sig')
assert '0.37.2.0' in (root/'Plugin.json').read_text(encoding='utf-8-sig')
# Existing v0.37.1 auto-switch fix and v0.35 stages preserved.
switch = (root/'src/NunuWindboundBard/Core/InstrumentSwitcher.cs').read_text(encoding='utf-8-sig')
assert 'Native Performance' in switch and 'SendNativeInstrumentAction' in switch
assert (root/'src/NunuWindboundBard/MainWindow.StageFormations.cs').exists()
assert (root/'src/NunuWindboundBard/MainWindow.SoloStage.cs').exists()
print('PASS: Band Portraits tab, Solo availability, editor lifetime, thumbnails, version and existing feature guards.')
