using System;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace NunuWindboundBard.Core;

/// <summary>
/// Send musical note events directly to FFXIV's expanded Performance interface,
/// NOT into the Windows keyboard input stream. This lets FFXIV chat/other apps
/// receive normal typing during performance, with no foreground-window gate.
///
/// Technique informed by MidiBard2's publicly documented Performance addon
/// callback approach, originally by akira0245. This file is an independent
/// implementation against the FFXIVClientStructs API, not copied MidiBard code.
/// The game addon callback's meaning may change after game updates.
/// </summary>
public static unsafe class PerformanceOutput
{
    public static bool HasExpandedPerformanceAddon()
        => TryGetExpandedAddon() != null;

    private static AtkUnitBase* TryGetExpandedAddon()
    {
        AtkStage* stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null)
            return null;
        // Wide/full Performance UI: each pitch from C3 to C6 has index 0..36.
        // Mini layout needs octave state manipulation and is deliberately not used.
        return stage->RaptureAtkUnitManager->GetAddonByName("PerformanceModeWide");
    }

    /// <summary>Detect the electric-guitar Tone Change UI; absent on other instruments.</summary>
    public static bool HasGuitarToneAddon() => TryGetGuitarToneAddon() != null;

    private static AtkUnitBase* TryGetGuitarToneAddon()
    {
        AtkStage* stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null) return null;
        return stage->RaptureAtkUnitManager->GetAddonByName("PerformanceToneChange");
    }

    /// <summary>
    /// Selects a guitar tone via the in-game PerformanceToneChange UI callback.
    /// Returns false (without interrupting notes) when the tone UI is unavailable.
    /// This uses neither virtual key presses nor chat messages.
    /// Must be called on FFXIV's framework thread.
    /// </summary>
    public static bool TrySetGuitarTone(int tone)
    {
        if (!GuitarToneOverrides.IsValid(tone)) return false;
        AtkUnitBase* addon = TryGetGuitarToneAddon();
        if (addon == null) return false;
        AtkValue* args = stackalloc AtkValue[2];
        args[0] = default;
        args[0].Type = AtkValueType.Int;
        args[0].Int = 0; // "choose guitar tone"
        args[1] = default;
        args[1].Type = AtkValueType.Int;
        args[1].Int = tone;
        _ = addon->FireCallback(2, args, false);
        return true; // callback dispatched; audible effect still needs in-game verification
    }

    public static void NoteOn(int pitch) => Send(pitch, release: false);
    public static void NoteOff(int pitch) => Send(pitch, release: true);

    private static void Send(int pitch, bool release)
    {
        if (pitch is < NoteMapper.LowestNote or > NoteMapper.HighestNote)
            throw new ArgumentOutOfRangeException(nameof(pitch));
        AtkUnitBase* addon = TryGetExpandedAddon();
        if (addon == null)
            throw new InvalidOperationException("FFXIV's expanded Performance keyboard is not open. Select the 37-note layout.");

        // The callback consists of (action: 1=press, 2=release) and key 0..36.
        // The second callback parameter is an Int64 (type 4) in the game's
        // Performance addon event protocol. These values are version-sensitive.
        AtkValue* args = stackalloc AtkValue[2];
        args[0] = default;
        args[0].Type = AtkValueType.Int;
        args[0].Int = release ? 2 : 1;
        args[1] = default;
        args[1].Type = AtkValueType.Int64;
        args[1].Int64 = pitch - NoteMapper.LowestNote;
        _ = addon->FireCallback(2, args, false);
        // FireCallback's return value is not proof that a note was audible.
    }
}
