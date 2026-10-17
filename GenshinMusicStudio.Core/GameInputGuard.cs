namespace GenshinMusicStudio.Core;

/// <summary>守卫对本次取样给出的结论。</summary>
public enum GuardDecision
{
    /// <summary>守卫未启用或缺少颜色签名，不做判断。</summary>
    Disabled,

    /// <summary>乐器界面仍在，继续演奏。</summary>
    KeepPlaying,

    /// <summary>乐器界面已经消失，请求暂停并松开按键。</summary>
    Pause,
}

/// <summary>
/// 演奏界面守卫。每次取样只回答“乐器界面还在不在”，
/// 匹配度几乎归零时立刻暂停，略低则连续若干次未命中才暂停，
/// 避免偶发的按键高亮或画面抖动打断演奏。
/// 暂停由调用方执行，这里只负责给出结论，方便测试。
/// </summary>
public sealed class GameInputGuard
{
    /// <summary>默认连续未命中国家数。</summary>
    public const int DefaultMissThreshold = 2;

    /// <summary>默认匹配比例阈值，低于该比例视为一次未命中。</summary>
    public const double DefaultMatchThreshold = 0.5;

    /// <summary>
    /// 默认“几乎完全对不上”的阈值。匹配度跌到这里说明乐器界面确实已经没了，
    /// 立刻暂停，不再等连续未命中，避免这一小段时间继续把按键打进游戏。
    /// </summary>
    public const double DefaultHardMatchThreshold = 0.25;

    private readonly int missThreshold;
    private readonly double hardMatchThreshold;

    /// <summary>用阈值构造守卫。</summary>
    /// <param name="missThreshold">触发暂停所需的连续未命中国家数。</param>
    /// <param name="matchThreshold">匹配比例阈值。</param>
    /// <param name="hardMatchThreshold">立即暂停的匹配比例阈值。</param>
    public GameInputGuard(
        int missThreshold = DefaultMissThreshold,
        double matchThreshold = DefaultMatchThreshold,
        double hardMatchThreshold = DefaultHardMatchThreshold)
    {
        this.missThreshold = Math.Max(1, missThreshold);
        MatchThreshold = matchThreshold is < 0 or > 1 ? DefaultMatchThreshold : matchThreshold;
        this.hardMatchThreshold = hardMatchThreshold is < 0 or > 1 ? DefaultHardMatchThreshold : hardMatchThreshold;
    }

    /// <summary>触发暂停所需的连续未命中国家数。</summary>
    public int MissThreshold => missThreshold;

    /// <summary>匹配比例阈值，低于该比例视为一次未命中。</summary>
    public double MatchThreshold { get; }

    /// <summary>低于该比例立即暂停，不论是否连续未命中。</summary>
    public double HardMatchThreshold => hardMatchThreshold;

    /// <summary>守卫是否可用，即已装入颜色签名。</summary>
    public bool Enabled { get; private set; }

    /// <summary>当前使用的颜色签名。</summary>
    public InstrumentUiSignature? Signature { get; private set; }

    /// <summary>当前连续未命中次数。</summary>
    public int ConsecutiveMisses { get; private set; }

    /// <summary>最近一次取样得到的匹配比例。</summary>
    public double LastMatchRatio { get; private set; }

    /// <summary>装入颜色签名并启用守卫，签名不可用时保持停用。</summary>
    /// <param name="signature">在乐器界面打开时取到的颜色签名，可为 null。</param>
    public void Arm(InstrumentUiSignature? signature)
    {
        Signature = signature is { HasPoints: true } ? signature : null;
        Enabled = Signature is not null;
        ConsecutiveMisses = 0;
    }

    /// <summary>停用守卫并清空签名。</summary>
    public void Disarm()
    {
        Enabled = false;
        Signature = null;
        ConsecutiveMisses = 0;
    }

    /// <summary>用一次取样结果更新守卫状态并给出结论。</summary>
    /// <param name="matchRatio">本次取样的匹配比例。</param>
    /// <returns>守卫结论。</returns>
    public GuardDecision Check(double matchRatio)
    {
        LastMatchRatio = matchRatio;
        if (!Enabled || Signature is null) return GuardDecision.Disabled;

        if (matchRatio >= MatchThreshold)
        {
            ConsecutiveMisses = 0;
            return GuardDecision.KeepPlaying;
        }

        // 几乎完全对不上，乐器界面已经不在了：第一次取样就暂停。
        if (matchRatio <= hardMatchThreshold)
        {
            ConsecutiveMisses = 0;
            return GuardDecision.Pause;
        }

        ConsecutiveMisses++;
        if (ConsecutiveMisses >= missThreshold)
        {
            ConsecutiveMisses = 0;
            return GuardDecision.Pause;
        }

        return GuardDecision.KeepPlaying;
    }

    /// <summary>重置连续未命中计数，例如暂停或切歌后重新开始。</summary>
    public void Reset() => ConsecutiveMisses = 0;
}
