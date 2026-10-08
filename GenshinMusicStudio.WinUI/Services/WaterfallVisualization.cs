using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace GenshinMusicStudio_WinUI.Services;

public sealed class WaterfallVisualization : IMidiVisualization
{
    private const double PixelsPerSecond = 140;
    private const double RespawnInterval = 0.5;
    private const double HighlightInterval = 0.25;

    private IReadOnlyList<MidiPlayer.MidiNote> notes = Array.Empty<MidiPlayer.MidiNote>();
    private double duration;
    private double minPitch = 48;
    private double maxPitch = 83;
    private Canvas? canvas;
    private Grid? viewport;
    private TranslateTransform? transform;
    private double renderBase;
    private double lastHighlight;

    public string Name => "瀑布流";

    public void Initialize(Grid owner, IReadOnlyList<MidiPlayer.MidiNote> notes, double duration)
    {
        this.notes = notes;
        this.duration = duration;
        viewport = owner;
        minPitch = Math.Floor((double)notes.Min(n => n.Pitch));
        maxPitch = Math.Ceiling((double)notes.Max(n => n.Pitch));
        if (maxPitch - minPitch < 23) maxPitch = minPitch + 23;

        owner.Children.Clear();
        canvas = new Canvas();
        transform = new TranslateTransform();
        canvas.RenderTransform = transform;
        owner.Children.Add(canvas);
        var hitLine = new Rectangle
        {
            Height = 3,
            VerticalAlignment = VerticalAlignment.Bottom,
            Fill = new SolidColorBrush(Microsoft.UI.Colors.Orange),
            IsHitTestVisible = false,
        };
        owner.Children.Add(hitLine);
        renderBase = 0;
        RenderWindow(0);
    }

    public void Update(double position)
    {
        if (canvas is null || transform is null || viewport is null) return;
        if (position < renderBase || position - renderBase >= RespawnInterval)
        {
            renderBase = Math.Floor(position / RespawnInterval) * RespawnInterval;
            RenderWindow(renderBase);
        }
        transform.Y = (position - renderBase) * PixelsPerSecond;
        if (position - lastHighlight >= HighlightInterval)
        {
            lastHighlight = position;
            HighlightPlayingNotes(position);
        }
    }

    public void Reset()
    {
        renderBase = 0;
        if (transform is not null) transform.Y = 0;
        RenderWindow(0);
    }

    private void RenderWindow(double baseTime)
    {
        if (canvas is null || viewport is null || transform is null) return;
        var range = (int)(maxPitch - minPitch);
        if (range < 35) range = 35;
        var viewportWidth = viewport.ActualWidth;
        if (double.IsNaN(viewportWidth) || viewportWidth < 100) viewportWidth = 1100;
        var viewportHeight = viewport.ActualHeight;
        if (double.IsNaN(viewportHeight) || viewportHeight < 100) viewportHeight = 420;
        var keyWidth = Math.Clamp(viewportWidth / (range + 1), 10, 26);
        var windowSeconds = viewportHeight / PixelsPerSecond + 1.5;
        var canvasHeight = windowSeconds * PixelsPerSecond;
        canvas.Width = (range + 1) * keyWidth;
        canvas.Height = canvasHeight;
        canvas.Children.Clear();

        var laneBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 128, 128, 128));
        var cLineBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 128, 128, 128));
        for (var pitch = (int)minPitch; pitch <= (int)maxPitch; pitch++)
        {
            var line = new Rectangle
            {
                Width = 1,
                Height = canvasHeight,
                Fill = pitch % 12 == 0 ? cLineBrush : laneBrush,
            };
            Canvas.SetLeft(line, (pitch - minPitch) * keyWidth + keyWidth - 1);
            Canvas.SetTop(line, 0);
            canvas.Children.Add(line);
        }

        var noteBrush = (Brush)Application.Current.Resources["AccentBlueBrush"];
        foreach (var note in notes)
        {
            if (note.End < baseTime - 0.1) continue;
            if (note.Start > baseTime + windowSeconds) continue;
            var top = canvasHeight - (note.End - baseTime) * PixelsPerSecond;
            if (top < -viewportHeight || top > canvasHeight) continue;
            var rect = new Rectangle
            {
                Width = Math.Max(4, keyWidth - 3),
                Height = Math.Max(6, (note.End - note.Start) * PixelsPerSecond),
                RadiusX = 3,
                RadiusY = 3,
                Fill = noteBrush,
                Opacity = 0.88,
                Tag = note,
            };
            Canvas.SetLeft(rect, (note.Pitch - minPitch) * keyWidth + 1.5);
            Canvas.SetTop(rect, top);
            canvas.Children.Add(rect);
        }
    }

    private void HighlightPlayingNotes(double position)
    {
        if (canvas is null) return;
        var normal = (Brush)Application.Current.Resources["AccentBlueBrush"];
        var active = new SolidColorBrush(Microsoft.UI.Colors.White);
        foreach (var child in canvas.Children.OfType<Rectangle>())
        {
            if (child.Tag is not MidiPlayer.MidiNote note) continue;
            child.Fill = position >= note.Start - 0.02 && position <= note.End ? active : normal;
        }
    }
}
