using NAudio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GenshinMusicStudio_WinUI.Services;

public sealed class InstrumentPlayer : IDisposable
{
    private const int TargetRate = 44100;

    private readonly object gate = new();
    private readonly Dictionary<int, float[]> samples = new();
    private WaveOutEvent? output;
    private MixingSampleProvider? mixer;
    private string currentInstrument = string.Empty;

    public string CurrentInstrument => currentInstrument;

    public void LoadInstrument(string folder)
    {
        lock (gate)
        {
            samples.Clear();
            currentInstrument = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
            foreach (var file in Directory.EnumerateFiles(folder, "*.mp3"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(name, out var key)) continue;
                if (key < 0 || key >= Key21Layout.KeyCount) continue;
                try
                {
                    samples[key] = LoadSampleBuffer(file);
                }
                catch
                {
                    // A broken sample should not break the whole instrument.
                }
            }
            EnsureOutput();
        }
    }

    public void PlayKey(int keyIndex, double gain)
    {
        lock (gate)
        {
            if (mixer is null || !samples.TryGetValue(keyIndex, out var buffer)) return;
            var source = new CachedSampleSource(buffer) { Gain = (float)Math.Clamp(gain, 0.05, 1.0) };
            mixer.AddMixerInput(source);
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
        output = new WaveOutEvent();
        output.Init(mixer);
        output.Play();
    }

    private static float[] LoadSampleBuffer(string file)
    {
        using var reader = new AudioFileReader(file);
        ISampleProvider source = reader;
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

        public WaveFormat WaveFormat { get; }
        public float Gain { get; set; } = 1f;

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
            for (var i = 0; i < toCopy; i++)
            {
                target[offset + i] = buffer[position + i] * Gain;
            }
            position += toCopy;
            return toCopy;
        }
    }
}
