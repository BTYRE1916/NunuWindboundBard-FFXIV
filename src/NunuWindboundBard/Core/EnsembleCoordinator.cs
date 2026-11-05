using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NunuWindboundBard.Core;

public enum EnsembleRole { Solo = 0, Conductor = 1, Follower = 2 }
public enum EnsembleCommand { None = 0, Start = 1, Stop = 2 }
public readonly record struct EnsembleCue(long Sequence, long StartQpc, long SongHash, int SpeedThousand,
    EnsembleCommand Command);

/// <summary>
/// Same-Windows-session conductor/follower cue bus. All plugin clients on the same PC
/// share QueryPerformanceCounter (Stopwatch.GetTimestamp) ticks, which can be used
/// as a common, monotonic start instant. This is NOT a network/party-chat sync layer,
/// nor does it hook the FFXIV native ensemble ready-check.
/// </summary>
public sealed class EnsembleCoordinator : IDisposable
{
    private const int Magic = unchecked((int)0x4E574233); // NWB3
    private const int ProtocolVersion = 1;
    private const int MapSize = 64;
    private string openedGroup = "";
    private MemoryMappedFile? map;
    private MemoryMappedViewAccessor? accessor;
    private Mutex? mutex;
    private long previousSequence;

    public string Group => openedGroup;

    public static long FingerprintFile(string path)
    {
        using var file = File.OpenRead(path);
        byte[] digest = SHA256.HashData(file);
        return BitConverter.ToInt64(digest, 0);
    }

    public static long StartAfterMilliseconds(int milliseconds) =>
        checked(Stopwatch.GetTimestamp() + (long)Math.Round(
            Math.Clamp(milliseconds, 1500, 15000) * (double)Stopwatch.Frequency / 1000.0));

    public static double RemainingSeconds(long targetQpc) =>
        Math.Max(0, (targetQpc - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);

    private void Open(string group)
    {
        group = group.Trim();
        if (group.Length is < 1 or > 50)
            throw new ArgumentException("Ensemble group must be between 1 and 50 characters.", nameof(group));
        if (openedGroup == group && map is not null) return;
        Close();
        string shortHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(group.ToLowerInvariant())))[..20];
        string name = @"Local\NunuWindboundBard_" + shortHash;
        // 'Local' Windows kernel objects are only shared among processes in this login session.
        map = MemoryMappedFile.CreateOrOpen(name, MapSize, MemoryMappedFileAccess.ReadWrite);
        accessor = map.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
        mutex = new Mutex(false, name + "_lock");
        openedGroup = group;
        previousSequence = 0;
    }

    public EnsembleCue Publish(string group, EnsembleCommand command, long startQpc,
        long songHash, int speedThousand)
    {
        Open(group);
        bool locked = false;
        try
        {
            try { locked = mutex!.WaitOne(TimeSpan.FromMilliseconds(50)); }
            catch (AbandonedMutexException) { locked = true; }
            if (!locked) throw new IOException("Ensemble bus is busy. Please try again.");
            long seq = Math.Max(DateTime.UtcNow.Ticks, accessor!.ReadInt64(8) + 1);
            accessor.Write(0, 0); // invalidate the record until all fields are written
            accessor.Write(4, ProtocolVersion);
            accessor.Write(8, seq);
            accessor.Write(16, startQpc);
            accessor.Write(24, songHash);
            accessor.Write(32, speedThousand);
            accessor.Write(36, (int)command);
            accessor.Write(0, Magic);
            accessor.Flush();
            previousSequence = seq;
            return new EnsembleCue(seq, startQpc, songHash, speedThousand, command);
        }
        finally { if (locked) mutex!.ReleaseMutex(); }
    }

    public EnsembleCue? Poll(string group)
    {
        Open(group);
        bool locked = false;
        try
        {
            try { locked = mutex!.WaitOne(0); }
            catch (AbandonedMutexException) { locked = true; }
            if (!locked || accessor is null || accessor.ReadInt32(0) != Magic ||
                accessor.ReadInt32(4) != ProtocolVersion) return null;
            long seq = accessor.ReadInt64(8);
            if (seq <= previousSequence) return null;
            previousSequence = seq;
            var command = (EnsembleCommand)accessor.ReadInt32(36);
            if (command is not (EnsembleCommand.Start or EnsembleCommand.Stop)) return null;
            return new EnsembleCue(seq, accessor.ReadInt64(16), accessor.ReadInt64(24),
                accessor.ReadInt32(32), command);
        }
        finally { if (locked) mutex!.ReleaseMutex(); }
    }

    private void Close()
    {
        accessor?.Dispose(); accessor = null;
        map?.Dispose(); map = null;
        mutex?.Dispose(); mutex = null;
        openedGroup = "";
        previousSequence = 0;
    }

    public void Dispose() => Close();
}
