using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Linq;

namespace NunuWindboundBard.Core;

public enum GamePartyCueKind { Song, Stop, Start, Prepare, Ready, Failed,
    NativeFile, NativeFileReady, NativeFileFailed, NativeSetInstruments,
    NativeSetTracks, NativeTracksReady, NativeTracksFailed, NativeStopUnequip,
    StageProbe, StageReport }
public readonly record struct GamePartyCue(GamePartyCueKind Kind, int PlaylistIndex, string Hash,
    long StartUnixMs = 0, int SpeedThousand = 0, string Nonce = "",
    string RosterTag = "", string PackedAssignments = "", string PackedInstruments = "",
    int StageFlags = 0, int StageInstrument = 0, int StageLanes = 0,
    int TargetSlot = 0);

/// <summary>
/// A deliberately compact, strictly validated FFXIV party-chat protocol.
/// Every command is visible in party chat. No MIDI bytes, filesystem paths,
/// program execution or network addresses are transmitted.
/// </summary>
public static class GamePartyCueProtocol
{
    public const string Prefix = "!windbound";
    public const int MaxChatCharacters = 130;

    public static string GetHash(string midiPath)
    {
        using var stream = File.OpenRead(midiPath);
        byte[] digest = SHA256.HashData(stream);
        return Convert.ToHexString(digest, 0, 8);
    }

