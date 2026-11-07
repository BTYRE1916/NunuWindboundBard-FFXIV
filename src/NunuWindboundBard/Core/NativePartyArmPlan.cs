using System;

namespace NunuWindboundBard.Core;

public enum PartyArmDecision { Wait, Armed, Silent, Disarmed, Reject }
public readonly record struct PartyArmEvaluation(PartyArmDecision Decision, string Reason);

/// <summary>
/// Deterministic conductor-ARM safety checks, isolated from FFXIV/ImGui.
/// A remote ARM is an opt-in preparation command, never a musical START.
/// </summary>
public static class NativePartyArmPlan
{
    public static PartyArmEvaluation Evaluate(bool armRequested, bool followerEnabled,
        bool allowLeaderArm, bool playing, bool busy, bool loadingConductorFile,
        string requestedHash, string loadedHash, bool midiLoaded, int localLaneCount)
    {
        if (!followerEnabled) return new(PartyArmDecision.Reject, "DISABLED");
        if (!allowLeaderArm) return new(PartyArmDecision.Reject, "CONSENT");
        if (playing) return new(PartyArmDecision.Reject, "PLAYING");
        if (busy)
            return loadingConductorFile && armRequested
                ? new(PartyArmDecision.Wait, "LOADING")
                : new(PartyArmDecision.Reject, "BUSY");
        // DISARM requires no MIDI file: the user may have removed or changed it.
        if (!armRequested) return new(PartyArmDecision.Disarmed, "DISARMED");
        if (!midiLoaded || string.IsNullOrWhiteSpace(loadedHash) ||
            !string.Equals(requestedHash, loadedHash, StringComparison.OrdinalIgnoreCase))
            return loadingConductorFile ? new(PartyArmDecision.Wait, "LOADING") :
                new(PartyArmDecision.Reject, "WRONGMIDI");
        if (localLaneCount <= 0) return new(PartyArmDecision.Silent, "SILENT");
        return new(PartyArmDecision.Armed, "ARMED");
    }
}
