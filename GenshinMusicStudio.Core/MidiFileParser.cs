namespace GenshinMusicStudio.Core;

/// <summary>A MIDI note produced by the parser.</summary>
public sealed record ParsedMidiNote(double Start, double End, int Pitch, int Velocity);

/// <summary>A note-on or note-off event with an absolute timestamp in seconds.</summary>
public sealed record MidiPlaybackEvent(double Time, bool IsOn, int Pitch, int Velocity);

/// <summary>Parsed events and duration for one MIDI file.</summary>
public sealed record MidiFileData(IReadOnlyList<MidiPlaybackEvent> Events, double Duration);

/// <summary>
/// Minimal SMF parser used by the player. It supports the format-0/format-1
/// features needed by the app: tempo changes, running status and note events.
/// </summary>
public static class MidiFileParser
{
    private const int LowNote = 48;
    private const int HighNote = 83;

    /// <summary>Parses all note-on/off events from a MIDI file and pairs them into notes.</summary>
    /// <param name="path">MIDI file path.</param>
    /// <returns>Completed notes and the file duration in seconds.</returns>
    public static (List<ParsedMidiNote> Notes, double Duration) ParseNotes(string path)
    {
        var (events, duration) = ParseFile(path);
        var notes = new List<ParsedMidiNote>();
        var active = new Dictionary<int, double>();

        foreach (var midiEvent in events)
        {
            if (midiEvent.IsOn)
            {
                if (active.TryGetValue(midiEvent.Pitch, out var previousStart))
                {
                    notes.Add(new ParsedMidiNote(previousStart, midiEvent.Time, midiEvent.Pitch, 80));
                }

                active[midiEvent.Pitch] = midiEvent.Time;
            }
            else if (active.TryGetValue(midiEvent.Pitch, out var start))
            {
                active.Remove(midiEvent.Pitch);
                if (midiEvent.Time - start > 0.001)
                {
                    notes.Add(new ParsedMidiNote(start, midiEvent.Time, midiEvent.Pitch, Math.Max(40, midiEvent.Velocity)));
                }
            }
        }

        // A malformed file may end before all note-offs arrive. Close the open
        // notes at a small tail so playback still terminates cleanly.
        foreach (var pair in active)
        {
            notes.Add(new ParsedMidiNote(pair.Value, Math.Max(duration, pair.Value + 0.2), pair.Key, 80));
        }

        notes.Sort((left, right) => left.Start.CompareTo(right.Start));
        return (notes, duration);
    }

