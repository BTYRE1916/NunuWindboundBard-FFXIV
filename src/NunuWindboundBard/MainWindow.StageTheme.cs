using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// v0.39 purely presentational workspaces, concert header and display preferences.
/// Every existing module renderer is still called once from its original controls.
/// No musical action is dispatched by changing a workspace or appearance setting.
/// </summary>
public sealed partial class MainWindow
{
    private void DrawWindboundChrome()
    {
        if (cfg.DarkStageThemeEnabled) DrawStageHero();
        else
        {
            ImGui.TextColored(WindboundStageStyle.Rose, Loc.T("NUNU'S WINDBOUND BARD   /windbound"));
            ImGui.TextDisabled(Loc.T("FFXIV Performance Suite  |  Classic display"));
        }

        bool hasRoom = ImGui.GetContentRegionAvail().X > 510f;
        if (ImGui.SmallButton(Loc.T(plugin.IsHudVisible ? "Hide Performance HUD##windbound" : "Show Performance HUD##windbound")))
            plugin.SetHudVisible(!plugin.IsHudVisible);
        ImGui.SameLine();
        if (ImGui.SmallButton(cfg.DarkStageThemeEnabled ? "Plain colours##windboundAppearance" : "Dark Stage UI##windboundAppearance"))
        {
            cfg.DarkStageThemeEnabled = !cfg.DarkStageThemeEnabled;
            cfg.Save();
        }
        if (hasRoom) ImGui.SameLine();
        else ImGui.Spacing();
        ImGui.TextUnformatted(Loc.T("Language:"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(128);
        string selectedLanguage = cfg.Language switch
        {
            "fr" => "Français", "de" => "Deutsch", _ => "English"
        };
        if (ImGui.BeginCombo("##windboundLocale", selectedLanguage))
        {
            if (ImGui.Selectable("English##lang-en", cfg.Language == "en")) SetLanguage("en");
            if (ImGui.Selectable("Français##lang-fr", cfg.Language == "fr")) SetLanguage("fr");
            if (ImGui.Selectable("Deutsch##lang-de", cfg.Language == "de")) SetLanguage("de");
            ImGui.EndCombo();
        }

        if (cfg.DarkStageThemeEnabled) DrawStageStatusRail();
        else
        {
            ImGui.TextWrapped(Loc.T(status));
            ImGui.TextWrapped(Loc.T(plugin.Player.Status));
            ImGui.TextColored(plugin.IsGameOutputSafe() ? WindboundStageStyle.Good : WindboundStageStyle.Warning,
                Loc.T(plugin.IsGameOutputSafe() ? "Performance: FULL 37-KEY READY" :
                    plugin.IsPerforming ? "Open expanded 37-note Performance keyboard for direct output" :
                        "Performance mode not detected (preview still works)"));
        }
        if (!string.IsNullOrWhiteSpace(plugin.Player.GuitarWarning))
            ImGui.TextColored(WindboundStageStyle.Warning, Loc.T(plugin.Player.GuitarWarning));
        ImGui.Separator();
    }

    private void DrawStageHero()
    {
        float width = Math.Max(250f, ImGui.GetContentRegionAvail().X);
        const float height = 94f;
        Vector2 top = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));
        var draw = ImGui.GetWindowDrawList();
        Vector4 accent = WindboundStageStyle.Accent(cfg.StageUiAccent);
        draw.AddRectFilled(top, top + new Vector2(width, height), 0xFF1D1424, 10f);
        // Layered, restrained neon lighting rather than rapidly flashing decoration.
        for (int i = 0; i < 24; i++)
        {
            float start = i / 24f;
            Vector4 shade = Vector4.Lerp(new Vector4(.12f, .08f, .18f, .98f),
                new Vector4(.08f + accent.X * .16f, .08f + accent.Y * .12f,
                    .15f + accent.Z * .18f, .98f), start);
            float x = top.X + start * (width - 2f);
            float right = top.X + (i + 1) / 24f * (width - 2f);
            draw.AddRectFilled(new Vector2(x, top.Y + 3),
                new Vector2(right + 1, top.Y + height - 3), ImGui.ColorConvertFloat4ToU32(shade));
        }
        draw.AddRectFilled(top + new Vector2(13, 15), top + new Vector2(17, 69),
            ImGui.ColorConvertFloat4ToU32(accent), 2f);
        draw.AddText(top + new Vector2(29, 14), 0xFFF9E8F9, "NUNU'S WINDBOUND BARD");
        draw.AddText(top + new Vector2(29, 37), 0xFFCBB6E0, "THE SOUL WEEPER'S PERFORMANCE SUITE");
        draw.AddText(top + new Vector2(29, 64), 0xFFB8ADD0,
            "SONG  " + StageUiTruncate(string.IsNullOrEmpty(loaded) ? "No MIDI loaded" : Path.GetFileName(loaded),
                width < 540 ? 28 : 57));
        if (width > 570)
            draw.AddText(top + new Vector2(width - 108, 14), ImGui.ColorConvertFloat4ToU32(accent), plugin.Player.IsPlaying ? "v0.39  LIVE" : "v0.39  READY");
        float shimmer = cfg.StageUiMotionEnabled
            ? .65f + .27f * (float)(.5d + .5d * Math.Sin(Environment.TickCount64 / 650d))
            : .8f;
        draw.AddRectFilled(top + new Vector2(0, height - 3), top + new Vector2(width * shimmer, height),
            ImGui.ColorConvertFloat4ToU32(accent));
    }

