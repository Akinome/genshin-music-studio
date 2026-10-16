using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class MidiFileParserTests
{
    [Fact]
    public void Parse_HandlesTempoRunningStatusAndCommonMessages()
    {
        var track = new List<byte>();
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20);
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0xC0, 0x2E);
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0x90, 60, 100);
        Add(track, TestMidiBuilder.VarLen(480));
        Add(track, 0x80, 60, 0);

        // Running status: only data bytes are present.
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0x90, 61, 90);
        Add(track, TestMidiBuilder.VarLen(480));
        Add(track, 61, 0);

        // Channel pressure and control-change messages advance the parser.
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0xD0, 0x40);
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0xB0, 7, 127);
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0xF0, 2, 1, 2);
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0xFF, 0x2F, 0);

        var (events, duration) = MidiFileParser.Parse(TestMidiBuilder.CreateSingleTrack(track.ToArray()));

        Assert.Equal(4, events.Count);
        Assert.True(events[0].IsOn);
        Assert.Equal(60, events[0].Pitch);
        Assert.Equal(0.5, events[1].Time, 6);
        Assert.True(events[2].IsOn);
        Assert.Equal(61, events[2].Pitch);
        Assert.Equal(1.0, events[3].Time, 6);
        Assert.Equal(1.0, duration, 6);
    }

    [Fact]
    public void Parse_HandlesMultipleTracksAndDefaultMessageWidths()
    {
        var first = new List<byte>();
        Add(first, TestMidiBuilder.VarLen(0));
        Add(first, 0x90, 60, 100);
        Add(first, TestMidiBuilder.VarLen(120));
        Add(first, 0x80, 60, 0);

        var second = new List<byte>();
        Add(second, TestMidiBuilder.VarLen(0));
        Add(second, 0xE0, 0, 64);
        Add(second, TestMidiBuilder.VarLen(0));
        Add(second, 0x90, 64, 90);
        Add(second, TestMidiBuilder.VarLen(240));
        Add(second, 0x80, 64, 0);

        var (events, _) = MidiFileParser.Parse(
            TestMidiBuilder.CreateTwoTracks(first.ToArray(), second.ToArray()));

        Assert.Equal(4, events.Count);
        Assert.Contains(events, midiEvent => midiEvent.Pitch == 64 && midiEvent.IsOn);
    }

    [Fact]
    public void Parse_ClosesActiveNotesAtTheEndOfFile()
    {
        var track = new List<byte>();
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0x90, 60, 100);
        Add(track, TestMidiBuilder.VarLen(480));
        Add(track, 0xFF, 0x2F, 0);

        var bytes = TestMidiBuilder.CreateSingleTrack(track.ToArray());
        var path = NewTempPath();
        try
        {
            File.WriteAllBytes(path, bytes);

            var (notes, duration) = MidiFileParser.ParseNotes(path);

            var note = Assert.Single(notes);
            Assert.Equal(0.5, duration, 6);
            Assert.Equal(0.5, note.End, 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_PreservesReleaseBeforePressAtTheSameTick()
    {
        var track = new List<byte>();
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0x90, 60, 100);
        Add(track, TestMidiBuilder.VarLen(240));
        Add(track, 0x80, 60, 0);
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0x90, 60, 100);
        Add(track, TestMidiBuilder.VarLen(240));
        Add(track, 0x80, 60, 0);

        var (events, _) = MidiFileParser.Parse(TestMidiBuilder.CreateSingleTrack(track.ToArray()));

        Assert.Equal(new[] { true, false, true, false }, events.Select(item => item.IsOn));
        Assert.Equal(0.25, events[1].Time, 6);
        Assert.Equal(0.25, events[2].Time, 6);
    }

    [Fact]
    public void ParseNotes_RetriggersDuplicateNoteOnAndDropsZeroLengthNotes()
    {
        var track = new List<byte>();
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0x90, 60, 100);
        Add(track, TestMidiBuilder.VarLen(240));
        Add(track, 0x90, 60, 100);
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0x90, 61, 100);
        Add(track, TestMidiBuilder.VarLen(0));
        Add(track, 0x80, 61, 0);
        Add(track, TestMidiBuilder.VarLen(240));
        Add(track, 0x80, 60, 0);

        var path = NewTempPath();
        try
        {
            File.WriteAllBytes(path, TestMidiBuilder.CreateSingleTrack(track.ToArray()));
            var (notes, _) = MidiFileParser.ParseNotes(path);

            Assert.Equal(2, notes.Count);
            Assert.Equal(0, notes[0].Start, 6);
            Assert.Equal(0.25, notes[0].End, 6);
            Assert.Equal(0.25, notes[1].Start, 6);
            Assert.Equal(0.5, notes[1].End, 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_RejectsInvalidHeader()
    {
        Assert.Throws<ArgumentNullException>(() => MidiFileParser.Parse(null!));
        Assert.Throws<InvalidOperationException>(() => MidiFileParser.Parse(new byte[13]));
        Assert.Throws<InvalidOperationException>(() => MidiFileParser.Parse(new byte[14]));
    }

    [Fact]
    public void ParseFile_RejectsEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => MidiFileParser.ParseFile(""));
    }

    [Fact]
    public void Parse_StopsWhenTrackHeaderIsMissing()
    {
        var bytes = TestMidiBuilder.CreateSingleTrack(Array.Empty<byte>());
        bytes[14] = (byte)'X';
        var (events, duration) = MidiFileParser.Parse(bytes);

        Assert.Empty(events);
        Assert.Equal(0, duration);
    }

    [Fact]
    public void ParseVarLen_HandlesMultiByteAndMalformedValues()
    {
        var data = new byte[] { 0x81, 0x80, 0x01, 0x7F, 0x81 };
        var position = 0;

        Assert.Equal(16385, MidiFileParser.ReadVarLen(data, ref position, data.Length));
        Assert.Equal(0x7F, MidiFileParser.ReadVarLen(data, ref position, data.Length));
        Assert.Equal(1, MidiFileParser.ReadVarLen(data, ref position, data.Length));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(480, 0.5)]
    [InlineData(960, 1.0)]
    public void TicksToSeconds_ConvertsUsingTempo(long ticks, double expected)
    {
        Assert.Equal(expected, MidiFileParser.TicksToSeconds(ticks, 480, 500000), 6);
        Assert.Equal(0, MidiFileParser.TicksToSeconds(100, 0, 500000));
    }

    [Fact]
    public void ReadIntegerHelpers_ReturnZeroForTruncatedData()
    {
        Assert.Equal(0, MidiFileParser.ReadUInt16(new byte[1], 0));
        Assert.Equal(0u, MidiFileParser.ReadUInt32(new byte[3], 0));
        Assert.Equal(0x0102, MidiFileParser.ReadUInt16(new byte[] { 0x01, 0x02 }, 0));
        Assert.Equal(0x01020304u, MidiFileParser.ReadUInt32(new byte[] { 0x01, 0x02, 0x03, 0x04 }, 0));
    }

    [Fact]
    public void MidiMessage_FoldsNotesAndPreservesOtherStatuses()
    {
        var controlChange = 0xB0 | (7 << 8) | (100 << 16);
        Assert.Equal(controlChange, MidiMessage.Fold(controlChange));

        var lowNoteOn = 0x90 | (36 << 8) | (100 << 16);
        var foldedOn = MidiMessage.Fold(lowNoteOn);
        Assert.Equal(48, (foldedOn >> 8) & 0x7F);

        var highNoteOff = 0x80 | (100 << 8);
        var foldedOff = MidiMessage.Fold(highNoteOff);
        Assert.Equal(0x80, foldedOff & 0xF0);
        Assert.InRange((foldedOff >> 8) & 0x7F, Key21Layout.LowPitch, Key21Layout.HighPitch);
    }

    [Fact]
    public void MidiMessage_ScalesOnlyNoteOnVelocity()
    {
        var noteOn = 0x90 | (60 << 8) | (80 << 16);
        Assert.Equal(noteOn, MidiMessage.ScaleVelocity(noteOn, 1));
        Assert.Equal(120, (MidiMessage.ScaleVelocity(noteOn, 1.5) >> 16) & 0x7F);
        Assert.Equal(127, (MidiMessage.ScaleVelocity(noteOn, 4) >> 16) & 0x7F);

        var noteOff = 0x80 | (60 << 8);
        Assert.Equal(noteOff, MidiMessage.ScaleVelocity(noteOff, 2));
    }

    [Fact]
    public void MidiPlaylistBuilder_OffsetsFilesAndRecordsRanges()
    {
        var files = new[]
        {
            new MidiFileData(new[]
            {
                new MidiPlaybackEvent(0.1, true, 60, 90),
                new MidiPlaybackEvent(0.2, false, 60, 0),
            }, 0.5),
            new MidiFileData(new[]
            {
                new MidiPlaybackEvent(0.1, true, 62, 90),
            }, 0.25),
        };
        var ranges = new List<(double Start, double Duration)>();

        var events = MidiPlaylistBuilder.Build(files, ranges);

        Assert.Equal(3, events.Count);
        Assert.Equal((0.0, 0.5), ranges[0]);
        Assert.Equal((1.1, 0.25), ranges[1]);
        Assert.Equal(1.2, events[2].Time, 6);
    }

    [Fact]
    public void MidiPlaylistBuilder_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MidiPlaylistBuilder.Build(null!, new List<(double, double)>()));
        Assert.Throws<ArgumentNullException>(() =>
            MidiPlaylistBuilder.Build(Array.Empty<MidiFileData>(), null!));
    }

    private static void Add(List<byte> target, params byte[] values) => target.AddRange(values);

    private static string NewTempPath() =>
        Path.Combine(Path.GetTempPath(), "genshin-music-parser-" + Guid.NewGuid().ToString("N") + ".mid");
}
