namespace GenshinMusicStudio.Core;

/// <summary>
/// 从游戏画面读取到的一个像素颜色。
/// Win32 的 GetPixel 返回 BGR 排布的 COLORREF，这里统一换算成 RGB 三元组。
/// </summary>
public readonly record struct ObservedColor(byte R, byte G, byte B)
{
    /// <summary>把 Win32 COLORREF（0x00BBGGRR）换算成 RGB 颜色。</summary>
    /// <param name="colorRef">GetPixel 返回值。</param>
    /// <returns>RGB 颜色。</returns>
    public static ObservedColor FromColorRef(uint colorRef) =>
        new((byte)(colorRef & 0xFF), (byte)((colorRef >> 8) & 0xFF), (byte)((colorRef >> 16) & 0xFF));

    /// <summary>判断两个颜色是否在给定容差内一致。</summary>
    /// <param name="other">要比较的颜色。</param>
    /// <param name="tolerance">每个通道允许的最大偏差。</param>
    /// <returns>三个通道都未超出容差时为 true。</returns>
    public bool Matches(ObservedColor other, int tolerance)
    {
        var limit = Math.Max(0, tolerance);
        return Math.Abs(R - other.R) <= limit
            && Math.Abs(G - other.G) <= limit
            && Math.Abs(B - other.B) <= limit;
    }
}

/// <summary>以 1920x1080 为基准的一个取样点及其在乐器界面中看到的颜色。</summary>
public sealed record ProbePoint(int X, int Y, byte R, byte G, byte B);

/// <summary>
/// 乐器界面的颜色签名：在原琴界面打开时对固定取样点取色，
/// 演奏过程中反复比对这些点的颜色，判断界面是否仍然存在。
/// 被对话、菜单、多人界面等打断退出乐器界面时，匹配度会整体塌掉，
/// 这时必须停止发送按键，否则按键会打到游戏里造成误操作。
/// </summary>
public sealed class InstrumentUiSignature
{
    /// <summary>默认每通道容差，界面抗锯齿和轻微光照变化都在此范围内。</summary>
    public const int DefaultTolerance = 20;

    /// <summary>用一组取样点构造颜色签名。</summary>
    /// <param name="points">取样点及其期望颜色。</param>
    /// <param name="tolerance">每通道允许的最大偏差。</param>
    public InstrumentUiSignature(IReadOnlyList<ProbePoint> points, int tolerance)
    {
        ArgumentNullException.ThrowIfNull(points);
        Points = points;
        Tolerance = Math.Clamp(tolerance, 0, 255);
    }

    /// <summary>取样点列表，顺序必须与读取颜色时使用的顺序一致。</summary>
    public IReadOnlyList<ProbePoint> Points { get; }

    /// <summary>每通道允许的最大偏差。</summary>
    public int Tolerance { get; }

    /// <summary>签是否包含可用取样点。</summary>
    public bool HasPoints => Points.Count > 0;

    /// <summary>
    /// 在乐器界面打开时取色，生成颜色签名。
    /// </summary>
    /// <param name="positions">1920x1080 基准坐标下的取样点。</param>
    /// <param name="colors">同一顺序读取到的颜色。</param>
    /// <param name="tolerance">每通道允许的最大偏差。</param>
    /// <returns>颜色签名。</returns>
    public static InstrumentUiSignature Capture(
        IReadOnlyList<(int X, int Y)> positions,
        IReadOnlyList<ObservedColor> colors,
        int tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(colors);
        if (positions.Count != colors.Count)
        {
            throw new ArgumentException("取样点数量与读取到的颜色数量不一致。", nameof(colors));
        }

        var points = new List<ProbePoint>(positions.Count);
        for (var i = 0; i < positions.Count; i++)
        {
            var (x, y) = positions[i];
            var color = colors[i];
            points.Add(new ProbePoint(x, y, color.R, color.G, color.B));
        }

        return new InstrumentUiSignature(points, tolerance);
    }

    /// <summary>统计与签名一致的取样点数量。</summary>
    /// <param name="observed">同一顺序读取到的当前颜色。</param>
    /// <returns>匹配的取样点数量。</returns>
    public int CountMatching(IReadOnlyList<ObservedColor> observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        var matched = 0;
        for (var i = 0; i < Points.Count && i < observed.Count; i++)
        {
            var point = Points[i];
            if (observed[i].Matches(new ObservedColor(point.R, point.G, point.B), Tolerance))
            {
                matched++;
            }
        }

        return matched;
    }

    /// <summary>匹配比例，用于判断界面是否还在。</summary>
    /// <param name="observed">同一顺序读取到的当前颜色。</param>
    /// <returns>0 到 1 之间的匹配比例，没有取样点时返回 0。</returns>
    public double MatchRatio(IReadOnlyList<ObservedColor> observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        if (Points.Count == 0) return 0;
        return CountMatching(observed) / (double)Points.Count;
    }

    /// <summary>
    /// 判断取样点是否真的取到了乐器界面上的按钮，而不是背景。
    /// 按钮外侧取两个对照点：如果取样点和对照点颜色接近，说明取到的是同一片背景，
    /// 即用户在乐器界面没打开的时候按了取样。
    /// </summary>
    /// <param name="probe">取样点颜色。</param>
    /// <param name="control">按钮外侧的对照点颜色。</param>
    /// <param name="minimumDistance">判定为“确实是按钮”的平均颜色距离。</param>
    /// <returns>取样点与背景明显不同时为 true。</returns>
    public static bool ProbeDiffersFromBackground(
        IReadOnlyList<ObservedColor> probe,
        IReadOnlyList<ObservedColor> control,
        int minimumDistance = 25)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(control);
        if (probe.Count == 0 || control.Count == 0) return false;

        var probeAverage = Average(probe);
        var controlAverage = Average(control);
        return Distance(probeAverage, controlAverage) >= minimumDistance;
    }

    private static ObservedColor Average(IReadOnlyList<ObservedColor> colors)
    {
        long r = 0, g = 0, b = 0;
        foreach (var color in colors)
        {
            r += color.R;
            g += color.G;
            b += color.B;
        }

        return new ObservedColor((byte)(r / colors.Count), (byte)(g / colors.Count), (byte)(b / colors.Count));
    }

    private static int Distance(ObservedColor left, ObservedColor right) =>
        Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B);

    /// <summary>
    /// 判断一次取样是不是空白画面，比如窗口被完全遮挡、切到独占全屏，
    /// 或者读取失败时窗口 DC 交回来的全黑内容。
    /// 空白必须当成“未知”，不能当成“界面已关闭”，否则切窗口就会误暂停。
    /// </summary>
    /// <param name="colors">同一顺序读取到的颜色。</param>
    /// <returns>所有取样点颜色完全一致时为 true。</returns>
    public static bool IsUniform(IReadOnlyList<ObservedColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count < 2) return true;
        var first = colors[0];
        for (var i = 1; i < colors.Count; i++)
        {
            if (!colors[i].Equals(first)) return false;
        }

        return true;
    }
}
