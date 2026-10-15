namespace GenshinMusicStudio.Core;

/// <summary>
/// Describes the playable range and visual shape of a Genshin instrument.
/// Instrument metadata chooses one of the known shapes at load time.
/// </summary>
public sealed class InstrumentLayout
{
    private static readonly int[] ChromaticToDiatonic = { 0, 2, 2, 4, 4, 5, 7, 7, 9, 9, 11, 11 };
    private static readonly int[] SemitoneToDegree = { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6 };
    private static readonly int[] DegreeToSemitone = { 0, 2, 4, 5, 7, 9, 11 };
    private static readonly int[] DrumPitches = { 60, 62, 64, 65, 72, 74, 76, 77 };

    /// <summary>Number of diatonic octaves in a melodic instrument layout.</summary>
    public int Octaves { get; init; } = 3;

    /// <summary>Lowest octave offset relative to C3.</summary>
    public int LowOctave { get; init; }

    /// <summary>True when instrument samples are named by MIDI pitch instead of key index.</summary>
    public bool ByMidiName { get; init; }

    /// <summary>True for the four-by-two drum-kit layout.</summary>
    public bool IsDrumKit { get; init; }

    /// <summary>Number of visual columns in the instrument layout.</summary>
    public int Columns => IsDrumKit ? 4 : 7;

    /// <summary>Number of playable keys in the instrument layout.</summary>
    public int KeyCount => IsDrumKit ? 8 : Octaves * 7;

    /// <summary>Lowest playable MIDI pitch.</summary>
    public int LowPitch => LowOctave * 12 + 48;

    /// <summary>Highest playable MIDI pitch.</summary>
    public int HighPitch => LowPitch + Octaves * 12 - 1;

    /// <summary>Zero-based highest playable octave relative to C3.</summary>
    public int TopOctave => LowOctave + Octaves - 1;

    /// <summary>Folds an arbitrary MIDI pitch into this instrument's range by octaves.</summary>
    /// <param name="pitch">MIDI pitch to fold.</param>
    /// <returns>A pitch inside the instrument's playable range.</returns>
    public int FoldPitch(int pitch)
    {
        while (pitch < LowPitch) pitch += 12;
        while (pitch > HighPitch) pitch -= 12;
        return pitch;
    }

    /// <summary>Maps a MIDI pitch to a key index for this instrument.</summary>
    /// <param name="pitch">MIDI pitch to map.</param>
    /// <returns>The key index used by the instrument's sample table.</returns>
    public int KeyIndexForPitch(int pitch)
    {
        if (IsDrumKit)
        {
            return NearestDrumIndex(pitch);
        }

        pitch = FoldPitch(pitch);
        var mapped = ChromaticToDiatonic[pitch % 12];
        var degree = SemitoneToDegree[mapped];
        var octave = (pitch - 48) / 12;
        return (TopOctave - octave) * 7 + degree;
    }

    /// <summary>Maps a key index back to its preferred MIDI pitch.</summary>
    /// <param name="keyIndex">Zero-based key index.</param>
    /// <returns>The MIDI pitch represented by the key.</returns>
    public int PitchForKeyIndex(int keyIndex)
    {
        var octave = TopOctave - keyIndex / 7;
        var semitone = DegreeToSemitone[keyIndex % 7];
        return octave * 12 + 48 + semitone;
    }

    /// <summary>Creates a layout from the shape value stored in an instrument metadata file.</summary>
    /// <param name="shape">Shape identifier such as <c>genshin-3x7</c>.</param>
    /// <param name="byMidiName">True when samples are named by MIDI pitch.</param>
    /// <returns>The matching instrument layout.</returns>
    public static InstrumentLayout FromShape(string shape, bool byMidiName)
    {
        return shape switch
        {
            "genshin-2x7" => new InstrumentLayout { Octaves = 2, LowOctave = 0, ByMidiName = byMidiName },
            "genshin-2x7-high" => new InstrumentLayout { Octaves = 2, LowOctave = 1, ByMidiName = byMidiName },
            "genshin-2x4" => new InstrumentLayout { IsDrumKit = true, ByMidiName = byMidiName },
            _ => new InstrumentLayout { Octaves = 3, LowOctave = 0, ByMidiName = byMidiName },
        };
    }

    private static int NearestDrumIndex(int pitch)
    {
        while (pitch < DrumPitches[0]) pitch += 12;
        while (pitch > DrumPitches[^1]) pitch -= 12;

        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < DrumPitches.Length; i++)
        {
            var distance = Math.Abs(DrumPitches[i] - pitch);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }
}
