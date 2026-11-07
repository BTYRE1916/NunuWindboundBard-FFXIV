using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// FFXIV range, instrument, part and note-overlap inspection.
/// The inspector is read-only until the user explicitly clicks Auto-fit.
/// </summary>
public sealed partial class MainWindow
{
    private MidiInspectionReport? concertInspection;
    private string concertInspectionFile = "";
    private string concertInspectionStatus = "Select a MIDI and click Inspect Arrangement.";

    private void InspectCurrentArrangement()
    {
        try
        {
            if (song is null || string.IsNullOrWhiteSpace(loaded))
                throw new InvalidOperationException("Load a MIDI first.");
            var roster = cfg.EnsembleMode == (int)EnsembleRole.Conductor && cfg.GameEnsembleEnabled
                ? NativeRoster() : null;
            concertInspection = MidiArrangementInspector.Inspect(song, lanes, cfg.RepairOctaves,
                roster is null ? null : partyAssignments,
                roster, roster is null ? null : GetPartyInstrumentChoices());
            concertInspectionFile = loaded;
            concertInspectionStatus = $"Inspection complete: {concertInspection.Errors} error(s), " +
                $"{concertInspection.Warnings} warning(s), {concertInspection.PlayableNotes:N0} notes playable.";
        }
        catch (Exception ex) { concertInspection = null; concertInspectionStatus = "Inspection failed: " + ex.Message; }
    }

    private string BuildInspectionReport()
    {
        if (concertInspection is null) return "No arrangement inspection results.";
        var r = concertInspection;
        var lines = new List<string> {
            "Windbound Bard - MIDI Arrangement Inspector",
            "MIDI: " + Path.GetFileName(concertInspectionFile),
            $"Source notes: {r.SourceNotes}; Playable: {r.PlayableNotes}; Excluded: {r.ExcludedNotes}; Dropped: {r.DroppedNotes}",
            $"Issues: {r.Errors} error(s), {r.Warnings} warning(s)",
            "FFXIV range: C3 (MIDI 48) through C6 (MIDI 84)", ""
        };
        foreach (var item in r.Lanes)
            lines.Add($"T{item.Key.Track + 1}/Ch{item.Key.Channel + 1} {item.Name}: " +
                $"source={item.SourceNotes}, playable={item.PlayableNotes}, " +
                $"range={NoteMapper.GetNoteName(item.SourceLowest)}..{NoteMapper.GetNoteName(item.SourceHighest)}, " +
                $"out-of-range={item.OriginalOutOfRange}, shift={item.OctaveShift:+0;-0;0}, " +
                $"instrument={InstrumentCatalog.Display(item.SuggestedInstrument)}, " +
                $"tag={(item.HasExplicitOctave ? item.ExplicitOctave.ToString() : "none")}, " +
                $"Bard={item.AssignedPerformer:X}, enabled={item.Included}, overlap={item.OverlappingDuplicates}");
        lines.Add("");
        foreach (var issue in r.Issues)
            lines.Add($"[{issue.Severity}] " +
                (issue.Lane.HasValue ? $"T{issue.Lane.Value.Track + 1}/Ch{issue.Lane.Value.Channel + 1}: " : "") +
                issue.Description);
        return string.Join("\n", lines);
    }

    private void AutoFitUntaggedOctaves()
    {
        if (song is null || string.IsNullOrWhiteSpace(loaded)) return;
        try
        {
            if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
                throw new InvalidOperationException("Stop playback before changing octaves.");
            var manual = GetManualOctaveOverrides();
            var proposals = MidiOptimization.Suggest(song).ToDictionary(p => p.Key, p => p.OctaveShift);
            int changed = 0;
            var updated = new List<MidiLanePlan>();
            foreach (var current in lanes)
            {
                string key = GuitarToneOverrides.Key(current.Key);
                bool tagged = MidiTrackTags.TryGetLaneOctave(song, current.Key, out _);
                if (!current.Include || tagged || manual.Contains(key) ||
                    !proposals.TryGetValue(current.Key, out int suggested) ||
                    suggested == current.OctaveShift)
                { updated.Add(current); continue; }
                updated.Add(current with { OctaveShift = suggested });
                MarkOctaveManual(current.Key); // explicit approved edit survives reload
                changed++;
            }
            if (changed == 0)
            { concertInspectionStatus = "No untagged, non-manual octave lanes need adjusting."; return; }
            lanes = updated;
            RememberOctaves();
            Rebuild();
            InspectCurrentArrangement();
            concertInspectionStatus += $" Auto-fitted {changed} untagged lane(s); original MIDI unchanged.";
        }
        catch (Exception ex) { concertInspectionStatus = "Auto-fit cancelled: " + ex.Message; }
    }

