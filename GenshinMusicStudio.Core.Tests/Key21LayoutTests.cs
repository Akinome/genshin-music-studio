using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class Key21LayoutTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 5)]
    [InlineData(6, 11)]
    [InlineData(7, 0)]
    public void DegreeToSemitone_ReturnsExpectedValue(int degree, int expected)
    {
        Assert.Equal(expected, Key21Layout.DegreeToSemitone(degree));
    }

    [Theory]
    [InlineData(36, 48)]
    [InlineData(48, 48)]
    [InlineData(70, 70)]
    [InlineData(84, 72)]
    [InlineData(96, 72)]
    public void FoldPitch_KeepsPitchInsideTheLyreRange(int pitch, int expected)
    {
        var folded = Key21Layout.FoldPitch(pitch);

        Assert.Equal(expected, folded);
        Assert.InRange(folded, Key21Layout.LowPitch, Key21Layout.HighPitch);
    }

    [Theory]
    [InlineData(48, 0)]
    [InlineData(49, 1)]
    [InlineData(59, 6)]
    [InlineData(60, 7)]
    [InlineData(72, 14)]
    [InlineData(83, 20)]
    public void KeyIndexForPitch_MapsIntoTheTwentyOneKeyLayout(int pitch, int expected)
    {
        Assert.Equal(expected, Key21Layout.KeyIndexForPitch(pitch));
    }

    [Theory]
    [InlineData(48, "Z")]
    [InlineData(59, "M")]
    [InlineData(60, "A")]
    [InlineData(72, "Q")]
    [InlineData(83, "U")]
    public void PhysicalKeyForPitch_ReturnsTheExpectedQwertyKey(int pitch, string expected)
    {
        Assert.Equal(expected, Key21Layout.PhysicalKeyForPitch(pitch));
    }

    [Theory]
    [InlineData(0, 2, 0)]
    [InlineData(6, 2, 6)]
    [InlineData(7, 1, 0)]
    [InlineData(20, 0, 6)]
    public void KeyPosition_ReturnsVisualCoordinates(int keyIndex, int row, int column)
    {
        Assert.Equal((row, column), Key21Layout.KeyPosition(keyIndex));
    }

    [Fact]
    public void Constants_MatchTheStandardLyre()
    {
        Assert.Equal(21, Key21Layout.KeyCount);
        Assert.Equal(7, Key21Layout.DegreeNames.Length);
    }
}
