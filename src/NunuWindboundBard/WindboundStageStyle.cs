using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace NunuWindboundBard;

/// <summary>
/// Presentation-only, opt-in ImGui theme. Every Push is paired with Pop in try/finally;
/// never changes the process-wide ImGui style, FFXIV UI, MIDI callback, or game state.
/// </summary>
internal static class WindboundStageStyle
{
    public static readonly Vector4 Rose = new(.99f, .40f, .72f, 1f);
    public static readonly Vector4 Violet = new(.70f, .50f, .98f, 1f);
    public static readonly Vector4 Cyan = new(.39f, .83f, .98f, 1f);
    public static readonly Vector4 Good = new(.45f, .94f, .69f, 1f);
    public static readonly Vector4 Warning = new(1f, .76f, .38f, 1f);
    public static readonly Vector4 Danger = new(1f, .36f, .46f, 1f);
    public static readonly Vector4 Muted = new(.69f, .72f, .81f, 1f);

    // Also changes the highlight used by the concert status panels.
    public static Vector4 Accent(int preference) => preference switch
    {
        1 => Violet,
        2 => Cyan,
        _ => Rose
    };

    private const int ColorCount = 19;
    private const int VarCount = 4;
    public static void Push(int accentPreference)
    {
        Vector4 accent = Accent(accentPreference);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.08f, .09f, .16f, .95f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(.09f, .09f, .16f, .99f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(.32f, .27f, .45f, .65f));
        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(.40f, .31f, .55f, .68f));
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, Muted);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(.17f, .16f, .27f, .98f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(.25f, .22f, .38f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(.31f, .25f, .43f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.25f, .20f, .36f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.37f, .27f, .49f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.46f, .29f, .56f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(.24f, .20f, .37f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(.35f, .24f, .47f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(.42f, .27f, .51f, 1f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Vector4.Lerp(accent, new Vector4(1, 1, 1, 1), .30f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(.40f, .34f, .51f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, accent);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 5f));
    }

    public static void Pop()
    {
        ImGui.PopStyleVar(VarCount);
        ImGui.PopStyleColor(ColorCount);
    }

    /// <summary>A momentary accent/danger button without changing what the button does.</summary>
    public static void PushAction(Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(color.X * .46f, color.Y * .46f, color.Z * .46f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(color.X * .70f, color.Y * .70f, color.Z * .70f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, color);
    }
    public static void PopAction() => ImGui.PopStyleColor(3);

    /// <summary>Compact status chip; Dummy reserves a real ImGui rectangle.</summary>
    public static void Chip(string text, Vector4 color, string tooltip = "")
    {
        Vector2 textSize = ImGui.CalcTextSize(text);
        Vector2 dimensions = textSize + new Vector2(19, 11);
        Vector2 pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(dimensions);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + dimensions,
            ImGui.ColorConvertFloat4ToU32(new Vector4(color.X * .17f, color.Y * .17f, color.Z * .17f, .98f)), 7f);
        draw.AddRectFilled(pos, pos + new Vector2(3f, dimensions.Y),
            ImGui.ColorConvertFloat4ToU32(color), 1f);
        draw.AddText(pos + new Vector2(11, 5), ImGui.ColorConvertFloat4ToU32(color), text);
        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
    }
}
