using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class PlaybackProgressStateTests
{
    [Fact]
    public void FrameRefresh_AfterDirectoryProgressKeepsPlaylistDuration()
    {
        var state = new PlaybackProgressState();
        state.UpdateFromPlayer(total: 120);

        var duration = state.ResolveFrameDuration(
            playerDuration: 0,
            sheetDuration: 0);

        Assert.Equal(120, duration);
        Assert.Equal(25, state.CalculatePercent(position: 30, duration));
    }

    [Fact]
    public void FrameRefresh_UsesPlayerPlaylistDurationForDirectoryPlayback()
    {
        var state = new PlaybackProgressState();

        var duration = state.ResolveFrameDuration(
            playerDuration: 120,
            sheetDuration: 45);

        Assert.Equal(120, duration);
    }

    [Fact]
    public void PlayerEventAndFrameRefresh_UseTheSameProgressPercentage()
    {
        var state = new PlaybackProgressState();
        state.UpdateFromPlayer(total: 120);
        var eventDuration = state.ResolveFrameDuration(
            playerDuration: 120,
            sheetDuration: 0);
        var frameDuration = state.ResolveFrameDuration(
            playerDuration: 0,
            sheetDuration: 0);

        Assert.Equal(eventDuration, frameDuration);
        Assert.Equal(
            state.CalculatePercent(position: 30, eventDuration),
            state.CalculatePercent(position: 30, frameDuration));
    }
}
