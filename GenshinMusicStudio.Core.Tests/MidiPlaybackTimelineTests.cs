using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class MidiPlaybackTimelineTests
{
    [Fact]
    public void IsEventDue_ResumedClockDoesNotEmitFutureEventsImmediately()
    {
        Assert.False(MidiPlaybackTimeline.IsEventDue(
            eventTime: 40.0,
            startFrom: 36.0,
            elapsed: 0.05));
    }

    [Fact]
    public void IsEventDue_EmitsEventWhenItsRelativeTimeArrives()
    {
        Assert.True(MidiPlaybackTimeline.IsEventDue(
            eventTime: 36.1,
            startFrom: 36.0,
            elapsed: 0.1));
    }

    [Fact]
    public void ClampToDuration_StopsPositionAtTrackEnd()
    {
        Assert.Equal(114.0, MidiPlaybackTimeline.ClampToDuration(120.0, 114.0));
        Assert.Equal(95.0, MidiPlaybackTimeline.ClampToDuration(95.0, 114.0));
    }
}
