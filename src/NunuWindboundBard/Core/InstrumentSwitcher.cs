using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace NunuWindboundBard.Core;

/// <summary>
/// Instrument selection uses FFXIV's native PerformanceInstrument hotbar action.
/// Both ordinary instrument changes and STOP stow the old instrument using the
/// game's native Performance action with instrument 0. Dismissing the keyboard
/// window alone does NOT reliably unequip the character. No simulated input.
/// Both native actions must run on FFXIV's framework update thread, and are
/// game-version-sensitive: missing signatures fail closed with diagnostics.
/// </summary>
public sealed unsafe class InstrumentSwitcher
{
    private enum Phase
    {
        Idle, RequestExit, WaitForExit, WaitAfterExit, SendSelection,
        WaitForSelection, WaitForManualExit, ApplyGuitarTone, Settle, Ready, Failed,
        RequestUnequip, WaitForUnequip
    }

    // This is the PerformAction(0) exit pathway used by MidiBard2's stable
    // ensemble implementation, not the UI's Close() method. Signatures may
    // become invalid after FFXIV updates; never guess addresses or offset values.
    // Protocol/signature reference: MidiBard2, originally by akira0245.
    private const string NativePerformActionSignature =
        "48 89 6C 24 10 48 89 74 24 18 57 48 83 EC ?? 48 83 3D ?? ?? ?? ?? ?? 41 8B E8";
    private const string NativePerformanceStateSignature =
        "48 8B C2 0F B6 15 ?? ?? ?? ?? F6 C2 01";

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void NativePerformAction(nint performanceInfo, uint instrumentId, int unused);

    // Give game-owned state transitions and different instrument models time to
    // unload. Limited retries handle a first dispatch during teardown.
    private const int MaxExitAttempts = 2;
    private const int MaxSelectionAttempts = 2;
    private const long ExitWaitMs = 3100;
    private const long ExitSettleMs = 650;
    private const long SelectionWaitMs = 2700;

    private readonly IObjectTable objects;
    private readonly ICondition conditions;
    private readonly NativePerformAction? performAction;
    private readonly nint nativePerformanceInfo;
    private readonly string nativeUnequipAvailability;
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
    private int selectionAttemptCount;
    private string exitRoute = "Not attempted";

    public InstrumentSwitcher(IObjectTable objects, ICondition conditions, ISigScanner scanner)
    {
        this.objects = objects;
        this.conditions = conditions;
        try
        {
            if (!scanner.TryScanText(NativePerformActionSignature, out nint fn) || fn == 0 ||
                !scanner.TryGetStaticAddressFromSig(NativePerformanceStateSignature,
                    out nint state) || state == 0)
            {
                nativeUnequipAvailability = "Native Performance action signatures unavailable; " +
                    "FFXIV may have updated. Manually put away the instrument.";
                return;
            }
            performAction = Marshal.GetDelegateForFunctionPointer<NativePerformAction>(fn);
            nativePerformanceInfo = state;
            nativeUnequipAvailability = "Native FFXIV Performance action available.";
        }
        catch (Exception ex)
        {
            nativeUnequipAvailability = "Could not resolve native Performance action: " + ex.Message;
        }
    }

    private bool IsActuallyUnequipped => CurrentId == 0 && !conditions[ConditionFlag.Performing];
    public bool CanNativeUnequip => performAction is not null && nativePerformanceInfo != 0;
    public string NativeUnequipDiagnostics => nativeUnequipAvailability;

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
        Phase.WaitForManualExit or Phase.ApplyGuitarTone or Phase.Settle or
        Phase.RequestUnequip or Phase.WaitForUnequip;
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
        selectionAttemptCount = 0;
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
        // CurrentId can read as zero while the Performing condition is still
        // unwinding. Never equip the new model until BOTH report fully exited.
        else phase = IsActuallyUnequipped ? Phase.SendSelection : Phase.RequestExit;

