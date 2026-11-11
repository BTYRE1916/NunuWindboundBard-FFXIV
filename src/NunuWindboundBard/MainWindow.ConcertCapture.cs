using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>Manual OBS recording/streaming controls. Never changes MIDI playback.</summary>
public sealed partial class MainWindow
{
    // Password is session-only and cleared from the text box on Connect.
    private string obsPasswordInput = "";
    private bool obsGoLiveConfirmed;
    private bool obsConfirmRecordStop;
    private bool obsConfirmStreamStop;
    private long obsTitleTestUntilQpc;
    private readonly ObsSongTitleFileBridge obsTitleFile = new(
        Plugin.PluginInterface.GetPluginConfigDirectory());

    // Called every framework tick, including when the Concert Capture tab is closed.
    // The OBS client compares/coalesces updates and performs WebSocket I/O later.
    internal void PollConcertCaptureSongTitle()
    {
        string title = ObsSongTitleOverlay.FromMidiPath(loaded);
        string text = ObsSongTitleOverlay.TextForPlayback(title, plugin.Player.IsPlaying,
            cfg.ObsSongTitleOnlyWhilePlaying);
        // Test the full OBS chain without needing a MIDI currently playing.
        if (Stopwatch.GetTimestamp() < obsTitleTestUntilQpc)
            text = "WINDBOUND BARD — TEST TITLE";
        plugin.ObsCapture.SetSongTitle(cfg.ObsSongTitleSource ?? ObsSongTitleOverlay.DefaultSourceName,
            text, cfg.ObsSongTitleEnabled);
        // Fallback never needs OBS or sends anything to the game/party.
        obsTitleFile.SetText(cfg.ObsSongTitleEnabled ? text : "");
    }

