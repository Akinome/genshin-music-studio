using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class InstrumentLayoutTests
{
    [Fact]
    public void DefaultLayout_ExposesThreeOctaves()
    {
        var layout = new InstrumentLayout();

        Assert.Equal(3, layout.Octaves);
        Assert.Equal(7, layout.Columns);
        Assert.Equal(21, layout.KeyCount);
        Assert.Equal(48, layout.LowPitch);
        Assert.Equal(83, layout.HighPitch);
        Assert.Equal(2, layout.TopOctave);
        Assert.False(layout.IsDrumKit);
        Assert.False(layout.ByMidiName);
    }

    [Theory]
    [InlineData("genshin-2x7", false, 2, 0, 14, 48, 71)]
    [InlineData("genshin-2x7-high", true, 2, 1, 14, 60, 83)]
    [InlineData("unknown", false, 3, 0, 21, 48, 83)]
    public void FromShape_CreatesExpectedMelodicLayout(
        string shape,
        bool byMidiName,
        int octaves,
        int lowOctave,
        int keyCount,
        int lowPitch,
        int highPitch)
    {
        var layout = InstrumentLayout.FromShape(shape, byMidiName);

        Assert.Equal(octaves, layout.Octaves);
        Assert.Equal(lowOctave, layout.LowOctave);
        Assert.Equal(byMidiName, layout.ByMidiName);
        Assert.Equal(keyCount, layout.KeyCount);
        Assert.Equal(lowPitch, layout.LowPitch);
        Assert.Equal(highPitch, layout.HighPitch);
        Assert.Equal(7, layout.Columns);
    }

    [Fact]
    public void FromShape_CreatesDrumLayout()
    {
        var layout = InstrumentLayout.FromShape("genshin-2x4", true);

        Assert.True(layout.IsDrumKit);
        Assert.Equal(4, layout.Columns);
        Assert.Equal(8, layout.KeyCount);
    }

    [Theory]
    [InlineData(36, 48)]
    [InlineData(95, 83)]
    public void FoldPitch_StaysInsideConfiguredRange(int pitch, int expected)
    {
        var layout = new InstrumentLayout();

        Assert.Equal(expected, layout.FoldPitch(pitch));
    }

    [Theory]
    [InlineData(48, 14)]
    [InlineData(60, 7)]
    [InlineData(72, 0)]
    public void KeyIndexForPitch_MapsFromTopToBottom(int pitch, int expected)
    {
        var layout = new InstrumentLayout();

        Assert.Equal(expected, layout.KeyIndexForPitch(pitch));
    }

    [Theory]
    [InlineData(0, 72)]
    [InlineData(7, 60)]
    [InlineData(14, 48)]
    public void PitchForKeyIndex_RoundTripsTheDiatonicLayout(int keyIndex, int expected)
    {
        var layout = new InstrumentLayout();

        Assert.Equal(expected, layout.PitchForKeyIndex(keyIndex));
    }

    [Theory]
    [InlineData(60, 0)]
    [InlineData(61, 0)]
    [InlineData(66, 3)]
    [InlineData(90, 3)]
    public void KeyIndexForPitch_ChoosesNearestDrum(int pitch, int expected)
    {
        var layout = InstrumentLayout.FromShape("genshin-2x4", false);

        Assert.Equal(expected, layout.KeyIndexForPitch(pitch));
    }
}
