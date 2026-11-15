using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private static readonly string[] EditorGridLabels = ["Off", "Quarter", "Eighth", "Sixteenth", "Thirty-second"];
    private static readonly string[] EditorRollTools = ["Edit", "Move time only", "Move pitch only", "Erase"];
    private static readonly uint[] EditorColors = [0xFFCE90E6, 0xFF8ACDD9, 0xFF98D394, 0xFFE0B482, 0xFFCD85AA, 0xFFE5D68F, 0xFFB3A4F3, 0xFF90B8E9];
    private readonly List<(long Tick, int Value)> editorCurveStroke = new();
    private int editorGrid = 2, editorRollTool, editorLowestPitch = 36, editorPitchRows = 49, editorQuarters = 16;
    private bool editorByChannel, editorRollDragging;
    private enum EditorDragKind { Box, Move, TrimStart, TrimEnd }
    private EditorDragKind editorDragKind;
    private Vector2 editorDragOrigin;
    private EditorEvent[] editorDragEvents = [];
    private EditorEvent? editorDragAnchor;
    private int editorCurveMode, editorCurveChoice, editorCurveParameter = 1;
    private long EditorSpan => Math.Max(1, (editor?.Ppq ?? 480) * (long)editorQuarters);
    private int EditorSubdivision => editorGrid == 0 ? 0 : 1 << (editorGrid - 1);
    private bool EditorVisible(EditorEvent e) => !editorHiddenTracks.Contains(e.Track) && !editorHiddenChannels.Contains(e.Channel);

    private void DrawEditorRoll()
    {
        var doc = editor!;
        ImGui.SetNextItemWidth(118); ImGui.Combo("Tool##editorRoll", ref editorRollTool, EditorRollTools, EditorRollTools.Length);
        ImGui.SameLine(); ImGui.SetNextItemWidth(125); ImGui.Combo("Snap##editorGrid", ref editorGrid, EditorGridLabels, EditorGridLabels.Length);
        ImGui.SameLine(); ImGui.Checkbox("Color by channel##editor", ref editorByChannel);
        ImGui.SetNextItemWidth(135); ImGui.SliderInt("Quarter notes shown##editor", ref editorQuarters, 2, 128);
        ImGui.SameLine(); ImGui.SetNextItemWidth(110); ImGui.SliderInt("Pitches shown##editor", ref editorPitchRows, 12, 128);
        editorLowestPitch = Math.Clamp(editorLowestPitch, 0, 128 - editorPitchRows);
        ImGui.SetNextItemWidth(130); ImGui.SliderInt("Lowest pitch##editor", ref editorLowestPitch, 0, 128 - editorPitchRows);
        ImGui.SameLine(); if (ImGui.SmallButton("Reset view##editor"))
        { editorLowestPitch = 36; editorPitchRows = 49; editorQuarters = 16; editorViewStart = Math.Max(0, editorCursor - doc.Ppq); }
        int view = (int)editorViewStart;
        ImGui.SetNextItemWidth(170);
        if (ImGui.SliderInt("View start (ticks)##editor", ref view, 0, (int)Math.Max(doc.State.EndTick, editorViewStart))) editorViewStart = view;
        ImGui.SameLine(); ImGui.TextDisabled($"{editorSelection.Count:N0} selected · Cursor {editorCursor:N0} ticks");

        const float keyboard = 43, ruler = 23, height = 240;
        Vector2 origin = ImGui.GetCursorScreenPos();
        float width = Math.Max(160, ImGui.GetContentRegionAvail().X), plot = width - keyboard, row = height / editorPitchRows;
        int highest = editorLowestPitch + editorPitchRows - 1;
        Vector2 plotMin = origin + new Vector2(keyboard, ruler), plotMax = origin + new Vector2(width, ruler + height);
        double pixelsPerTick = plot / (double)EditorSpan;
        long viewEnd = editorViewStart + EditorSpan;
        float X(long tick) => plotMin.X + (float)((tick - editorViewStart) * pixelsPerTick);
        float Y(int pitch) => plotMin.Y + (highest - pitch) * row;
        long TickAt(float x) => Math.Clamp(editorViewStart + (long)Math.Round((x - plotMin.X) / pixelsPerTick), 0, MidiEditorDocument.MaxTick);
        int PitchAt(float y) => Math.Clamp(highest - (int)Math.Floor((y - plotMin.Y) / row), 0, 127);
        bool editable = !EditorBusy && !EditorPlaying;
        var mouse = ImGui.GetIO().MousePos;
        var draw = ImGui.GetWindowDrawList();
        ImGui.InvisibleButton("##editorRoll", new Vector2(width, ruler + height));
        bool hovered = ImGui.IsItemHovered();
        bool inPlot = hovered && mouse.X >= plotMin.X && mouse.Y >= plotMin.Y;
        draw.AddRectFilled(origin, plotMax, 0xFF191321, 4);
        draw.AddRectFilled(plotMin, plotMax, 0xFF211727);
        for (int pitch = editorLowestPitch; pitch <= highest; pitch++)
        {
            float y = Y(pitch);
            bool black = pitch % 12 is 1 or 3 or 6 or 8 or 10;
            draw.AddRectFilled(new(origin.X, y), new(plotMin.X - 1, y + row - .5f), black ? 0xFF393044u : 0xFFCDBDCEu);
            if (pitch % 12 == 0 && row >= 4)
                draw.AddText(new(origin.X + 3, y - 1), 0xFF35283F, NoteMapper.GetNoteName(pitch));
            if (pitch % 12 == 0) draw.AddLine(new(plotMin.X, y), new(plotMax.X, y), 0x505D4565);
        }
        draw.PushClipRect(origin, plotMax, true);
        DrawEditorGrid(draw, origin.Y, plotMax.Y, X, doc, viewEnd);
        EditorEvent? hit = null;
        bool nearStart = false, nearEnd = false;
        int rendered = 0;
        foreach (var e in doc.State.Events)
        {
            if (e.Tick > viewEnd) break;
            if (e.Kind != EditorEventKind.Note || !EditorVisible(e) || e.End <= editorViewStart || e.Value < editorLowestPitch || e.Value > highest) continue;
            if (++rendered > 20_000) break;
            float x1 = Math.Max(plotMin.X, X(e.Tick)), x2 = Math.Min(plotMax.X, Math.Max(x1 + 3, X(e.End)));
            float y = Y(e.Value), bottom = y + Math.Max(2, row - 1);
            bool selected = editorSelection.Contains(e.Id);
            int color = (editorByChannel ? e.Channel : e.Track) % EditorColors.Length;
            draw.AddRectFilled(new(x1, y), new(x2, bottom), selected ? 0xFF8CE0FF : EditorColors[color], 2);
            if (inPlot && mouse.X >= x1 && mouse.X <= x2 && mouse.Y >= y && mouse.Y <= bottom)
            {
                hit = e;
                float grab = Math.Min(6, (x2 - x1) / 4);
                nearStart = e.Tick >= editorViewStart && mouse.X - x1 < grab;
                nearEnd = e.End <= viewEnd && x2 - mouse.X < grab;
            }
        }
        long playhead = EditorPlaybackCursor();
        draw.AddLine(new(X(playhead), origin.Y), new(X(playhead), plotMax.Y), 0xFFF0D7AA, 2);

        if (editable && hovered && mouse.X >= plotMin.X && mouse.Y < plotMin.Y && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            SetEditorCursor(TickAt(mouse.X), false);
        if (editable && inPlot && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            long tick = doc.Clock.Snap(TickAt(mouse.X), EditorSubdivision);
            if (tick < MidiEditorDocument.MaxTick)
            {
                int length = (int)Math.Min(MidiEditorDocument.MaxTick - tick, Math.Max(1, doc.Ppq / Math.Max(1, EditorSubdivision)));
                var note = new EditorEvent(doc.NextId(), editorTrack, tick, editorChannel, EditorEventKind.Note, PitchAt(mouse.Y), 90, length);
                ApplyEditor(() => doc.Add([note]), false);
                editorSelection.Add(note.Id);
                SetEditorCursor(tick, false);
            }
        }

        if (editable && inPlot && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            bool shift = ImGui.GetIO().KeyShift, ctrl = ImGui.GetIO().KeyCtrl;
            if (editorRollTool == 3)
            {
                if (hit is not null) ApplyEditor(() => doc.Delete(new HashSet<int> { hit.Id }));
            }
            else if (hit is null)
            {
                if (!shift && !ctrl) editorSelection.Clear();
                editorDragKind = EditorDragKind.Box;
                editorDragOrigin = mouse;
                editorRollDragging = true;
                SetEditorCursor(TickAt(mouse.X), false);
            }
            else
            {
                if (ctrl) { editorSelection.Remove(hit.Id); }
                else
                {
                    if (!shift && !editorSelection.Contains(hit.Id)) editorSelection.Clear();
                    editorSelection.Add(hit.Id);
                    editorDragEvents = doc.State.Events.Where(e => e.Kind == EditorEventKind.Note && editorSelection.Contains(e.Id) && EditorVisible(e)).ToArray();
                    editorDragAnchor = hit;
                    editorDragOrigin = mouse;
                    editorDragKind = editorRollTool == 0 && nearStart ? EditorDragKind.TrimStart :
                        editorRollTool == 0 && nearEnd ? EditorDragKind.TrimEnd : EditorDragKind.Move;
                    editorRollDragging = true;
                }
            }
        }

        if (editorRollDragging && !editable) editorRollDragging = false;
        if (editorRollDragging)
        {
            var min = Vector2.Max(plotMin, Vector2.Min(editorDragOrigin, mouse));
            var max = Vector2.Min(plotMax, Vector2.Max(editorDragOrigin, mouse));
            bool moved = Vector2.DistanceSquared(editorDragOrigin, mouse) >= 9;
            if (editorDragKind == EditorDragKind.Box)
            {
                if (moved)
                {
                    draw.AddRectFilled(min, max, 0x3040CFFF);
                    draw.AddRect(min, max, 0xFF82D6ED);
                }
                if (moved && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
                    foreach (var e in doc.State.Events.Where(e => e.Kind == EditorEventKind.Note && EditorVisible(e)))
                        if (e.Tick <= TickAt(max.X) && e.End >= TickAt(min.X) && Y(e.Value) <= max.Y && Y(e.Value) + row >= min.Y)
                        {
                            if (ImGui.GetIO().KeyCtrl) editorSelection.Remove(e.Id); else editorSelection.Add(e.Id);
                        }
            }
            else if (moved && editorDragAnchor is not null && editorDragEvents.Length > 0)
            {
                var replacements = TransformEditorDrag(doc, mouse, pixelsPerTick, row);
                foreach (var note in replacements.Take(20_000))
                    draw.AddRect(new(X(note.Tick), Y(note.Value)), new(Math.Max(X(note.Tick) + 3, X(note.End)), Y(note.Value) + Math.Max(2, row - 1)), 0xFFF5E1FF, 2);
                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    var byId = replacements.ToDictionary(e => e.Id);
                    if (!replacements.SequenceEqual(editorDragEvents)) ApplyEditor(() => doc.Transform(byId.Keys.ToHashSet(), e => byId[e.Id]));
                }
            }
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) { editorRollDragging = false; editorDragEvents = []; editorDragAnchor = null; }
        }
        else if (hit is not null)
            ImGui.SetTooltip($"{NoteMapper.GetNoteName(hit.Value)} · Channel {hit.Channel} · Velocity {hit.Secondary} · {hit.Tick:N0} + {hit.Length:N0} ticks");
        draw.PopClipRect();
        if (editable && hovered && !editorRollDragging && !ImGui.GetIO().WantTextInput)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Delete) && editorSelection.Count > 0) ApplyEditor(() => doc.Delete(editorSelection), false);
            if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.A)) SelectEditorEvents(EditorVisible);
            if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.C)) CopyEditorEvents();
            if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.V)) PasteEditorEvents();
        }
        ImGui.TextDisabled("Click the ruler to seek. Drag notes or their edges. Drag empty space to select. Right-click to add a note.");
        DrawEditorCurve(width, keyboard, X, TickAt, viewEnd, editable);
    }

    private EditorEvent[] TransformEditorDrag(MidiEditorDocument doc, Vector2 mouse, double pixelsPerTick, float row)
    {
        var anchor = editorDragAnchor!;
        long raw = editorRollTool == 2 ? 0 : (long)Math.Round((mouse.X - editorDragOrigin.X) / pixelsPerTick);
        long reference = editorDragKind == EditorDragKind.TrimEnd ? anchor.End : anchor.Tick;
        long delta = raw == 0 ? 0 : doc.Clock.Snap(reference + raw, EditorSubdivision) - reference;
        int pitch = editorRollTool == 1 ? 0 : (int)Math.Round((editorDragOrigin.Y - mouse.Y) / row);
        if (editorDragKind == EditorDragKind.Move)
        {
            delta = Math.Clamp(delta, -editorDragEvents.Min(e => e.Tick), MidiEditorDocument.MaxTick - editorDragEvents.Max(e => e.End));
            pitch = Math.Clamp(pitch, -editorDragEvents.Min(e => e.Value), 127 - editorDragEvents.Max(e => e.Value));
            return editorDragEvents.Select(e => e with { Tick = e.Tick + delta, Value = e.Value + pitch }).ToArray();
        }
        if (editorDragKind == EditorDragKind.TrimStart)
        {
            delta = Math.Clamp(delta, -editorDragEvents.Min(e => e.Tick), editorDragEvents.Min(e => e.Length) - 1L);
            return editorDragEvents.Select(e => e with { Tick = e.Tick + delta, Length = (int)(e.Length - delta) }).ToArray();
        }
        delta = Math.Clamp(delta, 1L - editorDragEvents.Min(e => e.Length), MidiEditorDocument.MaxTick - editorDragEvents.Max(e => e.End));
        return editorDragEvents.Select(e => e with { Length = (int)(e.Length + delta) }).ToArray();
    }

    private void DrawEditorGrid(ImDrawListPtr draw, float top, float bottom, Func<long, float> x, MidiEditorDocument doc, long end)
    {
        double step = (double)doc.Ppq / Math.Max(1, EditorSubdivision);
        int rendered = 0;
        for (long barStart = editorViewStart; barStart <= end && rendered < 512;)
        {
            var bar = doc.Clock.Bar(barStart);
            if (bar.Start >= editorViewStart)
            {
                draw.AddLine(new(x(bar.Start), top), new(x(bar.Start), bottom), 0x986B567B);
                draw.AddText(new(x(bar.Start) + 3, top + 3), 0xFFCDB5D4, $"{doc.Clock.Milliseconds(bar.Start) / 1000:0.0}s");
                rendered++;
            }
            if (editorGrid != 0)
                for (long i = Math.Max(1, (long)Math.Ceiling((editorViewStart - bar.Start) / step)); rendered < 512; i++)
                {
                    long tick = bar.Start + (long)Math.Round(i * step);
                    if (tick >= bar.End || tick > end) break;
                    draw.AddLine(new(x(tick), top + 20), new(x(tick), bottom), 0x3A876B8E);
                    rendered++;
                }
            if (bar.End <= barStart) break;
            barStart = bar.End;
        }
        foreach (var mark in doc.State.Events.Where(e => e.Tick >= editorViewStart && e.Tick <= end && e.Kind == EditorEventKind.Marker).Take(40))
        {
            draw.AddLine(new(x(mark.Tick), top), new(x(mark.Tick), bottom), 0x9AD495BE);
            draw.AddText(new(x(mark.Tick) + 3, top + 5), 0xFFF3A2D1, mark.Text);
        }
    }

    private void DrawEditorCurve(float width, float keyboard, Func<long, float> x, Func<float, long> tickAt, long viewEnd, bool editable)
    {
        ImGui.SetNextItemWidth(132);
        ImGui.Combo("Lane##editor", ref editorCurveChoice, "Velocity\0Control change\0Pitch bend\0Key pressure\0Channel pressure\0");
        ImGui.SameLine(); ImGui.SetNextItemWidth(102);
        ImGui.Combo("Draw##editorCurve", ref editorCurveMode, "Single\0Freehand\0Line\0");
        EditorEventKind kind = (EditorEventKind)editorCurveChoice;
        if (kind is EditorEventKind.Control or EditorEventKind.KeyPressure)
        {
            ImGui.SetNextItemWidth(80); ImGui.InputInt(kind == EditorEventKind.Control ? "Controller##editorCurve" : "Pitch##editorCurve", ref editorCurveParameter);
            editorCurveParameter = Math.Clamp(editorCurveParameter, 0, 127);
        }
        var doc = editor!;
        const float height = 76;
        Vector2 top = ImGui.GetCursorScreenPos();
        var left = top + new Vector2(keyboard, 0);
        var bottom = top + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        int maximum = kind == EditorEventKind.PitchBend ? 16383 : 127;
        float Y(int value) => bottom.Y - Math.Clamp(value / (float)maximum, 0, 1) * (height - 3);
        bool Matches(EditorEvent e) => e.Kind == kind && e.Track == editorTrack && e.Channel == editorChannel &&
            (kind is not (EditorEventKind.Control or EditorEventKind.KeyPressure) || e.Value == editorCurveParameter);
        int Value(EditorEvent e) => kind is EditorEventKind.Note or EditorEventKind.Control or EditorEventKind.KeyPressure ? e.Secondary : e.Value;
        var lane = doc.State.Events.Where(e => e.Tick >= editorViewStart && e.Tick <= viewEnd && Matches(e)).Take(20_000).ToArray();
        ImGui.InvisibleButton("##editorCurveCanvas", new Vector2(width, height));
        bool hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(top, bottom, 0xFF201728, 4);
        draw.AddText(new(top.X + 5, top.Y + 3), 0xFFE5C4E2, maximum.ToString());
        draw.AddText(new(top.X + 5, bottom.Y - 15), 0xFFC8A7CC, "0");
        foreach (var e in lane) draw.AddLine(new(x(e.Tick), bottom.Y - 1), new(x(e.Tick), Y(Value(e))), editorSelection.Contains(e.Id) ? 0xFF8CE0FF : 0xFFDA8FC3, 3);
        var mouse = ImGui.GetIO().MousePos;
        if (!editable) editorCurveStroke.Clear();
        if (editable && hovered && mouse.X >= left.X && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) editorCurveStroke.Clear();
        bool drawing = editable && (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) || editorCurveStroke.Count > 0);
        if (!drawing) return;
        int sample = Math.Clamp((int)Math.Round((bottom.Y - mouse.Y) / (height - 3) * maximum), 0, maximum);
        long tick = doc.Clock.Snap(tickAt(Math.Clamp(mouse.X, left.X, bottom.X)), EditorSubdivision);
        if (editorCurveMode is 0 or 2 && editorCurveStroke.Count >= 2) editorCurveStroke.RemoveAt(1);
        if (editorCurveStroke.Count == 0 || editorCurveStroke[^1] != (tick, sample))
        {
            if (editorCurveStroke.Count < 2048) editorCurveStroke.Add((tick, sample));
        }
        draw.PushClipRect(left, bottom, true);
        foreach (var point in editorCurveStroke) draw.AddCircleFilled(new(x(point.Tick), Y(point.Value)), 3, 0xFFBDE8F8);
        if (editorCurveStroke.Count >= 2 && editorCurveMode == 2)
            draw.AddLine(new(x(editorCurveStroke[0].Tick), Y(editorCurveStroke[0].Value)), new(x(tick), Y(sample)), 0xFFBDE8F8, 2);
        draw.PopClipRect();
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) return;
        var points = editorCurveStroke.ToArray(); editorCurveStroke.Clear();
        if (points.Length == 0) return;
        if (kind == EditorEventKind.Note)
        {
            var changes = new Dictionary<int, int>();
            if (editorCurveMode == 0 || points.Length == 1)
            {
                var nearest = lane.MinBy(e => Math.Abs(e.Tick - tick));
                if (nearest is not null) changes[nearest.Id] = Math.Max(1, sample);
            }
            else
            {
                for (int i = 1; i < points.Length; i++)
                {
                    var a = points[i - 1]; var b = points[i];
                    foreach (var note in lane.Where(e => e.Tick >= Math.Min(a.Tick, b.Tick) && e.Tick <= Math.Max(a.Tick, b.Tick)))
                    {
                        double fraction = a.Tick == b.Tick ? 1 : (note.Tick - a.Tick) / (double)(b.Tick - a.Tick);
                        changes[note.Id] = Math.Clamp((int)Math.Round(a.Value + (b.Value - a.Value) * fraction), 1, 127);
                    }
                }
            }
            if (changes.Count > 0) ApplyEditor(() => doc.Transform(changes.Keys.ToHashSet(), e => e with { Secondary = changes[e.Id] }));
        }
        else
        {
            if (editorCurveMode == 0) points = [points[^1]];
            else if (editorCurveMode == 2 && points.Length > 1)
            {
                var a = points[0]; var b = points[^1];
                int count = Math.Clamp((int)(Math.Abs(b.Tick - a.Tick) / Math.Max(1, doc.Ppq / 8)) + 1, 2, 128);
                points = Enumerable.Range(0, count).Select(i => (a.Tick + (b.Tick - a.Tick) * i / (count - 1), a.Value + (b.Value - a.Value) * i / (count - 1))).ToArray();
            }
            var final = points.GroupBy(p => p.Tick).ToDictionary(g => g.Key, g => g.Last().Value);
            ApplyEditor(() => doc.Commit(doc.State.Events.Where(e => !(Matches(e) && final.ContainsKey(e.Tick))).Concat(final.Select(pair => new EditorEvent(doc.NextId(), editorTrack, pair.Key, editorChannel, kind,
                kind is EditorEventKind.Control or EditorEventKind.KeyPressure ? editorCurveParameter : pair.Value, pair.Value)))));
        }
    }
}
