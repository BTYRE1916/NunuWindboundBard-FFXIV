using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

internal sealed partial class ShortcutBarsUi
{
    private int iconPage = 580;
    private string iconQuery = "";
    private Task<NamedGameIcon[]>? namedIconCatalog;
    private NamedGameIcon[]? namedIconMatches;
    private int firstIconMatch;

    private bool DrawIconBrowser(ref int selectedIcon, bool focusSearch)
    {
        ImGui.SetNextItemWidth(Math.Max(140, ImGui.GetContentRegionAvail().X - 58));
        if (focusSearch) ImGui.SetKeyboardFocusHere();
        bool queryChanged = ImGui.InputTextWithHint("##iconNameSearch", "Search icon names (e.g. Cheer)", ref iconQuery, 128);
        if (iconQuery.Length != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
            {
                iconQuery = "";
                queryChanged = true;
            }
        }
        if (queryChanged)
        {
            namedIconMatches = null;
            firstIconMatch = 0;
        }
        if (!string.IsNullOrWhiteSpace(iconQuery))
            return DrawNamedIcons(ref selectedIcon);
        return DrawIconIdGrid(ref selectedIcon);
    }

    private bool DrawNamedIcons(ref int selectedIcon)
    {
        if (namedIconCatalog is null)
        {
            var data = Plugin.Data;
            namedIconCatalog = Task.Run(() => NamedGameIcons.Read(data));
        }
        if (!namedIconCatalog.IsCompleted)
        {
            ImGui.TextDisabled("Loading game icon names…");
            return false;
        }
        if (!namedIconCatalog.IsCompletedSuccessfully || namedIconCatalog.Result.Length == 0)
        {
            ImGui.TextDisabled("Game icon names are unavailable.");
            if (ImGui.SmallButton("Retry icon search")) namedIconCatalog = null;
            return false;
        }

        var matches = namedIconMatches ??= NamedGameIcons.Search(namedIconCatalog.Result, iconQuery);
        if (matches.Length == 0)
        {
            ImGui.TextDisabled("No named icons match.");
            return false;
        }

        const int pageSize = 8;
        if (matches.Length > pageSize)
        {
            ImGui.BeginDisabled(firstIconMatch == 0);
            if (ImGui.SmallButton("Previous matches")) firstIconMatch -= pageSize;
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(firstIconMatch + pageSize >= matches.Length);
            if (ImGui.SmallButton("Next matches")) firstIconMatch += pageSize;
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled($"{firstIconMatch + 1}–{Math.Min(firstIconMatch + pageSize, matches.Length)} of {matches.Length}");
        }

        bool changed = false;
        for (int i = firstIconMatch; i < Math.Min(firstIconMatch + pageSize, matches.Length); i++)
        {
            var entry = matches[i];
            string command = entry.Command.Length > 0 ? $" ({entry.Command})" : "";
            string label = $"       {VisibleLabel(entry.Name, 42)}{command} · {entry.Category}###NamedIcon{i}";
            if (ImGui.Selectable(label, selectedIcon == entry.Id, ImGuiSelectableFlags.None, new Vector2(0, 26)))
            {
                selectedIcon = entry.Id;
                changed = true;
            }
            DrawIconPreview(entry.Id, 22);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{entry.Name}{command} · {entry.Category}\nGame icon {entry.Id}");
        }
        return changed;
    }

    private bool DrawIconIdGrid(ref int selectedIcon)
    {
        const int pageSize = 32;
        int first = iconPage;
        if (ImGui.SmallButton("Previous icons")) first -= pageSize;
        ImGui.SameLine();
        if (ImGui.SmallButton("Next icons")) first += pageSize;
        ImGui.SetNextItemWidth(130);
        ImGui.InputInt("First icon ID", ref first);
        iconPage = Math.Clamp(first, 1, 999999 - pageSize);
        bool changed = false;
        int columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / 43), 3, 12);
        for (int i = 0; i < pageSize; i++)
        {
            int id = iconPage + i;
            if (i % columns != 0) ImGui.SameLine();
            if (ImGui.Button("###Icon" + id, new Vector2(34, 34)))
            {
                selectedIcon = id;
                changed = true;
            }
            DrawIconPreview(id, 30);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Game icon {id}");
        }
        return changed;
    }

    private static void DrawIconPreview(int id, int size)
    {
        Vector2 at = ImGui.GetItemRectMin() + new Vector2(2);
        if (TryGameIcon(id, out var icon) && icon is not null)
            ImGui.GetWindowDrawList().AddImage(icon.Handle, at, at + new Vector2(size));
        else
            ImGui.GetWindowDrawList().AddText(at + new Vector2(7, 5), 0xFFD6C4E3, "?");
    }
}
