using System;
using System.Diagnostics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace NunuWindboundBard.Core;

/// <summary>
/// Instrument selection through the game's native hotbar PerformanceInstrument action.
/// Unlike the v0.5 signature-scanned DoPerformAction pathway, this calls the
/// hotbar dispatcher exposed by FFXIVClientStructs. It never synthesises keystrokes.
/// This native game interface is version-sensitive and requires in-game testing.
/// All methods MUST run on the FFXIV framework/Draw thread.
/// </summary>
public sealed unsafe class InstrumentSwitcher
{
    private enum Phase
    {
        Idle, RequestExit, WaitForExit, WaitAfterExit, SendSelection,
        WaitForSelection, WaitForManualExit, ApplyGuitarTone, Settle, Ready, Failed
    }

    private readonly IObjectTable objects;
    private readonly Stopwatch elapsed = new();
    private Phase phase;
    private int selected;
    private int lastGuitarChoice = -1;
    private int initialInstrument;
    private long requestAt;
    private long settleAt;
    private long exitRequestedAt;
    private long exitConfirmedAt;
    private bool exitCallbackAccepted;
    private int exitAttemptCount;
    private string exitRoute = "Not attempted";

    public InstrumentSwitcher(IObjectTable objects) => this.objects = objects;

    public int CurrentId
    {
        get
        {
            // Do not cache a pointer to the player across loading screens or logouts.
            nint addr = objects.LocalPlayer?.Address ?? nint.Zero;
            if (addr == nint.Zero) return 0;
            Character* player = (Character*)addr;
            return player->Mode == CharacterModes.Performance ? player->ModeParam : 0;
        }
    }

    public string CurrentName => CurrentId == 0 ? "Not in Performance" : InstrumentCatalog.Display(CurrentId);
    public string Status { get; private set; } = "Ready for automatic instrument selection.";
    public string LastRoute { get; private set; } = "Not requested";
    public int LastNativeReturn { get; private set; } = -1;
    public bool InProgress => phase is Phase.RequestExit or Phase.WaitForExit or
        Phase.WaitAfterExit or Phase.SendSelection or Phase.WaitForSelection or
        Phase.WaitForManualExit or Phase.ApplyGuitarTone or Phase.Settle;
    public bool HasFailed => phase == Phase.Failed;
    public int TargetId => selected;

    public bool IsReadyFor(int id)
    {
        if (id == 0) return true; // 'Keep current' never requests an action.
        if (InProgress) return false;
        int current = CurrentId;
        if (id is >= 24 and <= 28)
        {
            // FFXIV may report the base guitar id regardless of the active tone.
            // A tone selection must be confirmed by our own callback this session.
            return current is >= 24 and <= 28 && lastGuitarChoice == id &&
                   phase == Phase.Ready && PerformanceOutput.HasExpandedPerformanceAddon();
        }
        return current == id && PerformanceOutput.HasExpandedPerformanceAddon();
    }

