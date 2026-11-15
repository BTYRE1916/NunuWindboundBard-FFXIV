using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NunuWindboundBard.Core;

namespace NunuWindboundBard.Core
{
    // Pure test-only stand-in for the real MidiLaneKey, which normally lives in
    // MIDI optimisation. Snapshot tests must not require the Dalamud SDK.
    public readonly record struct MidiLaneKey(int Track, int Channel);
}

public static class Program
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("Snapshot test FAILED: " + label);
    }
    static bool Rejects(ConcertSnapshot input)
    {
        try { ConcertSnapshotFormat.Validate(input); return false; }
        catch (InvalidDataException) { return true; }
    }

    public static void Main()
    {
        var midi1 = "C:/Concert/Opening.mid";
        var midi2 = "C:/Concert/Finale.mid";
        var s = new ConcertSnapshot
        {
            Name = "Nunu + Edea Octet",
            PlaylistName = "Concert in the Mist",
            Role = 1,
            PlaylistFiles = new() { midi1, midi2 },
            SelectedMidiPath = midi1,
            Options = new ConcertSnapshotOptions
            {
                PlaybackSpeed = 1.25f, GlobalTranspose = -12,
                KeepPartyAssignmentsAcrossSongs = true,
                ConcertAutoPrepareNext = true, NativeEnsembleAutoAccept = true
            },
            Lineup = new PartyTrackLineup { PerformerIdsBySlot = new() { 123, 456, 789 } },
            SmartPlaylist = new SmartPlaylistState {
                Shuffle = false, Repeat = PlaylistRepeatMode.Off,
                Favorites = new() { midi2 }, Tags = new() { [midi1] = new() { "duet", "encore" } },
                PlayNext = new() { midi2 }
            },
            Songs = new(StringComparer.OrdinalIgnoreCase)
            {
                [midi1] = new ConcertSnapshotSong
                {
                    Instrument = 2,
                    PartyInstruments = new() { ["000000000000007B"] = 2, ["00000000000001C8"] = 20 },
                    PartyTracks = new() { ["0:0"] = 123, ["1:0"] = 456 },
                    Octaves = new() { ["0:0"] = 12, ["1:0"] = -12 },
                    ManualOctaveOverrides = new() { "0:0" },
                    EnabledLanes = new() { "0:0" },
                    GuitarTones = new() { ["2:0"] = 1 },
                    Soulweaver = new SoulweaverPreset { Performers = 2 },
                    Cue = new ConcertSongCue { Announcement = "Welcome", HoldAfter = true, Encore = false }
                },
                [midi2] = new ConcertSnapshotSong { Instrument = 20 }
            }
        };
        ConcertSnapshotFormat.Validate(s);
        string json = ConcertSnapshotFormat.ExportToJson(s);
        Check(json.Contains("Nunu + Edea") && json.Contains("HoldAfter"), "all fields export");
        var imported = ConcertSnapshotFormat.ImportFromJson(json);
        Check(imported.Id != s.Id, "fresh imported snapshot ID");
        Check(imported.Name == s.Name && imported.Songs.Count == 2, "JSON roundtrip");
        Check(imported.Lineup!.PerformerIdsBySlot.SequenceEqual(new ulong[] { 123, 456, 789 }), "performer lineup saved");
        Check(imported.Songs[midi1].PartyTracks["1:0"] == 456, "party track assignment saved");
        Check(imported.Songs[midi1].Octaves["0:0"] == 12, "octave override saved");
        Check(imported.Songs[midi1].PartyInstruments["00000000000001C8"] == 20, "party instruments saved");
        Check(imported.Songs[midi1].Cue!.HoldAfter, "audience hold saved");
        Check(imported.SmartPlaylist.PlayNext.Single() == midi2, "queue saved");
        Check(imported.Options.ConcertAutoPrepareNext, "concert automation preference saved");
        imported.Songs[midi1].Octaves["0:0"] = 24;
        imported.Lineup.PerformerIdsBySlot[0] = 999;
        Check(s.Songs[midi1].Octaves["0:0"] == 12 && s.Lineup!.PerformerIdsBySlot[0] == 123,
            "deep copy never shares mutable settings");

        var bad = ConcertSnapshotFormat.Copy(s); bad.FormatVersion = 99;
        Check(Rejects(bad), "unknown format rejected");
        bad = ConcertSnapshotFormat.Copy(s); bad.Role = 8;
        Check(Rejects(bad), "invalid role rejected");
        bad = ConcertSnapshotFormat.Copy(s); bad.PlaylistFiles.Add(midi1);
        Check(Rejects(bad), "duplicate MIDI entry rejected");
        bad = ConcertSnapshotFormat.Copy(s); bad.PlaylistFiles.Clear();
        Check(Rejects(bad), "per-MIDI metadata without corresponding file rejected");
        bad = ConcertSnapshotFormat.Copy(s); bad.Options.PlaybackSpeed = -1f;
        Check(Rejects(bad), "invalid tempo rejected");
        bad = ConcertSnapshotFormat.Copy(s); bad.Songs[midi1].PartyInstruments["00000000000001C8"] = 29;
        Check(Rejects(bad), "invalid FFXIV instrument rejected");
        bad = ConcertSnapshotFormat.Copy(s); bad.Songs[midi1].Octaves["1:0"] = 999;
        Check(Rejects(bad), "invalid octave rejected");
        Check(!s.PlaylistFiles.Any(File.Exists), "missing local MIDIs do not make JSON invalid");
        Console.WriteLine("PASS: snapshot JSON, performer slots, MIDI settings, octaves, concert cues, deep copies, corrupt/invalid data handling.");
    }
}
