namespace GenshinMusicStudio_WinUI.Services;

public static class Key21Layout
{
    public const int KeyCount = 21;
    public const int LowPitch = 48;
    public const int HighPitch = 83;
    public static readonly string[] DegreeNames = { "do", "re", "mi", "fa", "sol", "la", "si" };

    private static readonly int[] ChromaticToDiatonic = { 0, 2, 2, 4, 4, 5, 7, 7, 9, 9, 11, 11 };
    private static readonly int[] SemitoneToDegree = { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6 };

    public static int FoldPitch(int pitch)
    {
        while (pitch < LowPitch) pitch += 12;
        while (pitch > HighPitch) pitch -= 12;
        return pitch;
    }

    public static int KeyIndexForPitch(int pitch)
    {
        pitch = FoldPitch(pitch);
        var mapped = ChromaticToDiatonic[pitch % 12];
        var degree = SemitoneToDegree[mapped];
        var octave = Math.Clamp((pitch - LowPitch) / 12, 0, 2);
        return octave * 7 + degree;
    }

    public static (int Row, int Column) KeyPosition(int keyIndex)
    {
        var octave = keyIndex / 7;
        var degree = keyIndex % 7;
        return (2 - octave, degree);
    }
}
