using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace NunuWindboundBard.Core;

/// <summary>
/// Encodes the conductor's instrument choice for each party member by sorted ContentId.
/// No account identifiers are sent in chat; the roster fingerprint detects mismatches.
/// 0 means Keep current, 1..28 map to the FFXIV Perform sheet instrument IDs.
/// </summary>
public static class PartyInstrumentPlan
{
    public static string Encode(IReadOnlyDictionary<ulong, int> choices,
        IReadOnlyList<ulong> roster, out string rosterTag)
    {
        var ids = roster.Where(x => x != 0).Distinct().OrderBy(x => x).ToArray();
        if (ids.Length is < 2 or > 15)
            throw new InvalidOperationException("An ensemble needs 2 to 15 identified party members.");
        rosterTag = OneClickEnsembleCue.RosterTag(ids);
        var data = new byte[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            int instrument = choices.GetValueOrDefault(ids[i]);
            if (instrument is < 0 or > 28)
                throw new InvalidOperationException("An instrument selection is invalid.");
            data[i] = (byte)instrument;
        }
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string encoded, string rosterTag, IReadOnlyList<ulong> roster,
        out Dictionary<ulong, int> choices, out string error)
    {
        choices = new Dictionary<ulong, int>();
        error = string.Empty;
        var ids = roster.Where(x => x != 0).Distinct().OrderBy(x => x).ToArray();
        if (ids.Length is < 2 or > 15 ||
            !string.Equals(rosterTag, OneClickEnsembleCue.RosterTag(ids), StringComparison.OrdinalIgnoreCase))
        { error = "FFXIV party roster differs from the conductor's roster."; return false; }
        try
        {
            if (encoded.Length is < 3 or > 24 || encoded.Any(x => !(char.IsLetterOrDigit(x) || x is '-' or '_')))
            { error = "Invalid instrument-map characters."; return false; }
            string padded = encoded.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            byte[] data = Convert.FromBase64String(padded);
            if (data.Length != ids.Length || data.Any(id => id > 28))
            { error = "Instrument map contains invalid or missing entries."; return false; }
            for (int i = 0; i < ids.Length; i++) choices.Add(ids[i], data[i]);
            return true;
        }
        catch (FormatException)
        { error = "Invalid instrument-map encoding."; return false; }
    }
}
