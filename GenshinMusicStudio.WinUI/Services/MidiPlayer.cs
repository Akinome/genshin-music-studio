using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GenshinMusicStudio_WinUI.Services;

public sealed class MidiPlayer : IDisposable
{
    private const int HarpProgram = 46;

    [DllImport("winmm.dll")]
    private static extern int midiOutOpen(out IntPtr handle, int device, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int midiOutClose(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int midiOutShortMsg(IntPtr handle, int message);

    [DllImport("winmm.dll")]
    private static extern int midiOutSetVolume(IntPtr handle, uint volume);

    public sealed record MidiNote(double Start, double End, int Pitch, int Velocity);

    private readonly object gate = new();
    private Thread? thread;
    private volatile bool stopRequested;
    private IntPtr handle;
    private bool opened;
    private IReadOnlyList<string> playlist = Array.Empty<string>();
    private double volume = 0.9;
    private double velocityBoost = 1.5;
    private List<(double Start, double Duration)> fileRanges = new();
    private Stopwatch? playbackClock;
    private double pausedPosition;
    private double playbackStart;
    private double currentDuration;
    private long instrumentSequence;
    private int playbackIndex;

    public event Action<bool>? PlayingChanged;
    public event Action<string, double, double>? ProgressChanged;
    public event Action<string>? PlaybackError;

    public bool IsPlaying => thread is { IsAlive: true } && !stopRequested;
    public bool IsPaused { get; private set; }
    public string? PlayingFolder { get; private set; }
    public string? CurrentFile { get; private set; }
    public double CurrentDuration => currentDuration;

    /// <summary>
    /// True once a selection has been applied, including the Windows synth.
    /// A null <see cref="Instrument"/> alone cannot distinguish "picked the
    /// Windows synth" from "nothing selected yet".
    /// </summary>
    public bool HasSelection { get; private set; }

    public InstrumentPlayer? Instrument { get; set; }

    /// <summary>Receives lyre keystrokes so a sheet can be played inside the game.</summary>
    public GameKeySender? GameOutput { get; set; }

    /// <summary>When true, note-ons are also sent to the game window.</summary>
    public bool PlayInGame { get; set; }

    /// <summary>
    /// Applies an instrument selection only when it is still the newest one.
    /// A null instrument means the Windows MIDI synth; it still counts as a
    /// selection so an older background load cannot override it.
    /// </summary>
    public void SetInstrument(InstrumentPlayer? instrument, long sequence)
    {
        lock (gate)
        {
            if (sequence < instrumentSequence) return;
            instrumentSequence = sequence;
            Instrument = instrument;
            HasSelection = true;
        }
    }

    public double CurrentPosition => playbackClock is null
        ? (IsPaused ? pausedPosition : playbackStart)
        : playbackStart + playbackClock.Elapsed.TotalSeconds;

    public double Volume
    {
        get => volume;
        set
        {
            volume = Math.Clamp(value, 0.0, 1.0);
            ApplyVolume();
        }
    }

    public double VelocityBoost
    {
        get => velocityBoost;
        set => velocityBoost = Math.Clamp(value, 0.5, 4.0);
    }

    private void ApplyVolume()
    {
        var level = (uint)Math.Round(volume * 0xFFFF);
        var both = level | (level << 16);
        lock (gate)
        {
            if (opened)
            {
                midiOutSetVolume(handle, both);
            }
        }
    }

    public static (List<MidiNote> Notes, double Duration) ParseNotes(string path)
    {
        // Keep the existing nested model for the visualizers while the actual
        // parsing and pairing logic lives in the testable core library.
        var (parsedNotes, duration) = MidiFileParser.ParseNotes(path);
        var notes = parsedNotes
            .Select(note => new MidiNote(note.Start, note.End, note.Pitch, note.Velocity))
            .ToList();
        return (notes, duration);
    }

    public void Play(IReadOnlyList<string> files, string? folder = null, double startAt = 0)
    {
        if (files.Count == 0) return;
        Stop();
        CloseSynthesizer();
        PlayingFolder = folder;
        CurrentFile = files.Count > 0 ? files[0] : null;
        stopRequested = false;
        playlist = files;
        playbackStart = Math.Max(0, startAt);
        playbackIndex = 0;
        currentDuration = 0;
        thread = new Thread(PlayLoop) { IsBackground = true };
        thread.Start();
        PlayingChanged?.Invoke(true);
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        pausedPosition = CurrentPosition;
        LogPlayback($"PAUSE at {pausedPosition:F3}s");
        IsPaused = true;
        stopRequested = true;
        var current = thread;
        if (current is { IsAlive: true } && current != Thread.CurrentThread)
        {
            current.Join(1500);
        }
        Instrument?.Suspend();
        // Never leave game keys pressed while paused.
        GameOutput?.ReleaseAll();
        // Silence the Windows synth too, otherwise its held notes keep ringing
        // and stack up once playback resumes.
        CloseSynthesizer();
        PlayingChanged?.Invoke(false);
    }

    public void Resume()
    {
        if (!IsPaused) return;
        // Defensive: never let a lingering loop overlap with the resumed one,
        // otherwise both threads would emit the same keys.
        var previous = thread;
        if (previous is { IsAlive: true } && previous != Thread.CurrentThread)
        {
            previous.Join(1500);
        }
        IsPaused = false;
        stopRequested = false;
        playbackStart = pausedPosition;
        LogPlayback($"RESUME from {playbackStart:F3}s");
        Instrument?.ResumeOutput();
        thread = new Thread(PlayLoop) { IsBackground = true };
        thread.Start();
        PlayingChanged?.Invoke(true);
    }

    public void Stop()
    {
        stopRequested = true;
        IsPaused = false;
        pausedPosition = 0;
        playbackStart = 0;
        currentDuration = 0;
        playbackIndex = 0;
        var current = thread;
        if (current is { IsAlive: true } && current != Thread.CurrentThread)
        {
            current.Join(1500);
        }
        PlayingFolder = null;
        CurrentFile = null;
        GameOutput?.ReleaseAll();
        PlayingChanged?.Invoke(false);
    }

    public void Dispose() => Stop();

    private IntPtr manualHandle;
    private bool manualOpened;

    private void CloseSynthesizer() => CloseSynthesizer(IntPtr.Zero);

    /// <summary>
    /// Closes the synth. When <paramref name="expectedHandle"/> is given the
    /// close only happens if that handle is still the active one, so a finishing
    /// playback thread can never close the handle a resumed thread just opened.
    /// </summary>
    private void CloseSynthesizer(IntPtr expectedHandle)
    {
        lock (gate)
        {
            if (opened && (expectedHandle == IntPtr.Zero || handle == expectedHandle))
            {
                for (var note = 0; note < 128; note++)
                {
                    midiOutShortMsg(handle, 0x80 | (note << 8));
                }
                midiOutClose(handle);
                opened = false;
            }
            if (expectedHandle == IntPtr.Zero && manualOpened)
            {
                for (var note = 0; note < 128; note++)
                {
                    midiOutShortMsg(manualHandle, 0x80 | (note << 8));
                }
                midiOutClose(manualHandle);
                manualOpened = false;
            }
        }
    }

    public void ManualNoteOn(int pitch, int velocity = 96)
    {
        if (Instrument is not null)
        {
            Instrument.PlayKey(pitch, Math.Clamp(velocity / 127.0 * velocityBoost, 0.05, 1.0));
            return;
        }
        lock (gate)
        {
            if (!manualOpened)
            {
                if (midiOutOpen(out manualHandle, 0, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                {
                    manualOpened = false;
                    PlaybackError?.Invoke("无法打开 Windows MIDI 合成器，系统可能未启用 Microsoft GS Wavetable Synth。");
                    return;
                }
                manualOpened = true;
                midiOutShortMsg(manualHandle, 0xC0 | HarpProgram);
            }
            var folded = Key21Layout.FoldPitch(pitch);
            midiOutShortMsg(manualHandle, 0x90 | (folded << 8) | (velocity << 16));
        }
    }

    public void ManualNoteOff(int pitch)
    {
        if (Instrument is not null)
        {
            Instrument.NoteOff(pitch);
            return;
        }
        lock (gate)
        {
            if (manualOpened)
            {
                midiOutShortMsg(manualHandle, 0x80 | (Key21Layout.FoldPitch(pitch) << 8));
            }
        }
    }

    private void PlayLoop()
    {
        var startFrom = playbackStart;
        var activeInstrument = Instrument;
        var handleAtStart = IntPtr.Zero;
        // In-game playback replaces the local sound so the two never overlap.
        var playLocalAudio = !PlayInGame;
        try
        {
            if (!playLocalAudio)
            {
                // Keys go to the game, so make sure no local voice is left sounding.
                activeInstrument?.Suspend();
            }
            else if (activeInstrument is null)
            {
                if (midiOutOpen(out handle, 0, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                {
                    opened = false;
                    throw new InvalidOperationException("无法打开 Windows MIDI 合成器。");
                }
                opened = true;
                handleAtStart = handle;
                midiOutShortMsg(handle, 0xC0 | HarpProgram);
                ApplyVolume();
            }
            else
            {
                // Make sure the device is running again if a pause halted it.
                activeInstrument.ResumeOutput();
            }

            fileRanges = new List<(double Start, double Duration)>();
            var events = BuildPlaylistEvents(playlist, fileRanges);
            if (PlayInGame)
            {
                // Genshin ignores a second key-down while the same key is held.
                // Normalize overlapping notes before either local or game output
                // consumes the event stream.
                events = GameKeyTimeline.Normalize(events);
            }
            var total = events.Count > 0 ? events[^1].Time : 0;
            currentDuration = total;

            // Start the clock only once the synth and playlist are ready, so
            // device setup time is never counted as playback time.
            var clock = Stopwatch.StartNew();
            playbackClock = clock;
            // Resume from the exact next event so nothing already played is
            // replayed (which used to fire a burst of notes after a pause).
            var index = playbackIndex;
            var lastReport = -1.0;
            while (index < events.Count)
            {
                if (stopRequested) return;
                var position = startFrom + clock.Elapsed.TotalSeconds;
                while (index < events.Count && events[index].Time - startFrom <= position)
                {
                    var evt = events[index++];
                    playbackIndex = index;
                    // Only skip a real backlog (a jump into the middle of a file);
                    // events left over from a pause are emitted right away.
                    if (startFrom > 0 && evt.Time < startFrom - 0.05) continue;
                    if (evt.IsOn)
                    {
                        if (PlayInGame && GameOutput is not null && GameKeyFor(evt.Pitch) is { } downKey)
                        {
                            LogPlayback($"KEY DOWN {downKey} @ {evt.Time:F3}s");
                            GameOutput.KeyDown(downKey);
                        }
                        if (playLocalAudio && activeInstrument is not null)
                        {
                            activeInstrument.PlayKey(evt.Pitch, Math.Clamp(velocityBoost, 0.1, 1.0));
                        }
                        else if (playLocalAudio)
                        {
                            midiOutShortMsg(handle, MidiMessage.ScaleVelocity(0x90 | (Key21Layout.FoldPitch(evt.Pitch) << 8) | (evt.Velocity << 16), velocityBoost));
                        }
                    }
                    else
                    {
                        // Release exactly when the note ends so a following note on
                        // the same key can retrigger instead of being swallowed.
                        if (PlayInGame && GameOutput is not null && GameKeyFor(evt.Pitch) is { } upKey)
                        {
                            LogPlayback($"KEY UP {upKey} @ {evt.Time:F3}s");
                            GameOutput.KeyUp(upKey);
                        }
                        if (playLocalAudio && activeInstrument is not null)
                        {
                            activeInstrument.NoteOff(evt.Pitch);
                        }
                        else if (playLocalAudio)
                        {
                            midiOutShortMsg(handle, 0x80 | (Key21Layout.FoldPitch(evt.Pitch) << 8));
                        }
                    }
                }

                if (position - lastReport >= 0.2)
                {
                    lastReport = position;
                    ReportProgress(position, total);
                }

                if (index >= events.Count) break;
                var nextDue = events[index].Time - startFrom;
                var remaining = nextDue - clock.Elapsed.TotalSeconds;
                // Sleep in small slices so progress keeps moving through long rests.
                Thread.Sleep(remaining > 0.05 ? 10 : 1);
            }
            ReportProgress(total, total);

            var tail = (total - startFrom) + 0.8 - clock.Elapsed.TotalSeconds;
            while (tail > 0 && !stopRequested)
            {
                ReportProgress(total, total);
                Thread.Sleep(200);
                tail = total + 0.8 - clock.Elapsed.TotalSeconds;
            }
        }
        catch (Exception ex)
        {
            PlaybackError?.Invoke(ex.Message);
        }
        finally
        {
            playbackClock?.Stop();
            playbackClock = null;
            if (activeInstrument is null)
            {
                CloseSynthesizer(handleAtStart);
            }
            PlayingFolder = null;
            PlayingChanged?.Invoke(false);
        }
    }

    /// <summary>Lyre key for a pitch, or null when the pitch has no key.</summary>
    private static char? GameKeyFor(int pitch)
    {
        var key = Key21Layout.PhysicalKeyForPitch(pitch);
        return string.IsNullOrEmpty(key) ? null : key[0];
    }

    /// <summary>Diagnostic trail for pause/resume investigations.</summary>
    private static void LogPlayback(string text)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GenshinMusicStudio");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] PLAY {text}{Environment.NewLine}");
        }
        catch
        {
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

    private static List<MidiPlaybackEvent> BuildPlaylistEvents(
        IReadOnlyList<string> files,
        List<(double Start, double Duration)> ranges)
    {
        var parsedFiles = files
            .Select(file =>
            {
                var (events, duration) = MidiFileParser.ParseFile(file);
                return new MidiFileData(events, duration);
            })
            .ToList();
        return MidiPlaylistBuilder.Build(parsedFiles, ranges);
    }
}
