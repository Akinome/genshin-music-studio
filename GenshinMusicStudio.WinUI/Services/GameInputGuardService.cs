using GenshinMusicStudio.Core;

namespace GenshinMusicStudio_WinUI.Services;

/// <summary>
/// 演奏界面守卫：原神内弹奏时周期性比对乐器界面颜色签名，
/// 一旦界面消失（被 Esc、对话、多人界面等打断退出乐器界面），
/// 立即暂停播放并松开全部按键，避免按键继续打到游戏里造成误操作。
/// 该服务只负责判断与暂停，界面与取样状态由播放页展示。
/// </summary>
public sealed class GameInputGuardService : IDisposable
{
    // 每 tick 只读 5 个像素，30ms 的代价可以忽略；
    // 反应时间基本由这个间隔决定：界面消失后约 30ms 到 60ms 内暂停。
    private const int SampleIntervalMilliseconds = 30;

    // 自动取样失败时的重试次数，避免乐器界面还没打开就放弃守卫。
    private const int MaxSampleAttempts = 8;

    private readonly MidiPlayer player;
    private readonly InstrumentUiSampler sampler;
    private readonly GameInputGuard guard = new();
    private readonly object gate = new();
    private Timer? timer;
    private int sampleAttempts;
    private bool disposed;

    /// <summary>用播放器和按键发送器构造守卫服务。</summary>
    /// <param name="player">负责播放与暂停的播放器。</param>
    /// <param name="gameKeys">负责查找原神窗口的发送器。</param>
    public GameInputGuardService(MidiPlayer player, GameKeySender gameKeys)
    {
        this.player = player;
        sampler = new InstrumentUiSampler(gameKeys);
    }

    /// <summary>用户是否开启了守卫，由播放页复选框驱动。</summary>
    public bool Enabled { get; set; }

    /// <summary>守卫是否可用，即已经取到颜色签名。</summary>
    public bool HasSignature => guard.Enabled;

    /// <summary>颜色签名里的取样点数量。</summary>
    public int SampleCount => guard.Signature?.Points.Count ?? 0;

    /// <summary>当前状态描述，变化时通过 <see cref="StatusChanged"/> 通知界面。</summary>
    public string Status { get; private set; } = "未开启";

    /// <summary>状态变化事件，在任意线程触发。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>开始周期性检测，重复调用无副作用。</summary>
    public void Start()
    {
        lock (gate)
        {
            if (disposed || timer is not null) return;
            sampleAttempts = 0;
            timer = new Timer(OnTick, null, SampleIntervalMilliseconds, SampleIntervalMilliseconds);
        }
    }

    /// <summary>停止周期性检测并重置连续未命中计数。</summary>
    public void Stop()
    {
        lock (gate)
        {
            timer?.Dispose();
            timer = null;
        }

        guard.Reset();
        sampleAttempts = 0;
    }

    private void OnTick(object? state)
    {
        // 守卫回调里不要 Stop()：定时器在自身回调中释放会等待回调结束。
        // 暂停后 IsPlaying 变假，这里自然空转，界面事件里再停止定时器。
        try
        {
            if (!Enabled) return;
            if (!player.PlayInGame || !player.IsPlaying) return;

            if (player.GameOutput is not GameKeySender sender) return;
            if (!sender.HasWindow && !sender.RefreshWindow())
            {
                Pause("未找到原神窗口，已暂停并松开按键");
                return;
            }

            // 还没有签名时先自动取样：正常流程是打开乐器界面后才按播放，
            // 这时候退出按钮就在原位，不需要用户手动点取样。
            if (!guard.Enabled)
            {
                TryAutoSample();
                return;
            }

            if (guard.Signature is null) return;

            var observed = sampler.Sample(guard.Signature);
            if (observed.Count == 0) return;

            // 全黑或纯色是读取失败（窗口被完全遮住、独占全屏等），
            // 属于未知状态，不能当成界面已关闭。
            if (InstrumentUiSignature.IsUniform(observed)) return;

            var ratio = guard.Signature.MatchRatio(observed);
            if (guard.Check(ratio) == GuardDecision.Pause)
            {
                Pause($"检测到演奏界面已关闭（匹配度 {ratio:P0}），已暂停并松开按键");
            }
        }
        catch (Exception ex)
        {
            Log("守卫检测异常：" + ex.Message);
        }
    }

    /// <summary>
    /// 播放开始时自动取样。取到的点和按钮外侧背景太接近，说明乐器界面没打开，
    /// 这时继续重试；重试用尽才把结论写进状态，让用户知道守卫为什么没生效。
    /// </summary>
    private void TryAutoSample()
    {
        if (sampleAttempts >= MaxSampleAttempts)
        {
            SetStatus("自动取样失败：请在乐器界面打开时播放");
            return;
        }

        sampleAttempts++;
        var sample = sampler.Capture();
        if (sample.Signature is null)
        {
            SetStatus("读取不到原神画面，正在重试");
            return;
        }

        // 全黑或纯色说明窗口现在读不到内容，等它可见了再取，不消耗重试次数。
        if (sample.Blank)
        {
            sampleAttempts--;
            SetStatus("读取到的画面是空白，等待原神可见…");
            return;
        }

        if (!sample.LooksLikeButton)
        {
            SetStatus($"取样自检未通过（{sampleAttempts}/{MaxSampleAttempts}）：请确认乐器界面已打开");
            return;
        }

        guard.Arm(sample.Signature);
        sampleAttempts = 0;
        Log($"自动取样成功，{sample.Signature.Points.Count} 个点");
        SetStatus($"已取样 {sample.Signature.Points.Count} 个点，退出乐器界面会自动暂停");
    }

    private void Pause(string reason)
    {
        guard.Reset();
        Log("守卫 " + reason);
        SetStatus(reason);
        // MidiPlayer.Pause 内部会 ReleaseAll，松开所有仍按住的琴键。
        player.Pause();
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private static void Log(string text)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GenshinMusicStudio");
            Directory.CreateDirectory(dir);
            File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss}] GUARD {text}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    /// <summary>停止检测并释放定时器。</summary>
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            timer?.Dispose();
            timer = null;
        }

        GC.SuppressFinalize(this);
    }
}
