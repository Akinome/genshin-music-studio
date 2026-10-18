using System.Globalization;

namespace GenshinMusicStudio.Core;

/// <summary>
/// 单个 MIDI 文件在 21 键风物之诗琴上的可播率统计结果。
/// 统计口径：先按八度折叠到琴的音域，再判断是否精确命中 21 个琴键音高。
/// </summary>
public sealed record MidiPlayability(int TotalNotes, int LyrePitchNotes)
{
    /// <summary>不能直接用 21 键弹奏的音符数。</summary>
    public int NonLyrePitchNotes => TotalNotes - LyrePitchNotes;

    /// <summary>可播率，范围 0~1。</summary>
    public double Ratio => TotalNotes == 0 ? 0 : (double)LyrePitchNotes / TotalNotes;

    /// <summary>可播率显示文本，例如 87.3%。</summary>
    public string RatioText => (Ratio * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    /// <summary>可播数量明细，例如 156 / 178 个音符。</summary>
    public string DetailText => TotalNotes == 0
        ? "没有可统计音符"
        : $"{LyrePitchNotes} / {TotalNotes} 个音符";
}

/// <summary>
/// 计算 MIDI 音高在 21 键风物之诗琴上的可播率。
/// 该口径与 BetterGenshinImpact 的默认风物之诗琴 MappingMode 一致：
/// 先八度折叠，再做精确音高匹配，因此黑键通常会被判为不可播。
/// </summary>
public static class MidiPlayabilityCalculator
{
    /// <summary>21 键风物之诗琴对应的 C3~B5 音高集合。</summary>
    public static readonly IReadOnlyList<int> LyrePitches = new[]
    {
        48, 50, 52, 53, 55, 57, 59,
        60, 62, 64, 65, 67, 69, 71,
        72, 74, 76, 77, 79, 81, 83,
    };

    /// <summary>
    /// 尝试把任意 MIDI 音高映射到 21 键琴上的某个音高。
    /// </summary>
    /// <param name="pitch">原始 MIDI 音高。</param>
    /// <param name="lyrePitch">成功时返回折叠后的琴键音高。</param>
    /// <returns>可以精确命中 21 键时返回 true。</returns>
    public static bool TryMapToLyrePitch(int pitch, out int lyrePitch)
    {
        var value = FoldToLyreRange(pitch);
        lyrePitch = value;
        return LyrePitches.Contains(value);
    }

    /// <summary>
    /// 判断一个 MIDI 音高是否能精确命中 21 键风物之诗琴。
    /// </summary>
    /// <param name="pitch">原始 MIDI 音高。</param>
    /// <returns>可弹时返回 true。</returns>
    public static bool IsLyrePitch(int pitch) => TryMapToLyrePitch(pitch, out _);

    /// <summary>
    /// 按音符音高统计 21 键可播率。
    /// </summary>
    /// <param name="pitches">MIDI 文件的音符音高序列。</param>
    /// <returns>可播率统计结果。</returns>
    public static MidiPlayability Analyze(IEnumerable<int> pitches)
    {
        ArgumentNullException.ThrowIfNull(pitches);

        var total = 0;
        var mapped = 0;
        foreach (var pitch in pitches)
        {
            total++;
            if (IsLyrePitch(pitch)) mapped++;
        }

        return new MidiPlayability(total, mapped);
    }

    private static int FoldToLyreRange(int pitch)
    {
        var low = LyrePitches[0];
        var high = LyrePitches[^1];
        while (pitch < low) pitch += 12;
        while (pitch > high) pitch -= 12;
        return pitch;
    }
}
