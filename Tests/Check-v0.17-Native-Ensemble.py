#!/usr/bin/env python3
"""Dependency-free structural regression checks. NOT an FFXIV simulation."""
from pathlib import Path
import hashlib
import re
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
code = root / "src" / "NunuWindboundBard"
checks = []
def check(name, ok):
    checks.append((name, bool(ok)))
    print(("PASS " if ok else "FAIL ") + name)

native = (code/"Core/GameEnsembleSync.cs").read_text()
plugin = (code/"Plugin.cs").read_text()
window = (code/"MainWindow.cs").read_text()
ui = (code/"MainWindow.GameEnsemble.cs").read_text()
cfg = (code/"Configuration.cs").read_text()
csproj = ET.parse(code/"NunuWindboundBard.csproj")
project_version = csproj.findtext(".//Version")

check("v0.17 metadata and settings version", project_version == "0.17.0.0" and "Version { get; set; } = 18;" in cfg)
check("native START signature + single hook", "NativeStartSignature" in native and "OnNativeStart" in native and "TryConsumeStart" in native)
check("native auto confirm receives FFXIV ReadyCheckReceive", 'GetReadyAddon("PerformanceReadyCheckReceive")' in native and 'AutoConfirmAttempts' in native)
check("native conductor sends FFXIV metronome Ready Check", 'GetReadyAddon("PerformanceMetronome")' in native and 'GetReadyAddon("PerformanceReadyCheck")' in native)
check("native action queue pumped on Framework.Update", "main.PollNativeReadyChecks();" in plugin and "GameEnsemble.PumpReadyChecks" in ui)
check("draw thread does NOT directly invoke conductor native callback", "PumpConductorRequest(); // May complete synchronously" not in native and "waitingForMetronome = true;" in native)
check("native starter does not schedule start using party chat", "StartSongCore(decision.StartQpc)" in ui and "if (cfg.GameEnsembleOneClick)" not in ui.split("public void PollGameEnsemble()")[1].split("if (plugin.GameEnsemble.TryConsumeStart")[0])
check("retired party timed start on migration", "Config.GameEnsembleOneClick = false;" in plugin)
check("HUD now displays native start", '"START ENSEMBLE"' in (code/"MainWindow.Hud.cs").read_text())
check("Main Play launches native conductor method", "if (cfg.EnsembleMode == (int)EnsembleRole.Conductor)" in window and "CueEnsemble();" in window)
check("no Win32 SendInput in native module", not re.search(r"SendInput|PostMessage|DateTimeOffset\.UtcNow|Socket|TcpClient|OneClickEnsembleCue", native))
# No unknown binary dependencies added.
check("source assets preserved", all((root/p).exists() for p in ["Assets/windbound-icon.png", "Samples/Bard-Chord-Callback-Test.mid", "Samples/Bard-37-Note-Calibration.mid"]))

# Check modified files have balanced braces, ignoring common string/comment spans.
# This is a coarse structural sanity check, not a C# compiler.
def brace_balanced(s):
    out=[]; i=0; mode='normal'
    while i<len(s):
        c=s[i]; n=s[i+1] if i+1<len(s) else ''
        if mode=='normal':
            if c=='/' and n=='/': mode='line';i+=2;continue
            if c=='/' and n=='*': mode='block';i+=2;continue
            if c=='@' and n=='"': mode='verbatim';i+=2;continue
            if c=='"': mode='string';i+=1;continue
            if c=="'": mode='char';i+=1;continue
            if c=='{': out.append('{')
            if c=='}':
                if not out: return False
                out.pop()
        elif mode=='line':
            if c=='\n': mode='normal'
        elif mode=='block':
            if c=='*' and n=='/': mode='normal';i+=2;continue
        elif mode in ('string','char'):
            if c=='\\': i+=2;continue
            if (c=='"' and mode=='string') or (c=="'" and mode=='char'): mode='normal'
        elif mode=='verbatim':
            if c=='"' and n=='"': i+=2;continue
            if c=='"': mode='normal'
        i+=1
    return not out and mode in ('normal','line')
for f in [code/"Plugin.cs",code/"MainWindow.cs",code/"MainWindow.Hud.cs",code/"MainWindow.GameEnsemble.cs",code/"Core/GameEnsembleSync.cs",code/"Configuration.cs"]:
    check("brace structure " + f.name,brace_balanced(f.read_text()))

ok = all(b for _,b in checks)
print(f"\n{sum(b for _,b in checks)}/{len(checks)} source checks passed. (Not compiled or tested in game.)")
sys.exit(0 if ok else 1)
