using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace NunuWindboundBard.Core;

/// <summary>
/// Textual instrument and octave directives embedded in Standard MIDI track
/// names (FF 03) or instrument names (FF 04): e.g. "#+12 #Lute" or
/// "#Violin #-12". Also accepts a filename tag when the MIDI has exactly ONE
/// melodic track/channel. All directives are metadata, never playback events.
/// </summary>
public static class MidiTrackTags
{
    private static readonly Regex OctaveTag = new(
        @"(?<![\p{L}\p{N}_])#(?<value>[+-]?(?:0|12|24|36|48))(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WordTag = new(
        @"(?<![\p{L}\p{N}_])#(?<value>[^#\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryGetOctave(string? label, out int semitones)
    {
        semitones = 0;
        if (string.IsNullOrWhiteSpace(label)) return false;
        var match = OctaveTag.Match(label);
        return match.Success && int.TryParse(match.Groups["value"].Value, out semitones) &&
            semitones is >= -48 and <= 48 && semitones % 12 == 0;
    }

    public static int ExplicitInstrumentId(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return 0;
        foreach (Match tag in WordTag.Matches(label))
        {
            string value = tag.Groups["value"].Value.Trim(' ', '\t', '-', ':', '#');
            if (value.Length == 0 || value[0] is '+' or '-' || char.IsDigit(value[0])) continue;
            // Reuse the FFXIV instrument mapping, not the General MIDI program number.
            int id = MidiInstrumentInference.MapInstrumentName(value);
            if (id != 0) return id;
        }
        return 0;
    }

    public static int ExplicitInstrumentId(MidiLaneInstrument lane, string fileName = "", int melodicLanes = 2)
    {
        int id = ExplicitInstrumentId(lane.TrackName);
        if (id == 0) id = ExplicitInstrumentId(lane.InstrumentName);
        if (id == 0) id = ExplicitInstrumentId(lane.TagMetadata);
        if (id == 0 && melodicLanes == 1) id = ExplicitInstrumentId(fileName);
        return id;
    }

    /// <summary>Rule for new tracks: file tag > older automatically saved shift;
    /// a current-version manual edit is the only persisted override.</summary>
    public static int SelectOctave(int suggested, int? previouslySaved, int? midiTag, bool manuallyOverridden)
    {
        if (midiTag.HasValue && !manuallyOverridden) return midiTag.Value;
        return previouslySaved ?? suggested;
    }

    public static bool TryGetLaneOctave(MidiSong song, MidiLaneKey lane, out int semitones)
    {
        semitones = 0;
        var instrument = song.LaneInstruments.FirstOrDefault(x => x.Track == lane.Track && x.Channel == lane.Channel);
        string trackName = instrument?.TrackName ?? song.Tracks.FirstOrDefault(x => x.Index == lane.Track)?.Name ?? "";
        if (TryGetOctave(trackName, out semitones)) return true;
        if (TryGetOctave(instrument?.InstrumentName, out semitones)) return true;
        if (TryGetOctave(instrument?.TagMetadata, out semitones)) return true;
        // A filename may mark one solo part, but NOT an octet's shared MIDI.
        if (song.LaneInstruments.Count(x => !x.IsPercussion) == 1 && lane.Channel != 9)
            return TryGetOctave(song.FileName, out semitones);
        return false;
    }
}
