namespace GenshinMusicStudio.Core;

/// <summary>
/// Maps MIDI pitches to the 21-key lyre layout used by the application.
/// The layout spans three diatonic octaves from C3 through B5.
/// </summary>
public static class Key21Layout
{
    /// <summary>Number of playable keys in the standard 21-key lyre.</summary>
    public const int KeyCount = 21;

    /// <summary>Lowest playable MIDI pitch (C3).</summary>
    public const int LowPitch = 48;

    /// <summary>Highest playable MIDI pitch (B5).</summary>
    public const int HighPitch = 83;

    /// <summary>Display names for the seven scale degrees.</summary>
    public static readonly string[] DegreeNames = { "do", "re", "mi", "fa", "sol", "la", "si" };

    private static readonly int[] ChromaticToDiatonic = { 0, 2, 2, 4, 4, 5, 7, 7, 9, 9, 11, 11 };
    private static readonly int[] SemitoneToDegree = { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6 };
    private static readonly int[] DegreeSemitone = { 0, 2, 4, 5, 7, 9, 11 };
    private static readonly string[] PhysicalKeyRows = { "ZXCVBNM", "ASDFGHJ", "QWERTYU" };

    /// <summary>Returns the semitone offset for a scale degree, or zero for an invalid degree.</summary>
    /// <param name="degree">Zero-based scale degree.</param>
    /// <returns>The semitone offset in the C major scale.</returns>
    public static int DegreeToSemitone(int degree)
    {
        return degree >= 0 && degree < DegreeSemitone.Length ? DegreeSemitone[degree] : 0;
    }

    /// <summary>Folds an arbitrary MIDI pitch into the playable 21-key range by octaves.</summary>
    /// <param name="pitch">MIDI pitch to fold.</param>
    /// <returns>A pitch between <see cref="LowPitch"/> and <see cref="HighPitch"/>.</returns>
    public static int FoldPitch(int pitch)
    {
        while (pitch < LowPitch) pitch += 12;
        while (pitch > HighPitch) pitch -= 12;
        return pitch;
    }

    /// <summary>Maps a MIDI pitch to a zero-based key index.</summary>
    /// <param name="pitch">MIDI pitch to map.</param>
    /// <returns>A key index between zero and twenty.</returns>
    public static int KeyIndexForPitch(int pitch)
    {
        pitch = FoldPitch(pitch);
        var mapped = ChromaticToDiatonic[pitch % 12];
        var degree = SemitoneToDegree[mapped];
        var octave = Math.Clamp((pitch - LowPitch) / 12, 0, 2);
        return octave * 7 + degree;
    }

    /// <summary>
    /// Physical QWERTY key label for a pitch, matching the on-screen keyboard:
    /// the top row plays the highest octave and the bottom row the lowest.
    /// </summary>
    /// <param name="pitch">MIDI pitch to map.</param>
    /// <returns>A one-character key label suitable for the game key sender.</returns>
    public static string PhysicalKeyForPitch(int pitch)
    {
        pitch = FoldPitch(pitch);
        var mapped = ChromaticToDiatonic[pitch % 12];
        var degree = SemitoneToDegree[mapped];
        var octave = Math.Clamp((pitch - LowPitch) / 12, 0, 2);
        return PhysicalKeyRows[octave][degree].ToString();
    }

    /// <summary>Converts a key index to its visual row and column coordinates.</summary>
    /// <param name="keyIndex">Zero-based key index.</param>
    /// <returns>The row and column used by the keyboard visualizer.</returns>
    public static (int Row, int Column) KeyPosition(int keyIndex)
    {
        var octave = keyIndex / 7;
        var degree = keyIndex % 7;
        return (2 - octave, degree);
    }
}
