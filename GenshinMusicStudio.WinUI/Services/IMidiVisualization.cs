using Microsoft.UI.Xaml.Controls;

namespace GenshinMusicStudio_WinUI.Services;

public interface IMidiVisualization
{
    string Name { get; }
    void Initialize(Grid viewport, IReadOnlyList<MidiPlayer.MidiNote> notes, double duration);
    void Update(double position);
    void Reset();
}
