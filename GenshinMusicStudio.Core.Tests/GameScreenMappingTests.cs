using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class GameScreenMappingTests
{
    [Fact]
    public void ToClient_MapsBaseCoordinatesToOneByOneAt1080p()
    {
        var (x, y) = GameScreenMapping.ToClient(560, 855, GameScreenMapping.BaseWidth, GameScreenMapping.BaseHeight);

        Assert.Equal((560, 855), (x, y));
    }

    [Fact]
    public void ToClient_ScalesByTheShorterSideAndCenters()
    {
        // 16:9 的 2K 画面按短边等比放大 1.33 倍，没有黑边，界面中心保持不变。
        var center = GameScreenMapping.ToClient(960, 540, 2560, 1440);

        Assert.Equal((1280, 720), center);

        var half = GameScreenMapping.ToClient(0, 0, 960, 540);
        Assert.Equal((0, 0), half);
    }

    [Fact]
    public void ToClient_CentersTheInterfaceOnUltraWideScreens()
    {
        // 2560x1080 下界面按 1.0 铺满高度，左右各留 320 像素。
        var topLeft = GameScreenMapping.ToClient(0, 0, 2560, 1080);
        var center = GameScreenMapping.ToClient(960, 540, 2560, 1080);

        Assert.Equal((320, 0), topLeft);
        Assert.Equal((1280, 540), center);
    }

    [Fact]
    public void ScaleFor_HandlesUpscaledAndWindowedSizes()
    {
        Assert.Equal(1.0, GameScreenMapping.ScaleFor(1920, 1080), 6);
        Assert.Equal(0.8333, GameScreenMapping.ScaleFor(1600, 900), 4);
        Assert.Equal(0.8333, GameScreenMapping.ScaleFor(1920, 900), 4);
        // 16:10 画面更高，界面按 1.0 铺满宽度，上下留黑边。
        Assert.Equal(1.0, GameScreenMapping.ScaleFor(1920, 1200), 6);
    }

    [Fact]
    public void ToClient_KeepsTheInterfaceCenteredWhenTheWindowIsTaller()
    {
        var topLeft = GameScreenMapping.ToClient(0, 0, 1920, 1200);
        var center = GameScreenMapping.ToClient(960, 540, 1920, 1200);

        Assert.Equal((0, 60), topLeft);
        Assert.Equal((960, 600), center);
    }

    [Fact]
    public void ScaleFor_TreatsEmptyClientAreaAsUnityScale()
    {
        Assert.Equal(1.0, GameScreenMapping.ScaleFor(0, 1080));
    }

    [Fact]
    public void DefaultProbePoints_SitOnTheExitButton()
    {
        var points = GameScreenMapping.DefaultProbePoints();
        var (centerX, centerY) = GameScreenMapping.ExitButtonCenter;

        // 四个盘面方向加中心叉号。
        Assert.Equal(GameScreenMapping.ExitButtonOffsets.Length + 1, points.Count);
        Assert.Contains((centerX, centerY), points);
        Assert.Equal(points.Distinct().Count(), points.Count);
        foreach (var (x, y) in points)
        {
            var offset = Math.Max(Math.Abs(x - centerX), Math.Abs(y - centerY));
            Assert.True(offset <= 14, $"取样点离按钮中心太远：({x},{y})");
        }
    }

    [Fact]
    public void DefaultControlPoints_StayOutsideTheExitButton()
    {
        var controls = GameScreenMapping.DefaultControlPoints();
        var (centerX, centerY) = GameScreenMapping.ExitButtonCenter;

        Assert.Equal(GameScreenMapping.ExitButtonControlOffsets.Length, controls.Count);
        foreach (var (x, y) in controls)
        {
            var offset = Math.Max(Math.Abs(x - centerX), Math.Abs(y - centerY));
            Assert.True(offset > 20, $"对照点落到按钮里面了：({x},{y})");
        }
    }
}
