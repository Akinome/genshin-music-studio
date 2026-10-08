namespace GenshinMusicStudio_WinUI.Services;

public sealed class InstrumentLayout
{
    private static readonly int[] ChromaticToDiatonic = { 0, 2, 2, 4, 4, 5, 7, 7, 9, 9, 11, 11 };
    private static readonly int[] SemitoneToDegree = { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6 };
    private static readonly int[] DegreeToSemitone = { 0, 2, 4, 5, 7, 9, 11 };

    public int Octaves { get; init; } = 3;
    public int LowOctave { get; init; }
    public bool ByMidiName { get; init; }

    public int KeyCount => Octaves * 7;
    public int LowPitch => LowOctave * 12 + 48;
    public int HighPitch => LowPitch + Octaves * 12 - 1;
    public int TopOctave => LowOctave + Octaves - 1;

    public int FoldPitch(int pitch)
    {
        while (pitch < LowPitch) pitch += 12;
        while (pitch > HighPitch) pitch -= 12;
        return pitch;
    }

    public int KeyIndexForPitch(int pitch)
    {
        pitch = FoldPitch(pitch);
        var mapped = ChromaticToDiatonic[pitch % 12];
        var degree = SemitoneToDegree[mapped];
        var octave = (pitch - 48) / 12;
        return (TopOctave - octave) * 7 + degree;
    }

    public int PitchForKeyIndex(int keyIndex)
    {
        var octave = TopOctave - keyIndex / 7;
        var semitone = DegreeToSemitone[keyIndex % 7];
        return octave * 12 + 48 + semitone;
    }

    public static InstrumentLayout FromShape(string shape, bool byMidiName)
    {
        return shape switch
        {
            "genshin-2x7" => new InstrumentLayout { Octaves = 2, LowOctave = 0, ByMidiName = byMidiName },
            "genshin-2x7-high" => new InstrumentLayout { Octaves = 2, LowOctave = 1, ByMidiName = byMidiName },
            _ => new InstrumentLayout { Octaves = 3, LowOctave = 0, ByMidiName = byMidiName },
        };
    }
}