    private void DrawArrangementInspector()
    {
        ImGui.TextColored(new Vector4(.98f,.60f,.84f,1f), Loc.T("MIDI ARRANGEMENT INSPECTOR — PRE-CONCERT CHECK"));
        ImGui.TextWrapped(Loc.T("Analyse each MIDI track before performance. The original MIDI file is never modified."));
        if (song is null)
        { ImGui.TextDisabled(Loc.T("Load a MIDI in Songs & Playback first.")); return; }
        if (ImGui.Button(Loc.T("INSPECT MIDI ARRANGEMENT"))) InspectCurrentArrangement();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Auto-fit untagged octaves (manual action)"))) AutoFitUntaggedOctaves();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Copy Inspection Report"))) ImGui.SetClipboardText(BuildInspectionReport());
        ImGui.TextDisabled(Loc.T("Auto-fit skips #+12 tags, disabled lanes and saved manual octave overrides."));
        ImGui.TextWrapped(Loc.T(concertInspectionStatus));
        var result = concertInspection;
        if (result is null || !string.Equals(concertInspectionFile, loaded, StringComparison.OrdinalIgnoreCase)) return;
        ImGui.TextColored(result.Errors > 0 ? new Vector4(1f,.58f,.42f,1f) : new Vector4(.45f,.94f,.67f,1f),
            Loc.T($"Playable notes: {result.PlayableNotes:N0}/{result.SourceNotes:N0} • Errors: {result.Errors} • Warnings: {result.Warnings}"));
        ImGui.TextDisabled(Loc.T("Each row: Source / playable / lowest-highest pitch / octave / instrument / assigned Bard"));
        ImGui.BeginChild("##InspectionLanes", new Vector2(-1, Math.Min(320, 32 * result.Lanes.Count + 12)), true);
        foreach (var lane in result.Lanes)
        {
            ImGui.TextUnformatted(Loc.T($"T{lane.Key.Track + 1} Ch{lane.Key.Channel + 1}: " ) + lane.Name);
            ImGui.SameLine();
            ImGui.TextDisabled(Loc.T($"{lane.PlayableNotes}/{lane.SourceNotes} notes • " +
                $"{NoteMapper.GetNoteName(lane.SourceLowest)}–{NoteMapper.GetNoteName(lane.SourceHighest)} " +
                $"• {lane.OctaveShift:+0;-0;0} • {InstrumentCatalog.Display(lane.SuggestedInstrument)}"));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"MIDI source: {lane.InstrumentSource}; " +
                    $"Tag: {(lane.HasExplicitOctave ? lane.ExplicitOctave.ToString() : "none")}; " +
                    $"Bard: {lane.AssignedPerformer:X}; overlapping notes: {lane.OverlappingDuplicates}");
        }
        ImGui.EndChild();
        if (result.Issues.Count == 0)
            ImGui.TextColored(new Vector4(.43f,.95f,.66f,1f), Loc.T("No problems detected in the current MIDI arrangement."));
        else if (ImGui.TreeNode(Loc.T($"Inspection findings ({result.Issues.Count})##inspectionFindings")))
        {
            foreach (var issue in result.Issues.Take(140))
                ImGui.TextColored(issue.Severity == MidiInspectionSeverity.Error
                    ? new Vector4(1f,.57f,.45f,1f) : issue.Severity == MidiInspectionSeverity.Warning
                    ? new Vector4(1f,.78f,.40f,1f) : new Vector4(.70f,.80f,.99f,1f),
                    Loc.T($"[{issue.Severity}] ") +
                    (issue.Lane.HasValue ? $"T{issue.Lane.Value.Track + 1}/Ch{issue.Lane.Value.Channel + 1}: " : "") +
                    Loc.T(issue.Description));
            if (result.Issues.Count > 140)
                ImGui.TextDisabled(Loc.T("Only the first 140 issues are shown; copy the full report for everything."));
            ImGui.TreePop();
        }
    }
}
