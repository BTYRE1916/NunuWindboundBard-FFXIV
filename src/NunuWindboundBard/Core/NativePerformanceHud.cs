using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace NunuWindboundBard.Core;

internal sealed unsafe class NativePerformanceHud
{
    private static readonly string[] AddonNames =
    [
        "PerformanceMode",
        "PerformanceModeWide",
        "PerformanceToneChange",
        "PerformanceMetronome",
    ];

    private readonly Dictionary<string, (nint Address, byte Alpha)> originalOpacity = new();
    private readonly IAddonLifecycle lifecycle;

    public NativePerformanceHud(IAddonLifecycle lifecycle)
    {
        this.lifecycle = lifecycle;
        lifecycle.RegisterListener(AddonEvent.PreFinalize, AddonNames, OnAddonFinalizing);
    }

    // All native access and saved opacity bookkeeping run on the framework thread.
    public void Update(bool hide)
    {
        if (!hide && originalOpacity.Count == 0)
            return;

        var stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null)
            return;

        foreach (string name in AddonNames)
        {
            var addon = stage->RaptureAtkUnitManager->GetAddonByName(name);
            if (addon == null)
                continue;

            if (hide)
            {
                if (addon->IsVisible && addon->RootNode != null && addon->Alpha != 0)
                {
                    // The game can reapply opacity when an instrument or layout changes.
                    originalOpacity[name] = ((nint)addon, addon->Alpha);
                    addon->SetAlpha(0);
                }
            }
            else if (originalOpacity.Remove(name, out var saved) &&
                     saved.Address == (nint)addon && addon->RootNode != null && addon->Alpha == 0)
            {
                addon->SetAlpha(saved.Alpha);
            }
        }
    }

    private void OnAddonFinalizing(AddonEvent _, AddonArgs args)
    {
        // Widget memory may be reused when the player changes instruments.
        string name = args.AddonName;
        if (originalOpacity.TryGetValue(name, out var saved) && saved.Address == args.Addon.Address)
            originalOpacity.Remove(name);
    }

    public void Unregister() => lifecycle.UnregisterListener(OnAddonFinalizing);
}
