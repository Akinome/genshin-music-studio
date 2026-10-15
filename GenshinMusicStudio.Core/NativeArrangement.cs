namespace GenshinMusicStudio.Core;

/// <summary>
/// Cleans transcribed notes and extracts a single melodic line that fits the
/// standard 21-key Genshin lyre.
/// </summary>
public static class NativeArrangement
{
    /// <summary>Merges fragments and quantizes note boundaries to a fixed grid.</summary>
    /// <param name="notes">Raw transcribed notes.</param>
    /// <param name="gridMs">Quantization grid in milliseconds.</param>
    /// <param name="minimumMs">Minimum output note length in milliseconds.</param>
    /// <param name="mergeGapMs">Maximum gap allowed when merging short fragments.</param>
    /// <returns>Notes sorted by start time.</returns>
    public static List<NativeNote> OptimizeNotes(IReadOnlyList<NativeNote> notes, double gridMs = 50,
        double minimumMs = 30, double mergeGapMs = 15)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gridMs);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumMs);
        ArgumentOutOfRangeException.ThrowIfNegative(mergeGapMs);

        var grid = gridMs / 1000.0;
        var minimum = minimumMs / 1000.0;
        var mergeGap = mergeGapMs / 1000.0;
        var merged = new List<NativeNote>();

        // Merge short same-pitch fragments before quantization. Long notes are
        // kept separate because joining them would smear repeated attacks.
        foreach (var group in notes.GroupBy(note => note.Pitch))
        {
            NativeNote? current = null;
            foreach (var note in group.OrderBy(note => note.Start))
            {
                if (current is null)
                {
                    current = note;
                }
                else if (note.Start - current.End <= mergeGap && current.End - current.Start <= 0.06)
                {
                    current = current with
                    {
                        End = Math.Max(current.End, note.End),
                        Amplitude = Math.Max(current.Amplitude, note.Amplitude),
                    };
                }
                else
                {
                    merged.Add(current);
                    current = note;
                }
            }

            if (current is not null) merged.Add(current);
        }

        var optimized = new List<NativeNote>();
        foreach (var note in merged)
        {
            var start = Math.Round(note.Start / grid) * grid;
            var end = Math.Max(start + minimum, Math.Round(note.End / grid) * grid);
            if (end - start >= minimum)
            {
                optimized.Add(note with { Start = start, End = end });
            }
        }

        return optimized.OrderBy(note => note.Start).ToList();
    }

    /// <summary>Selects the most plausible monophonic melody and folds it to the lyre range.</summary>
    /// <param name="notes">Clean transcribed notes.</param>
    /// <param name="minGapMs">Minimum spacing between output note starts in milliseconds.</param>
    /// <returns>A melody suitable for a single-line Genshin instrument.</returns>
    public static List<NativeNote> ExtractMelody(IReadOnlyList<NativeNote> notes, double minGapMs = 60)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentOutOfRangeException.ThrowIfNegative(minGapMs);

        if (notes.Count == 0) return new List<NativeNote>();

        var melody = ViterbiMelody(notes);
        var minGap = minGapMs / 1000.0;
        var result = new List<NativeNote>();
        foreach (var note in melody.OrderBy(note => note.Start))
        {
            var pitch = FoldToGenshinRange(note.Pitch);
            if (result.Count > 0 && note.Start - result[^1].Start < minGap) continue;
            result.Add(note with { Pitch = pitch });
        }

        return result;
    }

    private static List<NativeNote> ViterbiMelody(IReadOnlyList<NativeNote> notes)
    {
        var ordered = notes.OrderBy(note => note.Start).ThenByDescending(note => note.Pitch).ToList();
        var groups = new List<List<NativeNote>>();
        foreach (var note in ordered)
        {
            if (groups.Count == 0 || note.Start - groups[^1][0].Start > 0.03)
            {
                groups.Add(new List<NativeNote> { note });
            }
            else
            {
                groups[^1].Add(note);
            }
        }

        // Each group is a vertical slice of the piano roll. Viterbi keeps the
        // strongest candidate while penalizing large leaps and octave jumps.
        var states = new List<List<(double Score, int Previous, NativeNote Note)>>();
        foreach (var group in groups)
        {
            var candidates = group
                .OrderByDescending(note => note.Pitch)
                .ThenByDescending(note => note.Amplitude)
                .Take(10)
                .ToList();
            var current = new List<(double Score, int Previous, NativeNote Note)>();
            var topPitch = candidates.Max(note => note.Pitch);
            var medianPitch = candidates.Select(note => note.Pitch).OrderBy(pitch => pitch).ElementAt(candidates.Count / 2);

            foreach (var note in candidates)
            {
                var registerPenalty = Math.Min(Math.Abs(note.Pitch - medianPitch) / 12.0, 1.0);
                var baseScore = 0.30 * (note.Pitch / 127.0)
                              + 0.25 * note.Amplitude
                              + 0.25 * Math.Min((note.End - note.Start) / 0.5, 1.0)
                              + 0.20 * (note.Pitch == topPitch ? 1 : 0)
                              - 0.15 * registerPenalty;

                if (states.Count == 0)
                {
                    current.Add((baseScore, -1, note));
                    continue;
                }

                var bestScore = double.MinValue;
                var bestPrevious = -1;
                for (var previousIndex = 0; previousIndex < states[^1].Count; previousIndex++)
                {
                    var previous = states[^1][previousIndex];
                    var leap = Math.Abs(note.Pitch - previous.Note.Pitch);
                    var continuity = Math.Max(0, 1 - leap / 12.0);
                    var repeatBonus = leap <= 2 ? 0.15 : 0.0;
                    var octavePenalty = leap >= 12 ? 0.25 : 0.0;
                    var score = previous.Score + baseScore + 0.45 * continuity + repeatBonus - octavePenalty;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPrevious = previousIndex;
                    }
                }

                current.Add((bestScore, bestPrevious, note));
            }

            states.Add(current);
        }

        var bestIndex = 0;
        for (var index = 1; index < states[^1].Count; index++)
        {
            if (states[^1][index].Score > states[^1][bestIndex].Score) bestIndex = index;
        }

        var melody = new List<NativeNote>();
        for (var group = states.Count - 1; group >= 0; group--)
        {
            var state = states[group][bestIndex];
            melody.Add(state.Note);
            bestIndex = state.Previous;
        }

        melody.Reverse();

        // Smooth one-note octave spikes while preserving the surrounding contour.
        for (var index = 1; index < melody.Count - 1; index++)
        {
            var previous = melody[index - 1];
            var current = melody[index];
            var following = melody[index + 1];
            if (current.Pitch - previous.Pitch >= 12 && following.Pitch - current.Pitch <= -10)
            {
                melody[index] = current with { Pitch = current.Pitch - 12 };
            }
            else if (previous.Pitch - current.Pitch >= 12 && following.Pitch - current.Pitch >= 10)
            {
                melody[index] = current with { Pitch = current.Pitch + 12 };
            }
        }

        return melody;
    }

    private static int FoldToGenshinRange(int pitch)
    {
        var value = pitch;
        while (value < Key21Layout.LowPitch) value += 12;
        while (value > Key21Layout.HighPitch) value -= 12;
        return value;
    }
}
