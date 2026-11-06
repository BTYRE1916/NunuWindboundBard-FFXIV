using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NunuWindboundBard.Core;

// KeyMap is retained for config/backward compatibility and manual keybind display.
// Direct Performance playback DOES NOT synthesize physical/virtual Windows keys.
public sealed record BardPlaybackSettings(bool SendToGame,
    string KeyMap, double Speed, int HoldMilliseconds,
    bool EnableGuitarOverrides = false,
    IReadOnlyDictionary<MidiLaneKey, int>? GuitarTones = null);

/// <summary>
/// Frame-driven, timestamp-based MIDI event scheduler. Does NOT await each note.
/// Starts every member of a MIDI chord at its scheduled timestamp, and handles
/// note-off independently for overlapping notes. All game callbacks are issued
/// on the Dalamud framework update thread by Plugin.OnFrameworkUpdate.
/// </summary>
public sealed class PlaybackEngine : IDisposable
{
    private readonly object sync = new();
    private readonly Stopwatch clock = new();
    private readonly Dictionary<int, int> heldPitches = new();
    private List<ScheduledEvent> events = new();
    private BardPlaybackSettings settings = new(false, "", 1, 45);
    private long? scheduledStartQpc;
    private bool ensembleStarted;
    private int eventIndex;
    private int notesPlayed;
    private int toneSwitches;
    private int? currentGuitarTone;
    private string guitarWarning = "";
    private bool running;
    private bool paused;
    private bool stopRequested;
    private bool rewindRequested; // right-click reset / new MIDI: handled on framework update if notes are active
    private double positionMs;
    private string status = "Ready";
    private bool completedSuccessfully;
    private int? testPitch;
    private double testOffAtMs;
    private int testHoldMs;
    private bool testPressed;

    private readonly record struct ScheduledEvent(double AtMs, int Pitch,
        bool KeyDown, int Id, int Velocity, int Track, int Channel);

    public bool IsPlaying { get { lock (sync) return running; } }
    public bool IsStopPending { get { lock (sync) return stopRequested; } }
    public bool IsRewindPending { get { lock (sync) return rewindRequested; } }
    public bool IsEnsemblePlayback { get { lock (sync) return running && scheduledStartQpc.HasValue; } }
    public double EnsembleCountInSeconds { get { lock (sync) return scheduledStartQpc.HasValue && !ensembleStarted
        ? EnsembleCoordinator.RemainingSeconds(scheduledStartQpc.Value) : 0; } }
    public bool IsPaused { get { lock (sync) return paused; } }
    public int NotesPlayed { get { lock (sync) return notesPlayed; } }
    public int ToneSwitches { get { lock (sync) return toneSwitches; } }
    public string GuitarWarning { get { lock (sync) return guitarWarning; } }
    public double PositionMs { get { lock (sync) return positionMs; } }
    public string Status { get { lock (sync) return status; } }
    public bool CompletedSuccessfully { get { lock (sync) return completedSuccessfully; } }

    public void Start(IReadOnlyList<BardNote> notes, BardPlaybackSettings options, Func<bool> canSend,
        long? ensembleStartQpc = null)
    {
        if (notes.Count == 0) throw new InvalidOperationException("No playable notes. Load a MIDI first.");
        if (options.Speed is < 0.5 or > 2.0) throw new ArgumentOutOfRangeException(nameof(options.Speed));
        if (options.SendToGame && !canSend())
            throw new InvalidOperationException("Open the full 37-note FFXIV Performance keyboard first.");
        lock (sync)
        {
            if (rewindRequested || stopRequested) throw new InvalidOperationException("Wait for the previous stop/reset to finish before starting the MIDI.");
            if (running || testPitch.HasValue) throw new InvalidOperationException("Stop the current performance/test first.");
            // Freeze tone choices for this performance: editing the UI won't change an active song.
            settings = options with { GuitarTones = options.GuitarTones is null
                ? null : new Dictionary<MidiLaneKey, int>(options.GuitarTones) };
            scheduledStartQpc = ensembleStartQpc;
            ensembleStarted = false;
            // Keep original note timestamps; do not quantise/serialize chords.
            events = new List<ScheduledEvent>(notes.Count * 2);
            for (int i = 0; i < notes.Count; ++i)
            {
                var note = notes[i];
                double hold = Math.Clamp(note.DurationMs, options.HoldMilliseconds, 2000.0);
                events.Add(new ScheduledEvent(note.TimeMs, note.Pitch, true, i, note.Velocity, note.Track, note.Channel));
                events.Add(new ScheduledEvent(note.TimeMs + hold, note.Pitch, false, i, note.Velocity, note.Track, note.Channel));
            }
            events.Sort((a, b) =>
            {
                int t = a.AtMs.CompareTo(b.AtMs);
                if (t != 0) return t;
                // End held notes before pressing a new chord at the same timestamp.
                int edge = a.KeyDown.CompareTo(b.KeyDown);
                if (edge != 0) return edge;
                int pitch = a.Pitch.CompareTo(b.Pitch);
                return pitch != 0 ? pitch : a.Id.CompareTo(b.Id);
            });
            eventIndex = 0;
            notesPlayed = 0;
            toneSwitches = 0;
            currentGuitarTone = null;
            guitarWarning = "";
            positionMs = 0;
            paused = false;
            stopRequested = false;
            completedSuccessfully = false;
            status = options.SendToGame ? "Playing: direct FFXIV Performance events (no chat keystrokes)" : "Preview: all MIDI note events";
            if (scheduledStartQpc.HasValue) clock.Reset();
            else clock.Restart();
            running = true;
        }
    }

