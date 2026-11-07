using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    // The master lineup belongs to a saved Ensemble playlist. Other profiles
    // can arrange the same MIDI for completely different octets without conflict.
    private string CurrentLineupProfileId => SavedPlaylistLibrary.GetActive(cfg).Id;

    private void RememberCurrentPartyLineup()
    {
        if (!cfg.KeepPartyAssignmentsAcrossSongs ||
            (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor || lanes.Count == 0)
            return;
        SavePartyLineup(showStatus: false);
    }

    private void SavePartyLineup(bool showStatus = true)
    {
        if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
        {
            partyLineupStatus = "Cannot change party lineup while MIDI is playing.";
            return;
        }
        if (lanes.Count == 0 || partyAssignments.Count == 0)
        {
            partyLineupStatus = "No performer assignments to save. Choose party members above first.";
            return;
        }
        cfg.EnsembleLineupsByPlaylistId[CurrentLineupProfileId] =
            PartyTrackLineup.Capture(lanes.Select(l => l.Key).ToList(), partyAssignments);
        cfg.Save();
        if (showStatus) partyLineupStatus = "Default lineup stored for this Ensemble playlist.";
    }

    private void ApplySavedPartyLineup()
    {
        if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
        {
            partyLineupStatus = "Cannot change party lineup while MIDI is playing.";
            return;
        }
        if (lanes.Count == 0 ||
            !cfg.EnsembleLineupsByPlaylistId.TryGetValue(CurrentLineupProfileId, out var saved) || saved is null)
        {
            partyLineupStatus = "No saved party lineup available.";
            return;
        }
        var matched = saved.Apply(lanes.Select(l => l.Key).ToList());
        partyAssignments.Clear();
        foreach (var item in matched.Assignments) partyAssignments[item.Key] = item.Value;
        // Explicitly applying it means this song now has its own override.
        StorePartyAssignments();
        ApplyPartyAssignmentsToLocal();
        TryPublishPartyAssignments();
        partyLineupStatus = "Default lineup applied to this MIDI; edit any differing tracks before SET PARTY TRACKS.";
        if (matched.HasDifferentLaneCount)
            partyLineupStatus += Loc.IsFrench
                ? $" Formation enregistrée : {matched.SavedLaneCount}, pistes actuelles : {matched.NewLaneCount}. " +
                    $"Vérifiez {matched.UnassignedNewSlots} nouvelle(s) piste(s)."
                : $" Saved {matched.SavedLaneCount}, now {matched.NewLaneCount} lanes; " +
                    $"review assignments, including {matched.UnassignedNewSlots} new lane(s).";
    }

    private void DrawReusablePartyLineupControls()
    {
        bool reuse = cfg.KeepPartyAssignmentsAcrossSongs;
        if (ImGui.Checkbox(Loc.T("Keep party track assignments across songs"), ref reuse))
        {
            cfg.KeepPartyAssignmentsAcrossSongs = reuse;
            // Do not replace an existing master lineup when re-enabling after
            // a special song-specific edit. Saving the master is explicit.
            if (reuse && partyAssignments.Count > 0 &&
                !cfg.EnsembleLineupsByPlaylistId.ContainsKey(CurrentLineupProfileId))
                SavePartyLineup();
            cfg.Save();
        }
        ImGui.TextDisabled(Loc.T("Reuse the same Bard lineup in this Ensemble playlist. " +
            "New MIDI parts follow the saved track order; saved song-specific assignments take priority."));
        bool hasSaved = cfg.EnsembleLineupsByPlaylistId.TryGetValue(CurrentLineupProfileId, out var saved) && saved is not null;
        if (ImGui.SmallButton(Loc.T("Save current as default lineup"))) SavePartyLineup();
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Apply saved lineup to this MIDI"))) ApplySavedPartyLineup();
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Forget saved lineup")))
        {
            cfg.EnsembleLineupsByPlaylistId.Remove(CurrentLineupProfileId);
            cfg.Save();
            partyLineupStatus = "Saved lineup cleared; this MIDI's specific assignments remain.";
        }
        if (hasSaved)
            ImGui.TextDisabled(Loc.IsFrench
                ? $"Formation enregistrée : {saved!.PerformerIdsBySlot.Count} emplacements, " +
                    $"{saved.PerformerIdsBySlot.Count(x => x != 0)} attribués."
                : $"Saved lineup: {saved!.PerformerIdsBySlot.Count} slots, " +
                    $"{saved.PerformerIdsBySlot.Count(x => x != 0)} assigned.");
        ImGui.TextDisabled(Loc.T("To change only one song, untick reuse before editing tracks, then turn it on again afterwards."));
        ImGui.TextDisabled(Loc.T("The saved lineup is not erased by Clear party assignments; " +
            "choose Forget saved lineup to remove it."));
    }
}
