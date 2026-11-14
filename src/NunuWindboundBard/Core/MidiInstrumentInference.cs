using System;
using System.Collections.Generic;
using System.Linq;

namespace NunuWindboundBard.Core;

/// <summary>An inferred single FFXIV Performance instrument for a MIDI lane.
/// GM programs are an approximation: FFXIV has only 28 Perform instruments.</summary>
public sealed record MidiInstrumentSuggestion(int InstrumentId, string Source,
    bool Approximation, bool MixedPrograms, int NoteCount);

/// <summary>A single Bard can play several MIDI lanes but can equip only ONE
/// instrument. Choose the dominant assigned, recognised part by note count;
/// never silently change instruments mid-song.</summary>
public sealed record MidiPartyInstrumentSuggestion(ulong ContentId, int InstrumentId,
    string Source, bool ConflictingTracks, int MatchedNotes, int TotalTracks);

public static class MidiInstrumentInference
{
    public static MidiInstrumentSuggestion ForLane(MidiLaneInstrument lane,
        string fileName = "", int melodicLanes = 2)
    {
        // An explicit #Lute / #Piano / #Violin marker is intentional scoring
        // metadata and must beat a DAW's unrelated General MIDI program.
        int explicitId = MidiTrackTags.ExplicitInstrumentId(lane, fileName, melodicLanes);
        if (explicitId != 0)
            return new(explicitId, "Explicit MIDI tag: #" + InstrumentCatalog.Display(explicitId),
                false, false, lane.NoteCount);
        int metaId = MapInstrumentName(lane.InstrumentName);
        if (metaId != 0)
            return new(metaId, "MIDI FF04 instrument name: " + lane.InstrumentName,
                true, false, lane.NoteCount);
        if (lane.IsPercussion)
        {
            // FFXIV-scored percussion can already use playable pitches (e.g.
            // C4), which are different from GM drum-kit note identifiers.
            // A specific part name must beat that heuristic. Generic names
            // such as "Drums", "Percussion" and "Kit" still use GM voting.
            int namedPercussion = MapNamedPercussion(lane.TrackName?.ToLowerInvariant() ?? "");
            if (namedPercussion != 0)
                return new(namedPercussion, "MIDI percussion track name: " + lane.TrackName,
                    true, false, lane.NoteCount);
            var votes = lane.DrumPitches.Select(hit =>
                (Instrument: PercussionInstrument(hit.Pitch), hit.Count))
                .GroupBy(v => v.Instrument)
                .Select(v => (Instrument: v.Key, Count: v.Sum(x => x.Count)))
                .OrderByDescending(v => v.Count).ThenBy(v => v.Instrument).ToArray();
            int id = votes.Length == 0 ? 0 : votes[0].Instrument;
            return new(id, "MIDI percussion (channel 10)", votes.Length > 1,
                votes.Length > 1, lane.NoteCount);
        }
        // Explicitly named musical tracks (e.g. "Violin", "Piano Lead")
        // outrank the default GM Piano patch often exported by MIDI editors.
        // A generic "Track 01" won't match and will fall through to GM.
        int byTrack = MapInstrumentName(lane.TrackName);
        if (byTrack > 0) return new(byTrack, "MIDI track name: " + lane.TrackName,
            true, lane.DistinctPrograms > 1, lane.NoteCount);
        if (lane.Program is >= 0 and <= 127)
        {
            int id = MapGeneralMidi(lane.Program.Value);
            return new(id, "GM " + (lane.Program.Value + 1) + " → " +
                InstrumentCatalog.Display(id), true, lane.DistinctPrograms > 1, lane.NoteCount);
        }
        return new(0, "No instrument declared in MIDI", true, false, lane.NoteCount);
    }

    public static IReadOnlyList<MidiPartyInstrumentSuggestion> ForParty(
        MidiSong song, IReadOnlyDictionary<MidiLaneKey, ulong> assignments)
    {
        int melodicLanes = song.LaneInstruments.Count(lane => !lane.IsPercussion);
        var mapped = song.LaneInstruments.Select(lane => new {
            Lane = lane,
            Member = assignments.GetValueOrDefault(new MidiLaneKey(lane.Track, lane.Channel)),
            Suggestion = ForLane(lane, song.FileName, melodicLanes)
        }).Where(item => item.Member != 0).ToArray();
        return mapped.GroupBy(item => item.Member).Select(group =>
        {
            // Votes are weighted by NOTE COUNT per instrument. An unknown
            // track cannot defeat a recognised program by sheer note count.
            var recognised = group.Where(item => item.Suggestion.InstrumentId > 0).ToArray();
            var options = recognised.GroupBy(item => item.Suggestion.InstrumentId)
                .Select(option => (Id: option.Key, Notes: option.Sum(p => p.Lane.NoteCount),
                    FirstTrack: option.Min(p => p.Lane.Track)))
                .OrderByDescending(x => x.Notes).ThenBy(x => x.FirstTrack)
                .ThenBy(x => x.Id).ToArray();
            int selected = options.Length == 0 ? 0 : options[0].Id;
            var source = recognised.FirstOrDefault(item => item.Suggestion.InstrumentId == selected);
            bool conflict = options.Length > 1 || group.Any(item => item.Suggestion.MixedPrograms);
            return new MidiPartyInstrumentSuggestion(group.Key, selected,
                source?.Suggestion.Source ?? "No instrument program/name found",
                conflict, options.Length == 0 ? 0 : options[0].Notes,
                group.Count());
        }).OrderBy(x => x.ContentId).ToArray();
    }

