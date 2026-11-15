using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NunuWindboundBard.Core;

// Standalone .NET 10 test of the EXACT network source used by the plugin.
// No FFXIV client required for this connectivity/clock/packet test.
static void Require(bool condition, string text)
{
    if (!condition) throw new Exception("FAILED: " + text);
}
static bool Until(Func<bool> condition, int timeoutMs = 9000)
{
    var timer = Stopwatch.StartNew();
    while (timer.ElapsedMilliseconds < timeoutMs)
    {
        if (condition()) return true;
        Thread.Sleep(50);
    }
    return condition();
}
var probe = new TcpListener(IPAddress.Loopback, 0);
probe.Start();
int port = ((IPEndPoint)probe.LocalEndpoint).Port;
probe.Stop();
string stage = "WindboundTest_" + Guid.NewGuid().ToString("N")[..8];
string code = MultiDeviceEnsemble.NewSessionCode();
using var host = new MultiDeviceEnsemble();
using var follower = new MultiDeviceEnsemble();
host.Host(port, stage, code);
follower.Join("127.0.0.1", port, stage, code, "Edea Uematsu", 123456789UL);
Require(Until(() => follower.IsConnected && host.Performers.Count == 1), "Handshake / join");
byte[] midi = {
    0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 0, 0, 1, 0, 96,
    0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 4, 0, 0xFF, 0x2F, 0
};
string path = Path.Combine(Path.GetTempPath(), "WindboundNetworkTest_" + Guid.NewGuid().ToString("N") + ".mid");
try
{
    File.WriteAllBytes(path, midi);
    bool first = host.PublishSession(path, new RemoteArrangementSettings(true, 0, 0, 80, 95,
        new Dictionary<string, int> { ["0:0"] = 12 }), new Dictionary<string, ulong> { ["0:0"] = 123456789 });
    Require(first && !host.PublishSession(path, new RemoteArrangementSettings(true, 0, 0, 80, 95,
        new Dictionary<string, int> { ["0:0"] = 12 }), new Dictionary<string, ulong> { ["0:0"] = 123456789 }),
        "Same revision not republished");
    NetworkNotice? session = null;
    Require(Until(() => {
        while (follower.TryDequeue(out var n)) if (n.Kind == NetworkNoticeKind.Session) session = n;
        return session is not null;
    }), "MIDI/assignment transfer");
    Require(session!.Midi!.SequenceEqual(midi), "Transferred MIDI bytes exact");
    Require(session.Arrangement?.Octaves.GetValueOrDefault("0:0") == 12, "Octaves transferred");
    Require(session.Assignments?.GetValueOrDefault("0:0") == 123456789, "Party-track map transferred");
    Require(Until(() => follower.HasClockSync), "Clock probe sampling");
    follower.SendReady(session.Revision, session.Hash, true, "Ready for duet");
    Require(Until(() => host.CanCue(out _)), "Ready acknowledgement");
    double startAt = MultiDeviceEnsemble.NowMs() + 4000;
    host.BroadcastStart(startAt, 1000);
    NetworkNotice? cue = null;
    Require(Until(() => {
        while (follower.TryDequeue(out var n)) if (n.Kind == NetworkNoticeKind.Cue) cue = n;
        return cue is not null;
    }), "Start cue arrived");
    Require(follower.TryConvertHostTimeToLocalQpc(cue!.HostStartMs, out long localQpc), "Converted start time");
    double remaining = (localQpc - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency;
    Require(remaining > 1 && remaining < 5, "Count-in preserved across devices");
    host.BroadcastStop();
    Require(Until(() => {
        while (follower.TryDequeue(out var n)) if (n.Kind == NetworkNoticeKind.Stop) return true;
        return false;
    }), "STOP cue arrived");
    Console.WriteLine($"PASS: pairing, MIDI hash, assignments, octaves, clock RTT {follower.RoundTripMs:0.0}ms, READY, START/STOP.");
}
finally { try { File.Delete(path); } catch {} }
