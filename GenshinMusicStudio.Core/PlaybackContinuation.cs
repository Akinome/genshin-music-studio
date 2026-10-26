namespace GenshinMusicStudio.Core;

/// <summary>
/// Describes how playback should continue while an instrument is changed.
/// </summary>
public sealed record PlaybackContinuation(bool WasPlaying, bool WasPaused)
{
    /// <summary>True when the active playback must be paused during the change.</summary>
    public bool ShouldPause => WasPlaying;

    /// <summary>True when the active playback must resume after the change.</summary>
    public bool ShouldResume => WasPlaying;

    /// <summary>Captures the state before an instrument change.</summary>
    /// <param name="isPlaying">Whether playback is currently running.</param>
    /// <param name="isPaused">Whether playback is currently paused.</param>
    /// <returns>The continuation state for the instrument change.</returns>
    public static PlaybackContinuation Capture(bool isPlaying, bool isPaused)
        => new(isPlaying, isPaused);
}
