using System;
using System.Collections.Generic;

namespace NunuWindboundBard.Core;

/// <summary>
/// Solo MIDI controller → FFXIV 37-key Performance bridge.
/// Driver thread only enqueues; ProcessFrameworkUpdate alone calls FFXIV.
/// This is intentionally independent from MIDI file playback / ensemble cues.
/// </summary>
public sealed class LiveMidiInputService : IDisposable
{
    private readonly WinMmMidiInput input = new();
    private readonly LiveMidiNoteRouter router = new();
    private IReadOnlyList<WinMmMidiInput.Port> ports = Array.Empty<WinMmMidiInput.Port>();
    private bool panicRequested;
    private bool outputWasAllowed;
    private int currentTranspose = int.MinValue;
    private bool currentFold;
    private bool currentSustain;
    private int currentChannel = -1;
    private int previousOverflow;
    private int previousDriverErrors;
    private bool disposed;

    public IReadOnlyList<WinMmMidiInput.Port> Ports => ports;
    public bool Connected => input.Connected;
    public string ConnectedName => input.ConnectedName;
    public string Status { get; private set; } = "MIDI input off. Choose a controller in Solo mode.";
    public int NotesHeld => router.HeldPitches;
    public int NotesReceived => router.ReceivedNotes;
    public int OutOfRangeCount => router.SkippedOutOfRange;
    public int LastInputNote => router.LastOriginalNote;
    public int LastGameNote => router.LastMappedNote;
    public int DriverErrors => input.DriverErrors;
    public int OverflowCount => input.OverflowCount;

    public void RefreshPorts()
    {
        if (disposed) return;
        try
        {
            ports = WinMmMidiInput.GetPorts();
            Status = ports.Count == 0 ? "No MIDI input devices found. Connect your USB piano and press Refresh." :
                $"Found {ports.Count} MIDI input port(s). Choose the USB piano and Connect.";
        }
        catch (Exception ex)
        {
            ports = Array.Empty<WinMmMidiInput.Port>();
            Status = "MIDI input enumeration unavailable: " + ex.Message;
        }
    }

    public bool Connect(string name, out string error)
    {
        error = "";
        if (disposed) { error = "MIDI input has been disposed."; return false; }
        RefreshPorts();
        WinMmMidiInput.Port? port = null;
        foreach (var entry in ports)
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)) { port = entry; break; }
        if (port is null) { error = "MIDI input port not found. Connect the keyboard, refresh and select its name."; return false; }
        try
        {
            Disconnect();
            input.Open(port);
            Status = $"Connected to '{port.Name}'. ARM and open the 37-key Performance keyboard to play.";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Status = "Could not connect: " + error;
            return false;
        }
    }

    public void Disconnect()
    {
        panicRequested = true;
        input.Close();
        Status = "MIDI keyboard disconnected. Notes will be released on the game update thread.";
    }

    /// <summary>Can be invoked by UI/command without native FFXIV calls.</summary>
    public void Panic() => panicRequested = true;

    private static void GameNoteOn(int note) => PerformanceOutput.NoteOn(note);
    private static void GameNoteOff(int note)
    {
        try { PerformanceOutput.NoteOff(note); }
        catch { /* addon may have closed since previous framework tick */ }
    }

    private void ReleaseAll(bool performanceReady)
    {
        if (performanceReady) router.AllNotesOff(GameNoteOff);
        else router.AllNotesOff(static _ => { });
    }

    /// <summary>Call ONLY on Dalamud Framework.Update, after normal MIDI Player.Update.</summary>
    public void ProcessFrameworkUpdate(bool enabled, bool solo, bool armed, bool performanceReady,
        bool scheduledMidiBusy, bool instrumentBusy, int transpose, bool fold, bool sustain, int channel)
    {
        if (disposed) return;
        bool changed = transpose != currentTranspose || fold != currentFold ||
            sustain != currentSustain || channel != currentChannel;
        if (changed)
        {
            // Release old pitches under old mapping BEFORE applying a new transpose.
            ReleaseAll(performanceReady);
            currentTranspose = transpose;
            currentFold = fold;
            currentSustain = sustain;
            currentChannel = channel;
            router.Transpose = transpose;
            router.FoldInto37Keys = fold;
            router.UseSustainPedal = sustain;
            router.ChannelFilter = channel;
            input.Drain();
        }
        if (panicRequested)
        {
            ReleaseAll(performanceReady);
            input.Drain();
            panicRequested = false;
        }

        bool allowed = enabled && solo && armed && performanceReady && input.Connected &&
                       !scheduledMidiBusy && !instrumentBusy;
        if (!allowed)
        {
            if (outputWasAllowed) ReleaseAll(performanceReady);
            outputWasAllowed = false;
            // Don't replay stale MIDI notes upon re-arming or re-entering Performance.
            input.Drain();
            return;
        }
        outputWasAllowed = true;
        int driverErrors = input.DriverErrors;
        if (driverErrors != previousDriverErrors)
        {
            previousDriverErrors = driverErrors;
            ReleaseAll(performanceReady);
            input.Drain();
            Status = "MIDI hardware reported an invalid message: released all held notes. Check the MIDI cable/driver.";
            return;
        }
        int overflow = input.OverflowCount;
        if (overflow != previousOverflow)
        {
            previousOverflow = overflow;
            ReleaseAll(performanceReady);
            input.Drain();
            Status = "MIDI input overflow: released all notes. Check computer load or device driver.";
            return;
        }
        int processed = 0;
        while (processed < 512 && input.TryDequeue(out uint midi))
        {
            try { router.Process(midi, GameNoteOn, GameNoteOff); }
            catch (Exception ex)
            {
                ReleaseAll(performanceReady);
                input.Drain();
                Status = "MIDI-to-Performance callback failed: " + ex.Message;
                return;
            }
            processed++;
        }
        if (processed > 0) Status = $"Live MIDI playing • {router.HeldPitches} notes held; MIDI input events this frame: {processed}.";
    }

    public void Dispose()
    {
        if (disposed) return;
        // Plugin disposal runs on Dalamud's game thread. Release any held notes
        // while the game addon still exists, then stop the WinMM callback.
        disposed = true;
        ReleaseAll(PerformanceOutput.HasExpandedPerformanceAddon());
        input.Dispose();
    }
}
