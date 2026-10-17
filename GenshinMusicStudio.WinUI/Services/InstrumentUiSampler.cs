using System.Runtime.InteropServices;
using GenshinMusicStudio.Core;

namespace GenshinMusicStudio_WinUI.Services;

    /// <summary>
    /// 从原神窗口自身的设备上下文读取取样点颜色。
    /// 只取窗口 DC、不取屏幕 DC：屏幕 DC 会和 DWM 合成器互斥，
    /// 每 30ms 取一次会让 alt+tab 切窗口时整个系统卡住。
    /// 窗口 DC 的坐标原点就是客户区左上角，所以直接用基准坐标换算。
    /// </summary>
public sealed class InstrumentUiSampler
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hDc, int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private readonly GameKeySender gameKeys;

    /// <summary>取样结果：颜色签名，以及是否确实取到了乐器界面上的退出按钮。</summary>
    /// <param name="Signature">颜色签名，读取失败时为 null。</param>
    /// <param name="LooksLikeButton">取样点与按钮外侧背景明显不同时为 true。</param>
    /// <param name="Blank">取样点颜色完全一致时为 true，说明读到的是空白画面。</param>
    public sealed record CaptureResult(InstrumentUiSignature? Signature, bool LooksLikeButton, bool Blank);

    /// <summary>用游戏窗口句柄来源构造取样器。</summary>
    /// <param name="gameKeys">负责查找原神窗口的发送器。</param>
    public InstrumentUiSampler(GameKeySender gameKeys)
    {
        this.gameKeys = gameKeys;
    }

    /// <summary>按签名记录的取样点读取当前颜色，读取失败时返回空列表。</summary>
    /// <param name="signature">乐器界面颜色签名。</param>
    /// <returns>与签名同顺序的颜色列表。</returns>
    public List<ObservedColor> Sample(InstrumentUiSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        var positions = signature.Points.Select(point => (point.X, point.Y)).ToList();
        return TryRead(positions, out var colors) ? colors : new List<ObservedColor>();
    }

    /// <summary>在乐器界面打开时取色，生成颜色签名。</summary>
    /// <param name="tolerance">每通道允许的最大偏差。</param>
    /// <returns>取样结果。</returns>
    public CaptureResult Capture(int tolerance = InstrumentUiSignature.DefaultTolerance)
    {
        var positions = GameScreenMapping.DefaultProbePoints();
        if (!TryRead(positions, out var colors))
        {
            return new CaptureResult(null, false, false);
        }

        // 顺带读取按钮外侧的背景作对照，确认用户是在乐器界面打开时按的取样。
        var looksLikeButton = TryRead(GameScreenMapping.DefaultControlPoints(), out var control)
            && InstrumentUiSignature.ProbeDiffersFromBackground(colors, control);

        var blank = InstrumentUiSignature.IsUniform(colors);
        return new CaptureResult(InstrumentUiSignature.Capture(positions, colors, tolerance), looksLikeButton, blank);
    }

    private bool TryRead(IReadOnlyList<(int X, int Y)> basePoints, out List<ObservedColor> colors)
    {
        colors = new List<ObservedColor>();
        if (!gameKeys.HasWindow && !gameKeys.RefreshWindow()) return false;
        var handle = gameKeys.GameWindow;
        if (handle == IntPtr.Zero) return false;
        if (!GetClientRect(handle, out var rect)) return false;

        var clientWidth = rect.Right - rect.Left;
        var clientHeight = rect.Bottom - rect.Top;
        if (clientWidth <= 0 || clientHeight <= 0) return false;

        // 只取游戏窗口自己的 DC，坐标即客户区坐标。
        var window = GetDC(handle);
        if (window == IntPtr.Zero) return false;
        try
        {
            foreach (var (baseX, baseY) in basePoints)
            {
                var (x, y) = GameScreenMapping.ToClient(baseX, baseY, clientWidth, clientHeight);
                colors.Add(ObservedColor.FromColorRef(GetPixel(window, x, y)));
            }
        }
        finally
        {
            ReleaseDC(handle, window);
        }

        return colors.Count == basePoints.Count;
    }
}
