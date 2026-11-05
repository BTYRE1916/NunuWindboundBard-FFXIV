using System;
using System.Collections.Generic;

namespace NunuWindboundBard.Core;

/// <summary>Persisted Soulweaver choices, isolated per original MIDI path.</summary>
public sealed class SoulweaverPreset
{
    public int Performers { get; set; } = 2;
    public bool IncludeAllTracks { get; set; } = true;
    public int MaxNotesPerOnset { get; set; } = 0; // 0 = keep every note
    public Dictionary<int, int> InstrumentByPart { get; set; } = new();
    public Dictionary<int, ulong> PartyMemberByPart { get; set; } = new();
}
