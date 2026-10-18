using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class MidiPlayabilityTests
{
    [Fact]
    public void Analyze_ReturnsZeroForEmptyInput()
    {
        var result = MidiPlayabilityCalculator.Analyze(Array.Empty<int>());

        Assert.Equal(0, result.TotalNotes);
        Assert.Equal(0, result.LyrePitchNotes);
        Assert.Equal(0, result.Ratio);
        Assert.Equal("0.0%", result.RatioText);
        Assert.Equal("没有可统计音符", result.DetailText);
    }

    [Fact]
    public void Analyze_CountsLyrePitchesOnly()
    {
        var result = MidiPlayabilityCalculator.Analyze(new[] { 48, 50, 60, 83 });

        Assert.Equal(4, result.TotalNotes);
        Assert.Equal(4, result.LyrePitchNotes);
        Assert.Equal(0, result.NonLyrePitchNotes);
        Assert.Equal("100.0%", result.RatioText);
    }

    [Fact]
    public void Analyze_RejectsChromaticNotesWithoutFolding()
    {
        var result = MidiPlayabilityCalculator.Analyze(new[] { 49, 50, 61 });

        Assert.Equal(3, result.TotalNotes);
        Assert.Equal(1, result.LyrePitchNotes);
        Assert.Equal(2, result.NonLyrePitchNotes);
        Assert.Equal("33.3%", result.RatioText);
        Assert.Equal("1 / 3 个音符", result.DetailText);
    }

    [Fact]
    public void TryMapToLyrePitch_FoldsOutsideTheLyreRange()
    {
        Assert.Equal(48, MidiPlayabilityCalculator.TryMapToLyrePitch(36, out var lowFold) ? lowFold : 0);
        Assert.True(MidiPlayabilityCalculator.TryMapToLyrePitch(36, out _));
        Assert.True(MidiPlayabilityCalculator.TryMapToLyrePitch(84, out _));
        Assert.True(MidiPlayabilityCalculator.TryMapToLyrePitch(88, out _));
        Assert.True(MidiPlayabilityCalculator.TryMapToLyrePitch(100, out _));
        Assert.False(MidiPlayabilityCalculator.TryMapToLyrePitch(49, out _));
        Assert.False(MidiPlayabilityCalculator.TryMapToLyrePitch(51, out _));
    }

    [Fact]
    public void LyrePitches_MatchesTheTwentyOneKeyScale()
    {
        Assert.Equal(21, MidiPlayabilityCalculator.LyrePitches.Count);
        Assert.Equal(48, MidiPlayabilityCalculator.LyrePitches[0]);
        Assert.Equal(83, MidiPlayabilityCalculator.LyrePitches[^1]);
    }

    [Fact]
    public void Analyze_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MidiPlayabilityCalculator.Analyze(null!));
    }
}
