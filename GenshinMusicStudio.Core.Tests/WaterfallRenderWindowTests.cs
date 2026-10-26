using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class WaterfallRenderWindowTests
{
    [Fact]
    public void Update_KeepsVisibleNotesOwnedAcrossWindowAdvances()
    {
        var window = new WaterfallRenderWindow();
        var notes = new[]
        {
            new WaterfallNoteTiming(0.0, 2.0),
            new WaterfallNoteTiming(1.0, 3.0),
        };

        var first = window.Update(notes, 0.0, 2.0);
        var second = window.Update(notes, 0.5, 2.0);

        Assert.Equal(new[] { 0, 1 }, first.Added);
        Assert.Empty(first.Removed);
        Assert.Empty(second.Added);
        Assert.Empty(second.Removed);
        Assert.Equal(new[] { 0, 1 }, window.VisibleIndices.OrderBy(index => index));
    }

    [Fact]
    public void Update_AddsOnlyNotesEnteringTheWindow()
    {
        var window = new WaterfallRenderWindow();
        var notes = new[]
        {
            new WaterfallNoteTiming(0.0, 1.0),
            new WaterfallNoteTiming(2.0, 3.0),
        };

        window.Update(notes, 0.0, 1.5);
        var change = window.Update(notes, 0.5, 2.0);

        Assert.Equal(new[] { 1 }, change.Added);
        Assert.Empty(change.Removed);
    }

    [Fact]
    public void Update_RemovesOnlyNotesLeavingTheWindow()
    {
        var window = new WaterfallRenderWindow();
        var notes = new[]
        {
            new WaterfallNoteTiming(0.0, 1.0),
            new WaterfallNoteTiming(2.0, 3.0),
        };

        window.Update(notes, 0.0, 3.0);
        var change = window.Update(notes, 1.5, 3.0);

        Assert.Empty(change.Added);
        Assert.Equal(new[] { 0 }, change.Removed);
        Assert.Equal(new[] { 1 }, window.VisibleIndices);
    }
}
