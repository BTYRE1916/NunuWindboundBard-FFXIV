using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>SoundFont audio preview is intentionally separate from FFXIV Performance
/// and from the game MIDI scheduler; the preview has no ARM or party-chat control.</summary>
public sealed partial class MainWindow
{
    private string soundFontInput = "";
    private AudioPreviewMode audioPreviewMode = AudioPreviewMode.OriginalFile;
    private MidiLaneKey? previewSelectedLane;
    private bool previewApplyTrackOctave = true;
    private string previewUiStatus = "Choose a SoundFont (.sf2) to hear the MIDI through your speakers.";

    private static string PreviewModeLabel(AudioPreviewMode mode) => mode switch
    {
        AudioPreviewMode.OriginalFile => Loc.T("Original MIDI (all tracks and GM instruments)"),
        AudioPreviewMode.WindboundArrangement => Loc.T("Windbound C3-C6 arrangement (enabled local parts)"),
        AudioPreviewMode.SelectedTrack => Loc.T("Selected MIDI track (solo preview)"),
        _ => Loc.T("Original MIDI (all tracks and GM instruments)")
    };

    private void DrawAudioPreview()
    {
        var player = plugin.AudioPreview;
        ImGui.TextColored(new Vector4(.98f, .6f, .82f, 1f), Loc.T("LOCAL MIDI AUDIO PREVIEW — SOUNDFONT"));
        ImGui.TextWrapped(Loc.T("Hear MIDI through your own speakers before playing in FFXIV. This preview sends NO game notes, instruments, Ready Checks or party commands."));
        ImGui.TextDisabled(Loc.T("Choose an .sf2 SoundFont file. SoundFonts are not distributed with this plugin."));
        ImGui.SetNextItemWidth(-160);
        ImGui.InputText("##WindboundSf2Path", ref soundFontInput, 1024);
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Browse SoundFont..."))) BrowseSoundFont();
        if (ImGui.SmallButton(Loc.T("Save SoundFont path")))
        {
            cfg.PreviewSoundFontPath = soundFontInput.Trim().Trim('"');
            cfg.Save();
            previewUiStatus = File.Exists(cfg.PreviewSoundFontPath)
                ? "SoundFont path saved. Ready for local preview." : "SoundFont path saved but the file was not found.";
        }
        ImGui.SameLine();
        ImGui.TextDisabled(File.Exists(soundFontInput.Trim().Trim('"')) ? Loc.T("SoundFont found") : Loc.T("SoundFont not selected"));

        if (song is not null)
        {
            ImGui.TextUnformatted(Loc.T("Loaded MIDI:") + " " + Path.GetFileName(loaded));
            if (ImGui.BeginCombo(Loc.T("Preview source"), PreviewModeLabel(audioPreviewMode)))
            {
                foreach (AudioPreviewMode mode in Enum.GetValues<AudioPreviewMode>())
                {
                    if (ImGui.Selectable(PreviewModeLabel(mode), mode == audioPreviewMode)) audioPreviewMode = mode;
                }
                ImGui.EndCombo();
            }
            if (audioPreviewMode == AudioPreviewMode.SelectedTrack)
            {
                var keys = song.Notes.Select(n => new MidiLaneKey(n.Track, n.Channel))
                    .Distinct().OrderBy(k => k.Track).ThenBy(k => k.Channel).ToArray();
                if (previewSelectedLane is null || !keys.Contains(previewSelectedLane.Value))
                    previewSelectedLane = keys.FirstOrDefault();
                string label = DescribeLane(previewSelectedLane ?? default);
                if (ImGui.BeginCombo(Loc.T("Preview track"), label))
                {
                    foreach (var key in keys)
                        if (ImGui.Selectable(DescribeLane(key) + "##audio" + key.Track + ":" + key.Channel,
                                previewSelectedLane == key)) previewSelectedLane = key;
                    ImGui.EndCombo();
                }
                ImGui.Checkbox(Loc.T("Apply this track's saved octave shift"), ref previewApplyTrackOctave);
            }
            if (audioPreviewMode == AudioPreviewMode.WindboundArrangement)
                ImGui.TextDisabled(Loc.T("Uses Windbound's current enabled tracks, C3–C6 optimisation, octave shifts and chord handling."));
        }
        else ImGui.TextDisabled(Loc.T("Load a MIDI from Songs & Playback first."));

        float volume = cfg.PreviewVolume;
        if (ImGui.SliderFloat(Loc.T("Preview volume"), ref volume, 0f, 1f, "%.2f"))
        {
            cfg.PreviewVolume = volume; cfg.Save(); player.SetVolume(volume);
        }
        float speed = cfg.PreviewSpeed;
        if (ImGui.SliderFloat(Loc.T("Preview tempo"), ref speed, .5f, 2f, "%.2fx"))
        {
            cfg.PreviewSpeed = speed; cfg.Save(); player.SetSpeed(speed);
        }
        if (ImGui.Button(Loc.T("Play Audio Preview"))) StartAudioPreview();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T(player.IsPaused ? "Resume Audio" : "Pause Audio"))) player.PauseResume();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Stop Audio Preview"))) player.Stop();

        double elapsed = player.Position.TotalMilliseconds;
        double duration = player.Duration.TotalMilliseconds;
        float fraction = duration <= 0 ? 0 : (float)Math.Clamp(elapsed / duration, 0, 1);
        ImGui.ProgressBar(fraction, new Vector2(-1, 13), $"{fraction * 100:0}%");
        ImGui.TextDisabled(FormatPreviewTime(elapsed) + " / " + FormatPreviewTime(duration));
        ImGui.TextWrapped(Loc.T(player.Status));
        if (!string.IsNullOrWhiteSpace(previewUiStatus)) ImGui.TextWrapped(Loc.T(previewUiStatus));
        ImGui.TextDisabled(Loc.T("Audio only. Starting FFXIV MIDI performance automatically stops this local preview."));
    }

    private string DescribeLane(MidiLaneKey key)
    {
        string name = song?.Tracks.FirstOrDefault(t => t.Index == key.Track)?.Name ?? $"Track {key.Track + 1}";
        int count = song?.Notes.Count(n => n.Track == key.Track && n.Channel == key.Channel) ?? 0;
        return $"{key.Track + 1:00}:{key.Channel + 1:00}  {name} ({count} notes)";
    }
    private static string FormatPreviewTime(double ms)
    {
        TimeSpan span = TimeSpan.FromMilliseconds(Math.Clamp(ms, 0, 3600000));
        return span.Hours > 0 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"mm\:ss");
    }
    private void BrowseSoundFont()
    {
        string folder = Path.GetDirectoryName(soundFontInput) ?? "";
        if (!Directory.Exists(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        fileDialogs.OpenFileDialog(Loc.T("Choose a SoundFont (.sf2)"), "SoundFont{.sf2}",
            (bool selected, System.Collections.Generic.List<string> files) =>
            {
                if (selected && files.Count > 0)
                {
                    soundFontInput = files[0];
                    cfg.PreviewSoundFontPath = soundFontInput;
                    cfg.Save();
                    previewUiStatus = "SoundFont selected. Press Play Audio Preview to listen.";
                }
            }, 1, folder);
    }

    private void StartAudioPreview()
    {
        try
        {
            if (song is null || !File.Exists(loaded))
                throw new InvalidOperationException("Load a MIDI file in Songs & Playback before previewing.");
            if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
                throw new InvalidOperationException("Stop FFXIV MIDI playback before starting the audio preview.");
            string sf2 = soundFontInput.Trim().Trim('"');
            cfg.PreviewSoundFontPath = sf2;
            cfg.Save();
            byte[]? previewData = audioPreviewMode == AudioPreviewMode.OriginalFile
                ? null
                : AudioPreviewMidiBuilder.Build(song, arranged, audioPreviewMode,
                    previewSelectedLane, lanes, previewApplyTrackOctave);
            plugin.AudioPreview.SetVolume(cfg.PreviewVolume);
            plugin.AudioPreview.SetSpeed(cfg.PreviewSpeed);
            plugin.AudioPreview.Start(loaded, sf2, previewData);
            previewUiStatus = "Audio preview is local and will not play notes in FFXIV.";
        }
        catch (Exception ex)
        {
            previewUiStatus = "Audio preview could not start: " + ex.Message;
        }
    }
}