    public void Update(bool canSend)
    {
        lock (sync)
        {
            if (rewindRequested || stopRequested)
            {
                // UI requests never invoke FFXIV native callbacks directly.
                // Release notes here on Dalamud's framework update thread.
                bool wasRewind = rewindRequested;
                ReleaseTest();
                ReleaseAll();
                ResetPlaybackState(wasRewind ? "MIDI reset to start (00:00). Ready to play." :
                    "Playback stopped and rewound to 00:00.");
                return;
            }
            if (testPitch.HasValue)
            {
                try
                {
                    // A one-note calibration test, without Windows key events.
                    if (!canSend)
                    {
                        ReleaseTest();
                        status = "Test cancelled: Performance mode closed.";
                    }
                    else if (!testPressed)
                    {
                        PerformanceOutput.NoteOn(testPitch.Value);
                        testPressed = true;
                        testOffAtMs = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000 + testHoldMs;
                        status = "Test C4 callback dispatched. Check sound by ear.";
                    }
                    else if (Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000 >= testOffAtMs)
                    {
                        ReleaseTest();
                    }
                }
                catch (Exception ex)
                {
                    ReleaseTest();
                    status = "Test callback failed: " + ex.Message;
                }
            }
            if (!running || paused) return;
            if (settings.SendToGame && !canSend)
            {
                ReleaseAll();
                Finish(false, "Stopped: full FFXIV Performance keyboard was closed.");
                return;
            }
            try
            {
                double elapsed;
                if (scheduledStartQpc.HasValue)
                {
                    // Cross-process QPC timestamps are comparable on the same Windows machine.
                    // Late framework ticks catch up to the intended cue, not a fresh Stopwatch start.
                    long now = Stopwatch.GetTimestamp();
                    if (now < scheduledStartQpc.Value)
                    {
                        status = $"Ensemble starting in {EnsembleCoordinator.RemainingSeconds(scheduledStartQpc.Value):0.0}s";
                        return;
                    }
                    if (!ensembleStarted)
                    {
                        ensembleStarted = true;
                        status = "Ensemble playing: shared-clock start, direct Performance callbacks.";
                    }
                    elapsed = (now - scheduledStartQpc.Value) * 1000.0 / Stopwatch.Frequency * settings.Speed;
                }
                else elapsed = clock.Elapsed.TotalMilliseconds * settings.Speed;
                // Process due events without imposing a note-by-note delay.
                // A frame may contain multiple simultaneous note-ons and note-offs.
                int processed = 0;
                const int maxEventsPerFrame = 320;
                while (eventIndex < events.Count && events[eventIndex].AtMs <= elapsed && processed < maxEventsPerFrame)
                {
                    var evt = events[eventIndex++];
                    ++processed;
                    if (evt.KeyDown)
                    {
                        if (settings.SendToGame)
                        {
                            // Tone selection is per MIDI track+channel, applied just before note-on.
                            // A missing guitar tone UI never silences the note itself.
                            if (settings.EnableGuitarOverrides &&
                                GuitarToneOverrides.TryResolve(settings.GuitarTones, evt.Track, evt.Channel, out int requestedTone) &&
                                requestedTone != currentGuitarTone)
                            {
                                if (PerformanceOutput.TrySetGuitarTone(requestedTone))
                                {
                                    currentGuitarTone = requestedTone;
                                    toneSwitches++;
                                }
                                else if (guitarWarning.Length == 0)
                                {
                                    guitarWarning = "Guitar tone window not found; choose an electric guitar in Performance. Notes continue without tone override.";
                                }
                            }
                            // Same pitch repeated before its prior release: retrigger cleanly.
                            if (heldPitches.ContainsKey(evt.Pitch)) PerformanceOutput.NoteOff(evt.Pitch);
                            PerformanceOutput.NoteOn(evt.Pitch);
                            heldPitches[evt.Pitch] = evt.Id;
                        }
                        notesPlayed++;
                    }
                    else if (settings.SendToGame && heldPitches.TryGetValue(evt.Pitch, out int id) && id == evt.Id)
                    {
                        PerformanceOutput.NoteOff(evt.Pitch);
                        heldPitches.Remove(evt.Pitch);
                    }
                    positionMs = evt.AtMs;
                }
                if (eventIndex >= events.Count)
                {
                    ReleaseAll();
                    Finish(true, $"Finished {notesPlayed:N0} notes (chords preserved). Listen for game-side note limits.");
                }
            }
            catch (Exception error)
            {
                ReleaseAll();
                Finish(false, "Performance output halted: " + error.Message);
            }
        }
    }

