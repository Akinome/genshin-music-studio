using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GenshinMusicStudio_WinUI.Services;

/// <summary>
/// Sends lyre keystrokes to the game window so a sheet can be played in game.
/// Foreground mode uses SendInput; background mode posts the key messages
/// straight to the game window so it keeps working while unfocused.
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

    // Genshin runs on Unity, so its window class is stable across regions.
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

    // Games often ignore SetForegroundWindow, but honour this older API.
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
    private readonly List<(long DueMilliseconds, char Key)> pendingReleases = new();
    private Thread? releaseWorker;
    private volatile bool workerRunning;

    /// <summary>Send messages to the window instead of the foreground input queue.</summary>
    public bool BackgroundMode { get; set; }

    /// <summary>How long a key stays pressed, in milliseconds.</summary>
    public int HoldMilliseconds { get; set; } = 35;

    public IntPtr GameWindow { get; private set; } = IntPtr.Zero;
    public string GameWindowTitle { get; private set; } = string.Empty;
    public bool HasWindow => GameWindow != IntPtr.Zero;

    /// <summary>Last injection failure, surfaced to the UI so it is not silent.</summary>
    public string? LastFailure { get; private set; }

    public void ClearFailure() => LastFailure = null;

    /// <summary>
    /// True when this process runs elevated. Windows blocks input sent from a
    /// normal process to an elevated window, and Genshin normally runs elevated.
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

    /// <summary>Locates the running game window; returns true when one is found.</summary>
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
    /// Plays one note key. The press is sent immediately and the release is
    /// queued on a worker thread so playback timing is never blocked.
    /// </summary>
    public void Tap(char key)
    {
        var virtualKey = char.ToUpperInvariant(key);
        if (virtualKey < 'A' || virtualKey > 'Z') return;
        if (BackgroundMode && !HasWindow && !RefreshWindow()) return;

        if (!SendKey(virtualKey, down: true)) return;

        lock (gate)
        {
            pendingReleases.Add((Environment.TickCount64 + Math.Clamp(HoldMilliseconds, 5, 500), virtualKey));
        }
        EnsureWorker();
    }

    /// <summary>Releases every key still queued, e.g. when playback stops.</summary>
    public void ReleaseAll()
    {
        lock (gate)
        {
            pendingReleases.Clear();
        }
    }

    /// <summary>Brings the game to the foreground (needed for foreground playback).</summary>
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

    private void EnsureWorker()
    {
        if (releaseWorker is { IsAlive: true }) return;
        workerRunning = true;
        releaseWorker = new Thread(ReleaseLoop) { IsBackground = true, Name = "game-key-release" };
        releaseWorker.Start();
    }

    private void ReleaseLoop()
    {
        while (workerRunning)
        {
            char[] due;
            lock (gate)
            {
                var now = Environment.TickCount64;
                due = pendingReleases.Where(item => item.DueMilliseconds <= now)
                    .Select(item => item.Key)
                    .Distinct()
                    .ToArray();
                pendingReleases.RemoveAll(item => item.DueMilliseconds <= now);
            }

            foreach (var key in due)
            {
                SendKey(key, down: false);
            }
            Thread.Sleep(2);
        }
    }

    private bool SendKey(char virtualKey, bool down)
    {
        if (BackgroundMode)
        {
            if (!HasWindow && !RefreshWindow()) return false;
            // The game ignores posted keys while it is inactive, so re-activate
            // it first. This is what makes background play work at all.
            if (GetForegroundWindow() != GameWindow)
            {
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
            // A stale handle means the game restarted; look it up again next time.
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
                        // Plain virtual-key input matches what WindowsInput does
                        // and is what the game actually reacts to.
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
        workerRunning = false;
        try
        {
            releaseWorker?.Join(200);
        }
        catch
        {
        }
    }
}
