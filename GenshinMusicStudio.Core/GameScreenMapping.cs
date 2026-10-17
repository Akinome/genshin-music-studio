namespace GenshinMusicStudio.Core;

/// <summary>
/// 把 1920x1080 基准坐标换算成游戏窗口客户区像素。
/// 原神界面按短边等比缩放并居中，四周黑边不属于界面区域，
/// 因此取 min(宽/1920, 高/1080) 作为缩放比，再把基准坐标平移到画面中心。
/// </summary>
public static class GameScreenMapping
{
    /// <summary>基准坐标系宽度。</summary>
    public const int BaseWidth = 1920;

    /// <summary>基准坐标系高度。</summary>
    public const int BaseHeight = 1080;

    /// <summary>
    /// 乐器界面右上角退出按钮的中心（1920x1080 实测）。
    /// 只有打开乐器时才有这个按钮，关闭后该位置恢复成游戏画面，
    /// 因此它是“界面还在不在”最干净的信号。琴键和背景都会随演奏变化，不能用来判断。
    /// </summary>
    public static readonly (int X, int Y) ExitButtonCenter = (1852, 48);

    /// <summary>
    /// 退出按钮取样点：圆盘上四个方向的纯色盘面，加上中间的深色叉号。
    /// 圆盘直径约 36px，半径 14 的位置稳定落在纯色盘面上。
    /// </summary>
    public static readonly (int Dx, int Dy)[] ExitButtonOffsets =
    {
        (-14, 0), (14, 0), (0, -14), (0, 14),
    };

    /// <summary>
    /// 取样自检用的背景对照点：按钮外侧，落在天空或游戏画面上。
    /// 不写入签名，只用来判断用户是否真的在乐器界面打开时取样。
    /// </summary>
    public static readonly (int Dx, int Dy)[] ExitButtonControlOffsets =
    {
        (-46, 0), (0, 46),
    };

    /// <summary>计算窗口客户区相对 1920x1080 的缩放比。</summary>
    /// <param name="clientWidth">客户区宽度。</param>
    /// <param name="clientHeight">客户区高度。</param>
    /// <returns>缩放比，窗口尺寸非法时返回 1。</returns>
    public static double ScaleFor(int clientWidth, int clientHeight)
    {
        if (clientWidth <= 0 || clientHeight <= 0) return 1;
        return Math.Min(clientWidth / (double)BaseWidth, clientHeight / (double)BaseHeight);
    }

    /// <summary>把基准坐标换算成客户区像素。</summary>
    /// <param name="baseX">基准坐标 X。</param>
    /// <param name="baseY">基准坐标 Y。</param>
    /// <param name="clientWidth">客户区宽度。</param>
    /// <param name="clientHeight">客户区高度。</param>
    /// <returns>客户区像素坐标。</returns>
    public static (int X, int Y) ToClient(int baseX, int baseY, int clientWidth, int clientHeight)
    {
        var scale = ScaleFor(clientWidth, clientHeight);
        var offsetX = (clientWidth - BaseWidth * scale) / 2.0;
        var offsetY = (clientHeight - BaseHeight * scale) / 2.0;
        return (
            (int)Math.Round(offsetX + baseX * scale),
            (int)Math.Round(offsetY + baseY * scale));
    }

    /// <summary>
    /// 默认取样点：退出按钮上的五个位置（四个盘面方向加中心叉号）。
    /// </summary>
    /// <returns>1920x1080 基准坐标下的取样点。</returns>
    public static List<(int X, int Y)> DefaultProbePoints()
    {
        var (cx, cy) = ExitButtonCenter;
        var points = new List<(int X, int Y)>(ExitButtonOffsets.Length + 1);
        foreach (var (dx, dy) in ExitButtonOffsets)
        {
            points.Add((cx + dx, cy + dy));
        }

        // 中心的深色叉号：界面关闭后这里会变成亮背景，是很有区分度的一个点。
        points.Add((cx, cy));
        return points;
    }

    /// <summary>取样自检用的背景对照点。</summary>
    /// <returns>1920x1080 基准坐标下的对照点，位于按钮外侧。</returns>
    public static List<(int X, int Y)> DefaultControlPoints()
    {
        var (cx, cy) = ExitButtonCenter;
        return ExitButtonControlOffsets.Select(offset => (cx + offset.Dx, cy + offset.Dy)).ToList();
    }
}
