using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class NativeArrangementTests
{
    [Fact]
    public void OptimizeNotes_MergesShortFragmentsAndQuantizes()
    {
        var notes = new[]
        {
            new NativeNote(0.01, 0.03, 60, 0.4),
            new NativeNote(0.04, 0.06, 60, 0.8),
        };

        var result = NativeArrangement.OptimizeNotes(notes);

        var note = Assert.Single(result);
        Assert.Equal(0, note.Start);
        Assert.Equal(0.05, note.End, 6);
        Assert.Equal(0.8, note.Amplitude, 6);
    }

    [Fact]
    public void OptimizeNotes_DoesNotMergeLongNotes()
    {
        var notes = new[]
        {
            new NativeNote(0, 0.10, 60, 0.4),
            new NativeNote(0.11, 0.20, 60, 0.8),
        };

        var result = NativeArrangement.OptimizeNotes(notes);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Start);
        Assert.Equal(0.10, result[0].End, 6);
        Assert.Equal(0.10, result[1].Start, 6);
        Assert.Equal(0.20, result[1].End, 6);
    }

    [Fact]
    public void OptimizeNotes_ExtendsShortNotesToMinimumLength()
    {
        var notes = new[] { new NativeNote(0.01, 0.02, 64, 0.5) };

        var result = NativeArrangement.OptimizeNotes(notes, gridMs: 100, minimumMs: 150);

        var note = Assert.Single(result);
        Assert.Equal(0, note.Start);
        Assert.Equal(0.15, note.End, 6);
    }

    [Fact]
    public void OptimizeNotes_ReturnsEmptyForEmptyInput()
    {
        Assert.Empty(NativeArrangement.OptimizeNotes(Array.Empty<NativeNote>()));
    }

    [Fact]
    public void OptimizeNotes_RejectsInvalidParameters()
    {
        Assert.Throws<ArgumentNullException>(() => NativeArrangement.OptimizeNotes(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NativeArrangement.OptimizeNotes(Array.Empty<NativeNote>(), gridMs: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NativeArrangement.OptimizeNotes(Array.Empty<NativeNote>(), minimumMs: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NativeArrangement.OptimizeNotes(Array.Empty<NativeNote>(), mergeGapMs: -1));
    }

    [Fact]
    public void ExtractMelody_ReturnsEmptyForEmptyInput()
    {
        Assert.Empty(NativeArrangement.ExtractMelody(Array.Empty<NativeNote>()));
    }

    [Fact]
    public void ExtractMelody_SelectsOneNotePerTimeSlice()
    {
        var notes = new[]
        {
            new NativeNote(0, 0.2, 60, 0.9),
            new NativeNote(0, 0.2, 64, 0.5),
            new NativeNote(0.1, 0.3, 65, 0.8),
        };

        var result = NativeArrangement.ExtractMelody(notes, minGapMs: 0);

        Assert.Equal(2, result.Count);
        Assert.All(result, note => Assert.InRange(note.Pitch, Key21Layout.LowPitch, Key21Layout.HighPitch));
    }

    [Fact]
    public void ExtractMelody_FiltersNotesThatAreTooClose()
    {
        var notes = new[]
        {
            new NativeNote(0, 0.1, 60, 0.9),
            new NativeNote(0.05, 0.2, 62, 0.8),
            new NativeNote(0.20, 0.3, 64, 0.8),
        };

        var result = NativeArrangement.ExtractMelody(notes, minGapMs: 100);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Start, 6);
        Assert.Equal(0.20, result[1].Start, 6);
    }

    [Fact]
    public void ExtractMelody_FoldsNotesOutsideTheLyreRange()
    {
        var notes = new[]
        {
            new NativeNote(0, 0.1, 100, 0.9),
            new NativeNote(0.2, 0.3, 30, 0.9),
        };

        var result = NativeArrangement.ExtractMelody(notes, minGapMs: 0);

        Assert.All(result, note => Assert.InRange(note.Pitch, Key21Layout.LowPitch, Key21Layout.HighPitch));
    }

    [Fact]
    public void ExtractMelody_SmoothsOctaveSpikeDownward()
    {
        var notes = new[]
        {
            new NativeNote(0, 0.1, 60, 1),
            new NativeNote(0.1, 0.2, 84, 1),
            new NativeNote(0.2, 0.3, 60, 1),
        };

        var result = NativeArrangement.ExtractMelody(notes, minGapMs: 0);

        Assert.Equal(72, result[1].Pitch);
    }

    [Fact]
    public void ExtractMelody_SmoothsOctaveSpikeUpward()
    {
        var notes = new[]
        {
            new NativeNote(0, 0.1, 72, 1),
            new NativeNote(0.1, 0.2, 48, 1),
            new NativeNote(0.2, 0.3, 72, 1),
        };

        var result = NativeArrangement.ExtractMelody(notes, minGapMs: 0);

        Assert.Equal(60, result[1].Pitch);
    }

    [Fact]
    public void ExtractMelody_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => NativeArrangement.ExtractMelody(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NativeArrangement.ExtractMelody(Array.Empty<NativeNote>(), -1));
    }
}
