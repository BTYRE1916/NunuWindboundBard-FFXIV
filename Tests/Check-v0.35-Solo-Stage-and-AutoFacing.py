"""Source-only v0.35 stage integration smoke checks (does not compile the C# plugin).

Run: python Tests/Check-v0.35-Solo-Stage-and-AutoFacing.py
"""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
src = root / 'src' / 'NunuWindboundBard'
read = lambda relative: (src / relative).read_text(encoding='utf-8')
config = read('Configuration.cs')
plugin = read('Plugin.cs')
main = read('MainWindow.cs')
stage = read('MainWindow.StageFormations.cs')
designer = read('MainWindow.StageDesigner.cs')
solo = read('MainWindow.SoloStage.cs')
layout = read('Core/SoloStageLayout.cs')
facing = read('Core/StageFacingController.cs')
nav = read('Core/StageFormationNavigator.cs')
project = read('NunuWindboundBard.csproj')

# New file existence, type definitions and configuration service links.
assert 'class StageFacingController' in facing
assert 'class SoloStageLayout' in layout
assert 'partial class MainWindow' in solo
assert len(re.findall(r'\bstageFacing\s*=\s*new\(', stage)) == 1
assert 'IGameConfig GameConfig' in plugin
assert 'using Dalamud.Plugin.Services;' in plugin
assert 'SoloStageLayout? SoloStage' in config
assert 'AutoFaceAfterStageMovement' in config
assert 'if (Config.Version < 34)' in plugin
assert 'Config.AutoFaceAfterStageMovement = true;' in plugin
assert '<Version>0.35.0.0</Version>' in project
ET.parse(src / 'NunuWindboundBard.csproj')

# Solo mode must be presented in the always-present stage tab, outside all party role checks.
assert 'DrawStageFormations();' in main
assert 'if (ImGui.BeginTabItem(Loc.T("Stage Formations")))' in main
block = stage[stage.index('private void DrawStageFormations()'):]
assert block.index('DrawSoloStage();') < block.index('if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower)')
assert 'WALK TO SOLO SPOT & FACE' in solo
assert 'FACE FORWARD NOW' in solo
assert 'MARK / RE-ANCHOR SOLO STAGE' in solo
assert 'public Vector3 Destination' in layout and 'public float Facing' in layout
assert 'StageFormationMath.Destination(' in layout
assert 'stageNavigator.TryStart(here, layout.Destination' in solo
assert 'stageFacing.Start(layout.Facing)' in solo
assert 'soloStageFacingPending = false;' in solo

# Ensemble leader and follower must get heading, defer ACK until arrival + facing, and clean up.
assert 'stageFollowerTargetHeading = cue.FormationHeading;' in stage
assert 'stageFacing.Start(heading);' in stage
assert 'stageFacing.Start(StageFormationMath.DesiredHeading(leaderSpot' in stage
assert 'else if (stageFacing.Arrived)' in stage
assert 'QueueStageReply(stageMovementNonce, true, "")' in stage
assert 'stageFacing.Poll(local.Position, local.Rotation, safe);' in stage
assert 'PollSoloStage(safe);' in stage
assert stage.count('stageFacing.Cancel();') >= 3
assert 'StopStageMovementBeforeMusic()' in stage
assert 'ALL FACE AUDIENCE' in designer.upper()
assert 'Facing is a visual guide only' not in designer
assert 'FFXIVClientStructs.FFXIV.Client.Game' in facing
assert 'AutoFaceTargetPosition(&lookAt)' in facing
assert 'Plugin.GameConfig.TryGet(' in facing and 'Plugin.GameConfig.Set(' in facing
assert 'stageNavigator.TryStart' in stage and 'vnavmesh' in nav

print('PASS: v0.35 source integration, solo no-party UI, config migration,')
print('      leader/follower facing and safety/stop hooks verified.')
print('NOTE: Source-only. The C# compile and live FFXIV test remain unverified.')
