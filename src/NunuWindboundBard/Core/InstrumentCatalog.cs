using System;
using System.Collections.Generic;
using System.Linq;

namespace NunuWindboundBard.Core;

/// <summary>
/// FFXIV Perform sheet row identifiers for the Bard Performance instruments.
/// 0 is our "do not change instrument" configuration value, not a playable instrument.
/// </summary>
public sealed record BardInstrument(int Id, string DisplayName)
{
    public bool IsGuitar => Id is >= 24 and <= 28;
    public int GuitarTone => IsGuitar ? Id - 24 : -1;
}

public static class InstrumentCatalog
{
    public static readonly IReadOnlyList<BardInstrument> All = new[]
    {
        new BardInstrument(1, "Harp"),
        new BardInstrument(2, "Piano"),
        new BardInstrument(3, "Lute"),
        new BardInstrument(4, "Fiddle"),
        new BardInstrument(5, "Flute"),
        new BardInstrument(6, "Oboe"),
        new BardInstrument(7, "Clarinet"),
        new BardInstrument(8, "Fife"),
        new BardInstrument(9, "Panpipes"),
        new BardInstrument(10, "Timpani"),
        new BardInstrument(11, "Bongo"),
        new BardInstrument(12, "Bass Drum"),
        new BardInstrument(13, "Snare Drum"),
        new BardInstrument(14, "Cymbal"),
        new BardInstrument(15, "Trumpet"),
        new BardInstrument(16, "Trombone"),
        new BardInstrument(17, "Tuba"),
        new BardInstrument(18, "Horn"),
        new BardInstrument(19, "Saxophone"),
        new BardInstrument(20, "Violin"),
        new BardInstrument(21, "Viola"),
        new BardInstrument(22, "Cello"),
        new BardInstrument(23, "Double Bass"),
        new BardInstrument(24, "Electric Guitar — Overdriven"),
        new BardInstrument(25, "Electric Guitar — Clean"),
        new BardInstrument(26, "Electric Guitar — Muted"),
        new BardInstrument(27, "Electric Guitar — Power Chords"),
        new BardInstrument(28, "Electric Guitar — Special"),
    };

    // 750 is the generic Performance icon, NOT the Harp. The playable
    // instrument artwork begins at 751: Harp=751, Piano=752, ...,
    // Electric Guitar (Special)=778. The UI must use the instrument's ID,
    // not the combo row index (which includes Keep Current at ID 0).
    // Return no icon for Keep Current / invalid IDs.
    public static uint GameIconId(int id) => id is >= 1 and <= 28 ? (uint)(750 + id) : 0;

    public static BardInstrument? Find(int id) => All.FirstOrDefault(i => i.Id == id);
    public static bool Valid(int id) => id == 0 || Find(id) != null;
    public static string Display(int id) => id == 0 ? "Keep current instrument" :
        Find(id)?.DisplayName ?? $"Unknown instrument ({id})";
}
