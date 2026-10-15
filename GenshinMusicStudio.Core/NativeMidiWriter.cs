namespace GenshinMusicStudio.Core;

/// <summary>Writes a small, deterministic Standard MIDI File for native results.</summary>
public static class NativeMidiWriter
{
    private const int TicksPerBeat = 480;
    private const int Tempo = 500000;
    private static readonly double TicksPerSecond = TicksPerBeat * 1_000_000.0 / Tempo;

    /// <summary>Writes a type-0 MIDI file containing the supplied monophonic-friendly notes.</summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="notes">Notes to write.</param>
    /// <param name="totalSeconds">Minimum total duration of the output file.</param>
    public static void WriteNotes(string path, IReadOnlyList<NativeNote> notes, double totalSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(notes);

        var events = new List<(long Tick, int Order, byte[] Data)>();
        var ordered = notes.OrderBy(note => note.Start).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var note = ordered[index];
            var onTick = Math.Max(0, (long)Math.Round(note.Start * TicksPerSecond));
            var end = Math.Max(note.End, note.Start + 0.08);
            var offTick = Math.Max(onTick + 1, (long)Math.Round(end * TicksPerSecond));
            if (index + 1 < ordered.Count)
            {
                var nextTick = Math.Max(onTick + 1, (long)Math.Round(ordered[index + 1].Start * TicksPerSecond));
                offTick = Math.Min(offTick, nextTick - 1);
            }

            var velocity = (byte)Math.Clamp((int)Math.Round(note.Amplitude * 127), 1, 127);
            var pitch = (byte)Math.Clamp(note.Pitch, 0, 127);
            events.Add((onTick, 2, new byte[] { 0x90, pitch, velocity }));
            events.Add((offTick, 1, new byte[] { 0x80, pitch, 0x40 }));
        }

        var lastEnd = ordered.Count == 0 ? 0 : ordered.Max(note => note.End);
        var duration = Math.Max(totalSeconds, lastEnd);
        var totalTicks = Math.Max(0, (long)Math.Round(duration * TicksPerSecond));

        // Note-offs sort before note-ons at the same tick so repeated notes retrigger.
        events.Sort((left, right) => left.Tick == right.Tick
            ? left.Order.CompareTo(right.Order)
            : left.Tick.CompareTo(right.Tick));

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
        var remaining = Math.Max(0, value);
        buffer[count++] = (byte)(remaining & 0x7F);
        while ((remaining >>= 7) > 0)
        {
            buffer[count++] = (byte)((remaining & 0x7F) | 0x80);
        }

        for (var index = count - 1; index >= 0; index--) stream.WriteByte(buffer[index]);
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
