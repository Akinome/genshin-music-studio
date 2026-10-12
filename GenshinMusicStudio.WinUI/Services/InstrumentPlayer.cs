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
    private readonly Dictionary<string, InstrumentData> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> cacheOrder = new();
    private const int MaxCachedInstruments = 3;
    private WaveOutEvent? output;
    private MixingSampleProvider? mixer;
    private string currentInstrument = string.Empty;
    private double releaseSeconds;
    private InstrumentLayout layout = new();
    private long loadSequence;
    private long commitLock;
    private readonly object commitGate = new();

    public string CurrentInstrument => currentInstrument;
    public InstrumentLayout Layout => layout;

    public double LatencySeconds => 0.1;

    public long NextLoadSequence() => Interlocked.Increment(ref loadSequence);

    /// <summary>
    /// Decodes an instrument folder and publishes it only when its sequence is
    /// still the newest one, so a slow background load can never overwrite a
    /// selection the user made afterwards.
    /// </summary>
    public bool LoadInstrument(string folder, long sequence)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        lock (gate)
        {
            if (samples.Count > 0 && string.Equals(currentInstrument, name, StringComparison.OrdinalIgnoreCase))
            {
                lock (commitGate)
                {
                    commitLock = Math.Max(commitLock, sequence);
                }
                return true;
            }
        }

        if (!cache.TryGetValue(name, out var data))
        {
            data = Decode(folder);
        }
        lock (gate)
        {
            if (!cache.ContainsKey(name))
            {
                cache[name] = data;
                cacheOrder.Remove(name);
                cacheOrder.Add(name);
                while (cacheOrder.Count > MaxCachedInstruments)
                {
                    cache.Remove(cacheOrder[0]);
                    cacheOrder.RemoveAt(0);
                }
            }
        }

        lock (commitGate)
        {
            if (sequence < commitLock) return false;
            commitLock = sequence;
        }

        lock (gate)
        {
            ReplaceSamples(data);
        }
        EnsureOutput();
        return true;
    }

    private static InstrumentData Decode(string folder)
    {
        var release = ReadReleaseSeconds(folder);
        var newLayout = InstrumentLayout.FromShape(ReadShape(folder), DetectMidiNaming(folder));
        var files = Directory.EnumerateFiles(folder, "*.mp3").ToList();
        var decoded = new float[newLayout.KeyCount][];
        Parallel.ForEach(files, file =>
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var key = newLayout.ByMidiName
                ? newLayout.KeyIndexForPitch(int.TryParse(name.TrimStart('m', 'M'), out var midi) ? midi : -1)
                : int.TryParse(name, out var button) && button >= 0 && button < newLayout.KeyCount
                    ? button
                    : -1;
            if (key >= 0 && key < newLayout.KeyCount)
            {
                try
                {
                    decoded[key] = LoadSampleBuffer(file);
                }
                catch
                {
                    // A broken sample should not break the whole instrument.
                }
            }
        });

        var map = new Dictionary<int, float[]>();
        for (var key = 0; key < decoded.Length; key++)
        {
            if (decoded[key] is not null)
            {
                map[key] = decoded[key];
            }
        }
        return new InstrumentData(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)), map, newLayout, release);
    }

    private void ReplaceSamples(InstrumentData data)
    {
        samples.Clear();
        activeVoices.Clear();
        currentInstrument = data.Name;
        releaseSeconds = data.ReleaseSeconds;
        layout = data.Layout;
        foreach (var pair in data.Samples)
        {
            samples[pair.Key] = pair.Value;
        }
    }

    private sealed record InstrumentData(
        string Name,
        Dictionary<int, float[]> Samples,
        InstrumentLayout Layout,
        double ReleaseSeconds);

    public void PlayKey(int pitch, double gain)
    {
        lock (gate)
        {
            var keyIndex = layout.KeyIndexForPitch(pitch);
            if (mixer is null || !samples.TryGetValue(keyIndex, out var buffer)) return;
            var source = new CachedSampleSource(buffer) { Gain = (float)Math.Clamp(gain, 0.05, 1.0) };
            mixer.AddMixerInput(source);

            if (!activeVoices.TryGetValue(keyIndex, out var voices))
            {
                voices = new List<CachedSampleSource>();
                activeVoices[keyIndex] = voices;
            }
            voices.Add(source);
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

    public void ReleaseAll(double fadeSeconds = 0.06)
    {
        lock (gate)
        {
            foreach (var pair in activeVoices)
            {
                foreach (var voice in pair.Value)
                {
                    voice.StartFade(Math.Clamp(fadeSeconds, 0.02, 2.0));
                }
                pair.Value.Clear();
            }
            activeVoices.Clear();
        }
    }

    /// <summary>
    /// Silences every voice and halts the output device. Voices are removed
    /// outright rather than faded, so a resume right after a pause never layers
    /// new notes on top of the old ones (which is what caused the distortion).
    /// </summary>
    public void Suspend()
    {
        lock (gate)
        {
            foreach (var pair in activeVoices)
            {
                foreach (var voice in pair.Value)
                {
                    mixer?.RemoveMixerInput(voice);
                }
                pair.Value.Clear();
            }
            activeVoices.Clear();
            output?.Pause();
        }
    }

    /// <summary>Restarts the output device after <see cref="Suspend"/>.</summary>
    public void ResumeOutput()
    {
        lock (gate)
        {
            output?.Play();
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
