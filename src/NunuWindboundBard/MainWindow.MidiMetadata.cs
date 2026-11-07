using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// File-derived instrument/octave defaults are non-destructive. They are suggestions
/// for existing party SET commands and they never change the MIDI file itself.
/// Manual instrument choices and explicit lane octave edits win on later loads.
/// </summary>
public sealed partial class MainWindow
{
    private string midiMetadataStatus = "No MIDI instrument metadata scanned yet.";

    private HashSet<string> GetManualOctaveOverrides()
    {
        cfg.ManualOctaveLaneOverridesByFile ??= new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(loaded)) return new(StringComparer.OrdinalIgnoreCase);
        return cfg.ManualOctaveLaneOverridesByFile.TryGetValue(loaded, out var saved)
            ? saved.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase);
    }

    private void MarkOctaveManual(MidiLaneKey key)
    {
        if (string.IsNullOrWhiteSpace(loaded)) return;
        cfg.ManualOctaveLaneOverridesByFile ??= new(StringComparer.OrdinalIgnoreCase);
        if (!cfg.ManualOctaveLaneOverridesByFile.TryGetValue(loaded, out var saved))
            cfg.ManualOctaveLaneOverridesByFile[loaded] = saved = new List<string>();
        string lane = GuitarToneOverrides.Key(key);
        if (!saved.Contains(lane, StringComparer.OrdinalIgnoreCase)) saved.Add(lane);
        cfg.Save();
    }

    private void MarkAllOctavesManual()
    {
        foreach (var lane in lanes) MarkOctaveManual(lane.Key);
    }

    private void ApplyInitialOctaveShifts(MidiSong candidate, string fullPath)
    {
        var manualOverrides = GetManualOctaveOverrides();
        cfg.OctaveShiftsByFile.TryGetValue(fullPath, out var priorShifts);
        int tagged = 0;
        lanes = lanes.Select(lane =>
        {
            string key = GuitarToneOverrides.Key(lane.Key);
            // A user-edited shift explicitly overrides the file. Older saved
            // shifts (from automatic optimisation in pre-v0.22 releases) don't
            // prevent newly supported #+12 tags from taking effect.
            bool hasSaved = priorShifts != null && priorShifts.TryGetValue(key, out int saved) &&
                saved is >= -48 and <= 48 && saved % 12 == 0;
            int savedShift = hasSaved ? priorShifts![key] : lane.OctaveShift;
            int? tag = null;
            if (cfg.AutoReadMidiTrackOctaves &&
                MidiTrackTags.TryGetLaneOctave(candidate, lane.Key, out int shift))
            {
                tagged++;
                tag = shift;
            }
            return lane with { OctaveShift = MidiTrackTags.SelectOctave(
                lane.OctaveShift, hasSaved ? savedShift : null, tag, manualOverrides.Contains(key)) };
        }).ToList();
        midiMetadataStatus = $"Read {candidate.LaneInstruments.Count} MIDI channel/lane(s); " +
            $"found {tagged} explicit octave tag(s) and instrument suggestions.";
    }

    private int GetMidiSoloInstrumentSuggestion()
    {
        if (song is null || !cfg.AutoDetectMidiTrackInstruments) return 0;
        var parts = lanes.Where(l => l.Include)
            .ToDictionary(l => l.Key, _ => 1UL);
        return MidiInstrumentInference.ForParty(song, parts).FirstOrDefault()?.InstrumentId ?? 0;
    }

    private IReadOnlyList<MidiPartyInstrumentSuggestion> GetMidiPartySuggestions() =>
        song is null || !cfg.AutoDetectMidiTrackInstruments
            ? Array.Empty<MidiPartyInstrumentSuggestion>()
            : MidiInstrumentInference.ForParty(song, partyAssignments);

    private string BuildMidiMetadataReport()
    {
        if (song is null) return "No MIDI loaded.";
        var lines = new List<string> {
            "Windbound MIDI metadata report",
            "File: " + loaded,
            $"Detect instruments: {cfg.AutoDetectMidiTrackInstruments}; " +
                $"Read octave tags: {cfg.AutoReadMidiTrackOctaves}",
            $"MIDI lanes: {song.LaneInstruments.Count}; Active lanes: {lanes.Count(l => l.Include)}; " +
                $"Party assignments: {partyAssignments.Count}",
            $"Suggested Solo instrument: {InstrumentCatalog.Display(GetMidiSoloInstrumentSuggestion())}",
            $"Manual octave overrides: {string.Join(", ", GetManualOctaveOverrides())}"
        };
        foreach (var lane in song.LaneInstruments)
        {
            var key = new MidiLaneKey(lane.Track, lane.Channel);
            var inferred = MidiInstrumentInference.ForLane(lane, song.FileName,
                song.LaneInstruments.Count(x => !x.IsPercussion));
            bool tagged = MidiTrackTags.TryGetLaneOctave(song, key, out int tag);
            var applied = lanes.FirstOrDefault(l => l.Key == key);
            lines.Add($"Track {lane.Track + 1}, Ch {lane.Channel + 1}: FF03='{lane.TrackName}', " +
                $"FF04='{lane.InstrumentName}', FF01/marker tags='{lane.TagMetadata}', " +
                $"GM={(lane.Program.HasValue ? (lane.Program.Value + 1).ToString() : "none")}, " +
                $"instrument={InstrumentCatalog.Display(inferred.InstrumentId)}, source={inferred.Source}, " +
                $"octaveTag={(tagged ? tag.ToString() : "none")}, " +
                $"appliedShift={(applied?.OctaveShift.ToString() ?? "not assigned")}, " +
                $"included={(applied?.Include.ToString() ?? "missing")}");
        }
        if (song.LaneInstruments.Count == 0) lines.Add("No note-bearing MIDI channels found.");
        return string.Join("\n", lines);
    }

    private void DrawMidiMetadataControls()
    {
        if (song is null) return;
        ImGui.Separator();
        ImGui.TextColored(new Vector4(.70f, .92f, .98f, 1f),
            Loc.T("MIDI INSTRUMENTS & OCTAVE TAGS — automatic detection"));
        bool instruments = cfg.AutoDetectMidiTrackInstruments;
        if (ImGui.Checkbox(Loc.T("Auto-detect instruments from MIDI tracks / GM programs"), ref instruments))
        {
            cfg.AutoDetectMidiTrackInstruments = instruments;
            cfg.Save();
        }
        bool octaves = cfg.AutoReadMidiTrackOctaves;
        if (ImGui.Checkbox(Loc.T("Read #+12 / #-12 octave tags on MIDI tracks"), ref octaves))
        {
            cfg.AutoReadMidiTrackOctaves = octaves;
            ApplyInitialOctaveShifts(song, loaded);
            Rebuild();
            cfg.Save();
        }
        ImGui.TextWrapped(Loc.T("Track names such as #+12 #Lute or #-12 #Piano take priority over General MIDI programs. Octaves affect only their own tracks; a manual octave edit is kept on reload."));
        ImGui.TextDisabled(Loc.T("In Ensemble, first assign tracks to Bards, then press SET PARTY INSTRUMENTS to equip the automatically detected instruments."));
        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor &&
            ImGui.SmallButton(Loc.T("Use MIDI instrument suggestions (clear manual instrument overrides)")))
        {
            cfg.PartyInstrumentsByFile.Remove(loaded);
            cfg.InstrumentsByFile.Remove(loaded);
            cfg.Save();
            midiMetadataStatus = "Cleared saved manual instrument choices for this MIDI; using track metadata.";
        }
        if (ImGui.SmallButton(Loc.T("Restore MIDI octave tags (clear manual octave overrides)")))
        {
            cfg.ManualOctaveLaneOverridesByFile.Remove(loaded);
            cfg.OctaveShiftsByFile.Remove(loaded);
            lanes = MidiOptimization.Suggest(song).ToList();
            ApplyInitialOctaveShifts(song, loaded);
            RememberOctaves();
            Rebuild();
            midiMetadataStatus = "Restored the MIDI's explicit octave tags, without editing MIDI bytes.";
        }
        ImGui.TextDisabled(Loc.T(midiMetadataStatus));
        ImGui.TextDisabled(Loc.T($"Solo instrument suggested: {InstrumentCatalog.Display(GetMidiSoloInstrumentSuggestion())}; " +
            $"manual octave overrides: {GetManualOctaveOverrides().Count}."));
        if (ImGui.SmallButton(Loc.T("Re-read MIDI tags now (keep manual overrides)")))
        {
            ApplyInitialOctaveShifts(song, loaded);
            Rebuild();
            midiMetadataStatus += " Re-read complete; manual overrides preserved.";
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Copy detected MIDI metadata report")))
            ImGui.SetClipboardText(BuildMidiMetadataReport());

        if (ImGui.TreeNode(Loc.T("Detected MIDI tracks, instruments and octaves")))
        {
            foreach (var lane in song.LaneInstruments)
            {
                var suggested = MidiInstrumentInference.ForLane(lane, song.FileName,
                    song.LaneInstruments.Count(x => !x.IsPercussion));
                var key = new MidiLaneKey(lane.Track, lane.Channel);
                string oct = MidiTrackTags.TryGetLaneOctave(song, key, out int shift)
                    ? $"{shift:+0;-0;+0} semitones" : "(optimiser default)";
                string source = suggested.InstrumentId == 0 ? "Unknown / keep current" :
                    InstrumentCatalog.Display(suggested.InstrumentId);
                int? applied = lanes.FirstOrDefault(l => l.Key == key)?.OctaveShift;
                string effective = applied.HasValue ? $"{applied:+0;-0;+0}" : "(not in arrangement)";
                ImGui.TextWrapped(Loc.T($"Track {lane.Track + 1} / Ch {lane.Channel + 1}: " +
                    $"{lane.TrackName} → {source}; file octave {oct}; APPLIED {effective}. {suggested.Source}"));
            }
            ImGui.TreePop();
        }
        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor)
        {
            foreach (var suggestion in GetMidiPartySuggestions())
            {
                if (suggestion.InstrumentId == 0) continue;
                var member = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party)
                    .FirstOrDefault(x => x.ContentId == suggestion.ContentId);
                string memberName = member?.Name ?? "Unknown Bard";
                string conflict = suggestion.ConflictingTracks
                    ? " — conflicting instrument tracks; dominant instrument suggested (review!)" : "";
                ImGui.TextWrapped(Loc.T($"{memberName}: {InstrumentCatalog.Display(suggestion.InstrumentId)} " +
                    $"from {suggestion.TotalTracks} assigned MIDI lane(s){conflict}"));
            }
        }
    }
}