    private void ReleaseTest()
    {
        if (testPressed && testPitch.HasValue)
        {
            try { PerformanceOutput.NoteOff(testPitch.Value); }
            catch { /* the Performance addon may have been closed */ }
        }
        testPitch = null;
        testPressed = false;
        testOffAtMs = -1;
    }

    private void ReleaseAll()
    {
        if (settings.SendToGame)
        {
            foreach (int pitch in heldPitches.Keys.ToArray())
            {
                try { PerformanceOutput.NoteOff(pitch); }
                catch { /* addon may already have closed; don't crash FFXIV during stop */ }
            }
        }
        heldPitches.Clear();
    }

    private void Finish(bool success, string message)
    {
        clock.Reset();
        running = false;
        paused = false;
        completedSuccessfully = success;
        status = message;
        events.Clear();
        eventIndex = 0;
        scheduledStartQpc = null;
        ensembleStarted = false;
    }

    /// <summary>
    /// Stops and rewinds the current MIDI to 00:00, including chord/sustain state,
    /// any queued ensemble start and note counters. It never auto-plays again.
    /// When notes are active, native note-off callbacks are deferred to Update()
    /// on the FFXIV framework thread. When idle, the stale progress can be cleared
    /// immediately, e.g. on loading a different MIDI.
    /// </summary>
    public void RewindToStart()
    {
        lock (sync)
        {
            completedSuccessfully = false; // Never Auto Next after a manual rewind.
            positionMs = 0;
            notesPlayed = 0;
            if (running || testPitch.HasValue || heldPitches.Count > 0)
            {
                rewindRequested = true;
                stopRequested = false;
                status = "Rewind requested; releasing any held notes on the next game update.";
            }
            else
            {
                // No native UI calls necessary when no keys are held.
                ResetPlaybackState("MIDI at start (00:00). Ready to play.");
            }
        }
    }

    // Caller must hold sync. In particular, do not call ReleaseAll here: the
    // caller first releases keys on the framework thread when required.
    private void ResetPlaybackState(string message)
    {
        clock.Reset();
        running = false;
        paused = false;
        stopRequested = false;
        rewindRequested = false;
        completedSuccessfully = false;
        positionMs = 0;
        notesPlayed = 0;
        toneSwitches = 0;
        currentGuitarTone = null;
        guitarWarning = "";
        heldPitches.Clear();
        events.Clear();
        eventIndex = 0;
        scheduledStartQpc = null;
        ensembleStarted = false;
        testPitch = null;
        testPressed = false;
        testOffAtMs = -1;
        status = message;
    }

    public void QueueTestNote(int pitch, int holdMs, Func<bool> canSend)
    {
        if (pitch is < NoteMapper.LowestNote or > NoteMapper.HighestNote) throw new ArgumentOutOfRangeException(nameof(pitch));
        if (!canSend()) throw new InvalidOperationException("Open the full 37-note Performance keyboard before testing.");
        lock (sync)
        {
            if (running || testPitch.HasValue) throw new InvalidOperationException("Stop the song or previous test first.");
            testPitch = pitch;
            testPressed = false;
            testHoldMs = Math.Clamp(holdMs, 15, 500);
            testOffAtMs = -1;
            status = "C4 callback queued for next FFXIV framework update.";
        }
    }

    public void TogglePause()
    {
        lock (sync)
        {
            if (!running) return;
            if (scheduledStartQpc.HasValue)
            {
                status = "For ensemble synchronization, use STOP (not Pause). Cue again to restart.";
                return;
            }
            paused = !paused;
            if (paused) { clock.Stop(); ReleaseAll(); }
            else clock.Start();
        }
    }

    public void Stop() { lock (sync) stopRequested = true; }
    // Dispose never calls native UI from an arbitrary thread. Stop playback
    // before unloading a dev plugin so the next framework tick releases notes.
    public void Dispose() { lock (sync) { stopRequested = true; clock.Stop(); } }
}
