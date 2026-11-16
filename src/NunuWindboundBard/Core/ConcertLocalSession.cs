using System.Diagnostics;
using System.Text.Json;

namespace NunuWindboundBard.Core;

internal sealed record ConcertRejoinCue(Guid Id, ulong Target, long LeaderSignal, long Origin, long AtQpc,
    long Bar, double SongMs, double Speed, string Hash, string Parts, int Instrument);

internal sealed record ConcertLocalPeer(ulong Member, long Written, string Version, long Signal, long Origin,
    string Hash, double Speed, string Parts, int Instrument, bool Ready, bool Playing, string State,
    EnsembleDispatchTiming Timing, Guid SeenCue, string Result, ConcertRejoinCue? Cue,
    Guid ScanRequested, Guid ScanFinished, string[] Files);

internal static class ConcertLocalSession
{
    internal const int SendLeadMs = 900;
    internal const int LateLimitMs = 100;
    internal const int FreshMs = 2700;
    internal const int MaxInventory = 5000;
    private const int MaxMessageBytes = 256 * 1024;

    internal static double Milliseconds(long delta) => delta * 1000.0 / Stopwatch.Frequency;
    internal static bool Fresh(long sent, long now) => sent > 0 && sent <= now &&
        Milliseconds(now - sent) <= FreshMs;

    internal static bool SameTake(long a, long b) => a > 0 && b > 0 &&
        Math.Abs(Milliseconds(a - b)) <= 1500;

    internal static ConcertRejoinCue? Plan(MidiBarClock clock, ulong member, long signal, long origin,
        double speed, double end, string hash, string parts, int instrument, long now)
    {
        if (origin <= 0 || now < origin || !double.IsFinite(speed) || speed < .5 || speed > 2) return null;
        var boundary = clock.Next((Milliseconds(now - origin) + SendLeadMs) * speed, end);
        if (boundary is not { } bar) return null;
        var at = origin + (long)Math.Round(bar.TimeMs * Stopwatch.Frequency / (1000d * speed));
        return new(Guid.NewGuid(), member, signal, origin, at, bar.Number, bar.TimeMs, speed, hash, parts, instrument);
    }

    internal static bool ValidCue(ConcertRejoinCue cue, ulong me, long localSignal, long now) =>
        cue.Id != Guid.Empty && cue.Target == me && SameTake(cue.LeaderSignal, localSignal) &&
        cue.Origin > 0 && cue.AtQpc >= cue.Origin && cue.AtQpc > now &&
        Milliseconds(cue.AtQpc - now) < 120_000 && cue.Bar > 0 &&
        double.IsFinite(cue.SongMs) && cue.SongMs >= 0 && double.IsFinite(cue.Speed) &&
        cue.Speed is >= .5 and <= 2 &&
        Math.Abs(Milliseconds(cue.AtQpc - cue.Origin) * cue.Speed - cue.SongMs) < 0.1;

    internal static Dictionary<ulong, ConcertLocalPeer> Exchange(string directory, ConcertLocalPeer local, ulong[] peers)
    {
        Directory.CreateDirectory(directory);
        string destination = PathFor(directory, local.Member);
        string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(local);
            if (bytes.Length > MaxMessageBytes) throw new InvalidDataException("Local concert status is too large.");
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, destination, overwrite: true);
        }
        finally { File.Delete(temp); }

        var result = new Dictionary<ulong, ConcertLocalPeer>();
        foreach (ulong id in peers)
        {
            try
            {
                using var file = new FileStream(PathFor(directory, id), FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
                if (file.Length is <= 0 or > MaxMessageBytes) continue;
                var peer = JsonSerializer.Deserialize<ConcertLocalPeer>(file);
                if (peer is not null && peer.Member == id && peer.Files is { Length: <= MaxInventory } &&
                    peer.Version is { Length: <= 32 } && peer.Result is { Length: <= 160 } &&
                    peer.State is { Length: <= 80 } && peer.Hash is { Length: <= 64 } &&
                    Fresh(peer.Written, Stopwatch.GetTimestamp()))
                    result[id] = peer;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return result;
    }

    private static string PathFor(string directory, ulong member) => Path.Combine(directory, $"{member:X16}.json");
}
