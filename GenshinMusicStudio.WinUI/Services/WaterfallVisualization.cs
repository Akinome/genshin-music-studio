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
    private const double KeyStripHeight = 30;
    private const double BlackKeyWidthRatio = 0.62;
    private const int DefaultLowPitch = 48;
    private const int DefaultHighPitch = 83;

    private static readonly Windows.UI.Color WhiteKeyFill = Windows.UI.Color.FromArgb(255, 245, 246, 248);
    private static readonly Windows.UI.Color WhiteKeyBorder = Windows.UI.Color.FromArgb(255, 196, 202, 214);
    private static readonly Windows.UI.Color WhiteKeyText = Windows.UI.Color.FromArgb(255, 46, 52, 64);
    private static readonly Windows.UI.Color BlackKeyFill = Windows.UI.Color.FromArgb(255, 38, 42, 52);
    private static readonly Windows.UI.Color BlackKeyBorder = Windows.UI.Color.FromArgb(255, 22, 25, 32);

    private IReadOnlyList<MidiPlayer.MidiNote> notes = Array.Empty<MidiPlayer.MidiNote>();
    private double duration;
    private double minPitch = 48;
    private double maxPitch = 83;
    private Canvas? canvas;
    private Canvas? keyStrip;
    private Grid? viewport;
    private TranslateTransform? transform;
    private double renderBase;
    private double lastHighlight;
    private double whiteKeyWidth = 20;

    public string Name => "瀑布流";

    public void Initialize(Grid owner, IReadOnlyList<MidiPlayer.MidiNote> notes, double duration)
    {
        this.notes = notes;
        this.duration = duration;
        viewport = owner;
        if (notes.Count > 0)
        {
            minPitch = Math.Floor((double)notes.Min(n => n.Pitch));
            maxPitch = Math.Ceiling((double)notes.Max(n => n.Pitch));
            // Keep the full 21-key span visible so the keyboard never gets cut.
            if (minPitch > DefaultLowPitch) minPitch = DefaultLowPitch;
            if (maxPitch < DefaultHighPitch) maxPitch = DefaultHighPitch;
        }
        else
        {
            minPitch = DefaultLowPitch;
            maxPitch = DefaultHighPitch;
        }

        owner.Children.Clear();
        canvas = new Canvas
        {
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        transform = new TranslateTransform();
        canvas.RenderTransform = transform;
        owner.Children.Add(canvas);

        keyStrip = new Canvas
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
        };
        owner.Children.Add(keyStrip);

        var hitLine = new Rectangle
        {
            Height = 3,
            VerticalAlignment = VerticalAlignment.Bottom,
            Fill = new SolidColorBrush(Microsoft.UI.Colors.Orange),
            IsHitTestVisible = false,
        };
        owner.Children.Add(hitLine);
        renderBase = 0;
        if (notes.Count > 0)
        {
            RenderWindow(0);
        }
        else
        {
            RenderIdle();
        }
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

    public void RenderIdle()
    {
        if (canvas is null || keyStrip is null) return;
        minPitch = DefaultLowPitch;
        maxPitch = DefaultHighPitch;
        renderBase = 0;
        canvas.Width = 0;
        canvas.Height = 0;
        canvas.Children.Clear();
        if (transform is not null) transform.Y = 0;
        RenderKeyStrip();
    }

    private void RenderWindow(double baseTime)
    {
        if (canvas is null || viewport is null) return;
        var (canvasWidth, viewportHeight) = PrepareGeometry();
        var windowSeconds = viewportHeight / PixelsPerSecond + 1.5;
        var canvasHeight = windowSeconds * PixelsPerSecond;
        canvas.Width = canvasWidth;
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
            Canvas.SetLeft(line, PitchToX(pitch) + PitchLaneWidth(pitch) - 1);
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
            var laneWidth = PitchLaneWidth(note.Pitch);
            var rect = new Rectangle
            {
                Width = Math.Max(4, laneWidth - 3),
                Height = Math.Max(6, (note.End - note.Start) * PixelsPerSecond),
                RadiusX = 3,
                RadiusY = 3,
                Fill = noteBrush,
                Opacity = 0.88,
                Tag = note,
            };
            Canvas.SetLeft(rect, PitchToX(note.Pitch) + 1.5);
            Canvas.SetTop(rect, top);
            canvas.Children.Add(rect);
        }
        RenderKeyStrip();
    }

    /// <summary>
    /// Lays out the lanes the way a piano keyboard is laid out: seven wide
    /// natural-key columns and five narrow accidental columns per octave, so
    /// the keyboard drawn at the bottom lines up with the lanes exactly.
    /// </summary>
    private (double CanvasWidth, double ViewportHeight) PrepareGeometry()
    {
        var viewportWidth = viewport?.ActualWidth ?? 0;
        if (double.IsNaN(viewportWidth) || viewportWidth < 100) viewportWidth = 1100;
        var viewportHeight = viewport?.ActualHeight ?? 0;
        if (double.IsNaN(viewportHeight) || viewportHeight < 100) viewportHeight = 420;
        var low = (int)Math.Floor(minPitch);
        var high = (int)Math.Ceiling(maxPitch);
        var whiteKeys = 0;
        for (var pitch = low; pitch <= high; pitch++)
        {
            if (!IsSharp(pitch)) whiteKeys++;
        }
        if (whiteKeys < 1) whiteKeys = 15;
        whiteKeyWidth = Math.Clamp(viewportWidth / whiteKeys, 14, 64);
        var total = 0.0;
        for (var pitch = low; pitch <= high; pitch++)
        {
            total += PitchLaneWidth(pitch);
        }
        return (total, viewportHeight);
    }

    private double PitchLaneWidth(int pitch) => IsSharp(pitch) ? whiteKeyWidth * BlackKeyWidthRatio : whiteKeyWidth;

    private double PitchToX(int pitch)
    {
        var x = 0.0;
        var low = (int)Math.Floor(minPitch);
        for (var p = low; p <= pitch; p++)
        {
            if (p == pitch) return x;
            x += PitchLaneWidth(p);
        }
        return x;
    }

    private static bool IsSharp(int pitch)
    {
        return pitch % 12 is 1 or 3 or 6 or 8 or 10;
    }

    private void RenderKeyStrip()
    {
        if (keyStrip is null || viewport is null) return;
        var (width, _) = PrepareGeometry();
        var left = Math.Max(0, ((viewport.ActualWidth > 0 ? viewport.ActualWidth : width) - width) / 2);
        keyStrip.Width = viewport.ActualWidth > 0 ? viewport.ActualWidth : width;
        keyStrip.Height = KeyStripHeight;
        keyStrip.Children.Clear();

        var low = (int)Math.Floor(minPitch);
        var high = (int)Math.Ceiling(maxPitch);

        for (var pitch = low; pitch <= high; pitch++)
        {
            if (IsSharp(pitch)) continue;
            var key = new Border
            {
                Width = Math.Max(4, PitchLaneWidth(pitch) - 2),
                Height = KeyStripHeight,
                CornerRadius = new CornerRadius(0, 0, 4, 4),
                Background = new SolidColorBrush(WhiteKeyFill),
                BorderBrush = new SolidColorBrush(WhiteKeyBorder),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = Key21Layout.PhysicalKeyForPitch(pitch),
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(WhiteKeyText),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Canvas.SetLeft(key, left + PitchToX(pitch) + 1);
            Canvas.SetTop(key, 0);
            keyStrip.Children.Add(key);
        }

        for (var pitch = low; pitch <= high; pitch++)
        {
            if (!IsSharp(pitch)) continue;
            var key = new Border
            {
                Width = Math.Max(3, PitchLaneWidth(pitch) - 1),
                Height = KeyStripHeight * 0.6,
                CornerRadius = new CornerRadius(0, 0, 3, 3),
                Background = new SolidColorBrush(BlackKeyFill),
                BorderBrush = new SolidColorBrush(BlackKeyBorder),
                BorderThickness = new Thickness(1),
            };
            Canvas.SetLeft(key, left + PitchToX(pitch));
            Canvas.SetTop(key, 0);
            keyStrip.Children.Add(key);
        }
    }

    private void HighlightPlayingNotes(double position)
    {
        if (canvas is null) return;
        var normal = (Brush)Application.Current.Resources["AccentBlueBrush"];
        var active = new SolidColorBrush(Microsoft.UI.Colors.White);
        foreach (var child in canvas.Children)
        {
            if (child is not Rectangle rect || rect.Tag is not MidiPlayer.MidiNote note) continue;
            rect.Fill = position >= note.Start - 0.02 && position <= note.End ? active : normal;
        }
    }
}
