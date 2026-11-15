#!/usr/bin/env python3
"""Static source-contract verification; a Windows Dalamud build and game test remain mandatory."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
switch = (root / "src/NunuWindboundBard/Core/InstrumentSwitcher.cs").read_text(encoding="utf-8")
window = (root / "src/NunuWindboundBard/MainWindow.cs").read_text(encoding="utf-8")


def require(condition, description):
    if not condition:
        raise AssertionError("FAILED: " + description)
    print("PASS:", description)


def case(name, next_name):
    block = switch.split("case Phase." + name + ":", 1)[1].split("case Phase." + next_name + ":", 1)[0]
    require(block.strip() != "", name + " case is present")
    return block

exit_case = case("RequestExit", "WaitForExit")
require("performAction!(nativePerformanceInfo, 0u, 0)" in exit_case,
        "switching stows old instrument with the native FFXIV Performance(0) call")
require(exit_case.index("performAction!(nativePerformanceInfo, 0u, 0)") <
        exit_case.index("TryClosePerformanceAddon"),
        "native stow is preferred over UI close")
require("IsActuallyUnequipped" in exit_case and "CanNativeUnequip" in exit_case,
        "exit checks already-stowed state and guards unavailable signatures")
require("exitAttemptCount >= MaxExitAttempts" in exit_case,
        "stow attempts are bounded")
wait_exit = case("WaitForExit", "WaitAfterExit")
require("IsActuallyUnequipped" in wait_exit and "MarkExitConfirmed()" in wait_exit,
        "exit must be confirmed before advancing")
require("exitAttemptCount < MaxExitAttempts" in wait_exit and "Phase.WaitForManualExit" in wait_exit,
        "failure safely falls back to manual stow")
settling = case("WaitAfterExit", "SendSelection")
require("!IsActuallyUnequipped" in settling and "ExitSettleMs" in settling,
        "re-check state during settling delay")
selection = case("SendSelection", "WaitForSelection")
require(selection.index("!IsActuallyUnequipped") < selection.index("SendNativeInstrumentAction(selected)"),
        "never send new instrument while old one is equipped")
require("selectionAttemptCount++" in selection and "MaxSelectionAttempts" in selection,
        "selection retry is bounded")
waiting = case("WaitForSelection", "WaitForManualExit")
require("selectionAttemptCount < MaxSelectionAttempts" in waiting and "IsActuallyUnequipped" in waiting,
        "selection is retried only after stow confirmed")
manual = case("WaitForManualExit", "ApplyGuitarTone")
require("IsActuallyUnequipped" in manual,
        "manual fallback waits for confirmed stow")
require("initialInstrument is >= 24 and <= 28" in switch and
        "phase = Phase.ApplyGuitarTone" in switch and
        "PerformanceOutput.TrySetGuitarTone(selected - 24)" in switch,
        "guitar-to-guitar tone change path unchanged")
require("Phase.RequestUnequip" in switch and "Phase.WaitForUnequip" in switch and
        "performAction!(nativePerformanceInfo, 0u, 0)" in switch,
        "conductor STOP still uses existing stow method")
require("InstrumentCatalog.Display" in window and "Switch to selected instrument NOW" in window,
        "instrument UI retained")
require("NativePerformActionSignature" in switch and "NativePerformanceStateSignature" in switch,
        "native signatures remain fixed and are not guessed")
for name in ("MainWindow.MemberPortraits.cs", "MainWindow.InstrumentIcons.cs", "MainWindow.SoloStage.cs"):
    require((root / "src/NunuWindboundBard" / name).is_file(), name + " retained")
require("<Version>0.37.1.0</Version>" in (root / "src/NunuWindboundBard/NunuWindboundBard.csproj").read_text(),
        "project version bumped")
print("\nSource-level switch contract PASS. Requires Windows build + live game validation.")
