using System;
using System.Collections.Generic;
using NunuWindboundBard.Core;

namespace NunuWindboundBard.Core
{
    public readonly record struct MidiLaneKey(int Track, int Channel);
}

static class Smoke
{
    static void Main()
    {
        using var sender = new EnsembleAssignmentBus();
        using var receiver = new EnsembleAssignmentBus();
        string group = "smoke-" + Guid.NewGuid().ToString("N");
        long hash = 0x123456789AB;
        Dictionary<MidiLaneKey,ulong> tracks = new()
        {
            [new MidiLaneKey(0, 0)] = 12345,
            [new MidiLaneKey(1, 1)] = 67890,
            [new MidiLaneKey(2, 0)] = 67890
        };
        sender.Publish(group, hash, tracks);
        var data = receiver.Poll(group) ?? throw new Exception("Missing party map");
        if (data.SongHash != hash || data.Parts.Count != 3 ||
            data.Parts[new MidiLaneKey(1, 1)] != 67890 || receiver.Poll(group) != null)
            throw new Exception("Party assignment data mismatch");
        sender.Publish(group, hash, new Dictionary<MidiLaneKey,ulong>());
        var cleared = receiver.Poll(group) ?? throw new Exception("Missing clear message");
        if (cleared.Parts.Count != 0) throw new Exception("Clear didn't update map");
        Console.WriteLine("PASS: same-PC party part assignment publish, poll, clear.");
    }
}
