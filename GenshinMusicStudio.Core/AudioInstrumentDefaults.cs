namespace GenshinMusicStudio.Core;

/// <summary>
/// Resolves the audio-instrument selection shared by the WinUI pages and the
/// startup restoration code.
/// </summary>
public static class AudioInstrumentDefaults
{
    /// <summary>Folder name of the default Genshin lyre.</summary>
    public const string DefaultInstrumentName = "Lyre";

    /// <summary>
    /// Resolves the saved selection against the current application's asset
    /// directory. A missing setting means the default lyre, while an explicit
    /// empty string keeps the Windows synthesizer selected.
    /// </summary>
    /// <param name="savedInstrument">The saved path or instrument folder name.</param>
    /// <param name="instrumentsRoot">Current application's bundled instrument root.</param>
    /// <returns>An instrument directory, or an empty string for the Windows synthesizer.</returns>
    public static string Resolve(string? savedInstrument, string instrumentsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentsRoot);

        if (savedInstrument is not null && savedInstrument.Length == 0)
        {
            return string.Empty;
        }

        var name = string.IsNullOrWhiteSpace(savedInstrument)
            ? DefaultInstrumentName
            : Path.GetFileName(savedInstrument.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var bundled = Path.Combine(instrumentsRoot, name);
        if (Directory.Exists(bundled))
        {
            return bundled;
        }

        if (!string.IsNullOrWhiteSpace(savedInstrument)
            && Directory.Exists(savedInstrument))
        {
            return savedInstrument;
        }

        var defaultInstrument = Path.Combine(instrumentsRoot, DefaultInstrumentName);
        return Directory.Exists(defaultInstrument) ? defaultInstrument : string.Empty;
    }
}
