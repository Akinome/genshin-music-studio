namespace GenshinMusicStudio.Core;

/// <summary>Helpers for scheduling MIDI events on a resumed playback clock.</summary>
public static class MidiPlaybackTimeline
{
    private const double TimeTolerance = 1e-9;

    /// <summary>
    /// Returns whether an event is due on a clock that resumes at
    /// <paramref name="startFrom"/>.
    /// </summary>
    /// <param name="eventTime">Absolute event time in the playlist.</param>
    /// <param name="startFrom">Absolute position where this playback run started.</param>
    /// <param name="elapsed">Elapsed time since this playback run started.</param>
    /// <returns>True when the event should be emitted now.</returns>
    public static bool IsEventDue(double eventTime, double startFrom, double elapsed)
    {
        return eventTime - startFrom <= elapsed + TimeTolerance;
    }

    /// <summary>Clamps a playback position to the track duration.</summary>
    /// <param name="position">Current playback position.</param>
    /// <param name="duration">Total track duration.</param>
    /// <returns>The position at or before the end of the track.</returns>
    public static double ClampToDuration(double position, double duration)
        => duration > 0 ? Math.Min(position, duration) : position;
}