    private void DrawConcertCapture()
    {
        var obs = plugin.ObsCapture;
        var state = obs.Snapshot;
        Vector4 green = new(.38f, .94f, .65f, 1f);
        Vector4 pink = new(1f, .43f, .65f, 1f);
        Vector4 amber = new(1f, .76f, .37f, 1f);
        ImGui.TextColored(pink, "CONCERT CAPTURE STUDIO  |  OBS WEBSOCKET 5");
        ImGui.TextWrapped("Record a full concert for as many hours as OBS and available disk space allow. " +
            "WindboundBard only controls local OBS; OBS captures FFXIV, audio and optionally your microphone.");
        ImGui.TextDisabled("Recording never starts automatically with a song or ensemble. Streaming always requires separate confirmation.");
        ImGui.Separator();
        if (!state.Connected)
        {
            int port = cfg.ObsWebSocketPort;
            ImGui.SetNextItemWidth(145);
            if (ImGui.InputInt("OBS WebSocket port##windboundObsPort", ref port))
            {
                cfg.ObsWebSocketPort = Math.Clamp(port, 1, 65535);
                cfg.Save();
            }
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("OBS WebSocket password##windboundObsPassword", ref obsPasswordInput, 256,
                ImGuiInputTextFlags.Password);
            ImGui.TextDisabled("Connects only to 127.0.0.1. The password is never saved in WindboundBard's config.");
            if (!state.Busy && ImGui.Button("CONNECT TO LOCAL OBS##windboundObsConnect"))
            {
                obs.Connect(cfg.ObsWebSocketPort, obsPasswordInput);
                obsPasswordInput = "";
                obsGoLiveConfirmed = false;
            }
            ImGui.TextWrapped("In OBS: Tools > WebSocket Server Settings > Enable WebSocket server; " +
                "use port 4455 or your configured port. Configure your recording scene and Game Capture in OBS first.");
        }
        else
        {
            if (ImGui.Button("DISCONNECT OBS##windboundObsDisconnect"))
            {
                obs.Disconnect();
                obsGoLiveConfirmed = false;
            }
            ImGui.SameLine();
            ImGui.TextColored(green, "OBS CONNECTED  (LOCAL)");
        }
        ImGui.TextWrapped(state.Message);
        if (state.Busy) ImGui.TextColored(amber, "OBS is processing a command...");
        ImGui.Separator();

        if (state.Recording)
        {
            ImGui.TextColored(state.Paused ? amber : pink,
                (state.Paused ? "PAUSED" : "REC") + "   " + ObsRecordingClient.FormatDuration(state.RecordingMilliseconds));
            ImGui.TextDisabled($"Recorded: {state.RecordingBytes / 1048576.0:0} MiB");
        }
        else ImGui.TextDisabled("RECORDING: STANDBY");
        ImGui.SameLine();
        ImGui.TextColored(state.Streaming ? pink : green, state.Streaming ? "LIVE STREAM" : "STREAM OFF");
        if (state.OutputWidth > 0)
        {
            ImGui.TextUnformatted($"OBS output: {state.OutputWidth} x {state.OutputHeight}" +
                (state.OutputFrameRate > 0 ? $"  @ {state.OutputFrameRate:0.#} FPS" : ""));
            if (state.OutputWidth < 3840 || state.OutputHeight < 2160)
                ImGui.TextColored(amber, "For 4K, set OBS Settings > Video > Output to 3840 x 2160.");
        }
        ImGui.TextDisabled("Select NVENC H.264/HEVC/AV1 if available under OBS Settings > Output. " +
            "WindboundBard never changes OBS quality or encoder settings.");

        if (state.Connected)
        {
            ImGui.Spacing();
            if (!state.Recording)
            {
                if (!state.Busy && ImGui.Button("START FULL CONCERT RECORDING##windboundObsStart"))
                { obs.Command("StartRecord"); obsConfirmRecordStop = false; }
            }
            else
            {
                if (!state.Busy && ImGui.Button(state.Paused ? "RESUME RECORDING##windboundObsResume" :
                    "PAUSE RECORDING##windboundObsPause"))
                    obs.Command(state.Paused ? "ResumeRecord" : "PauseRecord");
                ImGui.SameLine();
                if (!state.Busy && ImGui.Button("SPLIT FILE NOW##windboundObsSplit"))
                    obs.Command("SplitRecordFile");
                if (ImGui.Button("STOP RECORDING...##windboundObsStopConfirm")) obsConfirmRecordStop = true;
                if (obsConfirmRecordStop)
                {
                    ImGui.TextColored(amber, "Stop recording the current concert? OBS will finalize the file.");
                    if (!state.Busy && ImGui.Button("YES, STOP & SAVE##windboundObsStop"))
                    { obs.Command("StopRecord"); obsConfirmRecordStop = false; }
                    ImGui.SameLine();
                    if (ImGui.Button("Keep recording##windboundObsKeep")) obsConfirmRecordStop = false;
                }
            }
        }
        ImGui.TextDisabled("There is NO recording time limit and no song-triggered stop. " +
            "OBS keeps recording if WindboundBard's UI closes or the plugin reloads.");
        if (!string.IsNullOrWhiteSpace(state.LastSavedFile))
            ImGui.TextWrapped("OBS saved recording: " + state.LastSavedFile);
        ImGui.Separator();

        ImGui.TextUnformatted("DISK CAPACITY MONITOR (advisory only)");
        string folder = cfg.ObsDiskMonitorFolder;
        ImGui.SetNextItemWidth(380);
        if (ImGui.InputText("Folder override##windboundObsFolder", ref folder, 512))
        { cfg.ObsDiskMonitorFolder = folder; cfg.Save(); obs.SetDiskMonitorFolder(folder); }
        ImGui.TextDisabled("Blank = the OBS recording folder when supported. Overrides affect warnings only, not OBS files.");
        double? freeGiB = state.FreeDiskGiB;
        if (freeGiB.HasValue)
        {
            if (freeGiB < cfg.ObsDiskWarningGiB)
                ImGui.TextColored(amber, $"LOW DISK SPACE: {freeGiB:0.0} GiB free. Check OBS recording storage.");
            else ImGui.TextColored(green, $"Recording drive: {freeGiB:0.0} GiB available");
        }
        else ImGui.TextDisabled("Free space unknown: choose a recording folder above if needed.");
        float warning = cfg.ObsDiskWarningGiB;
        ImGui.SetNextItemWidth(280);
        if (ImGui.SliderFloat("Low-space warning (GiB)##windboundObsWarn", ref warning, 2f, 100f, "%.0f GiB"))
        { cfg.ObsDiskWarningGiB = warning; cfg.Save(); }
        ImGui.Separator();

        ImGui.TextColored(WindboundStageStyle.Accent(cfg.StageUiAccent), "REPLAY BUFFER (OPTIONAL)");
        if (state.Connected)
        {
            if (!state.Busy && ImGui.Button(state.ReplayBuffer ? "STOP REPLAY BUFFER##windboundObsReplayStop" :
                "START REPLAY BUFFER##windboundObsReplayStart"))
                obs.Command(state.ReplayBuffer ? "StopReplayBuffer" : "StartReplayBuffer");
            ImGui.SameLine();
            if (state.ReplayBuffer && !state.Busy && ImGui.Button("SAVE LAST MOMENTS##windboundObsReplaySave"))
                obs.Command("SaveReplayBuffer");
        }
        ImGui.TextDisabled("Configure replay buffer length/memory in OBS. It is independent from unlimited concert recording.");
        ImGui.Separator();

        ImGui.TextColored(pink, "LIVE STREAMING (SEPARATE OPT-IN)");
        if (!state.Streaming)
        {
            ImGui.Checkbox("I understand this will broadcast my OBS scene publicly##windboundObsLiveConfirm", ref obsGoLiveConfirmed);
            if (state.Connected && obsGoLiveConfirmed && !state.Busy && ImGui.Button("GO LIVE##windboundObsGoLive"))
            { obs.Command("StartStream"); obsGoLiveConfirmed = false; }
        }
        else
        {
            ImGui.TextColored(pink, "LIVE — OBS is broadcasting");
            if (ImGui.Button("STOP LIVESTREAM...##windboundObsEndLiveConfirm")) obsConfirmStreamStop = true;
            if (obsConfirmStreamStop)
            {
                if (!state.Busy && ImGui.Button("YES, END STREAM##windboundObsEndLive"))
                { obs.Command("StopStream"); obsConfirmStreamStop = false; }
                ImGui.SameLine();
                if (ImGui.Button("Keep streaming##windboundObsLiveKeep")) obsConfirmStreamStop = false;
            }
        }
        ImGui.TextDisabled("Configure streaming service/stream key in OBS. No stream key is stored or transmitted by WindboundBard.");
        ImGui.Separator();

        ImGui.TextColored(WindboundStageStyle.Accent(cfg.StageUiAccent), "NOW PLAYING — OBS SONG TITLE OVERLAY");
        ImGui.TextWrapped("Displays the current MIDI filename (without .mid) on the OBS recording/stream. " +
            "The title source must be visible in your active OBS recording scene.");
        bool titleEnabled = cfg.ObsSongTitleEnabled;
        if (ImGui.Checkbox("Show current MIDI title on OBS video##windboundObsTitleToggle", ref titleEnabled))
        { cfg.ObsSongTitleEnabled = titleEnabled; cfg.Save(); }
        string sourceName = cfg.ObsSongTitleSource ?? ObsSongTitleOverlay.DefaultSourceName;
        ImGui.SetNextItemWidth(315);
        if (ImGui.InputText("OBS Text source name##windboundObsTitleSource", ref sourceName, 128))
        { cfg.ObsSongTitleSource = sourceName; cfg.Save(); }
        if (state.Connected)
        {
            if (!state.Busy && ImGui.Button("CREATE TEXT SOURCE IN OBS SCENE##wbObsTitleCreate"))
                obs.CreateSongTitleSource(cfg.ObsSongTitleSource ?? ObsSongTitleOverlay.DefaultSourceName);
            ImGui.SameLine();
            if (ImGui.SmallButton("Refresh OBS text sources##wbObsTitleRefresh"))
                obs.RefreshSongTitleSources();
            if (state.SongTitleSources.Length > 0)
            {
                if (ImGui.BeginCombo("Select existing OBS Text source##wbObsTitlePicker", sourceName))
                {
                    foreach (string available in state.SongTitleSources)
                        if (ImGui.Selectable(available + "##wbObsSource" + available,
                            string.Equals(available, sourceName, StringComparison.Ordinal)))
                        {
                            cfg.ObsSongTitleSource = available;
                            sourceName = available;
                            cfg.Save();
                            obs.RetrySongTitle();
                        }
                    ImGui.EndCombo();
                }
            }
            ImGui.TextDisabled("OBS program scene: " +
                (state.CurrentObsScene.Length == 0 ? "unknown" : state.CurrentObsScene));
        }
        bool onlyPlaying = cfg.ObsSongTitleOnlyWhilePlaying;
        if (ImGui.Checkbox("Hide title between songs##windboundObsTitleHide", ref onlyPlaying))
        { cfg.ObsSongTitleOnlyWhilePlaying = onlyPlaying; cfg.Save(); }
        string titlePreview = ObsSongTitleOverlay.FromMidiPath(loaded);
        ImGui.TextWrapped("Current MIDI: " + (titlePreview.Length == 0 ? "No song loaded" : titlePreview));
        ImGui.TextDisabled("Local MIDI output: " + (plugin.Player.IsPlaying ? "PLAYING" : "IDLE") +
            (cfg.ObsSongTitleOnlyWhilePlaying && !plugin.Player.IsPlaying
                ? " — Hide between songs is clearing the title (including a silent conductor)." : ""));
        ImGui.TextWrapped(state.SongTitleStatus);
        if (ImGui.SmallButton("COPY TITLE DIAGNOSTICS##wbObsTitleDiagnostics"))
            ImGui.SetClipboardText("WindboundBard OBS title diagnostics: " +
                "OBS Connected=" + state.Connected +
                "; Source='" + sourceName + "'; TitleEnabled=" + cfg.ObsSongTitleEnabled +
                "; LocalMidiPlaying=" + plugin.Player.IsPlaying +
                "; OnlyWhilePlaying=" + cfg.ObsSongTitleOnlyWhilePlaying +
                "; LoadedTitle='" + titlePreview + "'; OBS Scene='" + state.CurrentObsScene +
                "'; AvailableTextSources='" + string.Join(", ", state.SongTitleSources) +
                "'; TitleStatus=" + state.SongTitleStatus + "; TitleFileStatus=" + obsTitleFile.Status);
        if (state.Connected && ImGui.Button("SYNC SONG TITLE TO OBS NOW##windboundObsTitleSync"))
            obs.RetrySongTitle();
        ImGui.SameLine();
        if (ImGui.Button("SHOW TEST TITLE (10 seconds)##wbObsTitleTest"))
        {
            cfg.ObsSongTitleEnabled = true;
            cfg.Save();
            obsTitleTestUntilQpc = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
            obs.RetrySongTitle();
        }
        ImGui.TextDisabled("For WebSocket titles: OBS Text source must be visible, and Read from file must be OFF.");
        ImGui.Separator();
        ImGui.TextUnformatted("ALTERNATIVE: OBS TEXT-FILE SOURCE (NO WEBSOCKET NEEDED)");
        ImGui.TextWrapped("WindboundBard automatically maintains a small local UTF-8 text file while the " +
            "title overlay is enabled. In OBS Text (GDI+), turn ON Read from file and choose this path:");
        ImGui.TextWrapped(obsTitleFile.FilePath);
        if (ImGui.SmallButton("COPY TITLE FILE PATH##wbObsCopyFilePath"))
            ImGui.SetClipboardText(obsTitleFile.FilePath);
        ImGui.SameLine();
        if (ImGui.SmallButton("RETRY TITLE FILE WRITE##wbObsRetryFile"))
            obsTitleFile.Retry();
        ImGui.TextDisabled(obsTitleFile.Status);
        ImGui.TextDisabled("Use either OBS's WebSocket text property OR the text file, not both on the same source.");

        ImGui.TextWrapped("Long concerts: OBS MKV (or hybrid MP4) is recommended. " +
            "Use OBS Settings > Output for optional automatic file splitting; " +
            "the Split File button is a manual split without ending the recording session. " +
            "Monitor your available storage and FFXIV frame rate during 4K capture.");
    }
}
