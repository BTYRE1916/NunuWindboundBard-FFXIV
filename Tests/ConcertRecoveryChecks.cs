using System.Diagnostics;
using NunuWindboundBard.Core;

internal static class ConcertRecoveryChecks
{
    internal static void Run()
    {
        var clock = new MidiBarClock(480, [new(0, 0, 500_000), new(480, 500, 1_000_000)],
            [new(0, 4, 2), new(1920, 3, 2)]);
        Require(clock.Next(100, 10_000) == new MidiBar(2, 3500), "Tempo and meter change affect the next downbeat.");
        Require(clock.Next(3501, 10_000) == new MidiBar(3, 6500), "3/4 changes subsequent bar length.");
        Require(clock.Next(3501, 6500) is null, "The end of the song cannot be a rejoin boundary.");
        var shortBar = new MidiBarClock(480, [new(0, 0, 500_000)], [new(0, 4, 2), new(960, 3, 2)]);
        Require(shortBar.Next(100, 9000) == new MidiBar(2, 1000), "A mid-bar signature change begins a new bar.");

        long present = Stopwatch.GetTimestamp();
        long origin = present - Ticks(1000);
        Require(ConcertLocalSession.Plan(clock, 7, origin, origin, 2, 10000, "song", "parts", 4, present) is { Bar: 3 } cue &&
            ConcertLocalSession.ValidCue(cue, 7, origin, present) &&
            !ConcertLocalSession.ValidCue(cue, 8, origin, present) &&
            !ConcertLocalSession.ValidCue(cue, 7, origin - Ticks(3000), present) &&
            !ConcertLocalSession.ValidCue(cue, 7, origin, cue.AtQpc),
            "Same-PC planning uses playback speed and refuses wrong, old or late cues.");

        var notes = new BardNote[]
        {
            new(0, 60, 0, 100, 2000),
            new(500, 62, 0, 100, 2000),
            new(1000, 67, 0, 100, 2000),
            new(1600, 69, 0, 100, 2000),
        };
        using var target = new PlaybackEngine();
        using var other = new PlaybackEngine();
        var game = new BardPlaybackSettings(true, "", 1, 30);
        var preview = game with { SendToGame = false };
        long followerOrigin = present - Ticks(20);
        target.Start(notes, game, () => true, followerOrigin);
        other.Start(notes, preview, () => true, origin);
        target.Update(true);
        other.Update(true);
        double otherPosition = other.PositionMs;
        Require(!target.Rejoin(notes, game, () => true, followerOrigin - 10, origin, 1000),
            "An unrelated playback origin cannot be rejoined.");
        PerformanceOutput.Clear();
        Require(target.Rejoin(notes, game, () => true, followerOrigin, origin, 1000), "Active rejoin failed.");
        target.Update(true);
        Require(PerformanceOutput.OffCalls == 1 && PerformanceOutput.Pressed.SequenceEqual([67]) &&
            target.Timing is { Count: 1 } && target.Timing.OriginQpc == origin,
            "Release one held note, skip old onsets and time only the new downbeat.");
        Require(other.PositionMs == otherPosition && other.IsPlaying, "Rejoin did not touch the other player.");
        target.Stop();
        target.Update(true);
        PerformanceOutput.Clear();
        Require(target.Rejoin(notes, game, () => true, followerOrigin, origin, 1000), "Idle follower rejoin failed.");
        target.Update(true);
        Require(PerformanceOutput.Pressed.SequenceEqual([67]), "Idle follower replayed notes before the chosen bar.");
        target.Stop();
        target.Update(true);

        string scratch = Path.Combine(Path.GetTempPath(), "WindboundRecovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            ulong[] roster = [1, 2, 3, 4, 5, 6, 7, 8];
            ConcertLocalPeer State(ulong member, long written) => new(member, written, "0.45.0.0",
                origin, origin, "song", 1, "parts", 4, true, true, "Playing", default, Guid.Empty, "", null,
                Guid.Empty, Guid.Empty, []);
            Parallel.ForEach(roster, id => ConcertLocalSession.Exchange(scratch, State(id, Stopwatch.GetTimestamp()), []));
            var view = ConcertLocalSession.Exchange(scratch, State(1, Stopwatch.GetTimestamp()), roster[1..]);
            Require(view.Count == 7 && view.All(pair => pair.Value.Member == pair.Key),
                "All seven followers publish separate, readable state to the conductor.");
            _ = ConcertLocalSession.Exchange(scratch, State(8, Stopwatch.GetTimestamp() - Ticks(4000)), []);
            Require(ConcertLocalSession.Exchange(scratch, State(1, Stopwatch.GetTimestamp()), roster[1..]).Count == 6,
                "The octet excludes a stale follower.");

            string first = Path.Combine(scratch, "First.mid");
            string second = Path.Combine(scratch, "Second.mid");
            // SMF format 0, 96 PPQ, 3/4, one C4 lasting three quarter notes.
            byte[] midi = Convert.FromHexString("4D546864000000060000000100604D54726B0000001500FF58040302180800903C648240803C0000FF2F00");
            File.WriteAllBytes(first, midi);
            File.WriteAllBytes(second, midi);
            var loaded = MidiFileReader.Load(first);
            Require(loaded.Bars?.Next(100, 1800) == new MidiBar(2, 1500), "The MIDI reader uses the file's explicit meter.");
            var snapshot = new ConcertSnapshot { Name = "Octet Check", PlaylistName = "Octet",
                Role = 1, PlaylistFiles = [first, second], SelectedMidiPath = second };
            snapshot.Songs[first] = new ConcertSnapshotSong { Cue = new ConcertSongCue { Announcement = "Ready" } };
            snapshot.SmartPlaylist.PlayNext.Add(second);
            var stage = new StageFormationPreset { Name = "Stage", TerritoryId = 1,
                Anchor = new(1, 2, 3), Members = [new() { ContentId = 7, Offset = new(3, 0, 2) }, new() { ContentId = 8 }] };
            string pack = Path.Combine(scratch, "Octet.zip");
            ConcertPack.Export(pack, snapshot, [first], stage);
            var unpacked = ConcertPack.Open(pack, scratch, [second]);
            Require(unpacked.Snapshot.PlaylistFiles.All(File.Exists) &&
                unpacked.Snapshot.PlaylistFiles[1] == unpacked.Snapshot.SelectedMidiPath &&
                unpacked.Snapshot.SmartPlaylist.PlayNext.Single() == unpacked.Snapshot.SelectedMidiPath &&
                unpacked.Snapshot.Songs.Values.Single().Cue?.Announcement == "Ready" &&
                unpacked.Stage?.Members[0].Offset == stage.Members[0].Offset,
                "Concert pack round-trip preserves cues, path references and stage positions.");
            bool rejected = false;
            try { _ = ConcertPack.Open(pack, scratch, []); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Omitted MIDI must be present by fingerprint.");
        }
        finally { Directory.Delete(scratch, recursive: true); }
        Console.WriteLine("PASS: tempo and meter, selected next-bar rejoin, held note-off, timing, octet relay, and portable packs.");
    }

    private static long Ticks(double ms) => (long)(ms * Stopwatch.Frequency / 1000);
    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
