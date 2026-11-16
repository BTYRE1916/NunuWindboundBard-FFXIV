using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

internal sealed partial class ShortcutBarsUi(Plugin plugin, Action openEditor)
{
    private const int RadialPageSize = 8;
    private ShortcutBarSettings Settings => plugin.Config.ShortcutBars;
    private readonly ShortcutBarRuntime runtime = new(plugin);
    private readonly Dictionary<string, int> cyclePositions = new(StringComparer.Ordinal);
    private bool allBarsVisible = true;
    private string selectedBar = "";
    private string selectedItem = "";
    private string dragBar = "";
    private RadialMenu? radial;

    private sealed class RadialMenu(ShortcutBar bar, Vector2 center, Vector2 pressPosition)
    {
        public ShortcutBar Bar { get; } = bar;
        public Vector2 Center { get; } = center;
        public Vector2 PressPosition { get; } = pressPosition;
        public List<ShortcutItem> Items { get; set; } = bar.Items;
        public int Page { get; set; }
        public bool WaitForCenter { get; set; }
        public Stack<List<ShortcutItem>> ParentLists { get; } = new();
    }

    internal void Update() => runtime.Update(Settings.Bars.Count > 0);
    internal void ReceivePartyMacro(PartyCueEnvelope envelope) => runtime.ReceivePartyMacro(envelope);
    internal void ToggleVisibility() => allBarsVisible = !allBarsVisible;

