using System;
using System.Collections.Generic;

namespace NunuWindboundBard.Core;

/// <summary>
/// The five electric-guitar Performance tones, indexed as in FFXIV's tone selector.
/// -1 means "leave the current in-game tone unchanged".
/// </summary>
public static class GuitarToneOverrides
{
    public const int UseCurrent = -1;
    public const int Overdriven = 0;
    public const int Clean = 1;
    public const int Muted = 2;
    public const int PowerChords = 3;
    public const int Special = 4;

    public static readonly string[] Names =
    {
        "Overdriven", "Clean", "Muted", "Power Chords", "Special"
    };

    public static string Display(int tone) => tone == UseCurrent ? "Use current in-game tone" :
        IsValid(tone) ? Names[tone] : "Unknown tone";

    public static bool IsValid(int tone) => tone is >= 0 and <= 4;

    public static string Key(MidiLaneKey lane) => $"{lane.Track}:{lane.Channel}";

    public static bool TryResolve(IReadOnlyDictionary<MidiLaneKey, int>? mapping,
        int track, int channel, out int tone)
    {
        tone = UseCurrent;
        return mapping is not null &&
               mapping.TryGetValue(new MidiLaneKey(track, channel), out tone) &&
               IsValid(tone);
    }
}
