namespace GenshinMusicStudio_WinUI.Services;

/// <summary>The result of checking whether in-game playback can safely start.</summary>
public enum GamePlaybackStartResult
{
    /// <summary>The game can receive the playback keys.</summary>
    Ready,

    /// <summary>The application must be restarted with administrator privileges.</summary>
    RequiresAdministrator,

    /// <summary>The game window could not be brought to the foreground.</summary>
    GameWindowNotFound,
}

/// <summary>Shared preflight checks for playback started from any page.</summary>
public static class GamePlaybackPrerequisites
{
    /// <summary>
    /// Checks administrator access and, for foreground playback, brings the
    /// game window to the front before any notes are sent.
    /// </summary>
    public static GamePlaybackStartResult Check(
        GameKeySender gameKeys,
        bool playInGame,
        bool backgroundMode)
    {
        ArgumentNullException.ThrowIfNull(gameKeys);
        if (!playInGame) return GamePlaybackStartResult.Ready;
        if (!GameKeySender.IsProcessElevated())
        {
            return GamePlaybackStartResult.RequiresAdministrator;
        }
        if (!backgroundMode && !gameKeys.FocusGame())
        {
            return GamePlaybackStartResult.GameWindowNotFound;
        }
        return GamePlaybackStartResult.Ready;
    }
}
