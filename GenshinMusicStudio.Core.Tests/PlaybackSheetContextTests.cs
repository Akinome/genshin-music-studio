using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class PlaybackSheetContextTests
{
    [Fact]
    public void ResolvePageContext_RestoresSingleFileSheetAfterPageReentry()
    {
        var context = PlaybackSheetResolver.ResolvePageContext(
            playingFolder: null,
            currentFile: @"C:\Sheets\current.mid",
            currentFileStart: 0,
            timelineDuration: 180);

        Assert.NotNull(context);
        Assert.Equal(@"C:\Sheets\current.mid", context.FilePath);
        Assert.Equal(0, context.TimeOffset);
        Assert.Equal(180, context.TimelineDuration);
    }

    [Fact]
    public void ResolveDirectoryContext_ReturnsCurrentFileAndPlaylistOffset()
    {
        var context = PlaybackSheetResolver.ResolveDirectoryContext(
            playingFolder: @"C:\Sheets",
            currentFile: @"C:\Sheets\second.mid",
            currentFileStart: 75.5,
            timelineDuration: 180);

        Assert.NotNull(context);
        Assert.Equal(@"C:\Sheets\second.mid", context.FilePath);
        Assert.Equal(75.5, context.TimeOffset);
        Assert.Equal(180, context.TimelineDuration);
    }

    [Fact]
    public void ResolveDirectoryContext_ReturnsNullWithoutDirectoryPlayback()
    {
        var context = PlaybackSheetResolver.ResolveDirectoryContext(
            playingFolder: null,
            currentFile: @"C:\Sheets\first.mid",
            currentFileStart: 0,
            timelineDuration: 60);

        Assert.Null(context);
    }

    [Fact]
    public void OffsetNotes_MovesNotesOntoAbsolutePlaylistTimeline()
    {
        var notes = new[]
        {
            new ParsedMidiNote(1, 2, 60, 80),
            new ParsedMidiNote(3, 4, 62, 90),
        };

        var offset = PlaybackSheetResolver.OffsetNotes(notes, timeOffset: 10);

        Assert.Equal(11, offset[0].Start);
        Assert.Equal(12, offset[0].End);
        Assert.Equal(13, offset[1].Start);
        Assert.Equal(14, offset[1].End);
    }
}
