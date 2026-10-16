namespace GenshinMusicStudio.Core;

/// <summary>
/// Normalizes mapped note events for game-key injection. Genshin only emits a
/// note on a fresh key-down edge, so overlapping notes on the same key must be
/// separated by a short release before the next press. This mirrors the fix in
/// BetterGenshinImpact PR #3747.
/// </summary>
public static class GameKeyTimeline
{
    private const double RetriggerGapSeconds = 0.030;

    /// <summary>
    /// Adds retrigger releases for overlapping notes on the same physical key
    /// and drops the corresponding stale releases from the original stream.
    /// </summary>
    /// <param name="events">Raw note events in playlist order.</param>
    /// <returns>A stable, time-ordered stream with alternating key transitions.</returns>
    public static List<MidiPlaybackEvent> Normalize(IReadOnlyList<MidiPlaybackEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        // OrderBy is stable. Do not add a secondary "KeyUp first" sort: that
        // would move a generated release behind the press at the same tick and
        // turn the stream into down/down again.
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
                    // If there is not enough room for the full 30 ms gap, put
                    // the release at the press time so it still sorts first.
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

                // A stale release belongs to a hold that was already closed by
                // a generated retrigger release, so it must not be emitted.
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
