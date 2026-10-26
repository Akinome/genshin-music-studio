namespace GenshinMusicStudio.Core;

/// <summary>
/// Keeps the duration used by the player page consistent across the
/// player's progress event and the page's animation-frame refresh.
/// </summary>
public sealed class PlaybackProgressState
{
    /// <summary>The most recently reported playlist duration.</summary>
    public double PlaylistDuration { get; private set; }

    /// <summary>Records the authoritative duration reported by the player.</summary>
    public void UpdateFromPlayer(double total)
    {
        if (total > 0)
        {
            PlaylistDuration = total;
        }
    }

    /// <summary>
    /// Resolves the duration shown by the page's frame refresh.
    /// </summary>
    public double ResolveFrameDuration(double playerDuration, double sheetDuration)
    {
        if (playerDuration > 0)
        {
            return playerDuration;
        }

        if (PlaylistDuration > 0)
        {
            return PlaylistDuration;
        }

        return sheetDuration;
    }

    /// <summary>Calculates the progress-bar percentage for a position.</summary>
    public double CalculatePercent(double position, double total)
        => total > 0 ? Math.Clamp(position / total * 100, 0, 100) : 0;
}
