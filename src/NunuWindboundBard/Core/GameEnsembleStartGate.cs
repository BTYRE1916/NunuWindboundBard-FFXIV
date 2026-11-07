using System;

namespace NunuWindboundBard.Core;

/// <summary>A server Ensemble START observed by this FFXIV client. Timestamps are local QPC
/// values; they must NEVER be compared across different computers.</summary>
public readonly record struct GameEnsembleStartSignal(long Sequence, long LocalQpc);

public enum GameEnsembleReadiness
{
    Ready,
    TemporaryBlock,
    PermanentBlock,
    Silent
}

public enum GameEnsembleDecisionKind
{
    NoSignal,
    Waiting,
    Play,
    Silent,
    Failed
}

public readonly record struct GameEnsembleStartDecision(
    GameEnsembleDecisionKind Kind, long StartQpc, bool Late, string Reason);

/// <summary>
/// Makes a one-shot native Ensemble start resistant to momentary UI/Stop transitions.
/// Each performer has an independent instance. This class does not use or change FFXIV
/// network protocol, party chat, or the MIDI engine.
/// </summary>
public sealed class GameEnsembleStartGate
{
    public const int ReadinessGraceMs = 1200;
    public const int MaxCatchUpMs = 100;

    private GameEnsembleStartSignal? pending;
    public bool HasPending => pending.HasValue;
    public long LastSignalSequence { get; private set; }
    public string LastOutcome { get; private set; } = "No FFXIV Ensemble START observed yet.";

    public void Capture(GameEnsembleStartSignal signal)
    {
        if (signal.Sequence <= LastSignalSequence) return;
        LastSignalSequence = signal.Sequence;
        pending = signal;
        LastOutcome = $"Native Ensemble START #{signal.Sequence} received; checking local readiness.";
    }

    public GameEnsembleStartDecision Evaluate(GameEnsembleReadiness readiness,
        string reason, long nowQpc, long frequency)
    {
        if (pending is not { } eventSignal)
            return new GameEnsembleStartDecision(GameEnsembleDecisionKind.NoSignal, 0, false, LastOutcome);

        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        double ageMs = Math.Max(0, (nowQpc - eventSignal.LocalQpc) * 1000.0 / frequency);
        if (readiness == GameEnsembleReadiness.TemporaryBlock && ageMs <= ReadinessGraceMs)
        {
            string pendingReason = $"Ensemble START #{eventSignal.Sequence}: waiting for {reason} ({ageMs:0} ms).";
            LastOutcome = pendingReason;
            return new GameEnsembleStartDecision(GameEnsembleDecisionKind.Waiting, 0, false, pendingReason);
        }

        pending = null;
        if (readiness == GameEnsembleReadiness.PermanentBlock ||
            readiness == GameEnsembleReadiness.TemporaryBlock)
        {
            LastOutcome = $"Ensemble START #{eventSignal.Sequence} could not begin: {reason}" +
                          (readiness == GameEnsembleReadiness.TemporaryBlock ? " (ready window expired)." : ".");
            return new GameEnsembleStartDecision(GameEnsembleDecisionKind.Failed, 0, false, LastOutcome);
        }

        if (readiness == GameEnsembleReadiness.Silent)
        {
            LastOutcome = $"Ensemble START #{eventSignal.Sequence} received; no musical tracks assigned to this Bard (silent conductor).";
            return new GameEnsembleStartDecision(GameEnsembleDecisionKind.Silent, 0, false, LastOutcome);
        }

        // Keep the native event's timeline when possible. If the Performance
        // addon became ready much later, start from now rather than bursting a
        // backlog of hundreds of note events into the game all at once.
        bool late = ageMs > MaxCatchUpMs;
        long startQpc = late ? nowQpc : eventSignal.LocalQpc;
        LastOutcome = late
            ? $"Ensemble START #{eventSignal.Sequence} recovered {ageMs:0} ms late; starting from now (possible sync drift)."
            : $"Ensemble START #{eventSignal.Sequence} accepted after {ageMs:0} ms.";
        return new GameEnsembleStartDecision(GameEnsembleDecisionKind.Play, startQpc, late, LastOutcome);
    }

    public void Cancel(string reason)
    {
        pending = null;
        LastOutcome = reason;
    }
}
