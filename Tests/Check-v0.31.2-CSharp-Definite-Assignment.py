"""Source regression checks for v0.31.2; this is not a compiler substitute."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
src = root / "src" / "NunuWindboundBard"
stage = (src / "MainWindow.StageFormations.cs").read_text(encoding="utf-8")
protocol = (src / "Core" / "GamePartyCueProtocol.cs").read_text(encoding="utf-8")
project = src / "NunuWindboundBard.csproj"

# The first condition must assign why even if the second is short-circuited.
assert 'if (!StageSafe(out string why) || !stageNavigator.TryStart(here, target, out why))' in stage
assert 'if (!StageSafe(out _) || !stageNavigator.TryStart(here, target, out string why))' not in stage
assert 'stageStatus = "Conductor cannot move: " + why;' in stage

# ValidPose must initialise every out parameter before any possible return.
pose = re.search(r'private static bool ValidPose\([^)]*\)\s*\{(?P<body>.*?)\n    \}', protocol, re.S)
assert pose, "Missing ValidPose method"
body = pose.group('body')
assert body.index('zone = 0;') < body.index('if (!IsHex(')
assert body.index('location = default;') < body.index('if (!IsHex(')
assert body.index('angle = 0;') < body.index('if (!IsHex(')
assert 'out uint zone, out Vector3 location, out float angle' in protocol

ET.parse(project)
assert any(f'<Version>{v}</Version>' in project.read_text(encoding="utf-8")
    for v in ("0.31.2.0", "0.32.0.0")), "Unexpected version; this check covers the v0.31.2 hotfix carried into v0.32"
print('PASS: StageSafe error reason and ValidPose zone initialisation satisfy source-level definite assignment checks.')
