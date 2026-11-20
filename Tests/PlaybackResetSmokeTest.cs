// Standalone .NET regression test for Windbound's actual PlaybackEngine source.
// Native Performance callbacks are stubbed: test never writes to FFXIV.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NunuWindboundBard.Core;

static class Program
{
    static int Main()
    {
        var shortSong = new[] {
            new BardNote(0, 60, 0, 100, 40),
            new BardNote(60, 64, 0, 100, 30),
        };
        var longSong = new[] {
            new BardNote(0, 60, 0, 100, 2000),
            new BardNote(200, 64, 0, 100, 2000),
        };
        var preview = new BardPlaybackSettings(false, "", 1, 20);
        using var p = new PlaybackEngine();

        // Completed song deliberately retains its last event position for the UI;
        // selecting another MIDI must explicitly clear it.
        p.Start(shortSong, preview, () => true);
        Thread.Sleep(170);
        p.Update(true);
        Require(!p.IsPlaying && p.CompletedSuccessfully && p.PositionMs > 0,
            "Test setup: song must complete with non-zero stale cursor.");
        p.RewindToStart();
        ExpectRewound(p, "completed song then load");

        // A new MIDI starts at 00:00 without touching the previous song's progress.
        p.Start(longSong, preview, () => true);
        Require(p.PositionMs == 0 && p.NotesPlayed == 0, "Fresh song did not start at zero.");
        p.Update(true);
        Require(p.IsPlaying, "Long song not playing.");
        p.RewindToStart();
        Require(p.IsRewindPending && p.PositionMs == 0, "Active rewind was not queued.");
        p.Update(true); // game framework tick processes held-note release
        ExpectRewound(p, "right-click during playback");

        // Verify held real notes would be released only on Update, not UI thread.
        PerformanceOutput.Clear();
        var game = new BardPlaybackSettings(true, "", 1, 30);
        p.Start(longSong, game, () => true);
        p.Update(true);
        Require(PerformanceOutput.OnCalls == 1, "Expected native note-on on framework tick.");
        p.RewindToStart();
        Require(PerformanceOutput.OffCalls == 0, "Rewind must not call native UI on button thread.");
        p.Update(true);
        Require(PerformanceOutput.OffCalls == 1, "Held note not released by framework update.");
        ExpectRewound(p, "safe native note release");

        // STOP behaves as stop-and-rewind, even when the bar had progressed.
        p.Start(longSong, preview, () => true);
        p.Update(true);
        p.Stop();
        p.Update(true);
        ExpectRewound(p, "STOP");

        // Starting an ensemble countdown then rewinding must cancel the count-in.
        p.Start(longSong, preview, () => true,
            Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5L);
        Require(p.IsEnsemblePlayback && p.IsPlaying, "Ensemble count-in not queued.");
        p.RewindToStart();
        p.Update(true);
        Require(!p.IsEnsemblePlayback, "Old ensemble cue survived rewind.");
        ExpectRewound(p, "ensemble countdown");

        // Manual rewind cancels song flow as well; no unexpected Auto Next.
        var flow = new SongFlowCoordinator();
        flow.MarkStarted();
        flow.Cancel();
        Require(flow.Poll(false, p.CompletedSuccessfully, true, true).Kind == SongFlowActionKind.None,
            "Auto Next fired after manual reset.");
        Console.WriteLine("PASS: rewind after finish, new MIDI, active MIDI, note-off thread, STOP, ensemble countdown and Auto Next cancel.");
        CheckHostedTiming();
        ConcertRecoveryChecks.Run();
        return 0;
    }

    static void CheckHostedTiming()
    {
        long downbeat = Stopwatch.GetTimestamp();
        long second = Stopwatch.Frequency;
        Require(!MultiDeviceEnsembleTiming.ReportDue(0, downbeat, 1500) &&
            !MultiDeviceEnsembleTiming.ReportDue(downbeat, downbeat - second, 1500) &&
            !MultiDeviceEnsembleTiming.ReportDue(downbeat, downbeat + second, 1500) &&
            MultiDeviceEnsembleTiming.ReportDue(downbeat, downbeat + 2 * second, 1500) &&
            !MultiDeviceEnsembleTiming.ReportDue(downbeat, downbeat + 2 * second, 3000) &&
            MultiDeviceEnsembleTiming.ReportDue(downbeat, downbeat + 3 * second, 3000),
            "Hosted reports must run after the downbeat, including the no-notes report.");

        var notes = new[] { new BardNote(0, 60, 0, 100, 2000) };
        var settings = new BardPlaybackSettings(true, "", 1, 20);
        using var early = new PlaybackEngine();
        using var late = new PlaybackEngine();
        long shared = Stopwatch.GetTimestamp() + second / 2;
        long earlyTarget = MultiDeviceEnsembleTiming.ApplyTrim(shared, -1000);
        long lateTarget = MultiDeviceEnsembleTiming.ApplyTrim(shared, 1000);
        Require(earlyTarget == shared - second && lateTarget == shared + second &&
            MultiDeviceEnsembleTiming.ApplyTrim(shared, 0) == shared, "Signed follower trim in wall-clock ms.");
        early.Start(notes, settings, () => true, earlyTarget);
        late.Start(notes, settings, () => true, lateTarget);
        early.Update(true);
        late.Update(true);
        Require(early.Timing.Count == 1 && early.Timing.OriginQpc == earlyTarget &&
            late.Timing.Count == 0 && late.Timing.OriginQpc == lateTarget,
            "Copied notes with earlier and later trim dispatch on their respective timelines.");
        Console.WriteLine("PASS: copied-track follower START, signed trim and post-downbeat reporting.");
    }

    static void ExpectRewound(PlaybackEngine p, string label)
    {
        Require(!p.IsPlaying && !p.IsPaused && !p.IsRewindPending && !p.IsStopPending &&
                !p.CompletedSuccessfully && p.PositionMs == 0 && p.NotesPlayed == 0,
                "Reset failed for " + label + ": " + p.Status);
    }
    static void Require(bool condition, string error)
    {
        if (!condition) throw new Exception(error);
    }
}

namespace NunuWindboundBard.Core
{
    // Lightweight stand-ins allow testing core scheduling without a Dalamud or game runtime.
    public record BardNote(double TimeMs, int Pitch, int Track, int Velocity, double DurationMs, int Channel = 0);
    public readonly record struct MidiLaneKey(int Track, int Channel);
    public static class PerformanceOutput
    {
        public static int OnCalls { get; private set; }
        public static int OffCalls { get; private set; }
        public static List<int> Pressed { get; } = new();
        public static void Clear() { OnCalls = 0; OffCalls = 0; Pressed.Clear(); }
        public static void NoteOn(int pitch) { OnCalls++; Pressed.Add(pitch); }
        public static void NoteOff(int _) => OffCalls++;
        public static bool TrySetGuitarTone(int _) => true;
    }
    public static class EnsembleCoordinator
    {
        public static double RemainingSeconds(long startQpc) =>
            Math.Max(0, (startQpc - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);
    }
}