    public static string Song(int zeroBasedIndex, string midiPath)
    {
        if (zeroBasedIndex is < 0 or > 9999) throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex));
        return $"{Prefix} song {zeroBasedIndex + 1} {GetHash(midiPath)}";
    }

    public static string Stop => $"{Prefix} stop";

    /// <summary>
    /// One visible party message instructs opted-in clients to schedule a performance
    /// at the indicated Unix time. No FFXIV Ensemble Ready Check is required.
    /// Hash and roster tag prevent mismatched material from being cued.
    /// Nonce prevents a duplicated chat event from starting the song twice.
    /// </summary>
    public static string Start(int zeroBasedIndex, string midiPath, long startUnixMs,
        int speedThousand, string rosterTag, string packedAssignments, string? nonce = null)
    {
        if (zeroBasedIndex is < 0 or > 9999) throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex));
        if (startUnixMs is < 1577836800000L or > 4102444800000L)
            throw new ArgumentOutOfRangeException(nameof(startUnixMs));
        if (speedThousand is < 500 or > 2000) throw new ArgumentOutOfRangeException(nameof(speedThousand));
        nonce ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        if (!IsHex(nonce, 16) || !IsHex(rosterTag, 8) || !ValidRoles(packedAssignments))
            throw new ArgumentException("Invalid One-Click conductor cue fields.");
        string command = $"{Prefix} play {zeroBasedIndex + 1} {GetHash(midiPath)} {startUnixMs} " +
                         $"{speedThousand} {nonce.ToUpperInvariant()} {rosterTag.ToUpperInvariant()} {packedAssignments}";
        if (command.Length > MaxChatCharacters)
            throw new InvalidOperationException("Too many track assignments for one FFXIV party message. " +
                "Split this MIDI into fewer parts, or clear the party map and let performers choose tracks manually.");
        return command;
    }

    /// <summary>Prepare performers' instruments before the one-click START cue.
    /// Followers use the same existing session-level leader-start permission.
    /// </summary>
    public static string Prepare(int zeroBasedIndex, string midiPath, string nonce,
        string rosterTag, string packedInstruments)
    {
        if (zeroBasedIndex is < 0 or > 9999 || !IsHex(nonce, 16) ||
            !IsHex(rosterTag, 8) || !ValidInstrumentMap(packedInstruments))
            throw new ArgumentException("Invalid party instrument preparation command.");
        string command = $"{Prefix} prep {zeroBasedIndex + 1} {GetHash(midiPath)} " +
            $"{nonce.ToUpperInvariant()} {rosterTag.ToUpperInvariant()} {packedInstruments}";
        if (command.Length > MaxChatCharacters)
            throw new InvalidOperationException("Instrument preparation message is too long.");
        return command;
    }

    /// <summary>Follower status. The sender is verified against the party roster.</summary>
    public static string Ready(string nonce, int instrumentId)
    {
        if (!IsHex(nonce, 16) || instrumentId is < 0 or > 28)
            throw new ArgumentException("Invalid instrument READY acknowledgement.");
        return $"{Prefix} ready {nonce.ToUpperInvariant()} {instrumentId}";
    }

    public static string Failed(string nonce, string reasonCode)
    {
        if (!IsHex(nonce, 16) || reasonCode.Length is < 2 or > 16 ||
            reasonCode.Any(c => !(char.IsLetterOrDigit(c) || c == '-')))
            throw new ArgumentException("Invalid instrument preparation failure message.");
        return $"{Prefix} fail {nonce.ToUpperInvariant()} {reasonCode.ToUpperInvariant()}";
    }

    /// <summary>Native ensemble file + optionally party track assignments.
    /// This SELECTS a prepared MIDI locally, never starts audio.
    /// All followers must already have the exact MIDI file on disk.</summary>
    public static string NativeFile(int zeroBasedIndex, string path, string nonce,
        string rosterTag, string packedParts, int targetSlot = 0)
    {
        if (targetSlot is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(targetSlot));
        if (zeroBasedIndex is < 0 or > 9999 || !IsHex(nonce, 16) ||
            !IsHex(rosterTag, 8) || !ValidRoles(packedParts))
            throw new ArgumentException("Invalid native ensemble file selection.");
        string result = $"{Prefix} file {zeroBasedIndex + 1} {GetHash(path)} " +
            $"{nonce.ToUpperInvariant()} {rosterTag.ToUpperInvariant()} {packedParts}" + TargetSuffix(targetSlot);
        if (result.Length > MaxChatCharacters)
            throw new InvalidOperationException("Too many MIDI lanes for one party-chat file selection.");
        return result;
    }

    public static string NativeFileReady(string nonce) =>
        IsHex(nonce, 16) ? $"{Prefix} loaded {nonce.ToUpperInvariant()}" :
        throw new ArgumentException("Invalid MIDI selection acknowledgement nonce.");

    public static string NativeFileFailed(string nonce, string reasonCode) =>
        IsHex(nonce, 16) && ValidReason(reasonCode) ?
        $"{Prefix} filefail {nonce.ToUpperInvariant()} {reasonCode.ToUpperInvariant()}" :
        throw new ArgumentException("Invalid MIDI selection failure response.");

    public static string NativeSetInstruments(string nonce, string rosterTag, string packedInstruments,
        int targetSlot = 0)
    {
        if (!IsHex(nonce, 16) || !IsHex(rosterTag, 8) || !ValidInstrumentMap(packedInstruments))
            throw new ArgumentException("Invalid party instrument SET command.");
        if (targetSlot is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(targetSlot));
        string command = $"{Prefix} inst {nonce.ToUpperInvariant()} {rosterTag.ToUpperInvariant()} {packedInstruments}" + TargetSuffix(targetSlot);
        if (command.Length > MaxChatCharacters) throw new InvalidOperationException("Instrument command too long.");
        return command;
    }

    /// <summary>Native ensemble track map only. MIDI hash and party roster are
    /// checked before a follower may change its own selected lanes. No music START.</summary>
    public static string NativeSetTracks(string midiHash, string nonce,
        string rosterTag, string packedTracks, int targetSlot = 0)
    {
        if (targetSlot is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(targetSlot));
        if (!IsHex(midiHash, 16) || !IsHex(nonce, 16) || !IsHex(rosterTag, 8) ||
            !ValidRoles(packedTracks) || packedTracks == "-")
            throw new ArgumentException("Invalid conductor Set Party Tracks command.");
        string result = $"{Prefix} tracks {midiHash.ToUpperInvariant()} {nonce.ToUpperInvariant()} " +
            $"{rosterTag.ToUpperInvariant()} {packedTracks}" + TargetSuffix(targetSlot);
        if (result.Length > MaxChatCharacters)
            throw new InvalidOperationException("Track map exceeds the FFXIV party-chat limit; simplify the MIDI parts.");
        return result;
    }

    public static string NativeTracksReady(string nonce) =>
        IsHex(nonce, 16) ? $"{Prefix} trackok {nonce.ToUpperInvariant()}" :
        throw new ArgumentException("Invalid track ACK nonce.");

    public static string NativeTracksFailed(string nonce, string reasonCode) =>
        IsHex(nonce, 16) && ValidReason(reasonCode) ?
        $"{Prefix} trackfail {nonce.ToUpperInvariant()} {reasonCode.ToUpperInvariant()}" :
        throw new ArgumentException("Invalid track failure response.");

    /// <summary>The conductor requests a group STOP followed by unequipping Performance.
    /// Distinct from legacy STOP, so existing clients will reject rather than partly act.</summary>
    public static string NativeStopAndUnequip(string nonce) =>
        IsHex(nonce, 16) ? $"{Prefix} stow {nonce.ToUpperInvariant()}" :
        throw new ArgumentException("Invalid STOP-and-unequip nonce.");

    /// <summary>Leader-requested status snapshot. No playback action.
    /// Limit periodic probes; party chat is not a high-frequency telemetry bus.</summary>
    public static string StageProbe(string nonce, string midiHash) =>
        IsHex(nonce, 16) && IsHex(midiHash, 16) ?
        $"{Prefix} stage {nonce.ToUpperInvariant()} {midiHash.ToUpperInvariant()}" :
        throw new ArgumentException("Invalid stage request.");

    public static string StageReport(string nonce, string midiHash,
        int flags, int instrument, int enabledLanes)
    {
        if (!IsHex(nonce, 16) || !IsHex(midiHash, 16) ||
            flags is < 0 or > 255 || instrument is < 0 or > 28 || enabledLanes is < 0 or > 512)
            throw new ArgumentException("Invalid stage report.");
        return $"{Prefix} stageok {nonce.ToUpperInvariant()} {midiHash.ToUpperInvariant()} " +
            $"{flags:X2} {instrument} {enabledLanes}";
    }

    private static string TargetSuffix(int slot) => slot == 0 ? "" : " t" + slot;

    // An optional target suffix is scoped to a ContentId-sorted roster slot.
    // It is interpreted ONLY after the existing leader identity verification.
    private static bool TryTarget(string[] words, int normalLength, out int slot)
    {
        slot = 0;
        if (words.Length == normalLength) return true;
        if (words.Length != normalLength + 1 || words[normalLength].Length is < 2 or > 3 ||
            (words[normalLength][0] != 't' && words[normalLength][0] != 'T')) return false;
        return int.TryParse(words[normalLength].AsSpan(1), NumberStyles.None,
            CultureInfo.InvariantCulture, out slot) && slot is >= 1 and <= 15;
    }

    private static bool ValidReason(string value) =>
        value.Length is >= 2 and <= 16 &&
        value.All(c => char.IsLetterOrDigit(c) || c == '-');

    public static bool TryParse(string? text, out GamePartyCue cue)
    {
        cue = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 150) return false;
        string[] p = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 || !p[0].Equals(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (p.Length == 4 && p[1].Equals("stage", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16) || !IsHex(p[3], 16)) return false;
            cue = new GamePartyCue(GamePartyCueKind.StageProbe, -1, p[3].ToUpperInvariant(),
                Nonce: p[2].ToUpperInvariant());
            return true;
        }
        if (p.Length == 7 && p[1].Equals("stageok", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16) || !IsHex(p[3], 16) || !IsHex(p[4], 2) ||
                !int.TryParse(p[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int flags) ||
                !int.TryParse(p[5], NumberStyles.None, CultureInfo.InvariantCulture, out int instrument) ||
                instrument is < 0 or > 28 ||
                !int.TryParse(p[6], NumberStyles.None, CultureInfo.InvariantCulture, out int count) ||
                count is < 0 or > 512) return false;
            cue = new GamePartyCue(GamePartyCueKind.StageReport, -1, p[3].ToUpperInvariant(),
                Nonce: p[2].ToUpperInvariant(), StageFlags: flags,
                StageInstrument: instrument, StageLanes: count);
            return true;
        }
        if (TryTarget(p, 6, out int trackTarget) && p[1].Equals("tracks", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16) || !IsHex(p[3], 16) || !IsHex(p[4], 8) ||
                !ValidRoles(p[5]) || p[5] == "-") return false;
            cue = new GamePartyCue(GamePartyCueKind.NativeSetTracks, -1, p[2].ToUpperInvariant(),
                Nonce: p[3].ToUpperInvariant(), RosterTag: p[4].ToUpperInvariant(),
                PackedAssignments: p[5], TargetSlot: trackTarget);
            return true;
        }
        if (p.Length == 3 && p[1].Equals("trackok", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16)) return false;
            cue = new GamePartyCue(GamePartyCueKind.NativeTracksReady, -1, "", Nonce: p[2].ToUpperInvariant());
            return true;
        }
        if (p.Length == 4 && p[1].Equals("trackfail", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16) || !ValidReason(p[3])) return false;
            cue = new GamePartyCue(GamePartyCueKind.NativeTracksFailed, -1, "", Nonce: p[2].ToUpperInvariant(),
                PackedInstruments: p[3].ToUpperInvariant());
            return true;
        }
        if (p.Length == 3 && p[1].Equals("stow", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16)) return false;
            cue = new GamePartyCue(GamePartyCueKind.NativeStopUnequip, -1, "", Nonce: p[2].ToUpperInvariant());
            return true;
        }
        if (TryTarget(p, 7, out int fileTarget) && p[1].Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            if (!ParseIndex(p[2], out int fileIndex) || !IsHex(p[3], 16) ||
                !IsHex(p[4], 16) || !IsHex(p[5], 8) || !ValidRoles(p[6])) return false;
            cue = new GamePartyCue(GamePartyCueKind.NativeFile, fileIndex,
                p[3].ToUpperInvariant(), Nonce: p[4].ToUpperInvariant(),
                RosterTag: p[5].ToUpperInvariant(), PackedAssignments: p[6], TargetSlot: fileTarget);
            return true;
        }
        if (p.Length == 3 && p[1].Equals("loaded", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16)) return false;
            cue = new GamePartyCue(GamePartyCueKind.NativeFileReady, -1, "", Nonce: p[2].ToUpperInvariant());
            return true;
        }
        if (p.Length == 4 && p[1].Equals("filefail", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16) || !ValidReason(p[3])) return false;
            cue = new GamePartyCue(GamePartyCueKind.NativeFileFailed, -1, "", Nonce: p[2].ToUpperInvariant(),
                PackedInstruments: p[3].ToUpperInvariant());
            return true;
        }
        if (TryTarget(p, 5, out int instrumentTarget) && p[1].Equals("inst", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16) || !IsHex(p[3], 8) || !ValidInstrumentMap(p[4])) return false;
            cue = new GamePartyCue(GamePartyCueKind.NativeSetInstruments, -1, "", Nonce: p[2].ToUpperInvariant(),
                RosterTag: p[3].ToUpperInvariant(), PackedInstruments: p[4], TargetSlot: instrumentTarget);
            return true;
        }
        if (p.Length == 2 && p[1].Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            cue = new GamePartyCue(GamePartyCueKind.Stop, -1, "");
            return true;
        }
        if (p.Length == 7 && p[1].Equals("prep", StringComparison.OrdinalIgnoreCase))
        {
            if (!ParseIndex(p[2], out int prepIndex) || !IsHex(p[3], 16) ||
                !IsHex(p[4], 16) || !IsHex(p[5], 8) || !ValidInstrumentMap(p[6])) return false;
            cue = new GamePartyCue(GamePartyCueKind.Prepare, prepIndex, p[3].ToUpperInvariant(),
                Nonce: p[4].ToUpperInvariant(), RosterTag: p[5].ToUpperInvariant(),
                PackedInstruments: p[6]);
            return true;
        }
        if (p.Length == 4 && p[1].Equals("ready", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16) || !int.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out int instrument) ||
                instrument is < 0 or > 28) return false;
            cue = new GamePartyCue(GamePartyCueKind.Ready, -1, "", Nonce: p[2].ToUpperInvariant(),
                PackedInstruments: p[3]);
            return true;
        }
        if (p.Length == 4 && p[1].Equals("fail", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16) || p[3].Length is < 2 or > 16 ||
                p[3].Any(c => !(char.IsLetterOrDigit(c) || c == '-'))) return false;
            cue = new GamePartyCue(GamePartyCueKind.Failed, -1, "", Nonce: p[2].ToUpperInvariant(),
                PackedInstruments: p[3].ToUpperInvariant());
            return true;
        }
        if (p.Length == 4 && p[1].Equals("song", StringComparison.OrdinalIgnoreCase))
        {
            if (!ParseIndex(p[2], out int songIndex) || !IsHex(p[3], 16)) return false;
            cue = new GamePartyCue(GamePartyCueKind.Song, songIndex, p[3].ToUpperInvariant());
            return true;
        }
        if (p.Length != 9 || !p[1].Equals("play", StringComparison.OrdinalIgnoreCase) ||
            !ParseIndex(p[2], out int index) || !IsHex(p[3], 16) ||
            !long.TryParse(p[4], NumberStyles.None, CultureInfo.InvariantCulture, out long epoch) ||
            epoch is < 1577836800000L or > 4102444800000L ||
            !int.TryParse(p[5], NumberStyles.None, CultureInfo.InvariantCulture, out int speed) ||
            speed is < 500 or > 2000 || !IsHex(p[6], 16) || !IsHex(p[7], 8) || !ValidRoles(p[8]))
            return false;
        cue = new GamePartyCue(GamePartyCueKind.Start, index, p[3].ToUpperInvariant(),
            epoch, speed, p[6].ToUpperInvariant(), p[7].ToUpperInvariant(), p[8]);
        return true;
    }

    private static bool ValidInstrumentMap(string map)
    {
        if (map.Length is < 3 or > 24) return false;
        foreach (char c in map)
            if (!(char.IsLetterOrDigit(c) || c is '-' or '_')) return false;
        return true;
    }

    private static bool ParseIndex(string raw, out int index)
    {
        index = -1;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int oneBased) ||
            oneBased is < 1 or > 10000) return false;
        index = oneBased - 1;
        return true;
    }

    private static bool IsHex(string value, int length)
    {
        if (value.Length != length) return false;
        foreach (char c in value) if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private static bool ValidRoles(string roles)
    {
        if (roles == "-") return true;
        if (roles.Length is < 2 or > 72) return false;
        foreach (char c in roles)
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                  (c >= '0' && c <= '9') || c is '-' or '_')) return false;
        return true;
    }
}
