using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// An independent per-client Dalamud HUD designed for one-to-eight account
/// performances. It only calls the existing MainWindow orchestration methods.
/// Native MIDI output stays entirely in PlaybackEngine.Update on the game thread.
/// </summary>
public sealed class PerformanceHudWindow : Window
{
    private static readonly Vector4 Rose = new(1.0f, .51f, .80f, 1f);
    private static readonly Vector4 Good = new(.45f, .95f, .65f, 1f);
    private static readonly Vector4 Warning = new(1f, .76f, .36f, 1f);
    private readonly Plugin plugin;
    private readonly MainWindow main;
    private readonly Configuration cfg;

    public PerformanceHudWindow(Plugin plugin, MainWindow main)
        : base(Loc.T("Windbound Bard | Performance HUD") + "###NunuWindboundHud",
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoFocusOnAppearing,
            forceMainWindow: true)
    {
        this.plugin = plugin;
        this.main = main;
        cfg = plugin.Config;
        RespectCloseHotkey = false; // closing other windows with Esc must not hide the stage monitor
        ShowCloseButton = false;     // our Hide button persists the preference
        AllowClickthrough = false;   // HUD must remain interactive for PLAY/STOP
        AllowPinning = false;        // use our explicit Lock position toggle
        DisableWindowSounds = true;
        DisableFadeInFadeOut = true;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(310, 0),
            MaximumSize = new Vector2(525, 600)
        };
        Position = new Vector2(40, 90);
        PositionCondition = ImGuiCond.FirstUseEver; // ImGui saves movable position per client
    }

    public override void PreDraw()
    {
        BgAlpha = Math.Clamp(cfg.HudOpacity, .45f, 1f);
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.NoFocusOnAppearing |
                (cfg.HudLockPosition ? ImGuiWindowFlags.NoMove : ImGuiWindowFlags.None);
    }

    public override void OnClose()
    {
        if (cfg.HudVisible)
        {
            cfg.HudVisible = false;
            cfg.Save();
        }
    }

    public override void OnOpen()
    {
        if (!cfg.HudVisible)
        {
            cfg.HudVisible = true;
            cfg.Save();
        }
    }

    public override void Draw()
    {
        bool styled = cfg.DarkStageThemeEnabled;
        if (styled) WindboundStageStyle.Push(cfg.StageUiAccent);
        try { DrawStageHudContents(); }
        finally { if (styled) WindboundStageStyle.Pop(); }
    }

    private void DrawStageHudContents()
    {
        if (cfg.DarkStageThemeEnabled)
        {
            Vector2 pos = ImGui.GetCursorScreenPos();
            Vector2 size = new Vector2(Math.Max(200f, ImGui.GetContentRegionAvail().X), 4f);
            ImGui.Dummy(size);
            ImGui.GetWindowDrawList().AddRectFilled(pos, pos + size,
                ImGui.ColorConvertFloat4ToU32(WindboundStageStyle.Accent(cfg.StageUiAccent)), 2f);
            ImGui.Spacing();
        }
        string playerName = Plugin.Objects.LocalPlayer?.Name.TextValue ?? "Bard";
        ImGui.TextColored(Rose, Loc.T("WINDBOUND"));
        ImGui.SameLine();
        TextWithTooltip(Truncate(playerName, 23), playerName);
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T(main.HudRole));

        string song = main.HudSongName;
        TextWithTooltip(Truncate(song, cfg.HudCompact ? 39 : 54), song);

        double duration = Math.Max(1, main.HudDurationMs);
        double position = Math.Clamp(plugin.Player.PositionMs, 0, duration);
        float fraction = (float)(main.HudHasMidi ? position / duration : 0);
        ImGui.ProgressBar(fraction, new Vector2(-1, 12), Loc.T($"{fraction * 100:0}%"));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Right-click for Reset MIDI to 00:00 (local account only)."));
        if (ImGui.BeginPopupContextItem("##WindboundHudProgressPopup"))
        {
            if (ImGui.Selectable(Loc.T("Reset MIDI to start (00:00)")) && main.HudHasMidi)
                main.HudResetToStart();
            ImGui.TextDisabled(Loc.T("In ensemble mode, conductor must cue everybody again."));
            ImGui.EndPopup();
        }

        ImGui.TextDisabled(Loc.T($"{FormatTime(position)} / {FormatTime(main.HudDurationMs)}"));
        ImGui.SameLine();
        string playback = main.HudPlaybackStatus;
        ImGui.TextColored(GetStatusColor(playback), Loc.T(Truncate(playback, 31)));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T(playback));

        if (!cfg.HudCompact)
        {
            ImGui.TextDisabled(Loc.T($"Playlist: {Truncate(main.HudPlaylistName, 27)}  " +
                $"#{main.HudPlaylistIndex + 1}/{main.HudPlaylistCount}"));
            ImGui.TextDisabled(Loc.T($"Instrument: {Truncate(main.HudInstrumentName, 22)}" +
                (main.HudIsSolo ? "" : $"  |  {main.HudTrackCount} part(s)")));
            if (main.HudExpectedFollowers > 0)
                ImGui.TextColored(Good,
                    Loc.T($"Conductor READY: {main.HudReadyFollowers}/{main.HudExpectedFollowers} follower(s)"));
            if (cfg.HudShowNextSong)
                TextWithTooltip("Next: " + Truncate(main.HudNextSongName, 45), main.HudNextSongName);
            TextWithTooltip(Truncate(main.HudDetailStatus, 60), main.HudDetailStatus);
            ImGui.Separator();
        }

        if (cfg.HudCompact && main.HudExpectedFollowers > 0)
            ImGui.TextDisabled(Loc.T($"Ensemble READY: {main.HudReadyFollowers}/{main.HudExpectedFollowers} followers"));

        bool armed = main.HudOutputArmed;
        if (ImGui.Checkbox(Loc.T("ARM notes"), ref armed)) main.HudSetOutputArmed(armed);
        ImGui.SameLine();
        ImGui.TextColored(plugin.IsGameOutputSafe() ? Good : Warning,
            Loc.T(plugin.IsGameOutputSafe() ? "37-key READY" : "Performance not ready"));

        if (main.HudCanArmParty)
        {
            if (ImGui.SmallButton(Loc.T("ARM ALL BARDS##hudArmParty"))) main.HudArmParty();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T("ARM every active native Ensemble follower without starting notes."));
            if (!cfg.HudCompact) ImGui.TextDisabled(Loc.T(main.HudPartyArmStatus));
        }
        if (!main.HudIsFollower)
        {
            if (ImGui.SmallButton(Loc.T(main.HudPlayCaption + "##hudPlay"))) main.HudStartOrCue();
            ImGui.SameLine();
        }
        if (main.HudIsSolo)
        {
            if (ImGui.SmallButton(Loc.T(plugin.Player.IsPaused ? "Resume##hud" : "Pause##hud")))
                main.HudPauseResume();
            ImGui.SameLine();
        }
        if (ImGui.SmallButton(Loc.T("STOP##hudStop"))) main.HudStop();
        if (main.HudLiveMidiActive)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("MIDI PANIC##hudLivePanic"))) plugin.LiveMidi.Panic();
        }
        if (main.HudIsSolo)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Next##hudNext"))) main.HudNext();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Full player##hudFull"))) main.IsOpen = true;

        ImGui.Separator();
        if (ImGui.SmallButton(Loc.T(cfg.HudCompact ? "+ Details##hud" : "- Details##hud")))
        {
            cfg.HudCompact = !cfg.HudCompact;
            cfg.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T(cfg.HudLockPosition ? "Unlock##hud" : "Lock##hud")))
        {
            cfg.HudLockPosition = !cfg.HudLockPosition;
            cfg.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Hide HUD##hud"))) plugin.SetHudVisible(false);

        if (cfg.ObsShowCaptureHud)
        {
            var capture = plugin.ObsCapture.Snapshot;
            if (capture.Connected)
            {
                ImGui.Separator();
                ImGui.TextColored(capture.Recording ? Rose : Good,
                    capture.Recording ? (capture.Paused ? "OBS PAUSED " : "OBS REC ") +
                        ObsRecordingClient.FormatDuration(capture.RecordingMilliseconds) : "OBS STANDBY");
                if (capture.Streaming) { ImGui.SameLine(); ImGui.TextColored(Rose, "LIVE"); }
                if (capture.Recording && capture.FreeDiskGiB is { } free && free < cfg.ObsDiskWarningGiB)
                    ImGui.TextColored(Warning, $"OBS LOW DISK: {free:0.0} GiB free");
                if (!capture.Busy && !capture.Recording)
                {
                    if (ImGui.SmallButton("Record concert##hudObsRecord"))
                        plugin.ObsCapture.Command("StartRecord");
                }
                else if (!capture.Busy && capture.Recording)
                {
                    if (ImGui.SmallButton(capture.Paused ? "Resume OBS##hudObsResume" : "Pause OBS##hudObsPause"))
                        plugin.ObsCapture.Command(capture.Paused ? "ResumeRecord" : "PauseRecord");
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Open capture controls##hudObsOpen")) main.IsOpen = true;
                }
                ImGui.TextDisabled("Stop OBS recording in SHOW > Concert Capture (confirmation required).");
            }
        }

        if (!cfg.HudCompact)
        {
            float opacity = cfg.HudOpacity;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderFloat(Loc.T("Opacity##hud"), ref opacity, .45f, 1f, "%.2f"))
            {
                cfg.HudOpacity = opacity;
                cfg.Save();
            }
            bool showNext = cfg.HudShowNextSong;
            if (ImGui.Checkbox(Loc.T("Show next song##hud"), ref showNext))
            {
                cfg.HudShowNextSong = showNext;
                cfg.Save();
            }
            ImGui.TextDisabled(Loc.T("Drag the title bar to move. /windbound hud toggles visibility."));
        }
    }

    private static string FormatTime(double milliseconds)
    {
        TimeSpan time = TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 0, 86400000));
        return time.Hours > 0 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
    }

    private static Vector4 GetStatusColor(string text)
    {
        if (text.Contains("Playing", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Ready", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("starts in", StringComparison.OrdinalIgnoreCase)) return Good;
        return Warning;
    }

    private static void TextWithTooltip(string visible, string full)
    {
        ImGui.TextUnformatted(Loc.T(visible));
        if (ImGui.IsItemHovered() && !string.Equals(visible, full, StringComparison.Ordinal))
            ImGui.SetTooltip(Loc.T(full));
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }
}
