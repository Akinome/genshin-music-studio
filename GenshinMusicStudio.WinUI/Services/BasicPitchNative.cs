using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace GenshinMusicStudio_WinUI.Services;

public sealed record NativeNote(double Start, double End, int Pitch, double Amplitude);

public sealed class BasicPitchNative : IDisposable
{
    private const int SampleRate = 22050;
    private const int FftHop = 256;
    private const int AudioNSamples = 43844;
    private const int OverlapFrames = 30;
    private const int OverlapLength = OverlapFrames * FftHop;
    private const int HopSize = AudioNSamples - OverlapLength;
    private const int AnnotationFrames = 172;
    private const int AnnotationNoteBins = 88;
    private const int AnnotationFps = SampleRate / FftHop;
    private const int MinMidiPitch = 21;
    private const double AnnotationsBaseFrequency = 27.5;

    private readonly InferenceSession session;
    private readonly string inputName;
    private readonly string noteOutputName;
    private readonly string onsetOutputName;

    public BasicPitchNative(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("找不到 Basic Pitch ONNX 模型", modelPath);
        }
        var options = new SessionOptions
        {
            InterOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
        };
        session = new InferenceSession(modelPath, options);
        inputName = session.InputMetadata.Keys.First();
        noteOutputName = "StatefulPartitionedCall:1";
        onsetOutputName = "StatefulPartitionedCall:2";
    }

    public async Task<List<NativeNote>> TranscribeAsync(string audioPath, double onsetThreshold = 0.5,
        double frameThreshold = 0.3, double minimumNoteLengthMs = 127.7, CancellationToken cancellationToken = default)
    {
        var audio = await NativeAudioTools.DecodeToMono22050Async(audioPath, cancellationToken);
        return await Task.Run(
            () => Transcribe(audio, onsetThreshold, frameThreshold, minimumNoteLengthMs, cancellationToken),
            cancellationToken);
    }

    private List<NativeNote> Transcribe(float[] audio, double onsetThreshold, double frameThreshold,
        double minimumNoteLengthMs, CancellationToken cancellationToken)
    {
        var padded = new float[OverlapLength / 2 + audio.Length];
        Array.Copy(audio, 0, padded, OverlapLength / 2, audio.Length);
        var windows = new List<float[]>();
        for (var offset = 0; offset < padded.Length; offset += HopSize)
        {
            var window = new float[AudioNSamples];
            var count = Math.Min(AudioNSamples, padded.Length - offset);
            if (count > 0)
            {
                Array.Copy(padded, offset, window, 0, count);
            }
            windows.Add(window);
        }

        var framesPerWindow = AnnotationFrames - OverlapFrames;
        var noteFrames = new List<float[,]>();
        var onsetFrames = new List<float[,]>();
        foreach (var window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = new DenseTensor<float>(new[] { 1, AudioNSamples, 1 });
            for (var i = 0; i < AudioNSamples; i++)
            {
                input[0, i, 0] = window[i];
            }

            using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, input) });
            var note = results.First(x => x.Name == noteOutputName).AsTensor<float>();
            var onset = results.First(x => x.Name == onsetOutputName).AsTensor<float>();
            noteFrames.Add(CopyMiddleFrames(note));
            onsetFrames.Add(CopyMiddleFrames(onset));
        }

        var totalFrames = windows.Count * framesPerWindow;
        var notes = new float[totalFrames, AnnotationNoteBins];
        var onsets = new float[totalFrames, AnnotationNoteBins];
        for (var w = 0; w < windows.Count; w++)
        {
            for (var t = 0; t < framesPerWindow; t++)
            {
                for (var f = 0; f < AnnotationNoteBins; f++)
                {
                    notes[w * framesPerWindow + t, f] = noteFrames[w][t, f];
                    onsets[w * framesPerWindow + t, f] = onsetFrames[w][t, f];
                }
            }
        }

        var outputFrames = (int)Math.Floor(audio.Length * (double)AnnotationFps / SampleRate);
        outputFrames = Math.Min(outputFrames, totalFrames);
        if (outputFrames <= 0)
        {
            return new List<NativeNote>();
        }

        var clippedNotes = new float[outputFrames, AnnotationNoteBins];
        var clippedOnsets = new float[outputFrames, AnnotationNoteBins];
        for (var t = 0; t < outputFrames; t++)
        {
            for (var f = 0; f < AnnotationNoteBins; f++)
            {
                clippedNotes[t, f] = notes[t, f];
                clippedOnsets[t, f] = onsets[t, f];
            }
        }

        var minNoteLengthFrames = (int)Math.Round(minimumNoteLengthMs / 1000.0 * (SampleRate / (double)FftHop));
        return DecodeNotes(clippedNotes, clippedOnsets, onsetThreshold, frameThreshold, minNoteLengthFrames, outputFrames);
    }

    private static float[,] CopyMiddleFrames(Tensor<float> tensor)
    {
        var frames = tensor.Dimensions[1];
        var bins = tensor.Dimensions[2];
        var kept = frames - OverlapFrames;
        var result = new float[kept, bins];
        for (var t = 0; t < kept; t++)
        {
            for (var f = 0; f < bins; f++)
            {
                result[t, f] = tensor[0, t + OverlapFrames / 2, f];
            }
        }
        return result;
    }

    private List<NativeNote> DecodeNotes(float[,] frames, float[,] onsets, double onsetThreshold,
        double frameThreshold, int minNoteLengthFrames, int nFrames)
    {
        onsets = InferOnsets(onsets, frames);
        var peaks = new float[nFrames, AnnotationNoteBins];
        for (var t = 1; t < nFrames - 1; t++)
        {
            for (var f = 0; f < AnnotationNoteBins; f++)
            {
                if (onsets[t, f] > onsets[t - 1, f] && onsets[t, f] > onsets[t + 1, f] && onsets[t, f] >= onsetThreshold)
                {
                    peaks[t, f] = onsets[t, f];
                }
            }
        }

        var candidates = new List<(int Time, int Frequency)>();
        for (var t = nFrames - 2; t >= 0; t--)
        {
            for (var f = 0; f < AnnotationNoteBins; f++)
            {
                if (peaks[t, f] >= onsetThreshold)
                {
                    candidates.Add((t, f));
                }
            }
        }

        var remaining = (float[,])frames.Clone();
        var result = new List<(int Start, int End, int Pitch, double Amplitude)>();
        const int energyTolerance = 11;
        foreach (var (start, frequency) in candidates)
        {
            if (start >= nFrames - 1) continue;
            var i = start + 1;
            var below = 0;
            while (i < nFrames - 1 && below < energyTolerance)
            {
                if (remaining[i, frequency] < frameThreshold) below++;
                else below = 0;
                i++;
            }
            i -= below;
            if (i - start <= minNoteLengthFrames) continue;
            ZeroNeighbors(remaining, start, i, frequency);
            result.Add((start, i, frequency + MinMidiPitch, Mean(frames, start, i, frequency)));
        }

        while (Max(remaining) > frameThreshold)
        {
            var (mid, frequency) = FindMax(remaining);
            remaining[mid, frequency] = 0;
            var i = mid + 1;
            var below = 0;
            while (i < nFrames - 1 && below < energyTolerance)
            {
                if (remaining[i, frequency] < frameThreshold) below++;
                else below = 0;
                ZeroAt(remaining, i, frequency);
                i++;
            }
            var end = i - 1 - below;
            i = mid - 1;
            below = 0;
            while (i > 0 && below < energyTolerance)
            {
                if (remaining[i, frequency] < frameThreshold) below++;
                else below = 0;
                ZeroAt(remaining, i, frequency);
                i--;
            }
            var start = i + 1 + below;
            if (end - start <= minNoteLengthFrames) continue;
            result.Add((start, end, frequency + MinMidiPitch, Mean(frames, start, end, frequency)));
        }

        var times = ModelFramesToTime(nFrames);
        return result.Select(x => new NativeNote(
            times[Math.Clamp(x.Start, 0, nFrames - 1)],
            times[Math.Clamp(x.End, 0, nFrames - 1)],
            x.Pitch,
            x.Amplitude)).ToList();
    }

    private static float[,] InferOnsets(float[,] onsets, float[,] frames)
    {
        var nFrames = onsets.GetLength(0);
        var bins = onsets.GetLength(1);
        var diff = new float[nFrames, bins];
        for (var t = 0; t < nFrames; t++)
        {
            for (var f = 0; f < bins; f++) diff[t, f] = float.MaxValue;
        }
        for (var n = 1; n <= 2; n++)
        {
            for (var t = n; t < nFrames; t++)
            {
                for (var f = 0; f < bins; f++)
                {
                    var value = frames[t, f] - frames[t - n, f];
                    if (value < 0) value = 0;
                    diff[t, f] = Math.Min(diff[t, f], value);
                }
            }
        }
        var maxOnset = Max(onsets);
        var maxDiff = 0f;
        for (var t = 0; t < nFrames; t++)
        {
            for (var f = 0; f < bins; f++)
            {
                if (diff[t, f] == float.MaxValue) diff[t, f] = 0;
                maxDiff = Math.Max(maxDiff, diff[t, f]);
            }
        }
        var scale = maxDiff > 0 ? maxOnset / maxDiff : 0;
        var result = new float[nFrames, bins];
        for (var t = 0; t < nFrames; t++)
        {
            for (var f = 0; f < bins; f++)
            {
                result[t, f] = Math.Max(onsets[t, f], diff[t, f] * scale);
            }
        }
        return result;
    }

    private static void ZeroNeighbors(float[,] values, int start, int end, int frequency)
    {
        for (var t = start; t < end; t++)
        {
            ZeroAt(values, t, frequency);
            if (frequency > 0) values[t, frequency - 1] = 0;
            if (frequency + 1 < AnnotationNoteBins) values[t, frequency + 1] = 0;
        }
    }

    private static void ZeroAt(float[,] values, int time, int frequency)
    {
        if (time >= 0 && time < values.GetLength(0) && frequency >= 0 && frequency < values.GetLength(1))
        {
            values[time, frequency] = 0;
        }
    }

    private static float Mean(float[,] values, int start, int end, int frequency)
    {
        var sum = 0f;
        var count = Math.Max(1, end - start);
        for (var t = start; t < end; t++) sum += values[t, frequency];
        return sum / count;
    }

    private static float Max(float[,] values)
    {
        var max = float.MinValue;
        foreach (var value in values) max = Math.Max(max, value);
        return max;
    }

    private static (int Time, int Frequency) FindMax(float[,] values)
    {
        var bestTime = 0;
        var bestFrequency = 0;
        var best = float.MinValue;
        for (var t = 0; t < values.GetLength(0); t++)
        {
            for (var f = 0; f < values.GetLength(1); f++)
            {
                if (values[t, f] > best)
                {
                    best = values[t, f];
                    bestTime = t;
                    bestFrequency = f;
                }
            }
        }
        return (bestTime, bestFrequency);
    }

    private static double[] ModelFramesToTime(int nFrames)
    {
        var times = new double[nFrames];
        var annotationNFrames = AnnotationFps * 2;
        var windowOffset = (FftHop / (double)SampleRate) * (annotationNFrames - AudioNSamples / (double)FftHop) + 0.0018;
        for (var t = 0; t < nFrames; t++)
        {
            var original = t * FftHop / (double)SampleRate;
            var windowNumber = Math.Floor(t / (double)annotationNFrames);
            times[t] = original - windowOffset * windowNumber;
        }
        return times;
    }

    public void Dispose() => session.Dispose();
}
