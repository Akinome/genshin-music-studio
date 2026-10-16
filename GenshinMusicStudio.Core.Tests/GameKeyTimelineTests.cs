using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class GameKeyTimelineTests
{
    [Fact]
    public void Normalize_RetriggersOverlappingNotesOnTheSameKey()
    {
        var events = new[]
        {
            On(0.0, 60),
            On(1.0, 60),
            Off(2.0, 60),
            Off(3.0, 60),
        };

        var result = GameKeyTimeline.Normalize(events);

        Assert.Equal(4, result.Count);
        Assert.Equal((0.0, true), (result[0].Time, result[0].IsOn));
        Assert.Equal((0.97, false), (result[1].Time, result[1].IsOn));
        Assert.Equal((1.0, true), (result[2].Time, result[2].IsOn));
        Assert.Equal((3.0, false), (result[3].Time, result[3].IsOn));
    }

    [Fact]
    public void Normalize_KeepsNonOverlappingNotesUnchanged()
    {
        var events = new[]
        {
            On(0.0, 60),
            Off(0.5, 60),
            On(1.0, 60),
            Off(1.5, 60),
        };

        var result = GameKeyTimeline.Normalize(events);

        Assert.Equal(events, result);
    }

    [Fact]
    public void Normalize_UsesPressTimeWhenThereIsNoRoomForTheFullGap()
    {
        var events = new[]
        {
            On(0.0, 60),
            On(0.010, 60),
            Off(0.2, 60),
        };

        var result = GameKeyTimeline.Normalize(events);

        Assert.Equal(3, result.Count);
        Assert.Equal(0.010, result[1].Time, 6);
        Assert.False(result[1].IsOn);
        Assert.Equal(0.010, result[2].Time, 6);
        Assert.True(result[2].IsOn);
    }

    [Fact]
    public void Normalize_MovesReleaseAheadOfPressAtTheSameTimestamp()
    {
        var events = new[]
        {
            On(0.0, 60),
            Off(1.0, 60),
            On(1.0, 60),
            Off(2.0, 60),
        };

        var result = GameKeyTimeline.Normalize(events);

        Assert.Equal(new[] { true, false, true, false }, result.Select(item => item.IsOn));
        Assert.Equal(0.97, result[1].Time, 6);
        Assert.Equal(1.0, result[2].Time, 6);
    }

    [Fact]
    public void Normalize_DoesNotMoveReleaseBeforeThePreviousPress()
    {
        var events = new[]
        {
            On(0.98, 60),
            Off(1.0, 60),
            On(1.0, 60),
            Off(2.0, 60),
        };

        var result = GameKeyTimeline.Normalize(events);

        Assert.Equal(1.0, result[1].Time, 6);
        Assert.False(result[1].IsOn);
        Assert.Equal(1.0, result[2].Time, 6);
        Assert.True(result[2].IsOn);
    }

    [Fact]
    public void Normalize_HandlesDifferentKeysIndependently()
    {
        var events = new[]
        {
            On(0.0, 60),
            On(0.1, 62),
            Off(0.2, 60),
            Off(0.3, 62),
        };

        var result = GameKeyTimeline.Normalize(events);

        Assert.Equal(events, result);
    }

    [Fact]
    public void Normalize_DropsEveryStaleReleaseInAnOverlapChain()
    {
        var events = new[]
        {
            On(0.0, 60),
            On(1.0, 60),
            On(2.0, 60),
            Off(3.0, 60),
            Off(4.0, 60),
            Off(5.0, 60),
        };

        var result = GameKeyTimeline.Normalize(events);

        Assert.Equal(6, result.Count);
        Assert.False(result[1].IsOn);
        Assert.True(result[2].IsOn);
        Assert.False(result[3].IsOn);
        Assert.True(result[4].IsOn);
        Assert.Equal(5.0, result[5].Time, 6);
        Assert.False(result[5].IsOn);
    }

    [Fact]
    public void Normalize_IgnoresAReleaseWithoutAnActivePress()
    {
        var result = GameKeyTimeline.Normalize(new[] { Off(0.0, 60) });

        Assert.Empty(result);
    }

    [Fact]
    public void Normalize_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => GameKeyTimeline.Normalize(null!));
    }

    private static MidiPlaybackEvent On(double time, int pitch) =>
        new(time, true, pitch, 96);

    private static MidiPlaybackEvent Off(double time, int pitch) =>
        new(time, false, pitch, 0);
}
