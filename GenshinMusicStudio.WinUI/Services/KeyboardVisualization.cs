using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace GenshinMusicStudio_WinUI.Services;

public sealed class KeyboardVisualization : IMidiVisualization
{
    private const double PixelsPerSecond = 120;
    private const double RespawnInterval = 0.5;
    private const double HighlightInterval = 0.1;
    private const double KeyboardFraction = 0.32;

    private IReadOnlyList<MidiPlayer.MidiNote> notes = Array.Empty<MidiPlayer.MidiNote>();
    private double duration;
    private Canvas? fallCanvas;
    private Canvas? keyCanvas;
    private Grid? viewport;
    private TranslateTransform? transform;
    private double renderBase;
    private double lastHighlight;
    private readonly Dictionary<int, List<MidiPlayer.MidiNote>> keyNotes = new();
    private readonly HashSet<int> litKeys = new();
    private double keyboardHeight = 120;
    private double keyWidth = 70;

    public string Name => "21键键盘";

    public void Initialize(Grid owner, IReadOnlyList<MidiPlayer.MidiNote> notes, double duration)
    {
        this.notes = notes;
        this.duration = duration;
        viewport = owner;
        keyNotes.Clear();
        foreach (var note in notes)
        {
            var key = Key21Layout.KeyIndexForPitch(note.Pitch);
            if (!keyNotes.TryGetValue(key, out var list))
            {
                list = new List<MidiPlayer.MidiNote>();
                keyNotes[key] = list;
            }
            list.Add(note);
        }

        var viewportHeight = SafeHeight();
        keyboardHeight = Math.Max(96, viewportHeight * KeyboardFraction);

        owner.Children.Clear();
        fallCanvas = new Canvas();
        transform = new TranslateTransform();
        fallCanvas.RenderTransform = transform;
        Grid.SetRowSpan(fallCanvas, 1);
        owner.Children.Add(fallCanvas);

        keyCanvas = new Canvas
        {
            Height = keyboardHeight,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        owner.Children.Add(keyCanvas);
        BuildKeyboard();

        var hitLine = new Rectangle
        {
            Height = 3,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, keyboardHeight),
            Fill = new SolidColorBrush(Microsoft.UI.Colors.Orange),
            IsHitTestVisible = false,
        };
        owner.Children.Add(hitLine);
        renderBase = 0;
        RenderFallWindow(0);
    }

    public void Update(double position)
    {
        if (fallCanvas is null || transform is null || viewport is null) return;
        if (position - renderBase >= RespawnInterval)
        {
            renderBase = Math.Floor(position / RespawnInterval) * RespawnInterval;
            RenderFallWindow(renderBase);
        }
        transform.Y = (position - renderBase) * PixelsPerSecond;
        if (position - lastHighlight >= HighlightInterval)
        {
            lastHighlight = position;
            Highlight(position);
        }
    }

    public void Reset()
    {
        renderBase = 0;
        if (transform is not null) transform.Y = 0;
        RenderFallWindow(0);
        ClearLitKeys();
    }

    private void BuildKeyboard()
    {
        if (keyCanvas is null || viewport is null) return;
        var viewportWidth = SafeWidth();
        keyWidth = Math.Clamp((viewportWidth - 16) / 7, 40, 90);
        var keyboardWidth = keyWidth * 7;
        keyCanvas.Width = keyboardWidth;
        keyCanvas.Children.Clear();
        var rowHeight = (keyboardHeight - 8) / 3;
        var idleBrush = (Brush)Application.Current.Resources["CardStrokeBrush"];
        for (var keyIndex = 0; keyIndex < Key21Layout.KeyCount; keyIndex++)
        {
            var (row, column) = Key21Layout.KeyPosition(keyIndex);
            var key = new Rectangle
            {
                Width = keyWidth - 4,
                Height = rowHeight - 4,
                RadiusX = 5,
                RadiusY = 5,
                Fill = idleBrush,
                Opacity = 0.9,
                Tag = keyIndex,
            };
            Canvas.SetLeft(key, column * keyWidth + 2);
            Canvas.SetTop(key, row * rowHeight + 2);
            keyCanvas.Children.Add(key);

            var label = new TextBlock
            {
                Text = Key21Layout.DegreeNames[keyIndex % 7],
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, column * keyWidth + 2);
            Canvas.SetTop(label, row * rowHeight + rowHeight - 20);
            keyCanvas.Children.Add(label);
        }
    }

