using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class InstrumentUiSignatureTests
{
    [Fact]
    public void Capture_KeepsPointsAndToleranceTogether()
    {
        var positions = new[] { (10, 20), (30, 40) };
        var colors = new[] { new ObservedColor(1, 2, 3), new ObservedColor(4, 5, 6) };

        var signature = InstrumentUiSignature.Capture(positions, colors, 8);

        Assert.True(signature.HasPoints);
        Assert.Equal(2, signature.Points.Count);
        Assert.Equal(new ProbePoint(10, 20, 1, 2, 3), signature.Points[0]);
        Assert.Equal(new ProbePoint(30, 40, 4, 5, 6), signature.Points[1]);
        Assert.Equal(8, signature.Tolerance);
    }

    [Fact]
    public void Capture_RejectsMismatchedSampleCounts()
    {
        var positions = new[] { (0, 0) };
        var colors = Array.Empty<ObservedColor>();

        Assert.Throws<ArgumentException>(() => InstrumentUiSignature.Capture(positions, colors));
    }

    [Fact]
    public void MatchRatio_CountsOnlyPointsInsideTolerance()
    {
        var positions = new[] { (0, 0), (1, 1), (2, 2), (3, 3) };
        var colors = new[]
        {
            new ObservedColor(100, 100, 100),
            new ObservedColor(100, 100, 100),
            new ObservedColor(100, 100, 100),
            new ObservedColor(100, 100, 100),
        };
        var signature = InstrumentUiSignature.Capture(positions, colors, 10);
        var observed = new[]
        {
            new ObservedColor(100, 100, 100),
            new ObservedColor(105, 95, 105),
            // 超出容差 10，视为界面已经变了。
            new ObservedColor(130, 100, 100),
            new ObservedColor(100, 100, 100),
        };

        Assert.Equal(3, signature.CountMatching(observed));
        Assert.Equal(0.75, signature.MatchRatio(observed));
    }

    [Fact]
    public void MatchRatio_ReturnsZeroWithoutPoints()
    {
        Assert.Equal(0, new InstrumentUiSignature(Array.Empty<ProbePoint>(), 10).MatchRatio(new[] { new ObservedColor(1, 2, 3) }));
    }

    [Fact]
    public void ProbeDiffersFromBackground_AcceptsACapturedButton()
    {
        // 按钮是奶油色圆盘加深色叉号，背景是明亮的天空。
        var probe = new[]
        {
            new ObservedColor(255, 249, 239),
            new ObservedColor(255, 249, 239),
            new ObservedColor(240, 236, 220),
            new ObservedColor(59, 66, 85),
            new ObservedColor(220, 214, 196),
        };
        var control = new[]
        {
            new ObservedColor(152, 220, 251),
            new ObservedColor(150, 218, 250),
        };

        Assert.True(InstrumentUiSignature.ProbeDiffersFromBackground(probe, control));
    }

    [Fact]
    public void ProbeDiffersFromBackground_RejectsSamplingTheSameBackground()
    {
        // 乐器界面没打开就取样：按钮位置也是天空，和对照点没有区别。
        var same = new[]
        {
            new ObservedColor(152, 220, 251),
            new ObservedColor(150, 218, 250),
        };

        Assert.False(InstrumentUiSignature.ProbeDiffersFromBackground(same, same));
    }

    [Fact]
    public void ProbeDiffersFromBackground_RejectsEmptyInput()
    {
        Assert.False(InstrumentUiSignature.ProbeDiffersFromBackground(
            Array.Empty<ObservedColor>(),
            new[] { new ObservedColor(1, 2, 3) }));
        Assert.False(InstrumentUiSignature.ProbeDiffersFromBackground(
            new[] { new ObservedColor(1, 2, 3) },
            Array.Empty<ObservedColor>()));
    }

    [Fact]
    public void IsUniform_DetectsBlankReads()
    {
        Assert.True(InstrumentUiSignature.IsUniform(new[] { new ObservedColor(0, 0, 0), new ObservedColor(0, 0, 0) }));
        Assert.True(InstrumentUiSignature.IsUniform(new[] { new ObservedColor(255, 255, 255) }));
        Assert.True(InstrumentUiSignature.IsUniform(Array.Empty<ObservedColor>()));
    }

    [Fact]
    public void IsUniform_AcceptsARealSample()
    {
        var colors = new[]
        {
            new ObservedColor(255, 249, 239),
            new ObservedColor(255, 249, 239),
            new ObservedColor(221, 176, 85),
            new ObservedColor(59, 66, 85),
            new ObservedColor(152, 220, 251),
        };

        Assert.False(InstrumentUiSignature.IsUniform(colors));
    }
}
