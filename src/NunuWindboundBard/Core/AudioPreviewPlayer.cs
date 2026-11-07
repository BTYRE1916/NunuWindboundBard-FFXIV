using System;
using System.IO;
using System.Threading.Tasks;
using MeltySynth;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NunuWindboundBard.Core;

/// <summary>
/// Local-only SF2 MIDI audio preview. No Dalamud, Performance addon or native
/// FFXIV note callbacks here. MeltySynth (MIT) renders samples; NAudio WinMM
/// supplies speakers. The expensive SF2 load runs off the framework/UI thread;
/// WaveOut is initialised from Poll(), on the framework thread.
/// </summary>
public sealed class AudioPreviewPlayer : IDisposable
{
    private const int Rate = 44100;
    private readonly object gate = new();
    private int generation;
    private Task<PreparedPreview>? pending;
    private int pendingGeneration;
    private WaveOutEvent? waveOut;
    private PreviewSamples? samples;
    private string state = "Idle";
    private string currentSong = "";
    private bool paused;
    private float volume = .65f;
    private float speed = 1f;

    private sealed record PreparedPreview(MidiFile File, Synthesizer Synthesizer,
        MidiFileSequencer Sequencer, string Song);

    private sealed class PreviewSamples : ISampleProvider
    {
        private readonly object sync = new();
        private readonly MidiFileSequencer sequencer;
        private readonly TimeSpan duration;
        private bool reachedEnd;
        private int tailFrames;
        private bool finished;
        private float volume;

        public PreviewSamples(MidiFileSequencer sequencer, TimeSpan midiDuration, float initialVolume)
        {
            this.sequencer = sequencer;
            duration = midiDuration;
            volume = initialVolume;
        }
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
        public bool Finished { get { lock (sync) return finished; } }
        public TimeSpan Position { get { lock (sync) return sequencer.Position; } }
        // MeltySynth 2.4.1 exposes Length on MidiFile, not on MidiFileSequencer.
        // Keep the duration captured from the loaded MIDI when starting preview.
        public TimeSpan Duration => duration;
        public void SetVolume(float value) { lock (sync) volume = Math.Clamp(value, 0, 1); }
        public void SetSpeed(float value) { lock (sync) sequencer.Speed = Math.Clamp(value, .5f, 2f); }

        public int Read(float[] buffer, int offset, int count)
        {
            if (count < 2) return 0;
            count &= ~1; // stereo interleaved
            lock (sync)
            {
                if (finished) return 0;
                // The SoundFont renderer, unlike the original track parser, needs
                // a single-thread owner. All sequencer calls are under this lock.
                sequencer.RenderInterleaved(buffer.AsSpan(offset, count));
                if (sequencer.EndOfSequence)
                {
                    if (!reachedEnd)
                    {
                        reachedEnd = true;
                        tailFrames = Rate; // allow SF2 release envelopes/reverb to decay
                    }
                    tailFrames -= count / 2;
                    if (tailFrames <= 0) finished = true;
                }
                float level = volume;
                for (int i = offset; i < offset + count; i++) buffer[i] *= level;
                return count;
            }
        }
    }

    public string Status { get { lock (gate) return state; } }
    public string CurrentSong { get { lock (gate) return currentSong; } }
    public bool IsLoading { get { lock (gate) return pending != null; } }
    public bool IsPlaying { get { lock (gate) return waveOut != null; } }
    public bool IsPaused { get { lock (gate) return paused; } }
    public TimeSpan Position { get { lock (gate) return samples?.Position ?? TimeSpan.Zero; } }
    public TimeSpan Duration { get { lock (gate) return samples?.Duration ?? TimeSpan.Zero; } }

    public void SetVolume(float newVolume)
    {
        lock (gate)
        {
            volume = Math.Clamp(newVolume, 0f, 1f);
            samples?.SetVolume(volume);
        }
    }
    public void SetSpeed(float newSpeed)
    {
        lock (gate)
        {
            speed = Math.Clamp(newSpeed, .5f, 2f);
            samples?.SetSpeed(speed);
        }
    }

