namespace GenshinMusicStudio.Core;

/// <summary>
/// 把 MIDI 音符事件整理成原神可接收的按键时间线。
/// 原神只在检测到“新的按下沿”时发声，所以同一按键发生交叠音符时，
/// 必须先补一次松开，再发送下一次按下。
/// 该实现对应 BetterGenshinImpact PR #3747 的修复。
/// </summary>
public static class GameKeyTimeline
{
    private const double RetriggerGapSeconds = 0.030;

    /// <summary>
    /// 为同键交叠音符补发重触发松开，并丢弃旧按住对应的过期松开事件。
    /// </summary>
    /// <param name="events">播放列表中的原始音符事件。</param>
    /// <returns>按时间稳定排序、且同键按下和松开交替正确的事件流。</returns>
    public static List<MidiPlaybackEvent> Normalize(IReadOnlyList<MidiPlaybackEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        // OrderBy 是稳定排序，不要额外加“松开优先”的二次排序。
        // 否则补发的松开会被排到同一时刻的按下后面，又会变成连续两次按下。
        var sorted = events.OrderBy(item => item.Time).ToList();
        var activeCounts = new Dictionary<char, int>();
        var holdStarts = new Dictionary<char, double>();
        var result = new List<MidiPlaybackEvent>(sorted.Count);

        foreach (var item in sorted)
        {
            var key = GameKeyForPitch(item.Pitch);
            if (key is null) continue;

            activeCounts.TryGetValue(key.Value, out var activeCount);
            if (item.IsOn)
            {
                if (activeCount > 0 && holdStarts.TryGetValue(key.Value, out var holdStart))
                {
                    // 如果距离下一次按下不足 30ms，就把松开放在按下同一时刻。
                    // 稳定排序仍会保证松开排在下一次按下之前。
                    var retriggerTime = item.Time - RetriggerGapSeconds;
                    result.Add(item with
                    {
                        Time = retriggerTime > holdStart ? retriggerTime : item.Time,
                        IsOn = false,
                        Velocity = 0,
                    });
                }

                result.Add(item);
                holdStarts[key.Value] = item.Time;
                activeCounts[key.Value] = activeCount + 1;
            }
            else
            {
                if (activeCount == 0) continue;

                var remaining = activeCount - 1;
                activeCounts[key.Value] = remaining;
                if (remaining == 0)
                {
                    result.Add(item);
                    holdStarts.Remove(key.Value);
                }

                // 这条松开属于已经被“重触发松开”提前关闭的旧按住，
                // 因此不能再发送，否则会让后续按下失去配对。
            }
        }

        // 第二遍处理：如果松开与下一次按下发生在完全相同的时间点，
        // 原神可能把它们合并成一次重复按下。只有前一次按下更早时，
        // 才安全地把松开提前 30ms，形成真正可识别的重触发间隔。
        var previousDown = new Dictionary<char, double>();
        var previousUpIndex = new Dictionary<char, int>();
        for (var index = 0; index < result.Count; index++)
        {
            var item = result[index];
            var key = GameKeyForPitch(item.Pitch);
            if (key is null) continue;

            if (item.IsOn)
            {
                if (previousUpIndex.TryGetValue(key.Value, out var upIndex)
                    && result[upIndex].Time == item.Time
                    && previousDown.TryGetValue(key.Value, out var downTime))
                {
                    var earlyTime = item.Time - RetriggerGapSeconds;
                    if (earlyTime > downTime)
                    {
                        result[upIndex] = result[upIndex] with { Time = earlyTime };
                    }
                }

                previousDown[key.Value] = item.Time;
            }
            else
            {
                previousUpIndex[key.Value] = index;
            }
        }

        // 第三遍处理：原神按帧轮询键盘状态，同一帧内的多个 KEYDOWN
        // 可能只保留最后一个。软件内播放可以混音，但游戏内必须把所有
        // 按下事件拉开到至少 30ms，才能保证和弦和密集音符逐个触发。
        var nextKeyDownTime = double.NegativeInfinity;
        var lastScheduledDown = new Dictionary<char, double>();
        for (var index = 0; index < result.Count; index++)
        {
            var item = result[index];
            var key = GameKeyForPitch(item.Pitch);
            if (key is null) continue;

            if (item.IsOn)
            {
                var scheduledTime = Math.Max(item.Time, nextKeyDownTime);
                result[index] = item with { Time = scheduledTime };
                nextKeyDownTime = scheduledTime + RetriggerGapSeconds;
                lastScheduledDown[key.Value] = scheduledTime;
            }
            else if (lastScheduledDown.TryGetValue(key.Value, out var downTime))
            {
                // 按下被顺延后，对应松开不能早于按下本身。
                result[index] = item with { Time = Math.Max(item.Time, downTime + 0.001) };
            }
        }

        return result.OrderBy(item => item.Time).ToList();
    }

    private static char? GameKeyForPitch(int pitch)
    {
        var label = Key21Layout.PhysicalKeyForPitch(pitch);
        return string.IsNullOrEmpty(label) ? null : label[0];
    }
}