    /// <summary>General MIDI 1 program index (0..127) to the closest Perform row ID.
    /// Explicit case mapping prevents confusing program 1 (GM Piano) with
    /// instrument 1 (FFXIV Harp). Not all 128 GM timbres exist in FFXIV.</summary>
    public static int MapGeneralMidi(int program) => program switch
    {
        // Valid GM programs are explicitly mapped below; the default arm
        // handles any out-of-range program. Do not add a separate <0/>127
        // arm: doing so makes the final `_ => 0` unreachable (CS8510).
        >= 0 and <= 23 => 2,                             // Piano / keyboards / organs
        24 or 25 => 3,                         // Nylon and steel acoustic guitar → Lute
        26 or 27 => 25,                        // Jazz and clean electric guitar
        28 => 26,                              // Muted electric guitar
        29 or 30 => 24,                        // Overdriven / distorted guitar
        31 => 27,                              // Guitar harmonics → Power Chords
        >= 32 and <= 39 => 23,                // Bass → Double Bass
        40 => 20, 41 => 21, 42 => 22, 43 => 23,
        44 => 20, 45 => 4, 46 => 1, 47 => 10,
        >= 48 and <= 55 => 20,                // Strings / voice-like patches
        56 => 15, 57 => 16, 58 => 17, 59 => 15,
        60 => 18, 61 or 62 or 63 => 15,
        >= 64 and <= 67 => 19,                // Saxophones
        68 or 69 or 70 => 6, 71 => 7,
        72 => 8, 73 or 74 => 5, 75 => 9,
        76 => 5, 77 => 9, 78 => 8, 79 => 5,
        >= 80 and <= 87 => 2,                 // Synth leads
        >= 88 and <= 95 => 20,                // Synth pads
        >= 96 and <= 103 => 2,                // Sound effects
        104 or 105 or 106 => 3, 107 => 1,
        108 => 2, 109 => 5, 110 => 4, 111 => 6,
        112 => 2, >= 113 and <= 115 => 11,
        116 => 12, 117 or 118 => 11, 119 => 14,
        120 => 3, 121 => 5, 122 => 9, 123 => 8,
        124 => 2, 125 => 2, 126 => 11, 127 => 12,
        _ => 0
    };

    public static int PercussionInstrument(int note) => note switch
    {
        35 or 36 => 12,                         // Kick
        37 or 38 or 39 or 40 => 13,             // Rimshot / snare
        42 or 44 or 46 or 49 or 51 or 52 or 53 or 55 or 57 or 59 => 14,
        >= 41 and <= 50 => 10,                 // Toms → Timpani
        >= 60 and <= 68 => 11,                 // Bongo/conga/timbales
        _ => 11
    };

    /// <summary>Conservative fuzzy fallback; unknown names always return Keep Current.</summary>
    public static int MapInstrumentName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        string n = name.ToLowerInvariant();
        int percussion = MapNamedPercussion(n);
        if (percussion != 0) return percussion;
        if (Contains(n,"percussion","drum")) return 11;
        if (Contains(n,"overdrive","distort","lead guitar")) return 24;
        if (Contains(n,"muted guitar")) return 26;
        if (Contains(n,"power chord")) return 27;
        if (Contains(n,"electric guitar","clean guitar")) return 25;
        if (Contains(n,"guitar","acoustic","lute","banjo","guitare")) return 3;
        if (Contains(n,"piano","keyboard","keys","grandpiano","clavier")) return 2;
        if (Contains(n,"harp","harpe","koto")) return 1;
        if (Contains(n,"fiddle","violon folk")) return 4;
        if (Contains(n,"viola","alto")) return 21;
        if (Contains(n,"cello","violoncelle")) return 22;
        if (Contains(n,"double bass","contrabass","basse")) return 23;
        if (Contains(n,"violin","violon","strings","cordes")) return 20;
        if (Contains(n,"trombone")) return 16;
        if (Contains(n,"trumpet","trompette")) return 15;
        if (Contains(n,"tuba")) return 17;
        if (Contains(n,"french horn","cor d'harmonie")) return 18;
        if (Contains(n,"sax")) return 19;
        if (Contains(n,"oboe","hautbois","bassoon")) return 6;
        if (Contains(n,"clarinet","clarinette")) return 7;
        if (Contains(n,"panpipe","pan flute","flûte de pan")) return 9;
        if (Contains(n,"piccolo","fife","fifre")) return 8;
        if (Contains(n,"flute","flûte","recorder")) return 5;
        return 0;
    }

    // Input is lower-case. Keep specific drum names ahead of the generic
    // "drum" fallback: "Bassdrum" is a common MIDI label, not a Bongo.
    private static int MapNamedPercussion(string name)
    {
        if (Contains(name,"snare","caisse claire")) return 13;
        if (Contains(name,"kick","bass drum","bassdrum","bass-drum","bass_drum","grosse caisse")) return 12;
        if (Contains(name,"cymbal","hi-hat","crash","ride")) return 14;
        if (Contains(name,"timpani","timbale")) return 10;
        if (Contains(name,"bongo","conga")) return 11;
        return 0;
    }

    private static bool Contains(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.Ordinal));
}
