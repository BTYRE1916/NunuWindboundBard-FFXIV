using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NunuWindboundBard.Core;

public sealed record EnsembleAssignmentSnapshot(long Revision, long SongHash,
    IReadOnlyDictionary<MidiLaneKey, ulong> Parts);

/// <summary>
/// Assigns MIDI lanes to FFXIV party ContentIds on the SAME PC and Windows login session.
/// A separate map from the shared-clock start bus keeps v0.3 cue records intact.
/// Not an internet/LAN or game-chat transport.
/// </summary>
public sealed class EnsembleAssignmentBus : IDisposable
{
    private const int Magic = unchecked((int)0x4E574241); // NWBA
    private const int Version = 1;
    private const int Capacity = 512;
    private const int MapSize = 16384;
    private MemoryMappedFile? map;
    private MemoryMappedViewAccessor? accessor;
    private Mutex? mutex;
    private string group = "";
    private long lastRevision;

    private void Open(string stage)
    {
        stage = stage.Trim();
        if (stage.Length is < 1 or > 50) throw new ArgumentException("Stage name must be 1–50 characters.");
        if (map is not null && group == stage) return;
        Close();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stage.ToLowerInvariant())))[..20];
        string name = @"Local\NunuWindboundAssignments_" + hash;
        map = MemoryMappedFile.CreateOrOpen(name, MapSize, MemoryMappedFileAccess.ReadWrite);
        accessor = map.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
        mutex = new Mutex(false, name + "_lock");
        group = stage;
        lastRevision = 0;
    }

    public long Publish(string stage, long songHash, IReadOnlyDictionary<MidiLaneKey, ulong> assignments)
    {
        Open(stage);
        if (assignments.Count > Capacity) throw new InvalidOperationException("Too many MIDI parts for the ensemble bus.");
        bool locked = false;
        try
        {
            try { locked = mutex!.WaitOne(TimeSpan.FromMilliseconds(50)); }
            catch (AbandonedMutexException) { locked = true; }
            if (!locked) throw new IOException("The local ensemble assignment bus is busy.");
            long revision = Math.Max(DateTime.UtcNow.Ticks, accessor!.ReadInt64(8) + 1);
            accessor.Write(0, 0); // publish complete record atomically under mutex
            accessor.Write(4, Version);
            accessor.Write(8, revision);
            accessor.Write(16, songHash);
            accessor.Write(24, assignments.Count);
            int index = 0;
            foreach (var pair in assignments)
            {
                int offset = 32 + 16 * index++;
                accessor.Write(offset, pair.Key.Track);
                accessor.Write(offset + 4, pair.Key.Channel);
                accessor.Write(offset + 8, unchecked((long)pair.Value));
            }
            accessor.Write(0, Magic);
            accessor.Flush();
            lastRevision = revision;
            return revision;
        }
        finally { if (locked) mutex!.ReleaseMutex(); }
    }

    public EnsembleAssignmentSnapshot? Poll(string stage)
    {
        Open(stage);
        bool locked = false;
        try
        {
            try { locked = mutex!.WaitOne(0); }
            catch (AbandonedMutexException) { locked = true; }
            if (!locked || accessor!.ReadInt32(0) != Magic || accessor.ReadInt32(4) != Version) return null;
            long revision = accessor.ReadInt64(8);
            if (revision <= lastRevision) return null;
            int count = accessor.ReadInt32(24);
            if (count is < 0 or > Capacity) return null;
            long songHash = accessor.ReadInt64(16);
            Dictionary<MidiLaneKey, ulong> assignments = new();
            for (int i = 0; i < count; i++)
            {
                int offset = 32 + 16 * i;
                int track = accessor.ReadInt32(offset);
                int channel = accessor.ReadInt32(offset + 4);
                ulong id = unchecked((ulong)accessor.ReadInt64(offset + 8));
                if (track is < 0 or > 10000 || channel is < 0 or > 15 || id == 0) continue;
                assignments[new MidiLaneKey(track, channel)] = id;
            }
            lastRevision = revision;
            return new EnsembleAssignmentSnapshot(revision, songHash, assignments);
        }
        finally { if (locked) mutex!.ReleaseMutex(); }
    }

    private void Close()
    {
        accessor?.Dispose(); accessor = null;
        map?.Dispose(); map = null;
        mutex?.Dispose(); mutex = null;
        group = "";
        lastRevision = 0;
    }
    public void Dispose() => Close();
}