    private void RenderFallWindow(double baseTime)
    {
        if (fallCanvas is null || viewport is null || transform is null) return;
        var viewportWidth = SafeWidth();
        var fallHeight = SafeHeight() - keyboardHeight;
        var windowSeconds = fallHeight / PixelsPerSecond + 1.5;
        var canvasHeight = windowSeconds * PixelsPerSecond;
        var keyboardWidth = keyWidth * 7;
        var fallWidth = Math.Max(keyboardWidth, viewportWidth);
        fallCanvas.Width = fallWidth;
        fallCanvas.Height = canvasHeight;
        fallCanvas.Children.Clear();

        var laneBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 128, 128, 128));
        for (var degree = 0; degree < 7; degree++)
        {
            var line = new Rectangle
            {
                Width = 1,
                Height = canvasHeight,
                Fill = laneBrush,
            };
            Canvas.SetLeft(line, degree * keyWidth + keyWidth / 2 + (fallWidth - keyboardWidth) / 2);
            Canvas.SetTop(line, 0);
            fallCanvas.Children.Add(line);
        }

        var noteBrush = (Brush)Application.Current.Resources["AccentBlueBrush"];
        var offsetX = (fallWidth - keyboardWidth) / 2;
        foreach (var note in notes)
        {
            if (note.End < baseTime - 0.1) continue;
            if (note.Start > baseTime + windowSeconds) continue;
            var top = canvasHeight - (note.End - baseTime) * PixelsPerSecond;
            if (top < -fallHeight || top > canvasHeight) continue;
            var degree = Key21Layout.KeyIndexForPitch(note.Pitch) % 7;
            var rect = new Rectangle
            {
                Width = Math.Max(4, keyWidth - 6),
                Height = Math.Max(6, (note.End - note.Start) * PixelsPerSecond),
                RadiusX = 3,
                RadiusY = 3,
                Fill = noteBrush,
                Opacity = 0.88,
                Tag = note,
            };
            Canvas.SetLeft(rect, offsetX + degree * keyWidth + 3);
            Canvas.SetTop(rect, top);
            fallCanvas.Children.Add(rect);
        }
    }

    private void Highlight(double position)
    {
        if (keyCanvas is null) return;
        var next = new HashSet<int>();
        foreach (var pair in keyNotes)
        {
            foreach (var note in pair.Value)
            {
                if (position >= note.Start - 0.02 && position <= note.End)
                {
                    next.Add(pair.Key);
                    break;
                }
            }
        }
        if (next.Count == litKeys.Count && next.SetEquals(litKeys)) return;
        var idleBrush = (Brush)Application.Current.Resources["CardStrokeBrush"];
        var activeBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            ?? (Brush)Application.Current.Resources["AccentBlueBrush"];
        foreach (var keyIndex in next)
        {
            if (litKeys.Contains(keyIndex)) continue;
            SetKeyBrush(keyIndex, activeBrush);
        }
        foreach (var keyIndex in litKeys)
        {
            if (next.Contains(keyIndex)) continue;
            SetKeyBrush(keyIndex, idleBrush);
        }
        litKeys.Clear();
        foreach (var keyIndex in next) litKeys.Add(keyIndex);
    }

    private void SetKeyBrush(int keyIndex, Brush brush)
    {
        if (keyCanvas is null) return;
        foreach (var child in keyCanvas.Children.OfType<Rectangle>())
        {
            if (child.Tag is int index && index == keyIndex)
            {
                child.Fill = brush;
                return;
            }
        }
    }

    private void ClearLitKeys()
    {
        if (litKeys.Count == 0) return;
        var idleBrush = (Brush)Application.Current.Resources["CardStrokeBrush"];
        foreach (var keyIndex in litKeys) SetKeyBrush(keyIndex, idleBrush);
        litKeys.Clear();
    }

    private double SafeWidth()
    {
        var width = viewport?.ActualWidth ?? 0;
        if (double.IsNaN(width) || width < 100) width = 1100;
        return width;
    }

    private double SafeHeight()
    {
        var height = viewport?.ActualHeight ?? 0;
        if (double.IsNaN(height) || height < 100) height = 420;
        return height;
    }
}
