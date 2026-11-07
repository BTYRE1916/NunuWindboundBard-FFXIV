using System;
using System.Diagnostics;
using System.Threading;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace NunuWindboundBard.Core;

/// <summary>
/// MidiBard-style native FFXIV ensemble coordination: the conductor opens the
/// game's Ready Check, each participating client confirms the native receiving
/// dialog, and every client starts ONLY on FFXIV's ensemble-start event.
///
/// No party-chat countdown, network sockets, Windows keyboard injection or
/// cross-machine clock comparisons are used for this mode.
/// The signature and callback shapes follow the publicly documented game
/// interface as used by MidiBard 2 (v3-api15-stable). This is an independent
/// implementation, not a copy of MidiBard's licensed source.
/// </summary>
public sealed unsafe class GameEnsembleSync : IDisposable
{
    // Matches MidiBard 2's API 15 NetworkEnsembleStart signature.
    // May stop resolving after an FFXIV patch: fail closed and display diagnostics.
    private const string NativeStartSignature =
        "40 53 48 83 EC 20 48 8B D9 48 83 C1 78 E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8D 53 20";

    private delegate long NativeStart(nint a, nint b);
    private Hook<NativeStart>? startHook;
    private long pendingStarts;
    private long consumedStarts;
    private long lastSignalQpc;
    private long nativeCalls;
    private long acceptedStarts;
    private long receiveConfirmCalls;
    private long localConfirmCalls;
    private long beginCalls;
    private long requestedAtQpc;
    private bool waitingForMetronome;
    private bool waitingForLocalConfirm;
    private bool triedShowMetronome;
    private bool manualConfirmPending;
    private bool manualConfirmFollower;
    private string readinessStatus = "No Ready Check requested.";

    private const int RequestTimeoutMs = 6500;

    public bool Enabled { get; set; }
    public bool Available => startHook is not null;
    public bool IsPreparing => waitingForMetronome || waitingForLocalConfirm;
    public string Status { get; private set; } = "Initialising native FFXIV ensemble listener...";
    public string ReadyCheckStatus => readinessStatus;
    public long NativeCalls => Interlocked.Read(ref nativeCalls);
    public long StartSignals => Interlocked.Read(ref acceptedStarts);
    public long AutoConfirmAttempts => Interlocked.Read(ref receiveConfirmCalls);
    public long ConductorConfirmAttempts => Interlocked.Read(ref localConfirmCalls);
    public long LastSignalQpc => Interlocked.Read(ref lastSignalQpc);
    public string Diagnostics =>
        $"Native hook available={Available}; enabled={Enabled}; calls={NativeCalls}; " +
        $"native START signals={StartSignals}; pending={Math.Max(0, Interlocked.Read(ref pendingStarts) - consumedStarts)}; " +
        $"begin requests dispatched={Interlocked.Read(ref beginCalls)}; conductor confirmations={ConductorConfirmAttempts}; " +
        $"follower auto-confirm callbacks={AutoConfirmAttempts}; pending preparation={IsPreparing}; " +
        $"last local START QPC={LastSignalQpc}; {readinessStatus} {Status}";

    public GameEnsembleSync(ISigScanner scanner, IGameInteropProvider interop)
    {
        try
        {
            if (!scanner.TryScanText(NativeStartSignature, out nint address) || address == 0)
            {
                Status = "Native ensemble-start signature not found. FFXIV may have updated; native playback is disabled.";
                return;
            }
            startHook = interop.HookFromAddress<NativeStart>(address, OnNativeStart);
            startHook.Enable();
            Status = "Native FFXIV Ensemble START listener active (MidiBard-style).";
        }
        catch (Exception ex)
        {
            startHook?.Dispose();
            startHook = null;
            Status = "Couldn't initialise native ensemble listener: " + ex.Message;
        }
    }