    internal void DrawOverlay()
    {
        if (Settings.Bars.Count == 0 || !Plugin.ClientState.IsLoggedIn ||
            !Plugin.PluginInterface.UiBuilder.ShouldModifyUi)
        {
            radial = null;
            SaveMovedBar();
            return;
        }

        var context = ShortcutBarRuntime.Context(plugin);
        UpdateRadial(context);
        if (!allBarsVisible)
        {
            SaveMovedBar();
            return;
        }
        foreach (var bar in Settings.Bars)
            if (bar.Visible && Settings.IsAllowed(bar, context))
                DrawBar(bar);
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) SaveMovedBar();
    }

    private void DrawBar(ShortcutBar bar)
    {
        var viewport = ImGui.GetMainViewport();
        Vector2 size = viewport.WorkSize;
        var position = viewport.WorkPos;
        var pivot = Vector2.Zero;
        switch (bar.Dock)
        {
            case ShortcutDock.Top:
                position += new Vector2(size.X * bar.Alignment, 8);
                pivot.X = bar.Alignment;
                break;
            case ShortcutDock.Bottom:
                position += new Vector2(size.X * bar.Alignment, size.Y - 8);
                pivot = new Vector2(bar.Alignment, 1);
                break;
            case ShortcutDock.Left:
                position += new Vector2(8, size.Y * bar.Alignment);
                pivot.Y = bar.Alignment;
                break;
            case ShortcutDock.Right:
                position += new Vector2(size.X - 8, size.Y * bar.Alignment);
                pivot = new Vector2(1, bar.Alignment);
                break;
            default:
                position += new Vector2(Math.Clamp(bar.X, 0, Math.Max(0, size.X - 36)),
                    Math.Clamp(bar.Y, 0, Math.Max(0, size.Y - 36)));
                break;
        }

        ImGui.SetNextWindowPos(position, ImGuiCond.Always, pivot);
        ImGui.SetNextWindowBgAlpha(bar.Background ? bar.Opacity : 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, bar.Opacity);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoFocusOnAppearing;
        try
        {
            if (ImGui.Begin(bar.Name + "###WindboundShortcutBar" + bar.Id, flags))
            {
                if (!bar.Locked)
                {
                    if (bar.Dock == ShortcutDock.Floating)
                    {
                        ImGui.SmallButton("Drag");
                        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                        {
                            Vector2 delta = ImGui.GetIO().MouseDelta;
                            bar.X = Math.Clamp(position.X - viewport.WorkPos.X + delta.X, 0, Math.Max(0, size.X - 36));
                            bar.Y = Math.Clamp(position.Y - viewport.WorkPos.Y + delta.Y, 0, Math.Max(0, size.Y - 36));
                            dragBar = bar.Id;
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(bar.Name);
                    }
                    else if (ImGui.SmallButton(VisibleLabel(bar.Name, 24) + "###EditBar"))
                        Edit(bar);
                }

                if (bar.Items.Count == 0)
                {
                    if (ImGui.SmallButton("Add shortcuts")) Edit(bar);
                }
                else
                {
                    int columns = bar.Dock is ShortcutDock.Left or ShortcutDock.Right ? 1 : bar.Columns;
                    for (int index = 0; index < bar.Items.Count; index++)
                    {
                        if (index % columns != 0) ImGui.SameLine();
                        DrawShortcut(bar, bar.Items[index]);
                    }
                }

                if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right) &&
                    !ImGui.IsAnyItemHovered())
                    Edit(bar);
            }
            ImGui.End();
        }
        finally { ImGui.PopStyleVar(); }
    }

    private void SaveMovedBar()
    {
        if (string.IsNullOrEmpty(dragBar)) return;
        dragBar = "";
        plugin.Config.Save();
    }

    private void Edit(ShortcutBar bar, ShortcutItem? item = null)
    {
        selectedBar = bar.Id;
        selectedItem = item?.Id ?? "";
        openEditor();
    }

    private void DrawShortcut(ShortcutBar bar, ShortcutItem item)
    {
        ImGui.PushID(item.Id);
        try
        {
            if (item.Kind == ShortcutKind.Spacer)
            {
                ImGui.Dummy(new Vector2(item.Name == "Spacer" ? bar.ButtonSize :
                    ImGui.CalcTextSize(item.Name).X + 8, bar.ButtonSize));
                if (item.Name != "Spacer")
                {
                    Vector2 place = ImGui.GetItemRectMin() + new Vector2(4, (bar.ButtonSize - ImGui.GetTextLineHeight()) / 2);
                    ImGui.GetWindowDrawList().AddText(place, 0xFFD5C9E2, item.Name);
                }
                return;
            }

            bool hasIcon = TryIcon(item, out var icon) && icon is not null;
            bool iconOnly = hasIcon && bar.IconOnly;
            Vector2 button = iconOnly ? new(bar.ButtonSize) : new(0, bar.ButtonSize);
            string label = (hasIcon ? new string(' ', Math.Max(3, (int)(bar.ButtonSize / 4))) : "") +
                VisibleLabel(item.Name, 26) + (item.Kind == ShortcutKind.Category ? "  ›" : "");
            bool clicked = ImGui.Button((iconOnly ? "" : label) + "###shortcut", button);
            if (icon is not null)
            {
                var at = ImGui.GetItemRectMin() + new Vector2(4, 4);
                ImGui.GetWindowDrawList().AddImage(icon.Handle, at, at + new Vector2(bar.ButtonSize - 8));
            }

            if (ImGui.IsItemHovered())
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                    Edit(bar, item);
                else if (!string.IsNullOrEmpty(item.Tooltip) || iconOnly)
                    ImGui.SetTooltip(string.IsNullOrEmpty(item.Tooltip) ? item.Name : item.Tooltip);
            }

            if (clicked && runtime.CanActivate)
            {
                if (item.Kind == ShortcutKind.Category && item.Mode == ShortcutMode.All)
                    ImGui.OpenPopup("category");
                else
                    Activate(item);
            }

            if (ImGui.BeginPopup("category"))
            {
                if (item.Items.Count == 0 && ImGui.Button("Add shortcuts")) Edit(bar, item);
                foreach (var child in item.Items) DrawShortcut(bar, child);
                ImGui.EndPopup();
            }
        }
        finally { ImGui.PopID(); }
    }

    private void Activate(ShortcutItem item)
    {
        if (item.Kind == ShortcutKind.Spacer) return;
        if (item.Kind == ShortcutKind.Command)
        {
            runtime.Request(ShortcutCommands.Parse(item.Commands, item.Mode, NextIndex(item)));
            return;
        }

        if (item.Mode == ShortcutMode.All) return;
        var choices = item.Items.Where(IsActionable).ToArray();
        if (choices.Length > 0)
            Activate(choices[ShortcutCommands.Select(item.Mode, NextIndex(item), choices.Length)]);
    }

    private static bool IsActionable(ShortcutItem item) => item.Kind == ShortcutKind.Command ||
        (item.Kind == ShortcutKind.Category && item.Mode != ShortcutMode.All && item.Items.Any(IsActionable));

    private int NextIndex(ShortcutItem item)
    {
        if (item.Mode != ShortcutMode.Cycle) return 0;
        int index = cyclePositions.GetValueOrDefault(item.Id);
        cyclePositions[item.Id] = index == int.MaxValue ? 0 : index + 1;
        return index;
    }

    private void UpdateRadial(ShortcutContext context)
    {
        if (radial is null && !Settings.Bars.Exists(bar => bar.FunctionKey != 0)) return;
        if (ImGui.GetIO().WantTextInput || !runtime.CanActivate)
        {
            radial = null;
            return;
        }

        if (radial is { } active)
        {
            if (!Settings.Bars.Contains(active.Bar) || !Settings.IsAllowed(active.Bar, context) ||
                active.Bar.FunctionKey == 0 || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                radial = null;
                return;
            }
            DrawRadial(active);
            return;
        }

        var io = ImGui.GetIO();
        var modifiers = (io.KeyCtrl ? ShortcutModifiers.Ctrl : ShortcutModifiers.None) |
            (io.KeyShift ? ShortcutModifiers.Shift : ShortcutModifiers.None) |
            (io.KeyAlt ? ShortcutModifiers.Alt : ShortcutModifiers.None);

        foreach (var bar in Settings.Bars)
        {
            if (bar.FunctionKey == 0 || !Settings.IsAllowed(bar, context) ||
                bar.Modifiers != modifiers || io.KeySuper || !ImGui.IsKeyPressed(KeyFor(bar), false))
                continue;
            if (bar.HotkeyAction == ShortcutHotkeyAction.Toggle)
            {
                bar.Visible = !bar.Visible;
                plugin.Config.Save();
            }
            else
            {
                var viewport = ImGui.GetMainViewport();
                const float margin = 152;
                var viewportMargin = Vector2.Min(new Vector2(margin), viewport.WorkSize * 0.5f);
                Vector2 center = Vector2.Clamp(ImGui.GetMousePos(), viewport.WorkPos + viewportMargin,
                    viewport.WorkPos + viewport.WorkSize - viewportMargin);
                radial = new RadialMenu(bar, center, ImGui.GetMousePos());
                DrawRadial(radial);
            }
            break;
        }
    }

    private const float RadialDeadZone = 30;
    private const float RadialOuterRadius = 145;

    private void DrawRadial(RadialMenu menu)
    {
        var entries = menu.Items.Where(item => item.Kind != ShortcutKind.Spacer).ToArray();
        int pageCount = Math.Max(1, (entries.Length + RadialPageSize - 1) / RadialPageSize);
        float scroll = ImGui.GetIO().MouseWheel;
        if (scroll != 0)
        {
            menu.Page = Math.Clamp(menu.Page - Math.Sign(scroll), 0, pageCount - 1);
            menu.WaitForCenter = true;
        }

        int offset = menu.Page * RadialPageSize;
        int count = Math.Min(RadialPageSize, entries.Length - offset);
        Vector2 delta = ImGui.GetMousePos() - menu.Center;
        float distance = delta.Length();
        if (distance < RadialDeadZone) menu.WaitForCenter = false;
        int index = RadialIndex(delta, count);
        bool moved = Vector2.DistanceSquared(ImGui.GetMousePos(), menu.PressPosition) >= 100;
        var hovered = moved && !menu.WaitForCenter && index >= 0 ? entries[offset + index] : null;
        bool released = !ImGui.IsKeyDown(KeyFor(menu.Bar));

        if (released)
        {
            radial = null;
            if (hovered is not null) Activate(hovered);
            return;
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (hovered is { Kind: ShortcutKind.Category, Mode: ShortcutMode.All })
            {
                menu.ParentLists.Push(menu.Items);
                menu.Items = hovered.Items;
                menu.Page = 0;
                menu.WaitForCenter = true;
                return;
            }
            if (distance < RadialDeadZone && menu.ParentLists.TryPop(out var parent))
            {
                menu.Items = parent;
                menu.Page = 0;
                return;
            }
        }

        var draw = ImGui.GetForegroundDrawList();
        draw.AddCircleFilled(menu.Center, RadialOuterRadius, 0xF2231829, 64);
        draw.AddCircle(menu.Center, RadialOuterRadius, 0xFF8E6FA2, 64, 1.5f);
        draw.AddCircle(menu.Center, RadialDeadZone, 0xFF675173);
        if (pageCount > 1)
            DrawCentered(draw, menu.Center, $"{menu.Page + 1}/{pageCount}", 0xFFE6CEE9);
        else if (menu.ParentLists.Count > 0)
            DrawCentered(draw, menu.Center, "Back", 0xFFE6CEE9);
        else if (count == 0)
            DrawCentered(draw, menu.Center, "Empty", 0xFFE6CEE9);

        for (int i = 0; i < count; i++)
        {
            var item = entries[offset + i];
            float theta = MathF.Tau * i / count - MathF.PI / 2;
            var at = menu.Center + new Vector2(MathF.Cos(theta), MathF.Sin(theta)) * 99;
            bool active = ReferenceEquals(hovered, item);
            draw.AddCircleFilled(at, 28, active ? 0xFF74518A : 0xFF382644);
            string name = VisibleLabel(item.Name, 10) + (item.Kind == ShortcutKind.Category ? " ›" : "");
            if (TryIcon(item, out var icon) && icon is not null)
            {
                draw.AddImage(icon.Handle, at - new Vector2(13, 18), at + new Vector2(13, 8));
                at.Y += 17;
            }
            DrawCentered(draw, at, name, 0xFFF9EAFB);
        }
    }

    private static int RadialIndex(Vector2 point, int count)
    {
        float distanceSquared = point.LengthSquared();
        if (count == 0 || distanceSquared < RadialDeadZone * RadialDeadZone ||
            distanceSquared > RadialOuterRadius * RadialOuterRadius) return -1;
        float angle = MathF.Atan2(point.Y, point.X) + MathF.PI / 2;
        if (angle < 0) angle += MathF.Tau;
        return ((int)MathF.Floor(angle / MathF.Tau * count + 0.5f)) % count;
    }

    private static void DrawCentered(ImDrawListPtr draw, Vector2 center, string text, uint color) =>
        draw.AddText(center - ImGui.CalcTextSize(text) * 0.5f, color, text);

    private static ImGuiKey KeyFor(ShortcutBar bar) => (ImGuiKey)((int)ImGuiKey.F1 + bar.FunctionKey - 1);
    private static string VisibleLabel(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private bool TryIcon(ShortcutItem item, out IDalamudTextureWrap? wrap)
    {
        wrap = null;
        try
        {
            if (item.CustomIcon.Length > 0)
            {
                string name = ShortcutBarSettings.CleanIconName(item.CustomIcon);
                if (name.Length == 0) return false;
                return Plugin.Textures.GetFromFileAbsolute(Path.Combine(CustomIconsDirectory, name))
                    .TryGetWrap(out wrap, out _) && wrap is not null;
            }

            return TryGameIcon(item.Icon, out wrap);
        }
        catch { return false; }
    }

    private static bool TryGameIcon(int id, out IDalamudTextureWrap? wrap)
    {
        wrap = null;
        return id > 0 && Plugin.Textures.TryGetFromGameIcon(new GameIconLookup { IconId = (uint)id }, out var texture) &&
            texture is not null && texture.TryGetWrap(out wrap, out _) && wrap is not null;
    }
}
