using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class PlaybackContinuationTests
{
    [Fact]
    public void Capture_PlayingPlaybackPausesAndResumes()
    {
        var continuation = PlaybackContinuation.Capture(isPlaying: true, isPaused: false);

        Assert.True(continuation.ShouldPause);
        Assert.True(continuation.ShouldResume);
    }

    [Fact]
    public void Capture_PausedPlaybackStaysPaused()
    {
        var continuation = PlaybackContinuation.Capture(isPlaying: false, isPaused: true);

        Assert.False(continuation.ShouldPause);
        Assert.False(continuation.ShouldResume);
    }

    [Fact]
    public void Capture_StoppedPlaybackDoesNotRestart()
    {
        var continuation = PlaybackContinuation.Capture(isPlaying: false, isPaused: false);

        Assert.False(continuation.ShouldPause);
        Assert.False(continuation.ShouldResume);
    }
}