    private long OnNativeStart(nint a, nint b)
    {
        // Do not run playback, native UI callbacks or allocations in this detour.
        // Mark the signal before forwarding to the original handler, as MidiBard
        // starts its own playback before the game's handler returns.
        long timestamp = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref nativeCalls);
        if (Enabled)
        {
            long previous = Interlocked.Read(ref lastSignalQpc);
            if ((previous == 0 || timestamp - previous > Stopwatch.Frequency * 2) &&
                Interlocked.CompareExchange(ref lastSignalQpc, timestamp, previous) == previous)
            {
                Interlocked.Increment(ref acceptedStarts);
                Interlocked.Increment(ref pendingStarts);
            }
        }
        return startHook!.Original(a, b);
    }

    /// <summary>Consume one game-native START on the FFXIV framework thread.</summary>
    public bool TryConsumeStart(out GameEnsembleStartSignal signal)
    {
        long received = Interlocked.Read(ref pendingStarts);
        if (received <= consumedStarts)
        {
            signal = default;
            return false;
        }
        consumedStarts = received;
        waitingForMetronome = false;
        waitingForLocalConfirm = false;
        readinessStatus = "FFXIV native ensemble START received.";
        signal = new GameEnsembleStartSignal(received, LastSignalQpc);
        return true;
    }

    /// <summary>
    /// A SINGLE conductor click opens the FFXIV metronome if necessary, requests
    /// the native Ready Check and confirms the conductor on the next available tick.
    /// Participants are auto-confirmed separately by PumpReadyChecks.
    /// </summary>
    public bool TryBeginReadyCheck(out string detail)
    {
        if (!Enabled || !Available)
        {
            detail = !Available ? Status : "Enable native FFXIV Game Ensemble before starting.";
            return false;
        }
        if (IsPreparing)
        {
            detail = "Already preparing FFXIV Ensemble Ready Check; check the native status.";
            return true;
        }
        requestedAtQpc = Stopwatch.GetTimestamp();
        waitingForMetronome = true;
        waitingForLocalConfirm = false;
        triedShowMetronome = false;
        readinessStatus = "Opening FFXIV's Performance Metronome / requesting the native Ready Check...";
        // The ImGui Draw thread is not guaranteed to be the game's framework
        // thread. Only enqueue here; PumpReadyChecks performs native callbacks.
        detail = readinessStatus + " (queued on game framework thread)";
        return true;
    }

    /// <summary>
    /// Run on Dalamud's framework update thread while the interface is hidden
    /// or the user has alt-tabbed. This is the crucial behaviour omitted by
    /// previous Windbound releases and present in MidiBard's update loop.
    /// </summary>
    public void PumpReadyChecks(bool autoConfirmReceiving, bool inPerformanceMode)
    {
        if (!Enabled || !Available) return;
        if (IsPreparing) PumpConductorRequest();
        if (manualConfirmPending)
        {
            manualConfirmPending = false;
            var addon = GetReadyAddon(manualConfirmFollower
                ? "PerformanceReadyCheckReceive" : "PerformanceReadyCheck");
            if (addon == null) readinessStatus = "Manual confirmation: FFXIV Ready Check addon not allocated.";
            else
            {
                try { SendIntCallback(addon, 2); readinessStatus = "Manual FFXIV confirmation callback dispatched."; }
                catch (Exception ex) { readinessStatus = "Manual confirmation error: " + ex.Message; }
            }
        }
        if (!autoConfirmReceiving || !inPerformanceMode) return;

        // Like MidiBard's Playlib.ConfirmReceiveReadyCheck: dispatch on each
        // Framework.Update while enrolled; do NOT require the addon to be
        // foreground, focused or marked visible. It can exist while
        // the game presents it in a distinct layer. Game itself ignores callbacks
        // when there is no active Ready Check. Only enrolled/ARMed clients run it.
        var receiving = GetReadyAddon("PerformanceReadyCheckReceive");
        if (receiving == null) return;
        try
        {
            SendIntCallback(receiving, 2);
            Interlocked.Increment(ref receiveConfirmCalls);
        }
        catch (Exception ex)
        {
            readinessStatus = "Receiving Ready Check callback failed: " + ex.Message;
        }
    }

    private void PumpConductorRequest()
    {
        if (!IsPreparing) return;
        if ((Stopwatch.GetTimestamp() - requestedAtQpc) * 1000.0 / Stopwatch.Frequency > RequestTimeoutMs)
        {
            waitingForMetronome = false;
            waitingForLocalConfirm = false;
            readinessStatus = "Timed out waiting for FFXIV's Ready Check interface. Open the in-game metronome and try again.";
            return;
        }
        try
        {
            if (waitingForMetronome)
            {
                if (!triedShowMetronome)
                {
                    triedShowMetronome = true;
                    var agentModule = AgentModule.Instance();
                    AgentInterface* agent = agentModule == null ? null :
                        agentModule->GetAgentByInternalId(AgentId.PerformanceMetronome);
                    // A visible metronome window is not needed beforehand.
                    if (agent != null && !agent->IsAgentActive()) agent->Show();
                }
                var metronome = GetReadyAddon("PerformanceMetronome");
                if (metronome == null)
                {
                    readinessStatus = "Waiting for FFXIV Performance Metronome addon to initialise...";
                    return;
                }
                // Native callback values: Int(2), Bool(false), as in MidiBard.
                AtkValue* values = stackalloc AtkValue[2];
                values[0] = default;
                values[0].Type = AtkValueType.Int;
                values[0].Int = 2;
                values[1] = default;
                values[1].Type = AtkValueType.Bool;
                values[1].Bool = false;
                metronome->FireCallback(2, values, true);
                Interlocked.Increment(ref beginCalls);
                waitingForMetronome = false;
                waitingForLocalConfirm = true;
                readinessStatus = "Native Ensemble Ready Check requested; confirming conductor...";
            }
            if (waitingForLocalConfirm)
            {
                var owner = GetReadyAddon("PerformanceReadyCheck");
                if (owner == null) return; // Might appear on next framework tick.
                SendIntCallback(owner, 2);
                Interlocked.Increment(ref localConfirmCalls);
                waitingForLocalConfirm = false;
                readinessStatus = "Conductor confirmed; followers auto-accept, then FFXIV sends one native Ensemble START to everyone.";
            }
        }
        catch (Exception ex)
        {
            waitingForMetronome = false;
            waitingForLocalConfirm = false;
            readinessStatus = "Native Ready Check request failed: " + ex.Message;
        }
    }

    /// <summary>Manual fallback queues the action onto Framework.Update, never
    /// invokes game callbacks from the ImGui Draw thread.</summary>
    public bool TryConfirmReadyCheck(bool follower, out string detail)
    {
        if (!Available || !Enabled)
        {
            detail = "Native Ensemble listener isn't enabled for this client.";
            return false;
        }
        manualConfirmFollower = follower;
        manualConfirmPending = true;
        detail = "Manual native Ready Check confirmation queued for the next FFXIV framework update.";
        return true;
    }

    private static AtkUnitBase* GetReadyAddon(string name)
    {
        var stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null) return null;
        var addon = stage->RaptureAtkUnitManager->GetAddonByName(name);
        // Match MidiBard's Playlib: an allocated addon is enough for the
        // native UI callback. Requiring IsVisible/IsReady caused missed checks
        // when the UI was in a hidden or transition layer.
        return addon;
    }

    private static void SendIntCallback(AtkUnitBase* addon, int value)
    {
        AtkValue input = default;
        input.Type = AtkValueType.Int;
        input.Int = value;
        // Return value is not proof the game accepted a Ready Check. The server's
        // native START event is the authoritative confirmation for playback.
        addon->FireCallback(1, &input, true);
    }

    public void Dispose()
    {
        Enabled = false;
        waitingForMetronome = false;
        waitingForLocalConfirm = false;
        startHook?.Disable();
        startHook?.Dispose();
    }
}
