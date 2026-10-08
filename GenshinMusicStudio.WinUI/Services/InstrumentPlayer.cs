using System.Diagnostics;
using NAudio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GenshinMusicStudio_WinUI.Services;

public sealed class InstrumentPlayer : IDisposable
{
    private const int TargetRate = 44100;

    private readonly object gate = new();
    private readonly Dictionary<int, float[]> samples = new();
    private readonly Dictionary<int, List<CachedSampleSource>> activeVoices = new();
    private WaveOutEvent? output;
    private MixingSampleProvider? mixer;
    private string currentInstrument = string.Empty;
    private double releaseSeconds;
    private InstrumentLayout layout = new();

    public string CurrentInstrument => currentInstrument;
    public InstrumentLayout Layout => layout;

    public double LatencySeconds => 0.1;

    public void LoadInstrument(string folder)
    {
        lock (gate)
        {
            samples.Clear();
            activeVoices.Clear();
            currentInstrument = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
            releaseSeconds = ReadReleaseSeconds(folder);
            layout = InstrumentLayout.FromShape(ReadShape(folder), DetectMidiNaming(folder));
            foreach (var file in Directory.EnumerateFiles(folder, "*.mp3"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var key = layout.ByMidiName
                    ? layout.KeyIndexForPitch(int.TryParse(name.TrimStart('m', 'M'), out var midi) ? midi : -1)
                    : int.TryParse(name, out var button) && button >= 0 && button < layout.KeyCount
                        ? button
                        : -1;
                if (key >= 0 && key < layout.KeyCount)
                {
                    try
                    {
                        samples[key] = LoadSampleBuffer(file);
                    }
                    catch
                    {
                        // A broken sample should not break the whole instrument.
                    }
                }
            }
            EnsureOutput();
        }
    }

    public void PlayKey(int pitch, double gain)
    {
        lock (gate)
        {
            var keyIndex = layout.KeyIndexForPitch(pitch);
            if (mixer is null || !samples.TryGetValue(keyIndex, out var buffer)) return;
            var source = new CachedSampleSource(buffer) { Gain = (float)Math.Clamp(gain, 0.05, 1.0) };
            mixer.AddMixerInput(source);
            if (releaseSeconds > 0)
            {
                if (!activeVoices.TryGetValue(keyIndex, out var voices))
                {
                    voices = new List<CachedSampleSource>();
                    activeVoices[keyIndex] = voices;
                }
                voices.Add(source);
            }
        }
    }

    public void NoteOff(int pitch)
    {
        lock (gate)
        {
            if (releaseSeconds <= 0) return;
            var keyIndex = layout.KeyIndexForPitch(pitch);
            if (!activeVoices.TryGetValue(keyIndex, out var voices)) return;
            foreach (var voice in voices)
            {
                voice.StartFade(releaseSeconds);
            }
            voices.Clear();
        }
    }

    public void SetDeviceVolume(double volume)
    {
        lock (gate)
        {
            if (output is not null)
            {
                output.Volume = (float)Math.Clamp(volume, 0.0, 1.0);
            }
        }
    }

    private void EnsureOutput()
    {
        if (output is not null && mixer is not null) return;
        mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(TargetRate, 2))
        {
            ReadFully = true,
        };
        output = new WaveOutEvent
        {
            DesiredLatency = 150,
            NumberOfBuffers = 2,
        };
        output.Init(mixer);
        output.Play();
    }

    private static double ReadReleaseSeconds(string folder)
    {
        try
        {
            var metaPath = Path.Combine(folder, "meta.json");
            if (!File.Exists(metaPath)) return 0;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metaPath));
            if (doc.RootElement.TryGetProperty("sustain", out var sustain)
                && sustain.TryGetProperty("release", out var release)
                && release.TryGetDouble(out var seconds))
            {
                return Math.Clamp(seconds, 0, 5);
            }
        }
        catch
        {
        }
        return 0;
    }

    private static string ReadShape(string folder)
    {
        try
        {
            var metaPath = Path.Combine(folder, "meta.json");
            if (!File.Exists(metaPath)) return "";
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metaPath));
            return doc.RootElement.TryGetProperty("shape", out var shape) ? shape.GetString() ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static bool DetectMidiNaming(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*.mp3"))
        {
            var name = Path.GetFileName(file);
            return name.StartsWith('m') || name.StartsWith('M');
        }
        return false;
    }

    private static float[] LoadSampleBuffer(string file)
    {
        using WaveStream reader = file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            ? new Mp3FileReader(file)
            : new AudioFileReader(file);
        ISampleProvider source = reader.ToSampleProvider();
        if (reader.WaveFormat.SampleRate != TargetRate)
        {
            source = new WdlResamplingSampleProvider(source, TargetRate);
        }
        if (source.WaveFormat.Channels == 1)
        {
            source = source.ToStereo();
        }
        var collected = new List<float>();
        var buffer = new float[source.WaveFormat.Channels * 8192];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            collected.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        return collected.ToArray();
    }

    public void Dispose()
    {
        lock (gate)
        {
            output?.Stop();
            output?.Dispose();
            output = null;
            mixer = null;
            samples.Clear();
        }
    }

    private sealed class CachedSampleSource : ISampleProvider
    {
        private readonly float[] buffer;
        private int position;
        private long fadeStart;
        private double fadeSeconds;

        public WaveFormat WaveFormat { get; }
        public float Gain { get; set; } = 1f;
        public void StartFade(double seconds)
        {
            fadeStart = Stopwatch.GetTimestamp();
            fadeSeconds = seconds;
        }

        public CachedSampleSource(float[] buffer)
        {
            this.buffer = buffer;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        }

        public int Read(float[] target, int offset, int count)
        {
            var available = buffer.Length - position;
            if (available <= 0) return 0;
            var toCopy = Math.Min(count, available);
            if (fadeSeconds > 0)
            {
                var elapsed = (Stopwatch.GetTimestamp() - fadeStart) / (double)Stopwatch.Frequency;
                if (elapsed >= fadeSeconds) return 0;
                var fadeGain = (float)(Gain * (1.0 - elapsed / fadeSeconds));
                for (var i = 0; i < toCopy; i++)
                {
                    target[offset + i] = buffer[position + i] * fadeGain;
                }
            }
            else
            {
                for (var i = 0; i < toCopy; i++)
                {
                    target[offset + i] = buffer[position + i] * Gain;
                }
            }
            position += toCopy;
            return toCopy;
        }
    }
}
