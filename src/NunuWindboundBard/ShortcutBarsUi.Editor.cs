using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

internal sealed partial class ShortcutBarsUi
{
    private string selectedCondition = "";
    private ShortcutBarSettings.Share? pendingImport;
    private string editorStatus = "";
    private bool showIcons;
    private int iconPage = 580;
    private string? customIconsDirectory;
    private string CustomIconsDirectory => customIconsDirectory ??=
        Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "ShortcutIcons");

    internal void DrawEditor()
    {
        if (ImGui.Button(allBarsVisible ? "Hide all bars" : "Show all bars")) ToggleVisibility();
        ImGui.SameLine();
        if (ImGui.Button("Stop shortcut sequence")) runtime.Cancel();
        ImGui.TextWrapped("Right-click a bar or button to edit it. Bind Ctrl, Shift or Alt with F1–F12 for a radial menu or a visibility toggle.");
        if (runtime.Status.Length > 0) ImGui.TextWrapped(runtime.Status);
        if (editorStatus.Length > 0) ImGui.TextWrapped(editorStatus);

        if (!ImGui.BeginTabBar("ShortcutBarSettings")) return;
        if (ImGui.BeginTabItem("Bar manager"))
        {
            DrawBarManager();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Condition sets"))
        {
            DrawConditions();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawBarManager()
    {
        ImGui.BeginDisabled(Settings.Bars.Count >= ShortcutBarSettings.MaxBars);
        if (ImGui.Button("New bar")) AddBar(new ShortcutBar());
        ImGui.SameLine();
        if (ImGui.Button("Add Windbound example")) AddBar(ShortcutBarSettings.CreateExample());
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Read clipboard"))
        {
            editorStatus = ShortcutBarSettings.TryImport(ImGui.GetClipboardText() ?? "", out pendingImport)
                ? "Review the import below before adding it." : "The clipboard does not contain a Windbound shortcut export.";
        }

        var selected = Settings.Bars.Find(bar => bar.Id == selectedBar) ?? Settings.Bars.FirstOrDefault();
        DrawImport(selected);
        if (selected is null) return;
        selectedBar = selected.Id;

        if (ImGui.BeginCombo("Bar", selected.Name))
        {
            foreach (var bar in Settings.Bars)
                if (ImGui.Selectable(bar.Name + "###" + bar.Id, bar.Id == selectedBar))
                {
                    selectedBar = bar.Id;
                    selectedItem = "";
                }
            ImGui.EndCombo();
        }

        if (ImGui.Button("Copy bar"))
            CopyExport(Settings.Export(selected));
        ImGui.SameLine();
        if (ImGui.Button("Remove bar…")) ImGui.OpenPopup("Remove shortcut bar?");
        if (ImGui.BeginPopupModal("Remove shortcut bar?", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted($"Remove {selected.Name} and its shortcuts?");
            bool removed = ImGui.Button("Remove");
            if (removed)
            {
                ForgetCycle(selected.Items);
                Settings.Bars.Remove(selected);
                plugin.Config.Save();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            if (removed) return;
        }

        if (ImGui.CollapsingHeader("Layout, hotkey and visibility", ImGuiTreeNodeFlags.DefaultOpen))
            DrawBarProperties(selected);

        ImGui.Separator();
        ImGui.TextUnformatted("Shortcuts");
        DrawItemList(selected.Items, 1);
        if (FindItem(selected.Items, selectedItem) is { } item)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Edit shortcut");
            DrawItemEditor(item);
        }
    }

    private void DrawBarProperties(ShortcutBar bar)
    {
        ImGui.PushID(bar.Id);
        var name = bar.Name;
        var enabled = bar.Enabled;
        var visible = bar.Visible;
        var locked = bar.Locked;
        var iconOnly = bar.IconOnly;
        var background = bar.Background;
        var dock = bar.Dock;
        float alignment = bar.Alignment, opacity = bar.Opacity;
        int columns = bar.Columns, size = bar.ButtonSize, key = bar.FunctionKey;
        bool control = bar.Modifiers.HasFlag(ShortcutModifiers.Ctrl);
        bool shift = bar.Modifiers.HasFlag(ShortcutModifiers.Shift);
        bool alt = bar.Modifiers.HasFlag(ShortcutModifiers.Alt);
        var keyAction = bar.HotkeyAction;

        bool changed = ImGui.InputText("Name", ref name, 80);
        changed |= ImGui.Checkbox("Enabled", ref enabled); ImGui.SameLine();
        changed |= ImGui.Checkbox("Visible", ref visible); ImGui.SameLine();
        changed |= ImGui.Checkbox("Lock toolbar", ref locked);
        changed |= ImGui.Checkbox("Icon only when available", ref iconOnly); ImGui.SameLine();
        changed |= ImGui.Checkbox("Bar background", ref background);
        changed |= EnumCombo("Dock", ref dock);
        if (dock != ShortcutDock.Floating)
            changed |= ImGui.SliderFloat("Dock alignment", ref alignment, 0f, 1f);
        else if (ImGui.Button("Reset floating position"))
        {
            bar.X = 40;
            bar.Y = 40;
            changed = true;
        }

        if (dock is not (ShortcutDock.Left or ShortcutDock.Right))
            changed |= ImGui.SliderInt("Columns", ref columns, 1, 12);
        changed |= ImGui.SliderInt("Button size", ref size, 22, 70);
        changed |= ImGui.SliderFloat("Opacity", ref opacity, 0.2f, 1f);
        if (ImGui.BeginCombo("Hotkey", key == 0 ? "None" : $"F{key}"))
        {
            for (int i = 0; i <= 12; i++)
                if (ImGui.Selectable(i == 0 ? "None" : $"F{i}", i == key))
                {
                    key = i;
                    changed = true;
                }
            ImGui.EndCombo();
        }

        if (key > 0)
        {
            changed |= ImGui.Checkbox("Ctrl", ref control); ImGui.SameLine();
            changed |= ImGui.Checkbox("Shift", ref shift); ImGui.SameLine();
            changed |= ImGui.Checkbox("Alt", ref alt);
            changed |= EnumCombo("Hotkey action", ref keyAction);
            if (keyAction == ShortcutHotkeyAction.Radial)
                ImGui.TextWrapped("Hold the hotkey and release over an action. While holding, click a category to enter it, the hub to go back, or scroll for more items. Escape cancels.");
        }

        string conditionName = Settings.Conditions.Find(set => set.Id == bar.ConditionId)?.Name ?? "Always";
        if (ImGui.BeginCombo("Show when", conditionName))
        {
            if (ImGui.Selectable("Always", bar.ConditionId.Length == 0))
            {
                bar.ConditionId = "";
                changed = true;
            }
            foreach (var set in Settings.Conditions)
                if (ImGui.Selectable(set.Name + "###" + set.Id, set.Id == bar.ConditionId))
                {
                    bar.ConditionId = set.Id;
                    changed = true;
                }
            ImGui.EndCombo();
        }

        if (changed)
        {
            bar.Name = ShortcutBarSettings.CleanName(name, "Shortcut bar");
            bar.Enabled = enabled;
            bar.Visible = visible;
            bar.Locked = locked;
            bar.IconOnly = iconOnly;
            bar.Background = background;
            bar.Dock = dock;
            bar.Alignment = alignment;
            bar.Opacity = opacity;
            bar.Columns = columns;
            bar.ButtonSize = size;
            bar.FunctionKey = key;
            bar.Modifiers = (control ? ShortcutModifiers.Ctrl : ShortcutModifiers.None) |
                (shift ? ShortcutModifiers.Shift : ShortcutModifiers.None) |
                (alt ? ShortcutModifiers.Alt : ShortcutModifiers.None);
            bar.HotkeyAction = keyAction;
            plugin.Config.Save();
        }

        if (key > 0 && Settings.Bars.Exists(other => other != bar && other.Enabled && other.FunctionKey == key && other.Modifiers == bar.Modifiers))
            ImGui.TextWrapped("Another bar uses this hotkey. The first eligible bar in the list receives it.");
        ImGui.PopID();
    }

    private void DrawItemList(List<ShortcutItem> items, int depth)
    {
        ImGui.BeginDisabled(items.Count >= ShortcutBarSettings.MaxItems);
        if (ImGui.SmallButton("Add shortcut")) AddItem(items, new ShortcutItem());
        ImGui.SameLine();
        if (ImGui.SmallButton("Add category"))
            AddItem(items, new ShortcutItem { Kind = ShortcutKind.Category, Name = "Category" });
        ImGui.SameLine();
        if (ImGui.SmallButton("Add spacer"))
            AddItem(items, new ShortcutItem { Kind = ShortcutKind.Spacer, Name = "Spacer" });
        ImGui.EndDisabled();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            ImGui.PushID(item.Id);
            bool category = item.Kind == ShortcutKind.Category && depth < ShortcutBarSettings.MaxDepth;
            bool expanded = category && ImGui.TreeNodeEx("###category", ImGuiTreeNodeFlags.OpenOnArrow);
            if (category) ImGui.SameLine();
            if (ImGui.SmallButton(VisibleLabel(item.Name, 28) + (item.Id == selectedItem ? " •" : "") + "###EditItem"))
                selectedItem = item.Id;
            ImGui.SameLine();
            ImGui.BeginDisabled(i == 0);
            bool up = ImGui.SmallButton("↑");
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(i + 1 == items.Count);
            bool down = ImGui.SmallButton("↓");
            ImGui.EndDisabled();
            ImGui.SameLine();
            bool remove = ImGui.SmallButton("Remove");
            if (expanded)
            {
                DrawItemList(item.Items, depth + 1);
                ImGui.TreePop();
            }
            ImGui.PopID();

            if (remove)
            {
                cyclePositions.Remove(item.Id);
                ForgetCycle(item.Items);
                items.RemoveAt(i);
                plugin.Config.Save();
                break;
            }
            if (up || down)
            {
                int other = i + (up ? -1 : 1);
                (items[i], items[other]) = (items[other], items[i]);
                plugin.Config.Save();
                break;
            }
        }
    }

    private void DrawItemEditor(ShortcutItem item)
    {
        ImGui.PushID(item.Id);
        var name = item.Name;
        var tip = item.Tooltip;
        var commands = item.Commands;
        var customIcon = item.CustomIcon;
        int icon = item.Icon;
        var mode = item.Mode;
        bool changed = ImGui.InputText("Label", ref name, 80);
        if (item.Kind == ShortcutKind.Spacer)
            ImGui.TextWrapped("Name it Spacer for an empty gap, or enter text for a label.");
        else
        {
            changed |= ImGui.InputText("Tooltip", ref tip, 512);
            changed |= EnumCombo("Mode", ref mode);
            changed |= ImGui.InputInt("Game icon ID", ref icon, 1, 100);
            if (ImGui.SmallButton(showIcons ? "Close icon browser" : "Browse game icons")) showIcons = !showIcons;
            if (showIcons && DrawIconBrowser(ref icon)) changed = true;
            changed |= ImGui.InputText("Custom PNG filename", ref customIcon, 128);
            if (customIcon.Length > 0 && ShortcutBarSettings.CleanIconName(customIcon).Length == 0)
                ImGui.TextDisabled("Use a PNG filename with no folder path.");
            if (ImGui.SmallButton("Show custom icon folder"))
            {
                try { Directory.CreateDirectory(CustomIconsDirectory); editorStatus = CustomIconsDirectory; }
                catch (IOException error) { editorStatus = error.Message; }
                catch (UnauthorizedAccessException error) { editorStatus = error.Message; }
            }

            if (item.Kind == ShortcutKind.Command)
            {
                changed |= ImGui.InputTextMultiline("##commands", ref commands, 4096, new Vector2(-1, 110));
                ImGui.TextWrapped("One slash command per line. Use /wait 1, // comments, or //m0–//m199 for saved FFXIV macros.");
            }
        }

        if (ImGui.SmallButton("Copy shortcut")) CopyExport(ShortcutBarSettings.Export(item));
        if (changed)
        {
            item.Name = ShortcutBarSettings.CleanName(name, item.Kind == ShortcutKind.Spacer ? "Spacer" : "Shortcut");
            item.Tooltip = tip;
            item.Commands = commands;
            item.CustomIcon = customIcon;
            item.Icon = Math.Clamp(icon, 0, 999999);
            if (item.Mode != mode || item.Commands != commands) cyclePositions.Remove(item.Id);
            item.Mode = mode;
            plugin.Config.Save();
        }
        ImGui.PopID();
    }

    private bool DrawIconBrowser(ref int selectedIcon)
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
            if (TryGameIcon(id, out var icon) && icon is not null)
            {
                Vector2 at = ImGui.GetItemRectMin() + new Vector2(2);
                ImGui.GetWindowDrawList().AddImage(icon.Handle, at, at + new Vector2(30));
            }
            else
            {
                var at = ImGui.GetItemRectMin() + new Vector2(9, 8);
                ImGui.GetWindowDrawList().AddText(at, 0xFFD6C4E3, "?");
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Game icon {id}");
        }
        return changed;
    }

    private void DrawConditions()
    {
        ImGui.BeginDisabled(Settings.Conditions.Count >= ShortcutBarSettings.MaxConditions);
        if (ImGui.Button("New condition set"))
        {
            var created = new ShortcutConditionSet();
            Settings.Conditions.Add(created);
            selectedCondition = created.Id;
            plugin.Config.Save();
        }
        ImGui.EndDisabled();

        var condition = Settings.Conditions.Find(set => set.Id == selectedCondition) ?? Settings.Conditions.FirstOrDefault();
        if (condition is null) return;
        selectedCondition = condition.Id;
        if (ImGui.BeginCombo("Condition set", condition.Name))
        {
            foreach (var set in Settings.Conditions)
                if (ImGui.Selectable(set.Name + "###" + set.Id, set.Id == selectedCondition))
                    selectedCondition = set.Id;
            ImGui.EndCombo();
        }

        var name = condition.Name;
        bool any = condition.MatchAny;
        bool changed = ImGui.InputText("Name", ref name, 80);
        changed |= ImGui.Checkbox("Any required state matches", ref any);
        foreach (var state in Enum.GetValues<ShortcutContext>())
        {
            if (state == ShortcutContext.None) continue;
            int rule = condition.Required.HasFlag(state) ? 1 : condition.Excluded.HasFlag(state) ? 2 : 0;
            string label = state switch
            {
                ShortcutContext.InCombat => "In combat",
                ShortcutContext.InDuty => "In duty",
                ShortcutContext.InParty => "In party",
                _ => state.ToString(),
            };
            if (!ImGui.BeginCombo(label, rule == 1 ? "Require" : rule == 2 ? "Exclude" : "Ignore")) continue;
            if (ImGui.Selectable("Ignore", rule == 0))
            {
                condition.Required &= ~state;
                condition.Excluded &= ~state;
                changed = true;
            }
            if (ImGui.Selectable("Require", rule == 1))
            {
                condition.Required |= state;
                condition.Excluded &= ~state;
                changed = true;
            }
            if (ImGui.Selectable("Exclude", rule == 2))
            {
                condition.Required &= ~state;
                condition.Excluded |= state;
                changed = true;
            }
            ImGui.EndCombo();
        }
        if (changed)
        {
            condition.Name = ShortcutBarSettings.CleanName(name, "Condition set");
            condition.MatchAny = any;
            plugin.Config.Save();
        }

        if (ImGui.Button("Remove condition set…")) ImGui.OpenPopup("Remove condition set?");
        if (ImGui.BeginPopupModal("Remove condition set?", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Bars using this set will be disabled until you re-enable them.");
            if (ImGui.Button("Remove"))
            {
                foreach (var bar in Settings.Bars.Where(bar => bar.ConditionId == condition.Id))
                {
                    bar.ConditionId = "";
                    bar.Enabled = false;
                }
                Settings.Conditions.Remove(condition);
                plugin.Config.Save();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void DrawImport(ShortcutBar? target)
    {
        if (pendingImport is not { } import) return;
        if (import.Bar is { } bar)
        {
            ImGui.TextUnformatted($"{bar.Name} · {bar.Items.Count} entries");
            ImGui.TextWrapped("Imported bars start disabled with no hotkey. Review their commands before enabling them.");
            ImGui.BeginDisabled(Settings.Bars.Count >= ShortcutBarSettings.MaxBars ||
                (import.Conditions is not null && Settings.Conditions.Count >= ShortcutBarSettings.MaxConditions));
            if (ImGui.Button("Import disabled bar"))
            {
                if (import.Conditions is not null) Settings.Conditions.Add(import.Conditions);
                AddBar(bar);
                pendingImport = null;
                editorStatus = "Bar imported.";
            }
            ImGui.EndDisabled();
        }
        else if (import.Item is { } item)
        {
            var category = target is not null ? FindItem(target.Items, selectedItem) : null;
            var items = category?.Kind == ShortcutKind.Category ? category.Items : target?.Items;
            string destination = category?.Kind == ShortcutKind.Category ? category.Name : target?.Name ?? "select a bar";
            ImGui.TextUnformatted($"{item.Name} → {destination}");
            ImGui.BeginDisabled(items is null || items.Count >= ShortcutBarSettings.MaxItems);
            if (ImGui.Button("Import shortcut") && items is not null)
            {
                AddItem(items, item);
                pendingImport = null;
                editorStatus = "Shortcut imported. Review its commands before using it.";
            }
            ImGui.EndDisabled();
        }
        ImGui.SameLine();
        if (ImGui.Button("Discard import")) pendingImport = null;
    }

    private void AddBar(ShortcutBar bar)
    {
        Settings.Bars.Add(bar);
        selectedBar = bar.Id;
        selectedItem = "";
        plugin.Config.Save();
    }

    private void AddItem(List<ShortcutItem> items, ShortcutItem item)
    {
        items.Add(item);
        selectedItem = item.Id;
        plugin.Config.Save();
    }

    private void ForgetCycle(List<ShortcutItem> items)
    {
        foreach (var item in items)
        {
            cyclePositions.Remove(item.Id);
            ForgetCycle(item.Items);
        }
    }

    private void CopyExport(string text)
    {
        if (text.Length > ShortcutBarSettings.MaxShareLength)
        {
            editorStatus = "This export is too large. Copy individual shortcuts instead.";
            return;
        }
        ImGui.SetClipboardText(text);
        editorStatus = "Copied Windbound export to the clipboard.";
    }

    private static ShortcutItem? FindItem(List<ShortcutItem> items, string id)
    {
        foreach (var item in items)
        {
            if (item.Id == id) return item;
            if (FindItem(item.Items, id) is { } found) return found;
        }
        return null;
    }

    private static bool EnumCombo<T>(string label, ref T value) where T : struct, Enum
    {
        bool changed = false;
        if (!ImGui.BeginCombo(label, value.ToString())) return false;
        foreach (var choice in Enum.GetValues<T>())
            if (ImGui.Selectable(choice.ToString(), choice.Equals(value)))
            {
                value = choice;
                changed = true;
            }
        ImGui.EndCombo();
        return changed;
    }
}
