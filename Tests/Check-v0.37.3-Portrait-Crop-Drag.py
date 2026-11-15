"""Source-level v0.37.3 drag-crop regression checks. No game client is required."""
from pathlib import Path
import re, json
root = Path(__file__).resolve().parents[1]
portrait = (root/'src/NunuWindboundBard/MainWindow.MemberPortraits.cs').read_text(encoding='utf-8-sig')
main = (root/'src/NunuWindboundBard/MainWindow.cs').read_text(encoding='utf-8-sig')
config = (root/'src/NunuWindboundBard/Configuration.cs').read_text(encoding='utf-8-sig')
switcher = (root/'src/NunuWindboundBard/Core/InstrumentSwitcher.cs').read_text(encoding='utf-8-sig')
proto = (root/'src/NunuWindboundBard/Core/GamePartyCueProtocol.cs').read_text(encoding='utf-8-sig')

# The image must be an active hit target, not just a painted ImGui.Image element.
assert portrait.count('ImGui.InvisibleButton("##WindboundPortraitCropSurface", size)') == 1
assert 'ImGui.Image(screenshot.Handle, size)' not in portrait
assert 'cropDraw.AddImage(screenshot.Handle, imgMin, imgMax);' in portrait
assert 'Vector2 imgMin = ImGui.GetItemRectMin();' in portrait
assert 'bool hovered = ImGui.IsItemHovered();' in portrait
assert 'bool active = ImGui.IsItemActive();' in portrait
assert 'hovered && active && ImGui.IsMouseClicked(ImGuiMouseButton.Left)' in portrait
assert 'portraitDragging && active && ImGui.IsMouseDown(ImGuiMouseButton.Left)' in portrait
assert 'portraitDragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left)' in portrait
assert 'SetSquarePortraitCrop(portraitDragStart, end, imgMin, size)' in portrait
assert 'cropDraw.AddLine' in portrait
assert 'portraitDragging = false;' in portrait[portrait.index('private void CloseMemberPortraitSnapshot'):]
# Still exclusively saves the crop, not the full screen.
assert 'SaveMemberPortraitAsync(screenshot, portraitUvMin, portraitUvMax, output)' in portrait
assert 'NewWidth = 128' in portrait and 'NewHeight = 128' in portrait
assert 'CreateFromImGuiViewportAsync' in portrait
assert 'MemberPortraitFiles.Resolve' in portrait
# Dedicated tab stays reachable regardless of party and EnsembleRole.
assert main.count('BeginTabItem(Loc.T("Band Portraits"))') == 1
assert main.count('DrawMemberPortraitCaptureWindow();') == 1
assert 'DrawBandPortraitsTab();' in main
assert 'PartyRoster.GetChoices(Plugin.Objects, Plugin.Party)' in portrait
assert 'MemberPortraitFileByContentId' in config
assert 'MemberPortraitSelectable(' in portrait and 'DrawMemberPortraitThumbnail(' in portrait
# Preserve instrument/ensemble/stage systems.
assert 'SendNativeInstrumentAction' in switcher and 'Native Performance' in switcher
assert 'FormationMove' in proto
for file in ['src/NunuWindboundBard/MainWindow.SoloStage.cs', 'src/NunuWindboundBard/MainWindow.StageFormations.cs', 'src/NunuWindboundBard/MainWindow.StageDesigner.cs']:
    assert (root/file).is_file(), 'missing previously shipped stage feature: '+file
assert '<Version>0.37.3.0</Version>' in (root/'src/NunuWindboundBard/NunuWindboundBard.csproj').read_text(encoding='utf-8-sig')
assert json.loads((root/'Plugin.json').read_text(encoding='utf-8-sig'))[0]['AssemblyVersion']=='0.37.3.0'
print('PASS: Interactive portrait crop captures drag instead of moving the ImGui window; previous features preserved.')
