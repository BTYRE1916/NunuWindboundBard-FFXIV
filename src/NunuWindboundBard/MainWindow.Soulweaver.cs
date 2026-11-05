using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private SoulweaverResult? soulweaver;
    private string soulweaverMessage = "Load a MIDI and select Analyse & Generate to build an arrangement.";
    private string soulweaverLastExport = "";
    private readonly Vector4[] soulweaverColors =
    [
        new(.99f, .36f, .67f, 1), new(.41f, .85f, 1f, 1), new(.60f, .98f, .59f, 1),
        new(1f, .77f, .43f, 1), new(.78f, .60f, 1f, 1), new(.97f, .54f, .86f, 1),
        new(.44f, .91f, .85f, 1), new(.94f, .88f, .56f, 1),
    ];

    private void InvalidateSoulweaver()
    {
        soulweaver = null;
        soulweaverLastExport = "";
    }

    private SoulweaverPreset GetSoulweaverPreset()
    {
        cfg.SoulweaverPresetsByFile ??= new(StringComparer.OrdinalIgnoreCase);
        if (!cfg.SoulweaverPresetsByFile.TryGetValue(loaded, out var preset) || preset is null)
        {
            preset = new SoulweaverPreset();
            cfg.SoulweaverPresetsByFile[loaded] = preset;
        }
        preset.InstrumentByPart ??= new();
        preset.PartyMemberByPart ??= new();
        return preset;
    }

    private void DrawSoulweaver()
    {
        ImGui.TextColored(new Vector4(1f, .51f, .80f, 1f), "SOULWEAVER — ADAPTIVE ORCHESTRA STUDIO");
        ImGui.TextWrapped("Transform a source MIDI into separate Solo, Duet, Quartet or Octet parts. " +
            "This first release uses deterministic melody, bass and harmony heuristics; preview the resulting parts before exporting. " +
            "Your source MIDI, game note output and existing playlists are not changed by analysis.");
        if (song is null || string.IsNullOrWhiteSpace(loaded))
        {
            ImGui.TextDisabled("Load a MIDI in Songs & Playback first, then return here.");
            return;
        }
        var preset = GetSoulweaverPreset();
        ImGui.TextUnformatted("Source: " + Path.GetFileName(loaded));
        ImGui.TextDisabled($"{song.Notes.Count:N0} MIDI notes; {song.Tracks.Count} source tracks; " +
            $"C3–C6 target; {song.InitialBpm:0.#} initial BPM");
        ImGui.Separator();
        ImGui.TextUnformatted("Choose the number of musicians:");
        foreach (var n in new[] { 1, 2, 4, 8 })
        {
            string label = n switch { 1 => "Solo", 2 => "Duet", 4 => "Quartet", _ => "Octet" };
            if (ImGui.RadioButton($"{label}##Soulweaver{n}", preset.Performers == n))
            {
                preset.Performers = n; InvalidateSoulweaver(); cfg.Save();
            }
            if (n != 8) ImGui.SameLine();
        }
        bool all = preset.IncludeAllTracks;
        if (ImGui.Checkbox("Analyse all non-percussion tracks (ignore local ensemble mutes)", ref all))
        {
            preset.IncludeAllTracks = all; InvalidateSoulweaver(); cfg.Save();
        }
        ImGui.TextDisabled("Uncheck to respect the track mute choices currently shown in Songs & Playback.");
        int cap = Math.Clamp(preset.MaxNotesPerOnset, 0, 8);
        if (ImGui.SliderInt("Per-performer chord limit (0 = unlimited)", ref cap, 0, 8))
        {
            preset.MaxNotesPerOnset = cap; InvalidateSoulweaver(); cfg.Save();
        }
        ImGui.TextDisabled("Unlimited preserves every prepared note. A smaller cap may be useful for dense arrangements.");
        if (ImGui.Button("Analyse & Generate Parts")) GenerateSoulweaver();
        ImGui.SameLine();
        if (ImGui.Button("Octave adjustment help"))
            soulweaverMessage = "Use the FFXIV Optimiser tab to edit octave shifts, then come back and regenerate.";
        ImGui.TextWrapped(soulweaverMessage);
        if (soulweaver is null) return;

        ImGui.Separator();
        ImGui.TextColored(new Vector4(.75f, 1f, .83f, 1f), "ANALYSIS & PART GENERATION");
        var a = soulweaver.Analysis;
        ImGui.TextWrapped($"Estimated key: {a.EstimatedKey} (heuristic; confidence margin {a.KeyConfidence:0.0})  |  " +
            $"Chord onsets: {a.ChordOnsets:N0}  |  Musical phrases: {a.PhraseCount:N0}");
        ImGui.TextUnformatted($"Prepared notes: {a.SourceNotes:N0}  |  Exported notes: {a.ExportNotes:N0}  |  " +
            $"Range: {NoteMapper.GetNoteName(a.LowestPitch)}–{NoteMapper.GetNoteName(a.HighestPitch)}");
        foreach (string warning in a.Warnings)
            ImGui.TextColored(new Vector4(1f, .77f, .39f, 1f), warning);
        DrawSoulweaverTimeline(soulweaver);

        ImGui.Separator();
        ImGui.TextColored(new Vector4(.83f, .65f, 1f, 1f), "MUSICIAN PARTS & INSTRUMENT RECOMMENDATIONS");
        ImGui.TextDisabled("Each named track becomes a separate MIDI lane. You may change instrument choices and party assignments before export.");
        var partyChoices = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party);
        if (preset.Performers > 1 && ImGui.Button("Auto-assign parts to FFXIV party order"))
        {
            for (int i = 0; i < soulweaver.Parts.Count; ++i)
            {
                ulong id = i < partyChoices.Count ? partyChoices[i].ContentId : 0;
                if (id == 0) preset.PartyMemberByPart.Remove(i);
                else preset.PartyMemberByPart[i] = id;
            }
            cfg.Save();
        }
        ImGui.BeginChild("##SoulweaverPartsScroll", new Vector2(0, 305), true);
        foreach (var part in soulweaver.Parts)
        {
            int i = part.Index;
            ImGui.TextColored(soulweaverColors[i], $"{i + 1}. {part.Name} — {part.Notes.Count:N0} notes");
            ImGui.TextDisabled(part.RoleDescription + (part.Notes.Count > 0
                ? $"  |  {NoteMapper.GetNoteName(part.LowestPitch)}–{NoteMapper.GetNoteName(part.HighestPitch)}" : "  |  No notes for this part"));
            int id = preset.InstrumentByPart.TryGetValue(i, out int selectedId) && InstrumentCatalog.Valid(selectedId) && selectedId > 0
                ? selectedId : part.SuggestedInstrumentId;
            ImGui.SetNextItemWidth(Math.Min(320, Math.Max(190, ImGui.GetContentRegionAvail().X * .7f)));
            if (ImGui.BeginCombo($"Instrument##SoulInstrument{i}", InstrumentCatalog.Display(id)))
            {
                foreach (var instrument in InstrumentCatalog.All)
                {
                    if (ImGui.Selectable($"{instrument.DisplayName}##SoulInstrumentChoice{i}_{instrument.Id}", id == instrument.Id))
                    {
                        preset.InstrumentByPart[i] = instrument.Id; cfg.Save();
                    }
                }
                ImGui.EndCombo();
            }
            if (preset.Performers > 1)
            {
                ulong selected = preset.PartyMemberByPart.GetValueOrDefault(i);
                string label = selected == 0 ? "Assign in Ensemble later" :
                    partyChoices.FirstOrDefault(p => p.ContentId == selected)?.Name ?? "Player no longer in party";
                ImGui.SetNextItemWidth(Math.Min(320, Math.Max(190, ImGui.GetContentRegionAvail().X * .7f)));
                if (ImGui.BeginCombo($"Player##SoulPlayer{i}", label))
                {
                    if (ImGui.Selectable($"Assign in Ensemble later##SoulNoPlayer{i}", selected == 0))
                    {
                        preset.PartyMemberByPart.Remove(i); cfg.Save();
                    }
                    foreach (var member in partyChoices)
                        if (ImGui.Selectable($"{(member.IsLocal ? "[Me] " : "")}{member.Name}##SoulPlayer{i}_{member.ContentId}",
                                selected == member.ContentId))
                        {
                            preset.PartyMemberByPart[i] = member.ContentId; cfg.Save();
                        }
                    ImGui.EndCombo();
                }
            }
            ImGui.Separator();
        }
        ImGui.EndChild();
        ImGui.TextDisabled("Ensemble instruments are recommendations; each player equips their chosen instrument locally before cue.");

        ImGui.Separator();
        ImGui.TextColored(new Vector4(.83f, .65f, 1f, 1f), "SAVE / ADD TO LIBRARY");
        if (ImGui.Button("Export arrangement MIDI...")) SaveSoulweaverAs();
        ImGui.SameLine();
        if (ImGui.Button(preset.Performers == 1 ? "Export & add to Solo playlist" : "Export & add to Ensemble playlist"))
            SaveSoulweaverToPlaylist();
        ImGui.TextWrapped("Exports preserve original tempo changes and create one track per musician. " +
            "Your source MIDI is never overwritten. Existing Windbound multi-device ensembles can transfer the exported MIDI normally.");
        if (!string.IsNullOrWhiteSpace(soulweaverMessage))
            ImGui.TextWrapped("Export / studio status: " + soulweaverMessage);
        if (!string.IsNullOrWhiteSpace(soulweaverLastExport) && File.Exists(soulweaverLastExport))
        {
            ImGui.TextWrapped("Saved MIDI: " + soulweaverLastExport);
            if (ImGui.Button("Load exported arrangement in Windbound")) LoadFromUser(soulweaverLastExport);
            ImGui.SameLine();
            if (ImGui.Button("Add last export to playlist"))
                AddPreviousSoulweaverExportToPlaylist();
        }
    }

    private void GenerateSoulweaver()
    {
        try
        {
            if (song is null) throw new InvalidOperationException("Load a MIDI first.");
            var preset = GetSoulweaverPreset();
            var selectedLanes = preset.IncludeAllTracks
                ? lanes.Select(l => l with { Include = l.Key.Channel != 9 }).ToArray()
                : lanes.ToArray();
            var full = BardArranger.Build(song, new BardArrangeOptions(
                new MidiOptimizeOptions(selectedLanes, cfg.RepairOctaves),
                ChordStrategy.PreserveAll, cfg.GlobalTranspose, cfg.MinimumGapMs, cfg.ArpeggioSpacingMs));
            soulweaver = SoulweaverArranger.Generate(full, new SoulweaverOptions(preset.Performers,
                preset.MaxNotesPerOnset));
            soulweaverMessage = $"Generated {soulweaver.Parts.Count} musician part(s). " +
                $"{soulweaver.Analysis.ExportNotes:N0} notes ready for visual review and export.";
        }
        catch (Exception ex) { soulweaver = null; soulweaverMessage = "Soulweaver could not generate: " + ex.Message; }
    }

    private void SaveSoulweaverAs()
    {
        if (soulweaver is null || string.IsNullOrWhiteSpace(loaded)) return;
        string stem = SoulweaverFilenameStem();
        // Save dialog callback is user-initiated; it writes only the generated MIDI.
        fileDialogs.SaveFileDialog("Export Soulweaver arrangement", MidiFilter, stem + ".mid", ".mid",
            (bool ok, string path) =>
            {
                if (!ok || string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    if (!Path.HasExtension(path)) path += ".mid";
                    ExportSoulweaver(path, addToLibrary: false);
                }
                catch (Exception ex) { soulweaverMessage = "Export failed: " + ex.Message; }
            }, GetFileDialogStartFolder());
    }

    private string SoulweaverFilenameStem()
    {
        int performers = soulweaver?.Performers ?? 2;
        string mode = performers switch { 1 => "Solo", 2 => "Duet", 4 => "Quartet", _ => "Octet" };
        string stem = Path.GetFileNameWithoutExtension(loaded);
        stem = string.Concat(stem.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return (stem.Length > 72 ? stem[..72] : stem) + " - Soulweaver " + mode;
    }

    private void SaveSoulweaverToPlaylist()
    {
        if (soulweaver is null) return;
        try
        {
            string folder = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "SoulweaverArrangements");
            Directory.CreateDirectory(folder);
            string stem = SoulweaverFilenameStem();
            string target = Path.Combine(folder, stem + ".mid");
            for (int i = 2; File.Exists(target); ++i)
                target = Path.Combine(folder, stem + " (" + i + ").mid");
            ExportSoulweaver(target, addToLibrary: true);
        }
        catch (Exception ex) { soulweaverMessage = "Export to playlist failed: " + ex.Message; }
    }

    private void ExportSoulweaver(string target, bool addToLibrary)
    {
        if (soulweaver is null) throw new InvalidOperationException("Generate a Soulweaver arrangement first.");
        var preview = soulweaver;
        var preset = GetSoulweaverPreset();
        // Record the user's instrument choices in track names; players still choose
        // their own Performance instrument locally before a duet.
        var exportParts = preview.Parts.Select(p =>
        {
            int instrumentId = preset.InstrumentByPart.TryGetValue(p.Index, out var selected) &&
                InstrumentCatalog.Valid(selected) && selected > 0 ? selected : p.SuggestedInstrumentId;
            return p with { SuggestedInstrumentId = instrumentId,
                Name = $"{p.Name} ({InstrumentCatalog.Display(instrumentId)})" };
        }).ToArray();
        var exported = preview with { Parts = exportParts };
        string path = SoulweaverMidiExport.Save(loaded, target, exported);
        // Save() has reopened and verified the MIDI. Expose the path now, even if
        // playlist registration or config saving subsequently fails.
        soulweaverLastExport = path;
        if (!addToLibrary)
        {
            soulweaverMessage = "Export verified: " + path;
            return;
        }
        try
        {
            var profile = RegisterSoulweaverPlaylistExport(path, preview, preset);
            soulweaverMessage = $"Export verified: {Path.GetFileName(path)}. Added to {profile.Name} " +
                (preview.Performers == 1 ? "(Solo library)." : "(Ensemble library).") +
                " Switch libraries to see it if another library is open.";
        }
        catch (Exception ex)
        {
            // The MIDI itself is intact on disk; never report the whole export as
            // missing when only its saved playlist registration failed.
            soulweaverMessage = $"MIDI saved successfully to {path}, but adding it to the playlist failed: {ex.Message}. " +
                "Use 'Add last export to playlist' to retry.";
        }
    }

    private SavedPlaylist RegisterSoulweaverPlaylistExport(string path, SoulweaverResult preview, SoulweaverPreset preset)
    {
        int purpose = preview.Performers == 1 ? 0 : 1;
        var profile = SavedPlaylistLibrary.AddMidiToPurpose(cfg, path, purpose);
        if (preview.Performers == 1)
        {
            var soloId = preset.InstrumentByPart.GetValueOrDefault(0,
                preview.Parts[0].SuggestedInstrumentId);
            if (InstrumentCatalog.Valid(soloId) && soloId != 0) cfg.InstrumentsByFile[path] = soloId;
        }
        else
        {
            // MIDI track 0 is the tempo map; musician tracks start at 1.
            // The output channel assignment for overlapping notes can vary, so
            // assign every melodic channel of a performer track to that player.
            var assignments = new Dictionary<string, ulong>();
            // Channel distribution may have changed to preserve overlapping
            // same-pitch notes. Read the actual exported channels so the
            // conductor owns precisely the lanes that will play.
            var savedMidi = MidiFileReader.Load(path);
            foreach (var part in preview.Parts.Where(p => p.Notes.Count > 0))
            {
                ulong contentId = preset.PartyMemberByPart.GetValueOrDefault(part.Index);
                if (contentId == 0) continue;
                foreach (var channel in savedMidi.Notes.Where(n => n.Track == part.Index + 1)
                             .Select(n => n.Channel).Distinct())
                    assignments[$"{part.Index + 1}:{channel}"] = contentId;
            }
            if (assignments.Count > 0) cfg.PartyAssignmentsByFile[path] = assignments;
        }
        cfg.Save();
        return profile;
    }

    private void AddPreviousSoulweaverExportToPlaylist()
    {
        try
        {
            if (soulweaver is null || string.IsNullOrWhiteSpace(soulweaverLastExport) ||
                !File.Exists(soulweaverLastExport))
                throw new InvalidOperationException("Generate an arrangement and export it first.");
            var profile = RegisterSoulweaverPlaylistExport(soulweaverLastExport, soulweaver, GetSoulweaverPreset());
            soulweaverMessage = $"Added {Path.GetFileName(soulweaverLastExport)} to {profile.Name}. " +
                "Switch to its Solo/Ensemble library to find it.";
        }
        catch (Exception ex) { soulweaverMessage = "Add to playlist failed: " + ex.Message; }
    }

    private void DrawSoulweaverTimeline(SoulweaverResult preview)
    {
        float width = Math.Max(300f, ImGui.GetContentRegionAvail().X);
        const float row = 26f;
        float height = preview.Performers * row + 5f;
        Vector2 top = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        uint background = ImGui.ColorConvertFloat4ToU32(new Vector4(.07f, .065f, .12f, .96f));
        uint guide = ImGui.ColorConvertFloat4ToU32(new Vector4(.29f, .28f, .39f, 1f));
        draw.AddRectFilled(top, top + new Vector2(width, height), background);
        double duration = Math.Min(30_000, Math.Max(1, preview.DurationMs));
        for (int i = 0; i < preview.Parts.Count; i++)
        {
            float y = top.Y + i * row;
            draw.AddLine(new Vector2(top.X, y + row), new Vector2(top.X + width, y + row), guide);
            var notes = preview.Parts[i].Notes;
            uint colour = ImGui.ColorConvertFloat4ToU32(soulweaverColors[i]);
            // Keep UI responsive even with very large MIDI files: first 30 seconds and max 3,000 notes per part.
            foreach (var note in notes.Where(n => n.TimeMs <= duration).Take(3000))
            {
                float x = top.X + (float)(note.TimeMs / duration * width);
                float w = Math.Max(2.5f, (float)(Math.Min(note.DurationMs, duration) / duration * width));
                float offset = (note.Pitch - 48) / 36f * (row - 7);
                draw.AddRectFilled(new Vector2(x, y + row - 3 - offset),
                    new Vector2(Math.Min(top.X + width, x + w), y + row - offset), colour);
            }
        }
        ImGui.Dummy(new Vector2(width, height));
        ImGui.TextDisabled("Visual part preview: up to the first 30 seconds, colour-coded by musician (not an audio renderer).");
    }
}
