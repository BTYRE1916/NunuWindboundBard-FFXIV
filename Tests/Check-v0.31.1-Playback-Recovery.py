"""Fail-fast source-level hotfix checks; run: py Tests/Check-v0.31.1-Playback-Recovery.py."""
from pathlib import Path
root = Path(__file__).resolve().parents[1]/"src"/"NunuWindboundBard"
plugin = (root/"Plugin.cs").read_text()
engine = (root/"Core"/"PlaybackEngine.cs").read_text()
main = (root/"MainWindow.cs").read_text()
stage = (root/"MainWindow.StageFormations.cs").read_text()
native = (root/"Core"/"GameEnsembleSync.cs").read_text()
assert "main.PollStageFormations();" in plugin
assert plugin.index("Player.Update(performing && expandedKeyboard)") < plugin.index("main.PollStageFormations()")
assert "try { main.PollStageFormations(); }" in plugin
assert 'input.Equals("recover"' in plugin
assert "public void RecoverLocalPlayback()" in main
assert "plugin.Player.RewindToStart();" in main
assert "plugin.GameEnsemble.CancelLocalPreparation();" in main
assert "StopStageMovementBeforeMusic();" in main
assert "public void CancelLocalPreparation()" in native
assert "internal void RecoverStageAfterFailure(string detail)" in stage
assert "public void Update(bool canSend)" in engine
assert "VisualStageDesignerEnabled" in (root/"Configuration.cs").read_text()
assert "Enable Visual Stage Designer (experimental)##visualStageEnabled" in stage
assert "if (Config.Version < 33)" in plugin
assert '0.31.2.0' in (root/"NunuWindboundBard.csproj").read_text()
print('PASS: playback tick before optional stage poll, stage exception isolation, emergency recovery and native start reset')
