namespace GenshinMusicStudio.Core;

/// <summary>
/// Identifies the MIDI file whose sheet should be visible in the player UI.
/// </summary>
public sealed record PlaybackSheetContext(
    string FilePath,
    double TimeOffset,
    double TimelineDuration);

/// <summary>Resolves the sheet context for single-file and directory playback.</summary>
public static class PlaybackSheetResolver
{
    /// <summary>
    /// Resolves the sheet that should be restored when the player page is
    /// opened again while playback is active.
    /// </summary>
    public static PlaybackSheetContext? ResolvePageContext(
        string? playingFolder,
        string? currentFile,
        double currentFileStart,
        double timelineDuration)
    {
        if (string.IsNullOrWhiteSpace(currentFile))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(playingFolder))
        {
            return ResolveDirectoryContext(
                playingFolder,
                currentFile,
                currentFileStart,
                timelineDuration);
        }

        return new PlaybackSheetContext(
            currentFile,
            0,
            Math.Max(0, timelineDuration));
    }

    /// <summary>
    /// Returns the directory-playback sheet context when one is available.
    /// </summary>
    public static PlaybackSheetContext? ResolveDirectoryContext(
        string? playingFolder,
        string? currentFile,
        double currentFileStart,
        double timelineDuration)
    {
        if (string.IsNullOrWhiteSpace(playingFolder)
            || string.IsNullOrWhiteSpace(currentFile))
        {
            return null;
        }

        return new PlaybackSheetContext(
            currentFile,
            Math.Max(0, currentFileStart),
            Math.Max(0, timelineDuration));
    }

    /// <summary>Offsets note times onto the playlist's absolute timeline.</summary>
    public static List<ParsedMidiNote> OffsetNotes(
        IEnumerable<ParsedMidiNote> notes,
        double timeOffset)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var offset = Math.Max(0, timeOffset);
        return notes
            .Select(note => note with
            {
                Start = note.Start + offset,
                End = note.End + offset,
            })
            .ToList();
    }
}
