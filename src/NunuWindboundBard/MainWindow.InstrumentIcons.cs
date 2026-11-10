using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// Renders the in-game Bard Performance action icons beside instrument choices.
/// Uses ITextureProvider's shared icon textures: no file loading, copying or disposal.
/// Rendering fails quietly to a normal text-only selection if Dalamud/the icon
/// is temporarily unavailable (for example during a game update).
/// </summary>
public sealed partial class MainWindow
{
    private const float InstrumentIconPixels = 22f;

    private static bool TryInstrumentIcon(int instrumentId,
        out Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? wrap)
    {
        wrap = null;
        uint icon = InstrumentCatalog.GameIconId(instrumentId);
        if (icon == 0) return false;

        // TryGetFromGameIcon is non-throwing. TryGetWrap may return false while
        // an asynchronously loaded texture becomes available in a later frame.
        try
        {
            if (!Plugin.Textures.TryGetFromGameIcon(new GameIconLookup { IconId = icon }, out var texture) ||
                texture is null)
                return false;
            return texture.TryGetWrap(out wrap, out _) && wrap is not null;
        }
        catch
        {
            // The instrument selector must remain functional even without icons.
            wrap = null;
            return false;
        }
    }

    /// <summary>Draw a game icon before a normal ImGui widget (e.g. combo).</summary>
    private static bool DrawInstrumentPreviewIcon(int instrumentId)
    {
        if (!TryInstrumentIcon(instrumentId, out var wrap) || wrap is null) return false;
        ImGui.Image(wrap.Handle, new Vector2(InstrumentIconPixels, InstrumentIconPixels));
        return true;
    }

    /// <summary>
    /// A single click target that includes the icon AND name. Draw the icon
    /// over reserved text padding, not as a separate image with a dead click zone.
    /// </summary>
    private static bool InstrumentIconSelectable(int instrumentId, string displayName,
        string uniqueId, bool selected)
    {
        bool hasIcon = TryInstrumentIcon(instrumentId, out var wrap) && wrap is not null;
        string text = (hasIcon ? "      " : "") + Loc.T(displayName) + "##" + uniqueId;
        bool clicked = ImGui.Selectable(text, selected,
            ImGuiSelectableFlags.None, new Vector2(0f, 26f));
        if (hasIcon && wrap is not null)
        {
            var pos = ImGui.GetItemRectMin() + new Vector2(3f, 2f);
            ImGui.GetWindowDrawList().AddImage(wrap.Handle, pos,
                pos + new Vector2(InstrumentIconPixels, InstrumentIconPixels));
        }
        return clicked;
    }
}
