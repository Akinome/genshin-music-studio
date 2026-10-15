using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class NativeMidiWriterTests
{
    [Fact]
    public void WriteNotes_WritesAValidEmptyFile()
    {
        var path = NewTempPath();
        try
        {
            NativeMidiWriter.WriteNotes(path, Array.Empty<NativeNote>(), 0);

            var (events, duration) = MidiFileParser.ParseFile(path);
            Assert.Empty(events);
            Assert.Equal(0, duration);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteNotes_ClampsVelocityAndPitchAndRoundTrips()
    {
        var path = NewTempPath();
        try
        {
            var notes = new[]
            {
                new NativeNote(0, 0.5, 200, 2),
                new NativeNote(1, 1.5, -4, -1),
            };

            NativeMidiWriter.WriteNotes(path, notes, 2);
            var (parsed, duration) = MidiFileParser.ParseNotes(path);
            var (events, _) = MidiFileParser.ParseFile(path);

            Assert.Equal(2, parsed.Count);
            Assert.Equal(127, parsed[0].Pitch);
            Assert.Equal(127, events.First(midiEvent => midiEvent.IsOn && midiEvent.Pitch == 127).Velocity);
            Assert.Equal(0, parsed[1].Pitch);
            Assert.Equal(40, parsed[1].Velocity);
            Assert.Equal(1, events.First(midiEvent => midiEvent.IsOn && midiEvent.Pitch == 0).Velocity);
            Assert.True(duration >= 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteNotes_HandlesMultiByteVariableLengthDeltas()
    {
        var path = NewTempPath();
        try
        {
            var notes = new[]
            {
                new NativeNote(0, 0.1, 60, 0.8),
                new NativeNote(6000, 6000.2, 62, 0.8),
            };

            NativeMidiWriter.WriteNotes(path, notes, 6000.5);
            var (parsed, duration) = MidiFileParser.ParseNotes(path);

            Assert.Equal(2, parsed.Count);
            Assert.True(parsed[1].Start > 5900);
            Assert.True(duration > 5900);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteNotes_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => NativeMidiWriter.WriteNotes("", Array.Empty<NativeNote>(), 0));
        Assert.Throws<ArgumentNullException>(() => NativeMidiWriter.WriteNotes(NewTempPath(), null!, 0));
    }

    private static string NewTempPath() =>
        Path.Combine(Path.GetTempPath(), "genshin-music-core-" + Guid.NewGuid().ToString("N") + ".mid");
}
