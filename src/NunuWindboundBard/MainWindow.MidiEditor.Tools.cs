using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private EditorEvent[] editorClipboard = [];
    private int editorClipboardPpq = 480, editorPasteTracks, editorPasteChannels;
    private int editorTranspose, editorTupletCount = 3, editorTupletBeats = 1, editorBarCount = 1;
    private float editorTimeScale = 1, editorTempoStart = 120, editorTempoEnd = 120;
    private int editorTempoRampBeats = 4, editorNewKind, editorValue = 60, editorSecondary = 90;
    private int editorEventTicks, editorEventLength = 120, editorSelectedEventId, editorEventPage;
    private double editorSongSeconds = 4;
    private string editorEventText = "", editorEventSearch = "";
    private static readonly string[] EditorKinds = ["Note", "Control", "Pitch bend", "Key pressure", "Channel pressure", "Program", "Tempo", "Time signature", "Marker"];

    private void SelectEditorEvents(Func<EditorEvent, bool> predicate)
    {
        editorSelection.Clear();
        foreach (var e in editor!.State.Events.Where(predicate)) editorSelection.Add(e.Id);
    }

    private void CopyEditorEvents()
    {
        editorClipboard = editor!.State.Events.Where(e => editorSelection.Contains(e.Id)).ToArray();
        editorClipboardPpq = editor.Ppq;
        editorStatus = $"Copied {editorClipboard.Length:N0} events.";
    }

    private void PasteEditorEvents()
    {
        if (editorClipboard.Length == 0) return;
        var doc = editor!;
        long first = editorClipboard[0].Tick;
        var tracks = doc.State.Tracks.Select(t => t.Id).ToHashSet();
        var pasted = editorClipboard.Select(e => e with
        {
            Id = doc.NextId(),
            Tick = editorCursor + (long)Math.Round((e.Tick - first) * (double)doc.Ppq / editorClipboardPpq),
            Length = Math.Max(1, (int)Math.Round(e.Length * (double)doc.Ppq / editorClipboardPpq)),
            Track = editorPasteTracks == 1 && tracks.Contains(e.Track) ? e.Track : editorTrack,
            Channel = e.IsChannelEvent && editorPasteChannels == 0 ? editorChannel : e.Channel,
        }).ToArray();
        ApplyEditor(() =>
        {
            doc.Add(pasted);
            editorSelection.Clear();
            foreach (var e in pasted) editorSelection.Add(e.Id);
        });
    }

    private void DrawEditorTools()
    {
        var doc = editor!;
        if (ImGui.SmallButton("Select visible##editor")) SelectEditorEvents(EditorVisible);
        ImGui.SameLine(); if (ImGui.SmallButton("Select track##editor")) SelectEditorEvents(e => e.Track == editorTrack);
        ImGui.SameLine(); if (ImGui.SmallButton("Select channel##editor")) SelectEditorEvents(e => e.Channel == editorChannel);
        if (ImGui.SmallButton("Select before cursor##editor")) SelectEditorEvents(e => EditorVisible(e) && e.Tick < editorCursor);
        ImGui.SameLine(); if (ImGui.SmallButton("Select after cursor##editor")) SelectEditorEvents(e => EditorVisible(e) && e.Tick >= editorCursor);
        ImGui.BeginDisabled(!doc.CanUndo);
        if (ImGui.SmallButton("Undo##editor")) ApplyEditor(doc.Undo, false);
        ImGui.EndDisabled();
        if (ImGui.TreeNode("Edit history##editor"))
        {
            for (int i = doc.History.Count - 1; i >= 0; i--)
            {
                var step = doc.History[i];
                ImGui.TextUnformatted($"{doc.History.Count - i} edit(s) ago · {step.Tracks.Length} tracks · {step.Events.Length:N0} events");
            }
            if (doc.History.Count == 0) ImGui.TextDisabled("No edits in this session yet.");
            ImGui.TreePop();
        }
        ImGui.SameLine(); ImGui.BeginDisabled(!doc.CanRedo);
        if (ImGui.SmallButton("Redo##editor")) ApplyEditor(doc.Redo, false);
        ImGui.EndDisabled();
        ImGui.SameLine(); ImGui.BeginDisabled(editorSelection.Count == 0);
        if (ImGui.SmallButton("Copy##editor")) CopyEditorEvents();
        ImGui.SameLine(); if (ImGui.SmallButton("Delete##editor")) ApplyEditor(() => doc.Delete(editorSelection), false);
        ImGui.EndDisabled();
        ImGui.SameLine(); ImGui.BeginDisabled(editorClipboard.Length == 0);
        if (ImGui.SmallButton("Paste at cursor##editor")) PasteEditorEvents();
        ImGui.EndDisabled();

        if (ImGui.CollapsingHeader("Selection tools##editor"))
        {
            ImGui.SetNextItemWidth(140); ImGui.Combo("Paste tracks##editor", ref editorPasteTracks, "Active track\0Original tracks\0");
            ImGui.SameLine(); ImGui.SetNextItemWidth(140); ImGui.Combo("Paste channels##editor", ref editorPasteChannels, "Active channel\0Original channels\0");
            ImGui.BeginDisabled(editorSelection.Count == 0);
            if (ImGui.Button("Move to active track##editor")) ApplyEditor(() => doc.Transform(editorSelection, e => e with { Track = editorTrack }));
            ImGui.SameLine(); if (ImGui.Button("Move to active channel##editor")) ApplyEditor(() => doc.Transform(editorSelection,
                e => e.IsChannelEvent ? e with { Channel = editorChannel } : e));
            if (ImGui.Button("Align starts##editor"))
            {
                long first = doc.State.Events.Where(IsEditorSelected).Min(e => e.Tick);
                ApplyEditor(() => doc.Transform(editorSelection, e => e with { Tick = first }));
            }
            ImGui.SameLine(); if (ImGui.Button("Align ends##editor"))
            {
                long last = doc.State.Events.Where(IsEditorSelected).Max(e => e.End);
                ApplyEditor(() => doc.Transform(editorSelection, e => e with { Tick = last - (e.End - e.Tick) }));
            }
            ImGui.SameLine(); if (ImGui.Button("Equalize notes##editor"))
            {
                var notes = doc.State.Events.Where(e => IsEditorSelected(e) && e.Kind == EditorEventKind.Note).ToArray();
                if (notes.Length > 0)
                {
                    long first = notes.Min(e => e.Tick);
                    int length = (int)Math.Round(notes.Average(e => (double)e.Length));
                    ApplyEditor(() => doc.Transform(editorSelection, e => e.Kind == EditorEventKind.Note ? e with { Tick = first, Length = length } : e));
                }
            }
            ImGui.SetNextItemWidth(85); ImGui.InputInt("Semitones##editor", ref editorTranspose);
            ImGui.SameLine(); if (ImGui.SmallButton("Transpose##editor")) ApplyEditor(() => doc.Transform(editorSelection,
                e => e.Kind == EditorEventKind.Note ? e with { Value = e.Value + Math.Clamp(editorTranspose, -127, 127) } : e));
            ImGui.SetNextItemWidth(90); ImGui.SliderFloat("Time scale##editor", ref editorTimeScale, .25f, 4, "%.2fx");
            ImGui.SameLine(); if (ImGui.SmallButton("Scale selection##editor"))
            {
                long first = doc.State.Events.Where(IsEditorSelected).Min(e => e.Tick);
                ApplyEditor(() => doc.Transform(editorSelection, e => e with { Tick = first + (long)Math.Round((e.Tick - first) * (double)editorTimeScale),
                    Length = Math.Max(1, (int)Math.Round(e.Length * (double)editorTimeScale)) }));
            }
            if (ImGui.Button("Quantize to grid##editor"))
            {
                int grid = Math.Max(1, EditorSubdivision);
                ApplyEditor(() => doc.Transform(editorSelection, e =>
                {
                    long start = doc.Clock.Snap(e.Tick, grid), end = doc.Clock.Snap(e.End, grid);
                    return e with { Tick = start, Length = (int)Math.Max(1, end - start) };
                }));
            }
            ImGui.SetNextItemWidth(82); ImGui.SliderInt("Tuplet notes##editor", ref editorTupletCount, 2, 12);
            ImGui.SameLine(); ImGui.SetNextItemWidth(85); ImGui.SliderInt("Over beats##editor", ref editorTupletBeats, 1, 8);
            ImGui.SameLine(); if (ImGui.SmallButton("Quantize tuplet##editor")) ApplyEditorTuplet();
            ImGui.EndDisabled();
        }

        if (ImGui.CollapsingHeader("Tempo, time signatures and measures##editor"))
        {
            ImGui.SetNextItemWidth(90); ImGui.SliderFloat("Tempo##editor", ref editorTempoStart, 20, 400, "%.1f BPM");
            ImGui.SameLine(); if (ImGui.SmallButton("Set tempo at cursor##editor")) SetEditorTempo(editorCursor, editorTempoStart);
            ImGui.SetNextItemWidth(90); ImGui.SliderFloat("Ramp end##editor", ref editorTempoEnd, 20, 400, "%.1f BPM");
            ImGui.SameLine(); ImGui.SetNextItemWidth(86); ImGui.SliderInt("Beats##editorRamp", ref editorTempoRampBeats, 1, 64);
            ImGui.SameLine(); if (ImGui.SmallButton("Add tempo ramp##editor")) AddEditorTempoRamp();
            ImGui.TextDisabled("Add or edit time signatures and markers in the Event editor.");
            ImGui.SetNextItemWidth(90); ImGui.SliderInt("Bar count##editor", ref editorBarCount, 1, 64);
            ImGui.SameLine(); if (ImGui.SmallButton("Insert bars##editor"))
            {
                var bar = doc.Clock.Bar(editorCursor);
                ApplyEditor(() => doc.InsertTime(bar.Start, (bar.End - bar.Start) * editorBarCount));
            }
            ImGui.SameLine(); if (ImGui.SmallButton("Remove bars##editor"))
            {
                var bar = doc.Clock.Bar(editorCursor);
                long end = bar.End;
                for (int i = 1; i < editorBarCount && end < MidiEditorDocument.MaxTick; i++) end = doc.Clock.Bar(end).End;
                ApplyEditor(() => doc.RemoveTime(bar.Start, end));
            }
            float seconds = (float)editorSongSeconds;
            ImGui.SetNextItemWidth(105);
            if (ImGui.InputFloat("Duration (s)##editor", ref seconds)) editorSongSeconds = seconds;
            ImGui.SameLine(); if (ImGui.SmallButton("Set song duration##editor"))
            {
                if (double.IsFinite(editorSongSeconds))
                    ApplyEditor(() => doc.SetLength(doc.Clock.Ticks(Math.Clamp(editorSongSeconds, 0, 86_400) * 1000)));
                else editorStatus = "Enter a valid song duration in seconds.";
            }
        }

        if (ImGui.CollapsingHeader("Event editor##editor")) DrawEditorEventInspector();
    }

    private bool IsEditorSelected(EditorEvent e) => editorSelection.Contains(e.Id);

    private void ApplyEditorTuplet()
    {
        var doc = editor!;
        var onsets = doc.State.Events.Where(e => IsEditorSelected(e) && e.Kind == EditorEventKind.Note).Select(e => e.Tick).Distinct().ToArray();
        if (onsets.Length != editorTupletCount) { editorStatus = $"Select {editorTupletCount} distinct note starts for this tuplet."; return; }
        long anchor = doc.Clock.Snap(onsets[0], Math.Max(1, EditorSubdivision));
        double length = doc.Ppq * editorTupletBeats / (double)editorTupletCount;
        var points = onsets.Select((tick, i) => (tick, i)).ToDictionary(x => x.tick, x => x.i);
        ApplyEditor(() => doc.Transform(editorSelection, e =>
        {
            if (e.Kind != EditorEventKind.Note) return e;
            int i = points[e.Tick];
            long start = anchor + (long)Math.Round(i * length), end = anchor + (long)Math.Round((i + 1) * length);
            return e with { Tick = start, Length = (int)Math.Max(1, end - start) };
        }));
    }

    private static int EditorMicroseconds(float bpm) => (int)Math.Round(60_000_000 / Math.Clamp(bpm, 20, 400));
    private void SetEditorTempo(long tick, float bpm)
    {
        var doc = editor!;
        ApplyEditor(() => doc.Commit(doc.State.Events.Where(e => e.Kind != EditorEventKind.Tempo || e.Tick != tick)
            .Append(new(doc.NextId(), doc.State.Tracks[0].Id, tick, 0, EditorEventKind.Tempo, EditorMicroseconds(bpm)))));
    }

    private void AddEditorTempoRamp()
    {
        var doc = editor!;
        long end = editorCursor + doc.Ppq * (long)editorTempoRampBeats;
        var ramp = Enumerable.Range(0, editorTempoRampBeats * 4 + 1).Select(i =>
            new EditorEvent(doc.NextId(), doc.State.Tracks[0].Id, editorCursor + (long)Math.Round(i * doc.Ppq / 4.0), 0,
                EditorEventKind.Tempo, EditorMicroseconds(editorTempoStart + (editorTempoEnd - editorTempoStart) * i / (editorTempoRampBeats * 4f))));
        ApplyEditor(() => doc.Commit(doc.State.Events.Where(e => e.Kind != EditorEventKind.Tempo || e.Tick < editorCursor || e.Tick > end).Concat(ramp)));
    }

    private void DrawEditorEventInspector()
    {
        var doc = editor!;
        if (ImGui.InputText("Filter events##editor", ref editorEventSearch, 128)) editorEventPage = 0;
        var filtered = doc.State.Events.Where(e => EditorVisible(e) && (editorEventSearch.Length == 0 ||
            e.Kind.ToString().Contains(editorEventSearch, StringComparison.OrdinalIgnoreCase) ||
            e.Text.Contains(editorEventSearch, StringComparison.OrdinalIgnoreCase))).ToArray();
        const int pageSize = 150;
        int lastPage = Math.Max(0, (filtered.Length - 1) / pageSize);
        editorEventPage = Math.Clamp(editorEventPage, 0, lastPage);
        if (ImGui.SmallButton("Previous##editorEventPage")) editorEventPage = Math.Max(0, editorEventPage - 1);
        ImGui.SameLine(); if (ImGui.SmallButton("Next##editorEventPage")) editorEventPage = Math.Min(lastPage, editorEventPage + 1);
        ImGui.SameLine(); ImGui.TextUnformatted($"Page {editorEventPage + 1} of {lastPage + 1} · {filtered.Length:N0} events");
        if (ImGui.BeginChild("##editorEventList", new Vector2(0, 142), true))
        {
            foreach (var e in filtered.Skip(editorEventPage * pageSize).Take(pageSize))
            {
                string text = $"{e.Tick:N0} · Track {e.Track} · {e.Kind} · {(e.Text.Length > 0 ? e.Text : $"{e.Value} / {e.Secondary}")}";
                if (ImGui.Selectable(text + "##editorEvent" + e.Id, e.Id == editorSelectedEventId))
                {
                    editorSelectedEventId = e.Id; editorEventTicks = (int)e.Tick; editorEventLength = e.Length;
                    editorValue = e.Value; editorSecondary = e.Secondary; editorEventText = e.Text;
                    if (e.Kind != EditorEventKind.Other) editorNewKind = (int)e.Kind;
                    if (e.IsChannelEvent) editorChannel = e.Channel;
                    SetEditorTrack(doc.State.Tracks.First(t => t.Id == e.Track));
                    SetEditorCursor(e.Tick, true);
                    editorSelection.Clear(); editorSelection.Add(e.Id);
                }
            }
        }
        ImGui.EndChild();
        if (ImGui.SmallButton("Use cursor tick##editor")) editorEventTicks = (int)editorCursor;
        ImGui.SetNextItemWidth(125); ImGui.Combo("Event type##editor", ref editorNewKind, EditorKinds, EditorKinds.Length);
        ImGui.SetNextItemWidth(110); ImGui.InputInt("Tick##editorEvent", ref editorEventTicks);
        var kind = (EditorEventKind)editorNewKind;
        if (kind == EditorEventKind.Marker) ImGui.InputText("Marker text##editor", ref editorEventText, 4096);
        else
        {
            string primary = kind switch
            {
                EditorEventKind.Note or EditorEventKind.KeyPressure => "Pitch", EditorEventKind.Control => "Controller number",
                EditorEventKind.Tempo => "Microseconds per quarter", EditorEventKind.Meter => "Numerator", EditorEventKind.Program => "Program (0–127)",
                _ => "Value",
            };
            ImGui.SetNextItemWidth(115); ImGui.InputInt(primary + "##editorEvent", ref editorValue);
            if (kind is EditorEventKind.Note or EditorEventKind.Control or EditorEventKind.KeyPressure or EditorEventKind.Meter)
            {
                string secondary = kind == EditorEventKind.Meter ? "Denominator power (2 = 4, 3 = 8)" : kind == EditorEventKind.Note ? "Velocity" : "Value";
                ImGui.SetNextItemWidth(100); ImGui.InputInt(secondary + "##editorEvent", ref editorSecondary);
            }
            if (kind == EditorEventKind.Note) { ImGui.SetNextItemWidth(120); ImGui.InputInt("Length (ticks)##editorEvent", ref editorEventLength); }
        }
        var selected = doc.State.Events.FirstOrDefault(e => e.Id == editorSelectedEventId);
        EditorEvent Build(int id) => new(id, editorTrack, editorEventTicks,
            kind is EditorEventKind.Tempo or EditorEventKind.Meter or EditorEventKind.Marker ? 0 : editorChannel,
            kind, editorValue, editorSecondary, editorEventLength, editorEventText);
        if (ImGui.Button("Add event##editor")) ApplyEditor(() => doc.Add([Build(doc.NextId())]));
        ImGui.SameLine(); ImGui.BeginDisabled(selected is null || selected.Kind == EditorEventKind.Other);
        if (ImGui.Button("Update event##editor") && selected is not null) ApplyEditor(() => doc.Transform(new HashSet<int> { selected.Id },
            _ => Build(selected.Id) with { Original = selected.Kind == kind ? selected.Original : null }));
        ImGui.EndDisabled();
    }

    private void DrawEditorTracks()
    {
        var doc = editor!;
        if (!ImGui.CollapsingHeader("Tracks and channels##editor", ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.SetNextItemWidth(155);
        if (ImGui.BeginCombo("Active track##editor", doc.State.Tracks.First(t => t.Id == editorTrack).Name))
        {
            foreach (var track in doc.State.Tracks)
                if (ImGui.Selectable(track.Name + "##editorT" + track.Id, editorTrack == track.Id)) SetEditorTrack(track);
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.SetNextItemWidth(86); ImGui.SliderInt("Channel##editor", ref editorChannel, 1, 16);
        ImGui.SetNextItemWidth(170); ImGui.InputText("Track name##editor", ref editorTrackName, 240);
        if (ImGui.SmallButton("Rename##editor")) ApplyEditor(() => doc.RenameTrack(editorTrack, editorTrackName));
        ImGui.SameLine(); if (ImGui.SmallButton("Add empty track##editor")) ApplyEditor(() =>
        { int id = doc.AddTrack($"Track {doc.State.Tracks.Length + 1}"); SetEditorTrack(doc.State.Tracks.First(t => t.Id == id)); });
        ImGui.SameLine(); ImGui.BeginDisabled(doc.State.Tracks.Length < 2);
        if (ImGui.SmallButton("Delete active track##editor")) ApplyEditor(() => doc.RemoveTrack(editorTrack));
        ImGui.EndDisabled();
        if (ImGui.SmallButton("Delete active channel events##editor")) ApplyEditor(() =>
            doc.Delete(doc.State.Events.Where(e => e.Channel == editorChannel).Select(e => e.Id).ToHashSet()));
        ImGui.TextDisabled("Visibility controls the roll. Mute and Solo affect the next playback.");
        DrawEditorFilters("Track", doc.State.Tracks.Select(t => (t.Id, t.Name)), editorHiddenTracks, editorMutedTracks, ref editorSoloTrack);
        DrawEditorFilters("Channel", Enumerable.Range(1, 16).Select(i => (i, $"Channel {i}")), editorHiddenChannels, editorMutedChannels, ref editorSoloChannel);
    }

    private static void DrawEditorFilters(string name, IEnumerable<(int Id, string Label)> items, HashSet<int> hidden, HashSet<int> muted, ref int solo)
    {
        if (!ImGui.TreeNode(name + " visibility and playback##editor")) return;
        if (ImGui.SmallButton("Show all##" + name)) hidden.Clear();
        ImGui.SameLine(); if (ImGui.SmallButton("Hide all##" + name)) foreach (var item in items) hidden.Add(item.Id);
        if (ImGui.SmallButton("Unmute all##" + name)) { muted.Clear(); solo = 0; }
        ImGui.SameLine(); if (ImGui.SmallButton("Mute all##" + name)) { solo = 0; foreach (var item in items) muted.Add(item.Id); }
        if (ImGui.BeginChild("##editorFilter" + name, new Vector2(0, 145), true))
        {
            foreach (var (id, label) in items)
            {
                ImGui.PushID(id);
                bool visible = !hidden.Contains(id), mute = muted.Contains(id), isSolo = solo == id;
                if (ImGui.Checkbox("Visible", ref visible)) { if (visible) hidden.Remove(id); else hidden.Add(id); }
                ImGui.SameLine(); if (ImGui.Checkbox("Mute", ref mute)) { if (mute) muted.Add(id); else muted.Remove(id); }
                ImGui.SameLine(); if (ImGui.Checkbox("Solo", ref isSolo)) solo = isSolo ? id : 0;
                ImGui.SameLine(); ImGui.TextUnformatted(label);
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        ImGui.TreePop();
    }
}
