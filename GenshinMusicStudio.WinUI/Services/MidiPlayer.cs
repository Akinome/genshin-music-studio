using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GenshinMusicStudio_WinUI.Services;

public sealed class MidiPlayer : IDisposable
{
    private const int LowNote = 48;
    private const int HighNote = 83;
    private const int HarpProgram = 46;

    [DllImport("winmm.dll")]
    private static extern int midiOutOpen(out IntPtr handle, int device, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int midiOutClose(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int midiOutShortMsg(IntPtr handle, int message);

    private sealed class MidiEvent
    {
        public double Time;
        public int Message;
    }

    private readonly object gate = new();
    private Thread? thread;
    private volatile bool stopRequested;
    private IntPtr handle;
    private bool opened;
    private IReadOnlyList<string> playlist = Array.Empty<string>();
    private List<(double Start, double Duration)> fileRanges = new();

    public event Action<bool>? PlayingChanged;
    public event Action<string, double, double>? ProgressChanged;

    public bool IsPlaying => thread is { IsAlive: true } && !stopRequested;
    public string? PlayingFolder { get; private set; }

    public void Play(IReadOnlyList<string> files, string? folder = null)
    {
        if (files.Count == 0) return;
        Stop();
        PlayingFolder = folder;
        stopRequested = false;
        playlist = files;
        thread = new Thread(PlayLoop) { IsBackground = true };
        thread.Start();
        PlayingChanged?.Invoke(true);
    }

    public void Stop()
    {
        stopRequested = true;
        var current = thread;
        if (current is { IsAlive: true } && current != Thread.CurrentThread)
        {
            current.Join(1500);
        }
        PlayingFolder = null;
        PlayingChanged?.Invoke(false);
    }

    public void Dispose() => Stop();

    private void PlayLoop()
    {
        try
        {
            if (midiOutOpen(out handle, 0, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            {
                opened = false;
                throw new InvalidOperationException("无法打开 Windows MIDI 合成器。");
            }
            opened = true;
            midiOutShortMsg(handle, 0xC0 | HarpProgram);

            fileRanges = new List<(double Start, double Duration)>();
            var events = BuildPlaylistEvents(playlist, fileRanges);
            var clock = Stopwatch.StartNew();
            var total = events.Count > 0 ? events[^1].Time : 0;
            var lastReport = -1.0;
            foreach (var evt in events)
            {
                if (stopRequested) return;
                var wait = evt.Time - clock.Elapsed.TotalSeconds;
                if (wait > 0) Thread.Sleep((int)Math.Min(wait * 1000, 500));
                if (stopRequested) return;
                if (evt.Time - lastReport >= 0.25)
                {
                    lastReport = evt.Time;
                    ReportProgress(evt.Time, total);
                }
                midiOutShortMsg(handle, evt.Message);
            }

            var tail = total + 0.8 - clock.Elapsed.TotalSeconds;
            while (tail > 0 && !stopRequested)
            {
                ReportProgress(total, total);
                Thread.Sleep(200);
                tail = total + 0.8 - clock.Elapsed.TotalSeconds;
            }
        }
        finally
        {
            lock (gate)
            {
                if (opened)
                {
                    for (var note = 0; note < 128; note++)
                    {
                        midiOutShortMsg(handle, 0x80 | note);
                    }
                    midiOutClose(handle);
                    opened = false;
                }
            }
            PlayingFolder = null;
            PlayingChanged?.Invoke(false);
        }
    }

    private void ReportProgress(double position, double duration)
    {
        var name = string.Empty;
        for (var i = fileRanges.Count - 1; i >= 0; i--)
        {
            if (position >= fileRanges[i].Start)
            {
                name = i < playlist.Count ? playlist[i] : string.Empty;
                break;
            }
        }
        ProgressChanged?.Invoke(name, position, duration);
    }

    private static List<MidiEvent> BuildPlaylistEvents(IReadOnlyList<string> files, List<(double Start, double Duration)> ranges)
    {
        var events = new List<MidiEvent>();
        var offset = 0.0;
        foreach (var file in files)
        {
            var (fileEvents, duration) = ParseMidiFile(file);
            ranges.Add((offset, duration));
            foreach (var (time, message) in fileEvents)
            {
                events.Add(new MidiEvent { Time = offset + time, Message = FoldMessage(message) });
            }
            offset += duration + 0.6;
        }
        events.Sort((a, b) => a.Time.CompareTo(b.Time));
        return events;
    }

    private static int FoldMessage(int message)
    {
        var status = message & 0xF0;
        if (status is not (0x90 or 0x80)) return message;
        var note = (message >> 8) & 0x7F;
        while (note < LowNote) note += 12;
        while (note > HighNote) note -= 12;
        return (message & 0xFF00FF) | (note << 8);
    }

    private static (List<(double Time, int Message)> Events, double Duration) ParseMidiFile(string path)
    {
        var events = new List<(double Time, int Message)>();
        var bytes = File.ReadAllBytes(path);
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
        for (var track = 0; track < trackCount && position < bytes.Length - 8; track++)
        {
            if (bytes[position] != 'M' || bytes[position + 1] != 'T' || bytes[position + 2] != 'r' || bytes[position + 3] != 'k')
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
                    if (status < 0xF0) runningStatus = status;
                    else runningStatus = 0;
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
                        events.Add((TicksToSeconds(tick, ticksPerBeat, tempo), 0x90 | note | (velocity << 8)));
                    }
                    else
                    {
                        events.Add((TicksToSeconds(tick, ticksPerBeat, tempo), 0x80 | note));
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
            position = end;
        }

        events.Sort((a, b) => a.Time.CompareTo(b.Time));
        var duration = events.Count > 0 ? events[^1].Time : 0;
        return (events, duration);
    }

    private static double TicksToSeconds(long tick, int ticksPerBeat, int tempo)
    {
        if (ticksPerBeat <= 0) return 0;
        return tick * tempo / (ticksPerBeat * 1_000_000.0);
    }

    private static int ReadUInt16(byte[] data, int offset)
    {
        if (offset + 1 >= data.Length) return 0;
        return (data[offset] << 8) | data[offset + 1];
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        if (offset + 3 >= data.Length) return 0;
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
    }

    private static int ReadVarLen(byte[] data, ref int position, int end)
    {
        var value = 0;
        for (var i = 0; i < 4 && position < end; i++)
        {
            var b = data[position++];
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) == 0) break;
        }
        return value;
    }
}
