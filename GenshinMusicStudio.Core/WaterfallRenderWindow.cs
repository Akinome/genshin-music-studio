namespace GenshinMusicStudio.Core;

/// <summary>
/// Tracks the notes that belong to the waterfall's rolling render window.
/// The WinUI visualizer uses the returned additions and removals to reuse
/// existing note shapes instead of rebuilding the whole canvas every frame.
/// </summary>
public sealed class WaterfallRenderWindow
{
    private readonly HashSet<int> visibleIndices = new();
    private readonly HashSet<int> desiredIndices = new();
    private readonly List<int> addedIndices = new();
    private readonly List<int> removedIndices = new();

    /// <summary>The note indexes currently owned by the render window.</summary>
    public IReadOnlyCollection<int> VisibleIndices => visibleIndices;

    /// <summary>
    /// Updates the window and reports which note shapes must be added or removed.
    /// Notes that remain visible keep their index, allowing callers to preserve
    /// the associated visual and its current highlight state.
    /// </summary>
    public WaterfallRenderWindowChange Update(
        IReadOnlyList<WaterfallNoteTiming> notes,
        double baseTime,
        double windowSeconds)
    {
        ArgumentNullException.ThrowIfNull(notes);

        desiredIndices.Clear();
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];
            if (note.End < baseTime - 0.1) continue;
            if (note.Start > baseTime + windowSeconds) continue;
            desiredIndices.Add(index);
        }

        addedIndices.Clear();
        removedIndices.Clear();
        foreach (var index in desiredIndices)
        {
            if (!visibleIndices.Contains(index)) addedIndices.Add(index);
        }
        foreach (var index in visibleIndices)
        {
            if (!desiredIndices.Contains(index)) removedIndices.Add(index);
        }
        addedIndices.Sort();
        removedIndices.Sort();

        visibleIndices.Clear();
        visibleIndices.UnionWith(desiredIndices);
        return new WaterfallRenderWindowChange(
            addedIndices.Count == 0 ? Array.Empty<int>() : addedIndices.ToArray(),
            removedIndices.Count == 0 ? Array.Empty<int>() : removedIndices.ToArray());
    }

    /// <summary>Clears all window ownership without recreating the manager.</summary>
    public void Clear() => visibleIndices.Clear();
}

/// <summary>The timing values needed to decide whether a note is visible.</summary>
public readonly record struct WaterfallNoteTiming(double Start, double End);

/// <summary>The incremental changes required by a waterfall render window.</summary>
public readonly record struct WaterfallRenderWindowChange(
    IReadOnlyList<int> Added,
    IReadOnlyList<int> Removed);