    public void Start(string midiPath, string soundFontPath, byte[]? preparedMidi = null)
    {
        Stop("Preparing preview...");
        if (string.IsNullOrWhiteSpace(soundFontPath) || !File.Exists(soundFontPath))
            throw new FileNotFoundException("Choose a valid SoundFont .sf2 file.", soundFontPath);
        if (!string.Equals(Path.GetExtension(soundFontPath), ".sf2", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a .sf2 SoundFont. Compressed SF3 files are not supported.");
        if (new FileInfo(soundFontPath).Length > 512L * 1024 * 1024)
            throw new InvalidDataException("SoundFont exceeds the 512MB preview safety limit.");
        if (preparedMidi is null && !File.Exists(midiPath))
            throw new FileNotFoundException("Load a MIDI before starting audio preview.", midiPath);

        var soundFont = Path.GetFullPath(soundFontPath);
        var midi = midiPath;
        var bytes = preparedMidi;
        lock (gate)
        {
            int id = ++generation;
            pendingGeneration = id;
            currentSong = Path.GetFileName(midiPath);
            state = "Loading SoundFont and MIDI for local preview...";
            pending = Task.Run(() =>
            {
                // Neither the live MIDI Performance plugin nor other players
                // share this synthesizer instance.
                var synth = new Synthesizer(soundFont, Rate);
                using var stream = bytes is null ? (Stream)File.OpenRead(midi) : new MemoryStream(bytes, writable: false);
                var file = new MidiFile(stream);
                var seq = new MidiFileSequencer(synth);
                seq.Play(file, false);
                return new PreparedPreview(file, synth, seq, Path.GetFileName(midi));
            });
        }
    }

    public void PauseResume()
    {
        lock (gate)
        {
            if (waveOut is null) return;
            if (paused) { waveOut.Play(); state = "Preview playing through local speakers."; }
            else { waveOut.Pause(); state = "Preview paused."; }
            paused = !paused;
        }
    }

    /// <summary>Call on framework update. An SF2 load and waveform playback are
    /// wholly independent of the FFXIV native note output/ensemble clock.</summary>
    public void Poll()
    {
        Task<PreparedPreview>? ready;
        int id;
        lock (gate)
        {
            ready = pending;
            id = pendingGeneration;
        }
        if (ready is not null && ready.IsCompleted)
        {
            lock (gate)
            {
                if (!ReferenceEquals(ready, pending) || id != generation) return;
                pending = null;
                try
                {
                    var prepared = ready.GetAwaiter().GetResult();
                    var provider = new PreviewSamples(prepared.Sequencer, prepared.File.Length, volume);
                    provider.SetSpeed(speed);
                    var output = new WaveOutEvent { DesiredLatency = 160, NumberOfBuffers = 3 };
                    try
                    {
                        // WinMM expects PCM; do not send float output directly.
                        output.Init(new SampleToWaveProvider16(provider));
                        output.Play();
                    }
                    catch { output.Dispose(); throw; }
                    samples = provider;
                    waveOut = output;
                    paused = false;
                    state = "Preview playing through local speakers.";
                }
                catch (Exception ex)
                {
                    state = "Audio preview failed: " + ex.GetBaseException().Message;
                }
            }
        }
        bool completed;
        lock (gate)
            completed = waveOut is not null && samples is { Finished: true } &&
                waveOut.PlaybackState == PlaybackState.Stopped;
        if (completed) Stop("Audio preview finished.");
    }

    public void Stop(string message = "Audio preview stopped.")
    {
        WaveOutEvent? old;
        lock (gate)
        {
            generation++;
            pending = null; // late SoundFont tasks cannot start audio after Stop
            old = waveOut;
            waveOut = null;
            samples = null;
            paused = false;
            state = message;
        }
        if (old is not null)
        {
            // NEVER hold the player lock while waiting for WinMM audio callback.
            try { old.Stop(); } catch { /* device might have been disconnected */ }
            try { old.Dispose(); } catch { /* shutdown should not crash the game */ }
        }
    }
    public void Dispose() => Stop();
}
