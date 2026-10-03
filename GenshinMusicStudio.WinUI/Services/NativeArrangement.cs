namespace GenshinMusicStudio_WinUI.Services;

public static class NativeArrangement
{
    public static List<NativeNote> OptimizeNotes(IReadOnlyList<NativeNote> notes, double gridMs = 50,
        double minimumMs = 30, double mergeGapMs = 15)
    {
        var grid = gridMs / 1000.0;
        var minimum = minimumMs / 1000.0;
        var mergeGap = mergeGapMs / 1000.0;
        var merged = new List<NativeNote>();
        foreach (var group in notes.GroupBy(n => n.Pitch))
        {
            NativeNote? current = null;
            foreach (var note in group.OrderBy(n => n.Start))
            {
                if (current is null)
                {
                    current = note;
                }
                else if (note.Start - current.End <= mergeGap && current.End - current.Start <= 0.06)
                {
                    current = current with { End = Math.Max(current.End, note.End), Amplitude = Math.Max(current.Amplitude, note.Amplitude) };
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
        return optimized.OrderBy(n => n.Start).ToList();
    }

    public static List<NativeNote> ExtractMelody(IReadOnlyList<NativeNote> notes, double minGapMs = 60)
    {
        if (notes.Count == 0) return new List<NativeNote>();
        var melody = ViterbiMelody(notes);
        var minGap = minGapMs / 1000.0;
        var result = new List<NativeNote>();
        foreach (var note in melody.OrderBy(n => n.Start))
        {
            var pitch = FoldToGenshinRange(note.Pitch);
            if (pitch is null) continue;
            if (result.Count > 0 && note.Start - result[^1].Start < minGap) continue;
            result.Add(note with { Pitch = pitch.Value });
        }
        return result;
    }

    private static List<NativeNote> ViterbiMelody(IReadOnlyList<NativeNote> notes)
    {
        var ordered = notes.OrderBy(n => n.Start).ThenByDescending(n => n.Pitch).ToList();
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

        var states = new List<List<(double Score, int Previous, NativeNote Note)>>();
        foreach (var group in groups)
        {
            var candidates = group.OrderByDescending(n => n.Pitch).ThenByDescending(n => n.Amplitude).Take(10).ToList();
            var current = new List<(double Score, int Previous, NativeNote Note)>();
            var topPitch = candidates.Max(n => n.Pitch);
            var medianPitch = candidates.Select(n => n.Pitch).OrderBy(p => p).ElementAt(candidates.Count / 2);
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
                for (var p = 0; p < states[^1].Count; p++)
                {
                    var previous = states[^1][p];
                    var leap = Math.Abs(note.Pitch - previous.Note.Pitch);
                    var continuity = Math.Max(0, 1 - leap / 12.0);
                    var repeatBonus = leap <= 2 ? 0.15 : 0.0;
                    var octavePenalty = leap >= 12 ? 0.25 : 0.0;
                    var score = previous.Score + baseScore + 0.45 * continuity + repeatBonus - octavePenalty;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPrevious = p;
                    }
                }
                current.Add((bestScore, bestPrevious, note));
            }
            states.Add(current);
        }

        var bestIndex = 0;
        for (var i = 1; i < states[^1].Count; i++)
        {
            if (states[^1][i].Score > states[^1][bestIndex].Score) bestIndex = i;
        }
        var melody = new List<NativeNote>();
        for (var group = states.Count - 1; group >= 0; group--)
        {
            var state = states[group][bestIndex];
            melody.Add(state.Note);
            bestIndex = state.Previous;
        }
        melody.Reverse();
        for (var i = 1; i < melody.Count - 1; i++)
        {
            var previous = melody[i - 1];
            var current = melody[i];
            var following = melody[i + 1];
            if (current.Pitch - previous.Pitch >= 12 && following.Pitch - current.Pitch <= -10)
            {
                melody[i] = current with { Pitch = current.Pitch - 12 };
            }
            else if (previous.Pitch - current.Pitch >= 12 && following.Pitch - current.Pitch >= 10)
            {
                melody[i] = current with { Pitch = current.Pitch + 12 };
            }
        }
        return melody;
    }

    private static int? FoldToGenshinRange(int pitch)
    {
        var value = pitch;
        while (value < 48) value += 12;
        while (value > 83) value -= 12;
        return value is >= 48 and <= 83 ? value : null;
    }
}

public static class NativeMidiWriter
{
    private const int TicksPerBeat = 480;
    private const int Tempo = 500000;
    private static readonly double TicksPerSecond = TicksPerBeat * 1_000_000.0 / Tempo;

    public static void WriteNotes(string path, IReadOnlyList<NativeNote> notes, double totalSeconds)
    {
        var events = new List<(long Tick, int Order, byte[] Data)>();
        var ordered = notes.OrderBy(n => n.Start).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var note = ordered[i];
            var onTick = Math.Max(0, (long)Math.Round(note.Start * TicksPerSecond));
            var end = Math.Max(note.End, note.Start + 0.08);
            var offTick = Math.Max(onTick + 1, (long)Math.Round(end * TicksPerSecond));
            if (i + 1 < ordered.Count)
            {
                var nextTick = Math.Max(onTick + 1, (long)Math.Round(ordered[i + 1].Start * TicksPerSecond));
                offTick = Math.Min(offTick, nextTick - 1);
            }
            var velocity = (byte)Math.Clamp((int)Math.Round(note.Amplitude * 127), 1, 127);
            var pitch = (byte)Math.Clamp(note.Pitch, 0, 127);
            events.Add((onTick, 2, new byte[] { 0x90, pitch, velocity }));
            events.Add((offTick, 1, new byte[] { 0x80, pitch, 0x40 }));
        }

        var totalTicks = Math.Max(1, (long)Math.Round(Math.Max(totalSeconds, ordered.LastOrDefault()?.End ?? 0) * TicksPerSecond));
        events.Sort((a, b) => a.Tick == b.Tick ? a.Order.CompareTo(b.Order) : a.Tick.CompareTo(b.Tick));
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        WriteBytes(file, new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0 });
        using var track = new MemoryStream();
        WriteVarLen(track, 0);
        WriteBytes(track, new byte[] { 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20 });
        var previousTick = 0L;
        foreach (var item in events)
        {
            WriteVarLen(track, item.Tick - previousTick);
            WriteBytes(track, item.Data);
            previousTick = item.Tick;
        }
        WriteVarLen(track, Math.Max(0, totalTicks - previousTick));
        WriteBytes(track, new byte[] { 0xFF, 0x2F, 0x00 });
        var trackBytes = track.ToArray();
        WriteBytes(file, new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });
        WriteUInt32BigEndian(file, (uint)trackBytes.Length);
        file.Write(trackBytes);
    }

    private static void WriteVarLen(Stream stream, long value)
    {
        var buffer = new byte[4];
        var count = 0;
        var v = Math.Max(0, value);
        buffer[count++] = (byte)(v & 0x7F);
        while ((v >>= 7) > 0)
        {
            buffer[count++] = (byte)((v & 0x7F) | 0x80);
        }
        for (var i = count - 1; i >= 0; i--) stream.WriteByte(buffer[i]);
    }

    private static void WriteBytes(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
