using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private Task<ConcertPack.Import?>? concertPackWork;
    private ConcertSnapshot? packDraft;
    private readonly HashSet<string> packFiles = new(StringComparer.OrdinalIgnoreCase);
    private bool packStage = true;
    private string packStatus = "";

    private void DrawConcertPacks()
    {
        if (!ImGui.TreeNode(Loc.T("Portable concert packs"))) return;
        ImGui.BeginDisabled(concertPackWork is not null);
        if (ImGui.Button(Loc.T("Choose files for concert pack")))
        {
            try
            {
                packDraft = CaptureConcertSnapshot(SavedPlaylistLibrary.GetActive(cfg).Name);
                if (packDraft.PlaylistFiles.Count == 0)
                    throw new InvalidOperationException("Add MIDI files to the active playlist before exporting.");
                packFiles.Clear();
                packFiles.UnionWith(packDraft.PlaylistFiles);
            }
            catch (Exception ex) { packDraft = null; packStatus = ex.Message; }
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Import concert pack...")))
        {
            fileDialogs.OpenFileDialog("Import concert pack", ".zip", (bool ok, List<string> paths) =>
            {
                if (!ok || paths.Count == 0) return;
                if (cfg.ConcertSnapshots.Count >= ConcertSnapshotFormat.MaxSnapshots)
                { packStatus = "Snapshot library is full."; return; }
                string dest = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "ConcertPacks");
                string[] library = BuildNativeSongCandidates(-1).ToArray();
                concertPackWork = Task.Run<ConcertPack.Import?>(() => ConcertPack.Open(paths[0], dest, library));
                packStatus = "Verifying and importing concert pack.";
            }, 1, GetFileDialogStartFolder());
        }
        if (packDraft is { } draft)
        {
            ImGui.TextWrapped("Include MIDI files you can share. Omitted songs will be looked up by their fingerprint when imported.");
            if (ImGui.SmallButton("Include all")) packFiles.UnionWith(draft.PlaylistFiles);
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear selection")) packFiles.Clear();
            ImGui.BeginChild("##PortableConcertFiles", new Vector2(-1, 118), true);
            for (int i = 0; i < draft.PlaylistFiles.Count; i++)
            {
                string path = draft.PlaylistFiles[i];
                bool included = packFiles.Contains(path);
                if (ImGui.Checkbox(Path.GetFileName(path) + $"##pack{i}", ref included))
                {
                    if (included) packFiles.Add(path);
                    else packFiles.Remove(path);
                }
            }
            ImGui.EndChild();
            if (cfg.StageFormations.Any(p => p.Id == cfg.SelectedStageFormationId))
                ImGui.Checkbox("Include selected stage formation", ref packStage);
            if (ImGui.Button("Export concert pack..."))
            {
                StageFormationPreset? stage = packStage
                    ? cfg.StageFormations.FirstOrDefault(p => p.Id == cfg.SelectedStageFormationId)
                    : null;
                HashSet<string> selected = new(packFiles, StringComparer.OrdinalIgnoreCase);
                fileDialogs.SaveFileDialog("Export concert pack", ".zip", "Windbound Concert", ".zip", (bool ok, string path) =>
                {
                    if (!ok) return;
                    if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) path += ".zip";
                    var snapshot = ConcertSnapshotFormat.Copy(draft);
                    // The stage preset is edited by the UI, so capture a copy for the worker.
                    var formation = stage is null ? null : new StageFormationPreset
                    {
                        Name = stage.Name, TerritoryId = stage.TerritoryId, Anchor = stage.Anchor,
                        AnchorHeading = stage.AnchorHeading,
                        Members = stage.Members.Select(m => new StageFormationMember
                        { ContentId = m.ContentId, Name = m.Name, Offset = m.Offset, HeadingOffset = m.HeadingOffset }).ToList(),
                    };
                    concertPackWork = Task.Run<ConcertPack.Import?>(() =>
                    {
                        ConcertPack.Export(path, snapshot, selected, formation);
                        return (ConcertPack.Import?)null;
                    });
                    packStatus = "Exporting concert pack.";
                }, GetFileDialogStartFolder());
            }
            ImGui.SameLine();
            if (ImGui.Button("Close selection")) packDraft = null;
        }
        ImGui.EndDisabled();
        if (packStatus.Length > 0) ImGui.TextWrapped(packStatus);
        ImGui.TreePop();
    }

    private void PollConcertPack()
    {
        if (concertPackWork is not { IsCompleted: true } work) return;
        concertPackWork = null;
        try
        {
            var imported = work.GetAwaiter().GetResult();
            if (imported is null) { packStatus = "Concert pack exported."; return; }
            if (cfg.ConcertSnapshots.Count >= ConcertSnapshotFormat.MaxSnapshots)
            {
                Directory.Delete(imported.Folder, recursive: true);
                packStatus = "Snapshot library is full.";
                return;
            }
            cfg.ConcertSnapshots.Add(imported.Snapshot);
            if (imported.Stage is { } stage)
            {
                stage.Id = Guid.NewGuid().ToString("N");
                cfg.StageFormations.Add(stage);
            }
            selectedSnapshotId = imported.Snapshot.Id;
            cfg.Save();
            packStatus = $"Imported '{imported.Snapshot.Name}'. Review it in Band → Concert Snapshots before restoring.";
        }
        catch (Exception ex) { packStatus = "Concert pack failed: " + ex.Message; }
    }
}