    /// <summary>Reads and parses raw events from a MIDI file.</summary>
    /// <param name="path">MIDI file path.</param>
    /// <returns>Raw note events and the file duration in seconds.</returns>
    public static (List<MidiPlaybackEvent> Events, double Duration) ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllBytes(path));
    }

    internal static (List<MidiPlaybackEvent> Events, double Duration) Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var events = new List<MidiPlaybackEvent>();
        if (bytes.Length < 14 || bytes[0] != 'M' || bytes[1] != 'T' || bytes[2] != 'h' || bytes[3] != 'd')
        {
            throw new InvalidOperationException("不是有效的 MIDI 文件。");
        }

        var trackCount = ReadUInt16(bytes, 10);
        var division = ReadUInt16(bytes, 12);
        var ticksPerBeat = (division & 0x8000) != 0 ? 480 : division;
        if (ticksPerBeat == 0) ticksPerBeat = 480;

        var position = 14;
        var tempo = 500000;
        var maxTick = 0L;
        for (var track = 0; track < trackCount && position < bytes.Length - 8; track++)
        {
            if (bytes[position] != 'M' || bytes[position + 1] != 'T' ||
                bytes[position + 2] != 'r' || bytes[position + 3] != 'k')
            {
                break;
            }

            var length = ReadUInt32(bytes, position + 4);
            var end = position + 8 + (int)Math.Min(length, int.MaxValue - position - 8);
            position += 8;

            var tick = 0;
            var runningStatus = 0;
            while (position < end)
            {
                var delta = ReadVarLen(bytes, ref position, end);
                tick += delta;
                if (position >= end) break;

                var status = (int)bytes[position];
                if (status < 0x80)
                {
                    if (runningStatus == 0) break;
                    status = runningStatus;
                }
                else
                {
                    position++;
                    runningStatus = status < 0xF0 ? status : 0;
                }

                var kind = status & 0xF0;
                if (status == 0xFF)
                {
                    if (position >= end) break;
                    var metaType = bytes[position++];
                    var metaLength = ReadVarLen(bytes, ref position, end);
                    if (metaType == 0x51 && metaLength == 3 && position + 2 < end)
                    {
                        tempo = (bytes[position] << 16) | (bytes[position + 1] << 8) | bytes[position + 2];
                    }

                    position += metaLength;
                }
                else if (status is 0xF0 or 0xF7)
                {
                    var sysexLength = ReadVarLen(bytes, ref position, end);
                    position += sysexLength;
                }
                else if (kind is 0x90 or 0x80)
                {
                    if (position + 1 >= end) break;
                    var note = bytes[position] & 0x7F;
                    var velocity = bytes[position + 1];
                    position += 2;
                    if (kind == 0x90 && velocity > 0)
                    {
                        events.Add(new MidiPlaybackEvent(TicksToSeconds(tick, ticksPerBeat, tempo), true, note, velocity));
                    }
                    else
                    {
                        events.Add(new MidiPlaybackEvent(TicksToSeconds(tick, ticksPerBeat, tempo), false, note, 0));
                    }
                }
                else if (kind is 0xC0 or 0xD0)
                {
                    position += 1;
                }
                else
                {
                    position += 2;
                }
            }

            maxTick = Math.Max(maxTick, tick);
            position = end;
        }

        // Keep the original file order for equal timestamps. In particular,
        // a note-off may need to stay immediately before a note-on on the same
        // key so the transport sees a retrigger instead of a duplicate press.
        events = events.OrderBy(item => item.Time).ToList();
        var duration = Math.Max(
            events.Count > 0 ? events[^1].Time : 0,
            TicksToSeconds(maxTick, ticksPerBeat, tempo));
        return (events, duration);
    }

    internal static double TicksToSeconds(long tick, int ticksPerBeat, int tempo)
    {
        if (ticksPerBeat <= 0) return 0;
        return tick * tempo / (ticksPerBeat * 1_000_000.0);
    }

    internal static int ReadUInt16(byte[] data, int offset)
    {
        if (offset + 1 >= data.Length) return 0;
        return (data[offset] << 8) | data[offset + 1];
    }

    internal static uint ReadUInt32(byte[] data, int offset)
    {
        if (offset + 3 >= data.Length) return 0;
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
    }

    internal static int ReadVarLen(byte[] data, ref int position, int end)
    {
        var value = 0;
        for (var index = 0; index < 4 && position < end; index++)
        {
            var current = data[position++];
            value = (value << 7) | (current & 0x7F);
            if ((current & 0x80) == 0) break;
        }

        return value;
    }
}

/// <summary>Helpers for folding and boosting raw MIDI short messages.</summary>
public static class MidiMessage
{
    /// <summary>Folds note messages into the standard 21-key range.</summary>
    /// <param name="message">Packed MIDI short message.</param>
    /// <returns>The folded message, or the original message for non-note statuses.</returns>
    public static int Fold(int message)
    {
        var status = message & 0xF0;
        if (status is not (0x90 or 0x80)) return message;

        var note = (message >> 8) & 0x7F;
        while (note < Key21Layout.LowPitch) note += 12;
        while (note > Key21Layout.HighPitch) note -= 12;

        if (status == 0x90)
        {
            var velocity = (message >> 16) & 0x7F;
            return 0x90 | (note << 8) | (velocity << 16);
        }

        return 0x80 | (note << 8);
    }

    /// <summary>Scales note-on velocity while preserving the status and note number.</summary>
    /// <param name="message">Packed MIDI short message.</param>
    /// <param name="boost">Velocity multiplier.</param>
    /// <returns>A note-on message with velocity clamped to the MIDI range.</returns>
    public static int ScaleVelocity(int message, double boost)
    {
        if ((message & 0xF0) != 0x90 || boost <= 1.0) return message;
        var velocity = (message >> 16) & 0x7F;
        var scaled = (int)Math.Min(127, Math.Max(1, Math.Round(velocity * boost)));
        return (message & 0xFE00FF) | (scaled << 16);
    }
}

/// <summary>Combines parsed files into the event stream used by the player.</summary>
public static class MidiPlaylistBuilder
{
    /// <summary>Builds an absolute-time event stream and records each file's range.</summary>
    /// <param name="files">Parsed MIDI files in playlist order.</param>
    /// <param name="ranges">Receives the start time and duration of each file.</param>
    /// <returns>Events sorted by absolute time.</returns>
    public static List<MidiPlaybackEvent> Build(
        IReadOnlyList<MidiFileData> files,
        ICollection<(double Start, double Duration)> ranges)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(ranges);

        var events = new List<MidiPlaybackEvent>();
        var offset = 0.0;
        foreach (var file in files)
        {
            ranges.Add((offset, file.Duration));
            foreach (var midiEvent in file.Events)
            {
                events.Add(midiEvent with { Time = offset + midiEvent.Time });
            }

            offset += file.Duration + 0.6;
        }

        events = events.OrderBy(item => item.Time).ToList();
        return events;
    }
}
