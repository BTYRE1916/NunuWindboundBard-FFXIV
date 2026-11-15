using NunuWindboundBard.Core;

string root = Path.Combine(Path.GetTempPath(), "WindboundSongSync-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string first = Path.Combine(root, "Bard melody.mid");
    string sameName = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "Other Album")).FullName, "Bard melody.mid");
    string alias = Path.Combine(root, "Renamed melody.midi");
    string bad = Path.Combine(root, "Invalid.mid");
    File.Copy(Sample("Bard-37-Note-Calibration.mid"), first);
    File.Copy(Sample("Bard-Chord-Callback-Test.mid"), sameName);
    File.Copy(first, alias);
    File.WriteAllText(bad, "This is a text document stored with a MIDI extension.");
    string stage = LocalBandSongSync.GetStageDirectory(root, "Hunters");
    Check(stage == LocalBandSongSync.GetStageDirectory(root, "  hunters "), "Normalized stage");
    string otherStage = LocalBandSongSync.GetStageDirectory(root, "Dancers");
    var sourceCache = new Dictionary<string, LocalBandSongSync.PublishedFile>();
    var published = LocalBandSongSync.Publish(stage, [first, sameName, alias, bad], sourceCache, default);
    Check(published.Count == 2 && published.Skipped == 1, "Copies deduplicated and invalid MIDI skipped");
    Check(LocalBandSongSync.Receive(otherStage, Guid.Empty, [], [], default) is null, "Stage isolation");
    var follower = LocalBandSongSync.Receive(stage, Guid.Empty, [], [], default)!;
    Check(follower.Songs.Count == 2, "Late follower receives two songs");
    Check(follower.Songs[0].Path != follower.Songs[1].Path &&
        Path.GetFileName(follower.Songs[0].Path) == Path.GetFileName(follower.Songs[1].Path),
        "Distinct MIDI files with identical names");
    Check(File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(follower.Songs[0].Path)) &&
        File.ReadAllBytes(sameName).SequenceEqual(File.ReadAllBytes(follower.Songs[1].Path)), "Byte identity and order");
    Check(LocalBandSongSync.Receive(stage, follower.Revision, [], [], default) is null, "Same revision ignored");
    var otherFollower = LocalBandSongSync.Receive(stage, Guid.Empty, [], [alias], default)!;
    Check(otherFollower.Songs[0].AlreadyListed && otherFollower.Songs[0].Path == alias &&
        !otherFollower.Songs[1].AlreadyListed, "Existing files retained by digest");
    string[] receipts = follower.Songs.Select(s => s.Song.Hash).ToArray();
    var reopened = LocalBandSongSync.Receive(stage, Guid.Empty, receipts, [], default)!;
    Check(reopened.Songs.Count == 0, "Receipts survive reopening and local removal");
    File.Delete(first);
    _ = MidiFileReader.Load(follower.Songs[0].Path);
    Check(File.Exists(follower.Songs[0].Path), "Copy survives removal of conductor source");
    string added = Path.Combine(root, "Lute.mid");
    File.Copy(Sample("Windbound-MIDI-Instrument-and-Octave-Tags.mid"), added);
    var octetFollowers = new LocalBandSongSync.Reception[7];
    Parallel.For(0, octetFollowers.Length, i =>
    {
        octetFollowers[i] = LocalBandSongSync.Receive(stage, Guid.Empty, [], [alias], default)!;
        Check(octetFollowers[i].Songs.Count == 2 && octetFollowers[i].Songs[0].AlreadyListed,
            $"Follower {i + 1} receives the initial playlist without duplicating a local file");
    });
    LocalBandSongSync.Publish(stage, [sameName, added], published.Sources, default);
    Parallel.For(0, octetFollowers.Length, i =>
    {
        var current = octetFollowers[i];
        var seen = current.Songs.Select(entry => entry.Song.Hash).ToArray();
        var next = LocalBandSongSync.Receive(stage, current.Revision, seen, [], default)!;
        Check(next.Songs.Count == 1 && File.ReadAllBytes(next.Songs[0].Path).SequenceEqual(File.ReadAllBytes(added)),
            $"Follower {i + 1} receives the same new MIDI copy");
        Check(LocalBandSongSync.Receive(stage, next.Revision, seen, [], default) is null,
            $"Follower {i + 1} ignores a revision it has already received");
    });
    var update = LocalBandSongSync.Receive(stage, follower.Revision, receipts, [], default)!;
    Check(update.Songs.Count == 1 && Path.GetFileName(update.Songs[0].Path) == "Lute.mid", "New song only");
    var cancelled = new CancellationToken(canceled: true);
    try
    {
        LocalBandSongSync.Publish(stage, [added], sourceCache, cancelled);
        throw new Exception("Cancelled publish completed");
    }
    catch (OperationCanceledException) { }
    Check(LocalBandSongSync.Receive(stage, update.Revision, [], [], default) is null, "Cancellation leaves prior index");

    // Readers can observe either complete revision while the conductor replaces it.
    Parallel.Invoke(
        () =>
        {
            for (int i = 0; i < 50; i++) LocalBandSongSync.Publish(stage, [sameName, added], sourceCache, default);
        },
        () =>
        {
            for (int i = 0; i < 50; i++)
                Check(LocalBandSongSync.Receive(stage, Guid.Empty, [], [], default)?.Songs.Count == 2, "Atomic index");
        });
    Console.WriteLine("Local song sync checks passed, including an octet and concurrent reads.");
}
finally { Directory.Delete(root, recursive: true); }

static string Sample(string file) => Path.Combine(AppContext.BaseDirectory, file);
static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
}
