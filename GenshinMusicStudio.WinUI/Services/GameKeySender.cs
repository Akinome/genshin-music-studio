using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GenshinMusicStudio_WinUI.Services;

/// <summary>
/// 向原神窗口发送琴键输入。
/// 前台模式使用 SendInput，后台模式直接向游戏窗口投递按键消息，
/// 因此在原神失去焦点时仍可继续演奏。
/// </summary>
public sealed class GameKeySender : IDisposable
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmActivate = 0x0006;
    private const int WaActive = 1;
    private const uint MapvkVkToVsc = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    // 原神使用 Unity，窗口类名在不同区服和版本中保持一致。
    private const string UnityWindowClass = "UnityWndClass";
    private static readonly string[] GameProcessNames = { "yuanshen", "genshinimpact" };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    // 游戏经常忽略 SetForegroundWindow，但对这个旧 API 更兼容。
    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hwnd, bool altTab);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private readonly object gate = new();
    // 记录每个物理键当前被多少个交叠音符占用，暂停或停止时才能全部释放。
    private readonly Dictionary<char, int> heldKeys = new();
    // 限制 WM_ACTIVATE 的发送频率，避免后台模式刷屏影响游戏线程。
    private long lastActivateMilliseconds;

    /// <summary>为 true 时直接向游戏窗口投递消息，而不是注入前台输入队列。</summary>
    public bool BackgroundMode { get; set; }

    public IntPtr GameWindow { get; private set; } = IntPtr.Zero;
    public string GameWindowTitle { get; private set; } = string.Empty;
    public bool HasWindow => GameWindow != IntPtr.Zero;

    /// <summary>最近一次按键注入失败信息，用于在界面上显示，避免静默失败。</summary>
    public string? LastFailure { get; private set; }

    public void ClearFailure() => LastFailure = null;

    /// <summary>
    /// 当前进程是否以管理员身份运行。
    /// Windows 会阻止普通权限进程向管理员权限窗口注入输入，而原神通常以管理员权限运行。
    /// </summary>
    public static bool IsProcessElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>查找正在运行的原神窗口，找到后返回 true。</summary>
    public bool RefreshWindow()
    {
        var found = IntPtr.Zero;
        var foundTitle = string.Empty;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            if (!string.Equals(className.ToString(), UnityWindowClass, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0) return true;
            string processName;
            try
            {
                processName = Process.GetProcessById((int)processId).ProcessName;
            }
            catch
            {
                return true;
            }

            if (!GameProcessNames.Any(name => processName.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            found = hwnd;
            foundTitle = title.ToString();
            return false;
        }, IntPtr.Zero);

        GameWindow = found;
        GameWindowTitle = foundTitle;
        return found != IntPtr.Zero;
    }

    /// <summary>
    /// 按下一个琴键，并保持到 <see cref="KeyUp"/>。
    /// 松开时间跟随后续音符结束时间，而不是使用固定点击时长。
    /// </summary>
    public void KeyDown(char key)
    {
        var virtualKey = Normalize(key);
        if (virtualKey == '\0') return;
        if (BackgroundMode && !HasWindow && !RefreshWindow()) return;

        var retrigger = false;
        lock (gate)
        {
            heldKeys.TryGetValue(virtualKey, out var count);
            heldKeys[virtualKey] = count + 1;
            // 正常情况下，时间线预处理已经在本次按下前插入松开。
            // 这里保留兜底逻辑：即使遇到未预料的交叠，也要制造新的按下沿，
            // 不能把第二次触发吞掉并一直按到整条交叠链结束。
            retrigger = count > 0;
        }

        if (retrigger)
        {
            if (SendKey(virtualKey, down: false))
            {
                LogKey("UP " + virtualKey + " (retrigger)");
            }
            Thread.Sleep(30);
        }
        if (SendKey(virtualKey, down: true))
        {
            LogKey("DOWN " + virtualKey);
        }
    }

    /// <summary>当同一按键的所有交叠音符都结束后，再真正松开该键。</summary>
    public void KeyUp(char key)
    {
        var virtualKey = Normalize(key);
        if (virtualKey == '\0') return;
        lock (gate)
        {
            if (!heldKeys.TryGetValue(virtualKey, out var count)) return;
            if (count > 1)
            {
                heldKeys[virtualKey] = count - 1;
                return;
            }
            heldKeys.Remove(virtualKey);
        }
        if (SendKey(virtualKey, down: false))
        {
            LogKey("UP " + virtualKey);
        }
    }

    /// <summary>释放所有仍处于按下状态的按键，例如暂停或停止播放时。</summary>
    public void ReleaseAll()
    {
        char[] held;
        lock (gate)
        {
            held = heldKeys.Keys.ToArray();
            heldKeys.Clear();
        }
        foreach (var key in held)
        {
            if (SendKey(key, down: false))
            {
                LogKey("UP " + key + " (release all)");
            }
        }
    }

    private static void LogKey(string text)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GenshinMusicStudio");
            Directory.CreateDirectory(dir);
            File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] GAMEKEY {text}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static char Normalize(char key)
    {
        var virtualKey = char.ToUpperInvariant(key);
        return virtualKey is >= 'A' and <= 'Z' ? virtualKey : '\0';
    }

    /// <summary>把原神切换到前台，仅前台演奏模式需要使用。</summary>
    public bool FocusGame()
    {
        if (!RefreshWindow()) return false;
        if (GetForegroundWindow() == GameWindow) return true;
        SwitchToThisWindow(GameWindow, true);
        Thread.Sleep(120);
        if (GetForegroundWindow() == GameWindow) return true;
        SetForegroundWindow(GameWindow);
        Thread.Sleep(120);
        return GetForegroundWindow() == GameWindow;
    }

    private bool SendKey(char virtualKey, bool down)
    {
        if (BackgroundMode)
        {
            if (!HasWindow && !RefreshWindow()) return false;
            // 游戏在非激活状态会忽略投递的按键，所以先补一个激活消息。
            // 但 WM_ACTIVATE 不能频繁发送，否则会干扰游戏线程。
            var now = Environment.TickCount64;
            if (GetForegroundWindow() != GameWindow && now - lastActivateMilliseconds > 250)
            {
                lastActivateMilliseconds = now;
                PostMessage(GameWindow, WmActivate, (IntPtr)WaActive, IntPtr.Zero);
            }
            var scan = (int)MapVirtualKey(virtualKey, MapvkVkToVsc);
            var lParam = 1 | (scan << 16) | (down ? 0 : unchecked((int)0xC0000000));
            var message = down ? WmKeyDown : WmKeyUp;
            if (PostMessage(GameWindow, message, (IntPtr)virtualKey, (IntPtr)lParam)) return true;
            var error = Marshal.GetLastWin32Error();
            ReportFailure(error == 5
                ? "系统拒绝了按键投递（错误 5：拒绝访问）。原神以管理员权限运行，本程序也需要以管理员身份运行。"
                : "按键投递失败，Win32 错误码 " + error + "。");
            // 句柄失效通常表示游戏重启，下次发送时重新查找窗口。
            GameWindow = IntPtr.Zero;
            return false;
        }

        var scanCode = (ushort)MapVirtualKey(virtualKey, MapvkVkToVsc);
        var inputs = new[]
        {
            new Input
            {
                Type = InputKeyboard,
                Union = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = virtualKey,
                        ScanCode = scanCode,
                        // 使用普通虚拟键输入，与 WindowsInput 的行为一致，
                        // 也是原神实际会响应的输入方式。
                        Flags = down ? 0 : KeyEventKeyUp,
                        Time = 0,
                        ExtraInfo = IntPtr.Zero,
                    },
                },
            },
        };
        if (SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1) return true;
        var sendError = Marshal.GetLastWin32Error();
        ReportFailure(sendError == 5
            ? "系统拒绝了按键注入（错误 5：拒绝访问）。原神以管理员权限运行，本程序也需要以管理员身份运行。"
            : "按键注入失败，Win32 错误码 " + sendError + "。");
        return false;
    }

    private void ReportFailure(string message)
    {
        if (string.Equals(LastFailure, message, StringComparison.Ordinal)) return;
        LastFailure = message;
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GenshinMusicStudio");
            Directory.CreateDirectory(dir);
            File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss}] GAMEKEY {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        ReleaseAll();
    }
}
