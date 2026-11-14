using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NunuWindboundBard.Core;

static class Program
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL " + label);
    }

    static void Main()
    {
        // Regression for CS8510: a single fallback covers invalid GM programs,
        // while all 128 valid GM programs retain their existing mappings.
        Check(MidiInstrumentInference.MapGeneralMidi(-1) == 0, "invalid negative GM program fallback");
        Check(MidiInstrumentInference.MapGeneralMidi(128) == 0, "invalid high GM program fallback");
        Check(MidiInstrumentInference.MapGeneralMidi(0) == 2, "GM Piano remains FFXIV Piano");
        Check(MidiInstrumentInference.MapGeneralMidi(24) == 3, "GM Guitar remains FFXIV Lute");
        Check(MidiInstrumentInference.MapGeneralMidi(40) == 20, "GM Violin remains FFXIV Violin");
        Check(MidiInstrumentInference.MapGeneralMidi(127) == 12, "last GM program remains mapped");
        Check(MidiTrackTags.TryGetOctave("#+12 #Lute", out int oct) && oct == 12, "#+12 #Lute");
        Check(MidiTrackTags.TryGetOctave("#Violin #-24", out oct) && oct == -24, "opposite tag order");
        Check(!MidiTrackTags.TryGetOctave("#+13 #Lute", out _), "reject non-octave offset");
        Check(MidiTrackTags.ExplicitInstrumentId("#+12 #Lute") == 3, "Lute explicit tag");
        Check(MidiTrackTags.ExplicitInstrumentId("#Piano #-12") == 2, "Piano explicit tag");
        Check(MidiTrackTags.SelectOctave(0, 0, 12, false) == 12, "file tag beats old auto shift");
        Check(MidiTrackTags.SelectOctave(0, -12, 12, true) == -12, "manual edit beats track tag");
        Check(MidiTrackTags.SelectOctave(12, null, null, false) == 12, "normal optimizer unchanged");

        foreach (string name in new[] {"Bassdrum", "BassDrum", "Bass Drum", "BASSDRUM", "bass-drum", "bass_drum"})
        {
            Check(MidiInstrumentInference.MapInstrumentName(name) == 12, name + " resolves to Bass Drum, not Bongo");
            Check(MidiTrackTags.ExplicitInstrumentId("#" + name) == 12, "explicit #" + name + " tag");
        }
        Check(InstrumentCatalog.Find(11)?.DisplayName == "Bongo", "Bongo remains Perform row 11");
        Check(InstrumentCatalog.Find(12)?.DisplayName == "Bass Drum", "Bass Drum remains Perform row 12");

        string path = Path.Combine(Path.GetTempPath(), "Windbound-Metadata-" + Guid.NewGuid().ToString("N") + ".mid");
        try
        {
            // Four real MIDI tracks; third uses FF04 instead of Program Change.
            MakeMidi(path, new[] {
                MakeTrack("Melody #+12 #Lute", 0, 0, 3, 50),      // GM Piano but #Lute wins
                MakeTrack("Strings #-12 #Violin", 1, 0, 1, 72),  // GM Piano but #Violin wins
                MakeTrack("Wind", 2, -1, 2, 60, "Oboe"),         // FF04 name wins
                MakeTrack("Brass", 3, 40, 1, 64),               // GM Violin program
                MakeTrackWithProgramSwitch(),                    // mixed GM programs, vote by note count
                MakePercussionTrack()                            // channel 10 drum map
            });
            MidiSong song = MidiFileReader.Load(path);
            Check(song.LaneInstruments.Count == 6 && song.Notes.Count == 14, "reader retained notes and lane metadata");
            Check(song.LaneInstruments[0].Program == 0 && song.LaneInstruments[0].NoteCount == 3,
                "program and note count captured");
            Check(MidiInstrumentInference.ForLane(song.LaneInstruments[0]).InstrumentId == 3, "#Lute outranks GM Piano");
            Check(MidiInstrumentInference.ForLane(song.LaneInstruments[1]).InstrumentId == 20, "#Violin tag");
            Check(MidiInstrumentInference.ForLane(song.LaneInstruments[2]).InstrumentId == 6, "FF04 Oboe");
            Check(MidiInstrumentInference.ForLane(song.LaneInstruments[3]).InstrumentId == 20, "GM Violin program");
            Check(song.LaneInstruments[4].DistinctPrograms == 2 && song.LaneInstruments[4].Program == 40,
                "program changes weighted by actual note-ons, not just first program");
            Check(MidiInstrumentInference.ForLane(song.LaneInstruments[4]).MixedPrograms,
                "mixed GM program warning");
            Check(MidiInstrumentInference.ForLane(song.LaneInstruments[5]).InstrumentId == 12,
                "drum channel majority maps to Bass Drum");
            Check(MidiTrackTags.TryGetLaneOctave(song, new MidiLaneKey(0, 0), out var plus) && plus == 12,
                "track 1 octave +12");
            Check(MidiTrackTags.TryGetLaneOctave(song, new MidiLaneKey(1, 1), out var minus) && minus == -12,
                "track 2 octave -12");
            Check(!MidiTrackTags.TryGetLaneOctave(song, new MidiLaneKey(2, 2), out _),
                "untagged track unaffected");
            var suggestions = MidiInstrumentInference.ForParty(song,
                new Dictionary<MidiLaneKey, ulong> {
                    [new(0, 0)] = 99,
                    [new(1, 1)] = 100,
                    [new(2, 2)] = 100,
                    [new(3, 3)] = 101
                });
            Check(suggestions.Single(x => x.ContentId == 99).InstrumentId == 3,
                "Lute assigned to corresponding party member");
            Check(suggestions.Single(x => x.ContentId == 100).ConflictingTracks,
                "multiple dissimilar instruments for same Bard flagged");
            Check(suggestions.Single(x => x.ContentId == 100).InstrumentId == 6,
                "dominant assigned track wins for shared Bard");
            var lane = new MidiLanePlan(new MidiLaneKey(0, 0), true, 12);
            Check(MidiOptimization.TransformPitch(50, lane, true) == 62,
                "octave shifts note exactly once; live MIDI range folding preserved");
            // Real-world DAWs often store #Lute/# +12 in FF01 text or FF06
            // markers, sometimes followed by a generic FF03 name. Ensure the
            // actual tag survives and dominates a conflicting GM Piano patch.
            MakeMidi(path, new[] {
                MakeTextTaggedTrack(),
                MakeMarkerTaggedTrack(),
                MakePlainNamedTrack(),
            });
            song = MidiFileReader.Load(path);
            Check(song.LaneInstruments.Count == 3, "metadata in 3 playable lanes");
            Check(song.Tracks[0].Name == "Track renamed by DAW", "later FF03 updates display name");
            Check(song.LaneInstruments[0].TagMetadata.Contains("#Lute"), "FF01 text tag retained");
            Check(MidiTrackTags.TryGetLaneOctave(song, new MidiLaneKey(0, 0), out plus) && plus == 12,
                "FF01 octave detected, despite a generic final FF03 title");
            Check(MidiInstrumentInference.ForLane(song.LaneInstruments[0]).InstrumentId == 3,
                "FF01 #Lute beats GM Piano");
            Check(MidiTrackTags.TryGetLaneOctave(song, new MidiLaneKey(1, 1), out minus) && minus == -12,
                "FF06 marker octave detected");
            Check(MidiInstrumentInference.ForLane(song.LaneInstruments[1]).InstrumentId == 20,
                "FF06 #Violin instrument detected");
            Check(MidiInstrumentInference.ForLane(song.LaneInstruments[2]).InstrumentId == 20,
                "plain Violin track name wins over a generic GM Piano program");

            // A separate conductor/meta track can declare the Program Change
            // for an otherwise note-only lane on the same MIDI channel.
            MakeMidi(path, new[] {MakeConductorProgramTrack(), MakeNoteOnlyTrack()});
            song = MidiFileReader.Load(path);
            Check(song.LaneInstruments.Count == 1 && song.LaneInstruments[0].Program == 40,
                "unambiguous GM program inherited from metadata-only track");
            Check(MidiInstrumentInference.ForLane(song.LaneInstruments[0]).InstrumentId == 20,
                "cross-track GM Violin detected");

            // FFXIV-scored percussion may already use playable pitches such as
            // C4 (60). That pitch must not override a specifically named part.
            MakeMidi(path, new[] {
                MakeTrack("Bassdrum", 0, 0, 1, 60),
                MakeTrack("Part 2", 1, 0, 1, 60, "Bassdrum"),
                MakeTrack("#Bassdrum", 9, -1, 1, 60),
                MakeTrack("Bassdrum", 9, -1, 1, 60),
                MakeTrack("Drums", 9, -1, 2, 35),
                MakeTrack("Bongo", 9, -1, 1, 60),
                MakeTrack("SnareDrum", 9, -1, 1, 60),
                MakeTrack("Cymbal", 9, -1, 1, 60),
                MakeTrack("Timpani", 9, -1, 1, 60),
                MakeTrack("Kit", 9, -1, 1, 60),
                MakePercussionTagTrack(0x01),
                MakePercussionTagTrack(0x06),
            });
            song = MidiFileReader.Load(path);
            int[] expected = {12, 12, 12, 12, 12, 11, 13, 14, 10, 11, 12, 12};
            Check(song.LaneInstruments.Count == expected.Length, "percussion regression lanes retained");
            for (int i = 0; i < expected.Length; i++)
                Check(MidiInstrumentInference.ForLane(song.LaneInstruments[i]).InstrumentId == expected[i],
                    "percussion metadata regression track " + i);
            Check(song.Notes.Count == 13 && song.Notes.Count(n => n.Pitch == 35) == 2,
                "instrument inference preserves drum notes and pitches");
            var bassPart = MidiInstrumentInference.ForParty(song,
                new Dictionary<MidiLaneKey, ulong> {[new(3, 9)] = 200}).Single();
            Check(bassPart.InstrumentId == 12, "named percussion Bassdrum reaches party assignment as ID 12");
            Console.WriteLine("PASS: Bassdrum spelling variants, named FFXIV percussion, FF01/FF06 tags, track names, split-track GM, #+12 #Lute and #-12 #Violin, GM/FF04 mapping, member assignments, conflicts, manual overrides, notes unchanged.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    static byte[] MakeTrack(string name, int channel, int gm, int notes, byte pitch, string? instrumentName = null)
    {
        using var buf = new MemoryStream();
        using var w = new BinaryWriter(buf);
        WriteMeta(w, 0x03, name);
        if (instrumentName is not null) WriteMeta(w, 0x04, instrumentName);
        if (gm >= 0) { w.Write((byte)0); w.Write((byte)(0xC0 | channel)); w.Write((byte)gm); }
        for (int i = 0; i < notes; i++)
        {
            w.Write((byte)0); w.Write((byte)(0x90 | channel)); w.Write(pitch); w.Write((byte)80);
            w.Write((byte)96); w.Write((byte)(0x80 | channel)); w.Write(pitch); w.Write((byte)0);
        }
        w.Write(new byte[] {0, 0xFF, 0x2F, 0});
        return buf.ToArray();
    }

    static byte[] MakeTextTaggedTrack()
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);
        WriteMeta(w, 0x03, "Lead");
        WriteMeta(w, 0x01, "#+12 #Lute");
        WriteMeta(w, 0x03, "Track renamed by DAW");
        w.Write(new byte[] {0, 0xC0, 0}); // GM Piano must not hide the FF01 tag.
        WriteNote(w, 0, 54);
        w.Write(new byte[] {0, 0xFF, 0x2F, 0});
        return buffer.ToArray();
    }

    static byte[] MakeMarkerTaggedTrack()
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);
        WriteMeta(w, 0x03, "Strings");
        WriteMeta(w, 0x06, "#-12 #Violin");
        w.Write(new byte[] {0, 0xC1, 0});
        WriteNote(w, 1, 74);
        w.Write(new byte[] {0, 0xFF, 0x2F, 0});
        return buffer.ToArray();
    }

    static byte[] MakePlainNamedTrack()
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);
        WriteMeta(w, 0x03, "Violin Melody");
        w.Write(new byte[] {0, 0xC2, 0});
        WriteNote(w, 2, 74);
        w.Write(new byte[] {0, 0xFF, 0x2F, 0});
        return buffer.ToArray();
    }

    static byte[] MakeConductorProgramTrack()
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);
        WriteMeta(w, 0x03, "Conductor");
        w.Write(new byte[] {0, 0xC3, 40}); // GM Violin, fourth channel.
        w.Write(new byte[] {0, 0xFF, 0x2F, 0});
        return buffer.ToArray();
    }

    static byte[] MakeNoteOnlyTrack()
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);
        WriteMeta(w, 0x03, "Part 2");
        WriteNote(w, 3, 66);
        w.Write(new byte[] {0, 0xFF, 0x2F, 0});
        return buffer.ToArray();
    }

    static byte[] MakeTrackWithProgramSwitch()
    {
        using var buf = new MemoryStream();
        using var w = new BinaryWriter(buf);
        WriteMeta(w, 0x03, "Mixed GM");
        w.Write(new byte[] {0, 0xC4, 0}); // ch5 GM Piano
        WriteNote(w, 4, 64);
        w.Write(new byte[] {0, 0xC4, 40}); // ch5 GM Violin
        for (int n = 0; n < 3; n++) WriteNote(w, 4, 67);
        w.Write(new byte[] {0, 0xFF, 0x2F, 0});
        return buf.ToArray();
    }

    static byte[] MakePercussionTrack()
    {
        using var buf = new MemoryStream();
        using var w = new BinaryWriter(buf);
        WriteMeta(w, 0x03, "Kit");
        WriteNote(w, 9, 35);
        WriteNote(w, 9, 36);
        WriteNote(w, 9, 38);
        w.Write(new byte[] {0, 0xFF, 0x2F, 0});
        return buf.ToArray();
    }

    static byte[] MakePercussionTagTrack(byte kind)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        WriteMeta(writer, 0x03, "Drums");
        WriteMeta(writer, kind, "#Bassdrum");
        WriteNote(writer, 9, 60);
        writer.Write(new byte[] {0, 0xFF, 0x2F, 0});
        return buffer.ToArray();
    }

    static void WriteNote(BinaryWriter w, int channel, byte pitch)
    {
        w.Write((byte)0); w.Write((byte)(0x90 | channel)); w.Write(pitch); w.Write((byte)90);
        w.Write((byte)96); w.Write((byte)(0x80 | channel)); w.Write(pitch); w.Write((byte)0);
    }

    static void WriteMeta(BinaryWriter writer, byte kind, string value)
    {
        byte[] data = Encoding.UTF8.GetBytes(value);
        Check(data.Length < 128, "short fixture meta event");
        writer.Write((byte)0); writer.Write((byte)0xFF); writer.Write(kind);
        writer.Write((byte)data.Length); writer.Write(data);
    }

    static void MakeMidi(string path, IEnumerable<byte[]> trackList)
    {
        var tracks = trackList.ToArray();
        using var file = new BinaryWriter(File.Create(path));
        file.Write(Encoding.ASCII.GetBytes("MThd")); Big32(file, 6);
        Big16(file, 1); Big16(file, (ushort)tracks.Length); Big16(file, 96);
        foreach (byte[] track in tracks)
        {
            file.Write(Encoding.ASCII.GetBytes("MTrk")); Big32(file, (uint)track.Length);
            file.Write(track);
        }
    }
    static void Big16(BinaryWriter w, ushort n) { w.Write((byte)(n>>8)); w.Write((byte)n); }
    static void Big32(BinaryWriter w, uint n) { w.Write((byte)(n>>24)); w.Write((byte)(n>>16)); w.Write((byte)(n>>8)); w.Write((byte)n); }
}