        Status = initialInstrument != 0 && phase == Phase.RequestExit
            ? $"Closing {InstrumentCatalog.Display(initialInstrument)} before selecting {InstrumentCatalog.Display(id)}..."
            : $"Preparing {InstrumentCatalog.Display(id)} using FFXIV's Performance action...";
    }

    /// <summary>Leave FFXIV Performance mode without selecting a replacement.
    /// Called ONLY from Framework.Update after MIDI note-offs have been released.
    /// The native instrument=0 game action actually stows the instrument; closing
    /// PerformanceModeWide is insufficient because it only dismisses the UI.</summary>
    public void RequestUnequip()
    {
        // STOP should override an in-flight switch to an instrument.
        if (InProgress) Cancel();
        selected = 0;
        if (IsActuallyUnequipped)
        {
            phase = Phase.Idle;
            Status = "Instrument already stowed; FFXIV Performance state inactive.";
            return;
        }
        exitAttemptCount = 0;
        exitCallbackAccepted = false;
        exitRoute = "Not attempted";
        exitRequestedAt = 0;
        elapsed.Restart();
        phase = Phase.RequestUnequip;
        Status = "Leader STOP: requesting FFXIV's native unequip action (instrument 0)...";
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
            if (elapsed.ElapsedMilliseconds > (phase == Phase.WaitForManualExit ? 45000 : 24000))
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
                case Phase.RequestUnequip:
                    if (IsActuallyUnequipped)
                    {
                        phase = Phase.Idle;
                        elapsed.Stop();
                        Status = "Instrument stowed: FFXIV confirms Performance mode ended.";
                        break;
                    }
                    if (!CanNativeUnequip)
                    {
                        Fail(nativeUnequipAvailability);
                        break;
                    }
                    if (exitAttemptCount >= 2)
                    {
                        Fail("FFXIV still has the instrument equipped after two native " +
                            "Performance action (0) requests. Manually stow it. " + Diagnostics);
                        break;
                    }
                    // Unlike addon.Close(true), the game's native PerformAction(0)
                    // requests an instrument change to NONE. Called once per attempt,
                    // only here on the framework update thread after MIDI note release.
                    performAction!(nativePerformanceInfo, 0u, 0);
                    exitAttemptCount++;
                    exitRoute = "Native Performance action (instrument=0)";
                    exitCallbackAccepted = true; // dispatched, NOT proof of success
                    exitRequestedAt = elapsed.ElapsedMilliseconds;
                    phase = Phase.WaitForUnequip;
                    Status = $"Native unequip requested (attempt {exitAttemptCount}/2); " +
                        "waiting for FFXIV to confirm the instrument is put away...";
                    break;
                case Phase.WaitForUnequip:
                    if (IsActuallyUnequipped)
                    {
                        phase = Phase.Idle;
                        elapsed.Stop();
                        Status = "Instrument stowed: FFXIV confirms Performance mode ended.";
                    }
                    else if (elapsed.ElapsedMilliseconds - exitRequestedAt > 2800)
                    {
                        phase = Phase.RequestUnequip;
                        Status = "FFXIV still reports Performance active. " +
                            "Retrying native instrument=0 once more...";
                    }
                    break;
                case Phase.RequestExit:
                    if (IsActuallyUnequipped)
                    {
                        MarkExitConfirmed();
                        break;
                    }
                    if (current == 0)
                    {
                        // Game character mode changed before Dalamud's condition
                        // cleared. Wait; don't send a new instrument too early.
                        exitRoute = "Waiting for FFXIV Performing flag to clear";
                        exitRequestedAt = elapsed.ElapsedMilliseconds;
                        phase = Phase.WaitForExit;
                        Status = "FFXIV is still leaving Performance. Waiting before selecting the next instrument...";
                        break;
                    }
                    if (exitAttemptCount >= MaxExitAttempts)
                    {
                        phase = Phase.WaitForManualExit;
                        Status = "FFXIV did not stow the old instrument after " +
                            MaxExitAttempts + " requests. Manually stow it once; Windbound will continue automatically. " + Diagnostics;
                        break;
                    }

                    // IMPORTANT: Closing PerformanceModeWide/PerformanceMode only
                    // dismisses the keyboard; the character can keep the old model.
                    // Use the same native instrument=0 route as STOP instead.
                    exitAttemptCount++;
                    exitRequestedAt = elapsed.ElapsedMilliseconds;
                    if (CanNativeUnequip)
                    {
                        performAction!(nativePerformanceInfo, 0u, 0);
                        exitRoute = "Native Performance action (instrument=0)";
                        exitCallbackAccepted = true; // dispatched, not proof of unequip
                        phase = Phase.WaitForExit;
                        Status = $"Stowing {InstrumentCatalog.Display(current)} using FFXIV's native action " +
                            $"({exitAttemptCount}/{MaxExitAttempts}). Waiting for the old instrument to disappear...";
                    }
                    else
                    {
                        // Compatibility fallback on builds where the signature is
                        // unavailable. This may only dismiss the UI; verify and
                        // ask for manual stow rather than claim success.
                        exitCallbackAccepted = TryClosePerformanceAddon(out exitRoute);
                        if (!exitCallbackAccepted)
                        {
                            phase = Phase.WaitForManualExit;
                            Status = "Native stow is unavailable (" + nativeUnequipAvailability +
                                "); Performance UI fallback failed (" + exitRoute +
                                "). Manually stow the instrument to continue.";
                        }
                        else
                        {
                            phase = Phase.WaitForExit;
                            Status = "Fallback Performance window close sent. " +
                                "Waiting for FFXIV to confirm the old instrument is actually stowed...";
                        }
                    }
                    break;
                case Phase.WaitForExit:
                    if (IsActuallyUnequipped)
                    {
                        MarkExitConfirmed();
                    }
                    else if (elapsed.ElapsedMilliseconds - exitRequestedAt > ExitWaitMs)
                    {
                        if (current != 0 && exitAttemptCount < MaxExitAttempts)
                        {
                            phase = Phase.RequestExit;
                            Status = "The old instrument is still equipped. " +
                                "Retrying FFXIV's native stow action once...";
                        }
                        else
                        {
                            phase = Phase.WaitForManualExit;
                            Status = "FFXIV hasn't fully exited the previous Performance session " +
                                $"({exitRoute}). Stow manually if needed; Windbound will resume after confirmation.";
                        }
                    }
                    break;
                case Phase.WaitAfterExit:
                    if (!IsActuallyUnequipped)
                    {
                        // A delayed state update from the game restored the old
                        // mode. Never start the next instrument over an active one.
                        if (current != 0)
                        {
                            phase = Phase.RequestExit;
                            Status = "Old Performance instrument became active again; verifying stow.";
                        }
                        else
                        {
                            phase = Phase.WaitForExit;
                            exitRequestedAt = elapsed.ElapsedMilliseconds;
                            Status = "Waiting for the Performance flag to finish clearing...";
                        }
                    }
                    else if (elapsed.ElapsedMilliseconds - exitConfirmedAt >= ExitSettleMs)
                    {
                        phase = Phase.SendSelection;
                        Status = $"Previous instrument stowed. Equipping {InstrumentCatalog.Display(selected)}...";
                    }
                    break;
                case Phase.SendSelection:
                    if (!IsActuallyUnequipped)
                    {
                        phase = Phase.RequestExit;
                        Status = "Performance is still active; delaying the new instrument until fully stowed.";
                        break;
                    }
                    if (selectionAttemptCount >= MaxSelectionAttempts)
                    {
                        Fail("FFXIV did not equip the requested instrument after two attempts. " + Diagnostics);
                        break;
                    }
                    // Dispatch through the same native PerformanceInstrument hotbar
                    // action category used by FFXIV's own UI. Never simulate keys.
                    SendNativeInstrumentAction(selected);
                    selectionAttemptCount++;
                    requestAt = elapsed.ElapsedMilliseconds;
                    phase = Phase.WaitForSelection;
                    Status = $"Game action sent for {InstrumentCatalog.Display(selected)} " +
                        $"({LastRoute}, attempt {selectionAttemptCount}/{MaxSelectionAttempts}). Waiting for equip confirmation...";
                    break;
                case Phase.WaitForSelection:
                    if (elapsed.ElapsedMilliseconds - requestAt > SelectionWaitMs)
                    {
                        if (current != 0 && current != selected)
                        {
                            // The previous instrument returned while a replacement
                            // action was pending. Stow before trying anything else.
                            phase = Phase.RequestExit;
                            Status = $"FFXIV still has {InstrumentCatalog.Display(current)} equipped. " +
                                "Stowing it before trying the new model...";
                        }
                        else if (IsActuallyUnequipped && selectionAttemptCount < MaxSelectionAttempts)
                        {
                            phase = Phase.SendSelection;
                            Status = "The game did not equip on the first action. " +
                                "Retrying after the confirmed exit...";
                        }
                        else
                        {
                            Fail("FFXIV did not activate the selected Performance instrument. " + Diagnostics +
                                ". Check that Bard Performance is unlocked and the instrument action is available.");
                        }
                    }
                    break;
                case Phase.WaitForManualExit:
                    if (IsActuallyUnequipped)
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

    public string Diagnostics => $"Phase={phase}; unequip native available={CanNativeUnequip}; " +
                                 $"FFXIV Performing flag={conditions[ConditionFlag.Performing]}; " +
                                 $"exit attempts={exitAttemptCount}/{MaxExitAttempts}; " +
                                 $"selection attempts={selectionAttemptCount}/{MaxSelectionAttempts}; " +
                                 $"exit route={exitRoute}; exit callback accepted={exitCallbackAccepted} " +
                                 $"(not proof of exit); game action={LastRoute}; raw native return={LastNativeReturn} " +
                                 $"(not a success indicator); requested={InstrumentCatalog.Display(selected)}; current={CurrentName}.";

    private void MarkExitConfirmed()
    {
        // Only reachable after character mode AND Performing flag are cleared.
        exitConfirmedAt = elapsed.ElapsedMilliseconds;
        phase = Phase.WaitAfterExit;
        Status = $"FFXIV confirms old Performance mode exited. Preparing {InstrumentCatalog.Display(selected)}...";
    }

    /// <summary>
    /// Compatibility fallback only. Addon Close(true) is not sufficient proof
    /// of unequipping: it may hide the Performance keyboard while the character
    /// is still holding the previous instrument. The caller MUST verify both
    /// character mode and the Performing flag before changing instruments.
    /// This is game-version-sensitive and runs on FFXIV's framework thread.
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