    public void Request(int id)
    {
        if (!InstrumentCatalog.Valid(id) || id == 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (InProgress)
            throw new InvalidOperationException("Another instrument switch is already preparing. Stop/cancel it first.");
        if (objects.LocalPlayer is null)
            throw new InvalidOperationException("Log into FFXIV before switching instruments.");

        selected = id;
        initialInstrument = CurrentId;
        elapsed.Restart();
        requestAt = 0;
        exitRequestedAt = 0;
        exitConfirmedAt = 0;
        exitRoute = "Not attempted";
        exitCallbackAccepted = false;
        exitAttemptCount = 0;
        lastGuitarChoice = -1;
        LastNativeReturn = -1;
        LastRoute = "Pending";

        if (id is >= 24 and <= 28 && initialInstrument is >= 24 and <= 28)
            phase = Phase.ApplyGuitarTone;
        else if (initialInstrument == id && PerformanceOutput.HasExpandedPerformanceAddon())
        {
            phase = Phase.Ready;
            elapsed.Stop();
            Status = $"Already equipped: {InstrumentCatalog.Display(id)}.";
            return;
        }
        else phase = initialInstrument != 0 ? Phase.RequestExit : Phase.SendSelection;

        Status = initialInstrument != 0 && phase == Phase.RequestExit
            ? $"Closing {InstrumentCatalog.Display(initialInstrument)} before selecting {InstrumentCatalog.Display(id)}..."
            : $"Preparing {InstrumentCatalog.Display(id)} using FFXIV's Performance action...";
    }

    public void Cancel()
    {
        if (!InProgress) return;
        phase = Phase.Idle;
        elapsed.Reset();
        Status = "Instrument request cancelled; no further action will be sent.";
    }

    public void Update(bool expandedPerformanceReady)
    {
        if (!InProgress) return;
        try
        {
            if (objects.LocalPlayer is null)
            {
                Fail("Instrument switching stopped because the player logged out or changed character.");
                return;
            }
            if (elapsed.ElapsedMilliseconds > (phase == Phase.WaitForManualExit ? 45000 : 14000))
            {
                Fail("Instrument switch timed out. " + Diagnostics);
                return;
            }

            int current = CurrentId;
            // Never claim success just because FFXIV accepted the API call; read
            // the player's actual Performance state before advancing.
            if (phase == Phase.WaitForSelection && current == selected)
            {
                if (selected is >= 24 and <= 28)
                    phase = Phase.ApplyGuitarTone;
                else
                    EnterSettle();
                return;
            }
            if (phase == Phase.WaitForSelection &&
                current is >= 24 and <= 28 && selected is >= 24 and <= 28)
            {
                phase = Phase.ApplyGuitarTone;
                return;
            }

            switch (phase)
            {
                case Phase.RequestExit:
                    if (current == 0)
                    {
                        MarkExitConfirmed();
                        break;
                    }
                    if (exitAttemptCount >= 2)
                    {
                        phase = Phase.WaitForManualExit;
                        Status = "Automatic close was attempted twice; FFXIV still has another instrument active. Close Performance once and Windbound will finish the switch.";
                        break;
                    }
                    // Ask FFXIV's own Performance addon to close with its callback,
                    // equivalent to closing that window through the game UI.
                    // No synthetic Esc, window-focus manipulation or chat input.
                    exitAttemptCount++;
                    exitCallbackAccepted = TryClosePerformanceAddon(out exitRoute);
                    exitRequestedAt = elapsed.ElapsedMilliseconds;
                    if (!exitCallbackAccepted)
                    {
                        // If the addon is absent/incompatible, do NOT try random
                        // memory offsets or force the character out of Performance.
                        phase = Phase.WaitForManualExit;
                        Status = $"Automatic close unavailable ({exitRoute}). Close Performance once; Windbound will finish the switch. No simulated keys were sent.";
                    }
                    else
                    {
                        phase = Phase.WaitForExit;
                        Status = $"Asked FFXIV to close {InstrumentCatalog.Display(current)} ({exitRoute}). Waiting for Performance to end...";
                    }
                    break;
                case Phase.WaitForExit:
                    if (current == 0)
                    {
                        MarkExitConfirmed();
                    }
                    else if (elapsed.ElapsedMilliseconds - exitRequestedAt > 2900)
                    {
                        // Closing the UI is not sufficient on every game version.
                        // Fall back to the existing manual exit instead of repeatedly
                        // closing or making unsupported native calls.
                        phase = Phase.WaitForManualExit;
                        Status = $"FFXIV did not exit Performance after the close callback ({exitRoute}). Close it manually once; Windbound will select {InstrumentCatalog.Display(selected)} afterward.";
                    }
                    break;
                case Phase.WaitAfterExit:
                    if (current != 0)
                    {
                        Fail("Another instrument became active while waiting for the switch. " + Diagnostics);
                    }
                    else if (elapsed.ElapsedMilliseconds - exitConfirmedAt >= 250)
                    {
                        phase = Phase.SendSelection;
                        Status = $"Previous instrument closed. Selecting {InstrumentCatalog.Display(selected)}...";
                    }
                    break;
                case Phase.SendSelection:
                    // Dispatch through the same native PerformanceInstrument hotbar action
                    // category used by FFXIV's own UI. We do not type or focus the game.
                    SendNativeInstrumentAction(selected);
                    requestAt = elapsed.ElapsedMilliseconds;
                    phase = Phase.WaitForSelection;
                    Status = $"Game action sent for {InstrumentCatalog.Display(selected)} ({LastRoute}). Waiting for the game to equip it...";
                    break;
                case Phase.WaitForSelection:
                    if (elapsed.ElapsedMilliseconds - requestAt > 2300)
                    {
                        if (current != 0 && current != selected)
                        {
                            // The game can reject the selection if the prior session
                            // was still unwinding. Retry the automatic close only
                            // once more, then fall back safely if FFXIV resists.
                            phase = Phase.RequestExit;
                            Status = $"FFXIV kept {InstrumentCatalog.Display(current)} active. Retrying with a confirmed Performance close...";
                        }
                        else
                        {
                            Fail("FFXIV did not activate the selected Performance action. " + Diagnostics +
                                ". Check that Bard Performance is unlocked and instrument actions are available.");
                        }
                    }
                    break;
                case Phase.WaitForManualExit:
                    if (current == 0)
                        MarkExitConfirmed();
                    break;
                case Phase.ApplyGuitarTone:
                    if (current is < 24 or > 28)
                    {
                        // Game hasn't opened guitar mode yet. Don't call a tone callback on
                        // another instrument's UI.
                        if (elapsed.ElapsedMilliseconds - requestAt > 2300 && requestAt != 0)
                            Fail("A guitar must be equipped before its tone can be selected.");
                        return;
                    }
                    if (!PerformanceOutput.HasGuitarToneAddon()) return;
                    if (!PerformanceOutput.TrySetGuitarTone(selected - 24))
                    {
                        Fail("Guitar tone selector was not available; select the tone manually.");
                        return;
                    }
                    lastGuitarChoice = selected;
                    LastRoute = "PerformanceToneChange addon callback";
                    LastNativeReturn = -1;
                    EnterSettle();
                    break;
                case Phase.Settle:
                    if (expandedPerformanceReady && elapsed.ElapsedMilliseconds - settleAt >= 220)
                    {
                        phase = Phase.Ready;
                        elapsed.Stop();
                        Status = $"Equipped: {InstrumentCatalog.Display(selected)}. Performance keyboard ready. Guitar tone changes should still be checked by ear.";
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Fail("Instrument selection error: " + ex.Message + ". " + Diagnostics);
        }
    }

    public string Diagnostics => $"Phase={phase}; exit attempts={exitAttemptCount}; exit route={exitRoute}; exit callback accepted={exitCallbackAccepted} " +
                                 $"(not proof of exit); game action={LastRoute}; raw native return={LastNativeReturn} " +
                                 $"(not a success indicator); requested={InstrumentCatalog.Display(selected)}; current={CurrentName}.";

    private void MarkExitConfirmed()
    {
        exitConfirmedAt = elapsed.ElapsedMilliseconds;
        phase = Phase.WaitAfterExit;
        Status = $"FFXIV confirms old Performance mode exited. Preparing {InstrumentCatalog.Display(selected)}...";
    }

    /// <summary>
    /// Ask the currently open Performance UI to perform its native Close with
    /// fireCallback=true, just as the game's UI does. This is different from
    /// simply hiding a window: the close callback can notify the game to end
    /// Performance. We still verify Character.Mode becomes non-Performance.
    /// This is game-version-sensitive and must run on FFXIV's framework thread.
    /// </summary>
    private static bool TryClosePerformanceAddon(out string route)
    {
        route = "Performance UI not found";
        AtkStage* stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null)
        {
            route = "UI manager not ready";
            return false;
        }
        foreach (string name in new[] { "PerformanceModeWide", "PerformanceMode" })
        {
            AtkUnitBase* addon = stage->RaptureAtkUnitManager->GetAddonByName(name);
            if (addon == null || !addon->IsReady) continue;
            bool result = addon->Close(true);
            route = $"{name}.Close(fireCallback: true) (returned {result})";
            return result;
        }
        return false;
    }

    private void EnterSettle()
    {
        settleAt = elapsed.ElapsedMilliseconds;
        phase = Phase.Settle;
        Status = $"FFXIV reports {InstrumentCatalog.Display(selected)}. Waiting for expanded Performance mode...";
    }

    private void SendNativeInstrumentAction(int instrumentId)
    {
        RaptureHotbarModule* hotbar = RaptureHotbarModule.Instance();
        if (hotbar == null || !hotbar->ModuleReady)
            throw new InvalidOperationException("FFXIV hotbar module is not ready.");

        // Prefer an existing game-owned instrument slot. The UI has already
        // constructed it and can execute it through the normal hotbar pathway.
        var bars = hotbar->StandardHotbars;
        for (int barIndex = 0; barIndex < bars.Length; barIndex++)
        {
            ref var bar = ref bars[barIndex];
            for (uint slotIndex = 0; slotIndex < 16; slotIndex++)
            {
                RaptureHotbarModule.HotbarSlot* slot = bar.GetHotbarSlot(slotIndex);
                if (slot == null || slot->CommandType != RaptureHotbarModule.HotbarSlotType.PerformanceInstrument ||
                    slot->CommandId != instrumentId) continue;
                LastNativeReturn = hotbar->ExecuteSlotById((uint)barIndex, slotIndex);
                LastRoute = $"existing instrument hotbar {barIndex + 1}/{slotIndex + 1}";
                return;
            }
        }

        // No existing action on the hotbars: submit a transient performance
        // action to the exact same game-owned dispatcher, without modifying
        // saved hotbars or taking control of keyboard/chat input.
        // ExecuteSlot dispatches by CommandType/CommandId; other fields are
        // included to make the apparent action consistent with the command.
        RaptureHotbarModule.HotbarSlot temporary = default;
        // Initialise the embedded Utf8String and hotbar-slot state before invoking
        // native methods; avoid a slot with an uninitialised in-game string field.
        temporary.Initialize();
        try
        {
            temporary.Set(RaptureHotbarModule.HotbarSlotType.PerformanceInstrument,
                          (uint)instrumentId);
            LastNativeReturn = hotbar->ExecuteSlot(&temporary);
            LastRoute = "transient native PerformanceInstrument hotbar action";
        }
        finally
        {
            // The temporary slot is not saved in any actual hotbar.
            // Dispose the embedded native string allocated by Initialise.
            temporary.Clear();
            temporary.PopUpHelp.Dtor();
        }
    }

    private void Fail(string message)
    {
        elapsed.Stop();
        phase = Phase.Failed;
        Status = message;
    }
}
