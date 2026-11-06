using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NunuWindboundBard.Core;

/// <summary>
/// Stateless, cross-platform helpers for a conductor party-chat START cue.
/// The track map uses the SAME MIDI lane order on every participant and
/// stable party ContentId ordering; it never exposes ContentIds in party chat.
/// </summary>
public static class OneClickEnsembleCue
{
    public static string RosterTag(IReadOnlyList<ulong> memberIds)
    {
        string serialized = string.Join(",", memberIds.Where(x => x != 0).Distinct().OrderBy(x => x)
            .Select(id => id.ToString("X16", CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(serialized)), 0, 4);
    }

    /// <summary>Encode 0 = silent, 1..N = party member sorted by ContentId; two lanes per byte.</summary>
    public static string EncodeAssignments(IReadOnlyList<MidiLaneKey> lanes,
        IReadOnlyDictionary<MidiLaneKey, ulong> assignments, IReadOnlyList<ulong> members,
        out string rosterTag, bool explicitMap = false)
    {
        rosterTag = "00000000";
        // For legacy file loading, '-' means keep manual track selection.
        // Set Party Tracks needs an explicit all-zero map to silence every Bard.
        if (assignments.Count == 0 && !explicitMap) return "-"; // existing behaviour retained
        var ids = members.Where(x => x != 0).Distinct().OrderBy(x => x).ToList();
        if (ids.Count is < 1 or > 15) throw new InvalidOperationException("Party roster needs 1 to 15 identified members.");
        if (lanes.Count is < 1 or > 108) throw new InvalidOperationException("Too many MIDI lanes for an FFXIV party-chat track map.");
        rosterTag = RosterTag(ids);
        var packed = new byte[(lanes.Count + 1) / 2];
        for (int i = 0; i < lanes.Count; i++)
        {
            int code = 0;
            if (assignments.TryGetValue(lanes[i], out ulong id) && id != 0)
            {
                int memberIndex = ids.IndexOf(id);
                if (memberIndex < 0) throw new InvalidOperationException("A track is assigned to someone who isn't in the current party.");
                code = memberIndex + 1;
            }
            if (i % 2 == 0) packed[i / 2] = (byte)(code << 4);
            else packed[i / 2] |= (byte)code;
        }
        // Base64url, without padding, to avoid punctuation that FFXIV might filter.
        return Convert.ToBase64String(packed).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecodeAssignments(string payload, string rosterTag,
        IReadOnlyList<MidiLaneKey> lanes, IReadOnlyList<ulong> members,
        out Dictionary<MidiLaneKey, ulong> result, out string error)
    {
        result = new Dictionary<MidiLaneKey, ulong>();
        error = "";
        if (payload == "-") return true;
        var ids = members.Where(x => x != 0).Distinct().OrderBy(x => x).ToList();
        if (ids.Count is < 1 or > 15 || !string.Equals(RosterTag(ids), rosterTag, StringComparison.OrdinalIgnoreCase))
        {
            error = "Party roster doesn't match the conductor's map. Rejoin the same FFXIV party.";
            return false;
        }
        if (lanes.Count is < 1 or > 108) { error = "MIDI lane count is unsupported."; return false; }
        byte[] decoded;
        try
        {
            string b64 = payload.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight((b64.Length + 3) / 4 * 4, '=');
            decoded = Convert.FromBase64String(b64);
        }
        catch (FormatException) { error = "Invalid track-map encoding."; return false; }
        if (decoded.Length != (lanes.Count + 1) / 2)
        {
            error = "Track-map length differs; check that everyone has the same MIDI tracks.";
            return false;
        }
        for (int i = 0; i < lanes.Count; i++)
        {
            int code = i % 2 == 0 ? decoded[i / 2] >> 4 : decoded[i / 2] & 15;
            if (code > ids.Count) { error = "Track-map references a missing party member."; return false; }
            if (code != 0) result[lanes[i]] = ids[code - 1];
        }
        return true;
    }

    /// <summary>
    /// Convert a common UTC start time to this PC's own monotonic Stopwatch clock.
    /// The two computers never compare Stopwatch values. Correct wall clocks are
    /// still needed; NTP differences and FFXIV chat jitter affect synchronisation.
    /// </summary>
    public static bool TryGetLocalStartQpc(long startUnixMs, long nowUnixMs, long nowQpc, long frequency,
        out long startQpc, out string reason)
    {
        startQpc = 0;
        double remainingMs = startUnixMs - nowUnixMs;
        if (remainingMs < 250 || remainingMs > 20000 || frequency <= 0)
        {
            reason = "Conductor cue arrived too late or the computers' clocks disagree. " +
                     "Sync both system clocks, then try again with a longer count-in.";
            return false;
        }
        startQpc = checked(nowQpc + (long)Math.Round(remainingMs / 1000.0 * frequency));
        reason = $"One-click cue accepted: performance starts in {remainingMs / 1000.0:0.0}s.";
        return true;
    }
}