    private static string StageUiTruncate(string value, int length) =>
        value.Length <= length ? value : value[..Math.Max(1, length - 3)] + "...";

    private void DrawStageStatusRail()
    {
        Vector4 accent = WindboundStageStyle.Accent(cfg.StageUiAccent);
        string role = ((EnsembleRole)cfg.EnsembleMode).ToString().ToUpperInvariant();
        WindboundStageStyle.Chip("ROLE  " + role, accent);
        ImGui.SameLine();
        WindboundStageStyle.Chip(plugin.Player.IsPlaying ? "ON AIR" : "STANDBY",
            plugin.Player.IsPlaying ? WindboundStageStyle.Rose : WindboundStageStyle.Cyan,
            plugin.Player.Status);
        if (ImGui.GetContentRegionAvail().X > 140) ImGui.SameLine();
        WindboundStageStyle.Chip(plugin.IsGameOutputSafe() ? "37-KEY READY" : "OUTPUT NOT READY",
            plugin.IsGameOutputSafe() ? WindboundStageStyle.Good : WindboundStageStyle.Warning,
            "This reports game keyboard readiness; it does not arm MIDI output.");
        if (ImGui.GetContentRegionAvail().X > 145) ImGui.SameLine();
        WindboundStageStyle.Chip(outputArmed ? "MIDI ARMED" : "MIDI SAFE",
            outputArmed ? WindboundStageStyle.Warning : WindboundStageStyle.Good,
            "Direct-game output ARM is controlled only in the player and HUD.");
        int statusCharacters = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / 7.1f), 30, 110);
        ImGui.TextDisabled(Loc.T(StageUiTruncate(status, statusCharacters)));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T(status));
        if (ImGui.CollapsingHeader(Loc.T("Detailed session and playback status##StageThemeStatus")))
        {
            ImGui.TextWrapped(Loc.T(status));
            ImGui.TextWrapped(Loc.T(plugin.Player.Status));
            ImGui.TextWrapped(Loc.T(plugin.Instruments.Status));
        }
    }

    private void DrawStageWorkspaceTitle(string title, string hint)
    {
        if (!cfg.DarkStageThemeEnabled) return;
        float width = Math.Max(230f, ImGui.GetContentRegionAvail().X);
        Vector2 at = ImGui.GetCursorScreenPos();
        const float height = 51;
        ImGui.Dummy(new Vector2(width, height));
        var draw = ImGui.GetWindowDrawList();
        Vector4 accent = WindboundStageStyle.Accent(cfg.StageUiAccent);
        draw.AddRectFilled(at, at + new Vector2(width, height), 0xFF271B33, 6f);
        draw.AddRectFilled(at, at + new Vector2(4f, height), ImGui.ColorConvertFloat4ToU32(accent), 2f);
        draw.AddText(at + new Vector2(14, 8), 0xFFFFF0FF, title);
        draw.AddText(at + new Vector2(14, 28), 0xFFCCB7D9, StageUiTruncate(hint, width < 530 ? 50 : 86));
        ImGui.Spacing();
    }

    /// <summary>6 navigational workspaces instead of 16 cramped top-level tabs.
    /// The same legacy renderers/controls are invoked, preserving stored IDs and state.</summary>
    private void DrawWindboundWorkspaces()
    {
        if (!ImGui.BeginTabBar("WindboundStageWorkspaces")) return;
        try
        {
            if (ImGui.BeginTabItem(Loc.T("PERFORMANCE")))
            {
                DrawStageWorkspaceTitle("PERFORMANCE DECK", "MIDI playback, instruments and live FFXIV performance");
                if (ImGui.BeginTabBar("WindboundPerformanceTabs"))
                {
                    if (ImGui.BeginTabItem(Loc.T("Songs & Playback"))) { DrawPlayer(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(Loc.T("Instruments"))) { DrawInstruments(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(Loc.T("Live MIDI Piano"))) { DrawLiveMidiInput(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(Loc.T("Guitar Tones"))) { DrawGuitarTones(); ImGui.EndTabItem(); }
                    ImGui.EndTabBar();
                }
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(Loc.T("BAND")))
            {
                DrawStageWorkspaceTitle("BAND COMMAND", "Conductor assignments, stage positions and member portraits");
                if (ImGui.BeginTabBar("WindboundBandTabs"))
                {
                    // Portraits deliberately come first; available in Solo too.
                    if (ImGui.BeginTabItem(Loc.T("Band Portraits"))) { DrawBandPortraitsTab(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(Loc.T("Ensemble"))) { DrawEnsemble(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(Loc.T("Stage Formations"))) { DrawStageFormations(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(Loc.T("Concert Snapshots"))) { DrawConcertSnapshots(); ImGui.EndTabItem(); }
                    ImGui.EndTabBar();
                }
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(Loc.T("STUDIO")))
            {
                DrawStageWorkspaceTitle("CREATIVE STUDIO", "Learn songs, inspect MIDI, compose and preview arrangements");
                if (ImGui.BeginTabBar("WindboundStudioTabs"))
                {
                    if (ImGui.BeginTabItem(Loc.T("Piano Academy"))) { DrawPianoAcademy(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(Loc.T("Soulweaver Studio"))) { DrawSoulweaver(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(Loc.T("Audio Preview"))) { DrawAudioPreview(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(Loc.T("MIDI Inspector"))) { DrawArrangementInspector(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(Loc.T("FFXIV Optimiser"))) { DrawOptimizer(); ImGui.EndTabItem(); }
                    ImGui.EndTabBar();
                }
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(Loc.T("SHOW")))
            {
                DrawStageWorkspaceTitle("SHOWMASTER", "Concert bows, encore routines and opted-in choreography");
                DrawShowmaster();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(Loc.T("TOOLS")))
            {
                DrawStageWorkspaceTitle("TECHNICAL DECK", "Output mapping, calibration and direct performance diagnostics");
                if (ImGui.BeginTabBar("WindboundToolsTabs"))
                {
                    if (ImGui.BeginTabItem(Loc.T("Direct Output & Test"))) { DrawKeyMapping(); ImGui.EndTabItem(); }
                    ImGui.EndTabBar();
                }
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(Loc.T("SETUP")))
            {
                DrawStageWorkspaceTitle("CUSTOMISE YOUR STAGE", "Colours, accessibility, help and installation notes");
                if (ImGui.BeginTabBar("WindboundSetupTabs"))
                {
                    if (ImGui.BeginTabItem(Loc.T("Appearance"))) { DrawStageAppearanceSettings(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(Loc.T("About / Setup"))) { DrawHelp(); ImGui.EndTabItem(); }
                    ImGui.EndTabBar();
                }
                ImGui.EndTabItem();
            }
        }
        finally { ImGui.EndTabBar(); }
    }

    private void DrawStageAppearanceSettings()
    {
        ImGui.TextColored(WindboundStageStyle.Accent(cfg.StageUiAccent), "DARK STAGE — VISUAL SETTINGS");
        ImGui.TextWrapped("Change only the plugin's appearance. MIDI, party commands, keymaps, emotes, " +
            "instrument switches, playback safety and saved concert data are unaffected.");
        bool active = cfg.DarkStageThemeEnabled;
        if (ImGui.Checkbox("Enable Dark Stage styling##windboundTheme", ref active))
        { cfg.DarkStageThemeEnabled = active; cfg.Save(); }
        int accent = Math.Clamp(cfg.StageUiAccent, 0, 2);
        string current = accent switch { 1 => "Void Violet", 2 => "Aether Cyan", _ => "Soul Rose" };
        ImGui.SetNextItemWidth(265);
        if (ImGui.BeginCombo("Accent colour##windboundTheme", current))
        {
            if (ImGui.Selectable("Soul Rose##windboundTheme0", accent == 0)) { cfg.StageUiAccent = 0; cfg.Save(); }
            if (ImGui.Selectable("Void Violet##windboundTheme1", accent == 1)) { cfg.StageUiAccent = 1; cfg.Save(); }
            if (ImGui.Selectable("Aether Cyan##windboundTheme2", accent == 2)) { cfg.StageUiAccent = 2; cfg.Save(); }
            ImGui.EndCombo();
        }
        bool motion = cfg.StageUiMotionEnabled;
        if (ImGui.Checkbox("Subtle animated concert highlight##windboundThemeMotion", ref motion))
        { cfg.StageUiMotionEnabled = motion; cfg.Save(); }
        ImGui.TextDisabled("Disable movement for a steady header. This option never affects MIDI timing.");
        ImGui.Separator();
        ImGui.TextUnformatted("Preview");
        WindboundStageStyle.Chip("STANDBY", WindboundStageStyle.Cyan);
        ImGui.SameLine();
        WindboundStageStyle.Chip("READY", WindboundStageStyle.Good);
        ImGui.SameLine();
        WindboundStageStyle.Chip("ON AIR", WindboundStageStyle.Rose);
        ImGui.TextDisabled("The concert header shows your song, role, output readiness and safe/armed state.");
    }
}
