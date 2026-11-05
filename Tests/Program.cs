using NunuWindboundBard.Core;
using System.Diagnostics;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The named shared memory cue bus is Windows-only.");
    return 2;
}
string group = "SmokeTest-" + Guid.NewGuid().ToString("N");
using var leader = new EnsembleCoordinator();
using var follower = new EnsembleCoordinator();
try
{
    if (follower.Poll(group).HasValue)
        throw new Exception("Unexpected initial cue");
    long start = EnsembleCoordinator.StartAfterMilliseconds(4500);
    var published = leader.Publish(group, EnsembleCommand.Start, start, 123456789L, 1000);
    var got = follower.Poll(group) ?? throw new Exception("Follower did not receive start cue");
    if (got.Command != EnsembleCommand.Start || got.Sequence != published.Sequence ||
        got.StartQpc != start || got.SongHash != 123456789L || got.SpeedThousand != 1000)
        throw new Exception("Start data mismatch");
    if (follower.Poll(group).HasValue)
        throw new Exception("Duplicate cue was delivered");
    if (EnsembleCoordinator.RemainingSeconds(start) < 2.0)
        throw new Exception("High-resolution count-in too short");
    leader.Publish(group, EnsembleCommand.Stop, 0, 123456789L, 1000);
    if (follower.Poll(group)?.Command != EnsembleCommand.Stop)
        throw new Exception("Stop cue not delivered");
    Console.WriteLine("PASS: same-PC start, song ID, speed, deduplication, countdown and STOP");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine("FAIL: " + e);
    return 1;
}
