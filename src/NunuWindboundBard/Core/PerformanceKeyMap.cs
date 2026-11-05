using System;
using System.Collections.Generic;

namespace NunuWindboundBard.Core;

/// <summary>
/// Manual 37-key reference mapping retained from v0.1. This mapping is for
/// human keyboard performance and does NOT control the direct in-game callback
/// backend. A Bard can chat and alt-tab while our callback player is active.
/// </summary>
public static class PerformanceKeyMap
{
    public const string DefaultKeys = "ZXCVBNMASDFGHJKLQWERTYUIOP1234567890;";

    public static bool TryValidate(string keys, out string message)
    {
        if (keys.Length != 37)
        {
            message = $"Map must contain exactly 37 keys. Current length: {keys.Length}.";
            return false;
        }
        HashSet<char> seen = new();
        foreach (char raw in keys)
        {
            char key = char.ToUpperInvariant(raw);
            if ((key is < 'A' or > 'Z') && (key is < '0' or > '9') && key != ';')
            {
                message = $"Unsupported mapping key: {key}. Use letters, numbers or ';'.";
                return false;
            }
            if (!seen.Add(key))
            {
                message = $"Duplicate mapping key: {key}. Each pitch needs a different key.";
                return false;
            }
        }
        message = "Map valid: 37 distinct keys from C3 to C6.";
        return true;
    }

    public static int ToVirtualKey(char source)
    {
        char key = char.ToUpperInvariant(source);
        return key == ';' ? 0xBA : key; // VK_OEM_1 (semicolon on US/UK keyboards)
    }

    public static char KeyForPitch(int pitch, string keys)
    {
        if (pitch is < NoteMapper.LowestNote or > NoteMapper.HighestNote)
            throw new ArgumentOutOfRangeException(nameof(pitch));
        if (!TryValidate(keys, out string reason)) throw new ArgumentException(reason, nameof(keys));
        return keys[pitch - NoteMapper.LowestNote];
    }
}
