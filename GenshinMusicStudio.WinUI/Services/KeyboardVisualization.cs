using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace GenshinMusicStudio_WinUI.Services;

public sealed class KeyboardVisualization : IMidiVisualization
{
    private const double PixelsPerSecond = 120;
    private const double RespawnInterval = 0.5;
    private const double HighlightInterval = 0.08;
    private const double KeyboardFraction = 0.38;

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
    private double keyboardHeight = 150;
    private double keyWidth = 70;

    public string Name => "21键键盘";

    private bool IsDarkTheme => Application.Current.RequestedTheme == ApplicationTheme.Dark;

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
        keyboardHeight = Math.Max(120, viewportHeight * KeyboardFraction);

        owner.Children.Clear();
        fallCanvas = new Canvas();
        transform = new TranslateTransform();
        fallCanvas.RenderTransform = transform;
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
            Height = 2,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, keyboardHeight + 2),
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 218, 165, 82)),
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
        keyWidth = Math.Clamp((viewportWidth - 24) / 7, 44, 96);
        var keyboardWidth = keyWidth * 7;
        keyCanvas.Width = keyboardWidth;
        keyCanvas.Children.Clear();
        var rowHeight = (keyboardHeight - 12) / 3;
        var diameter = Math.Min(keyWidth - 10, rowHeight - 8);

        var (keyFill, keyBorder, keyText) = ThemeKeys();
        for (var keyIndex = 0; keyIndex < Key21Layout.KeyCount; keyIndex++)
        {
            var (row, column) = Key21Layout.KeyPosition(keyIndex);
            var centerX = column * keyWidth + keyWidth / 2;
            var centerY = row * rowHeight + rowHeight / 2;

            var glow = new Ellipse
            {
                Width = diameter + 10,
                Height = diameter + 10,
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 255, 255, 255)),
                IsHitTestVisible = false,
                Tag = keyIndex + 1000,
            };
            Canvas.SetLeft(glow, centerX - glow.Width / 2);
            Canvas.SetTop(glow, centerY - glow.Height / 2);
            keyCanvas.Children.Add(glow);

            var circle = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = keyFill,
                Stroke = keyBorder,
                StrokeThickness = 2.5,
                Tag = keyIndex,
            };
            Canvas.SetLeft(circle, centerX - diameter / 2);
            Canvas.SetTop(circle, centerY - diameter / 2);
            keyCanvas.Children.Add(circle);

            var label = new TextBlock
            {
                Text = Key21Layout.DegreeNames[keyIndex % 7],
                FontSize = 12,
                Foreground = keyText,
                IsHitTestVisible = false,
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, centerX - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, centerY - label.DesiredSize.Height / 2);
            keyCanvas.Children.Add(label);
        }
    }

    private (Brush Fill, Brush Border, Brush Text) ThemeKeys()
    {
        return IsDarkTheme
            ? (new SolidColorBrush(Windows.UI.Color.FromArgb(255, 73, 84, 102)),
               new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 108, 126)),
               new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 232, 230)))
            : (new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 249, 239)),
               new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 229, 206)),
               new SolidColorBrush(Windows.UI.Color.FromArgb(255, 74, 62, 48)));
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
        var offsetX = (fallWidth - keyboardWidth) / 2;
        fallCanvas.Width = fallWidth;
        fallCanvas.Height = canvasHeight;
        fallCanvas.Children.Clear();

        var laneBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 128, 128, 128));
        for (var degree = 0; degree < 7; degree++)
        {
            var line = new Rectangle
            {
                Width = 1,
                Height = canvasHeight,
                Fill = laneBrush,
            };
            Canvas.SetLeft(line, offsetX + degree * keyWidth + keyWidth / 2);
            Canvas.SetTop(line, 0);
            fallCanvas.Children.Add(line);
        }

        var (noteFill, _, _) = ThemeKeys();
        foreach (var note in notes)
        {
            if (note.End < baseTime - 0.1) continue;
            if (note.Start > baseTime + windowSeconds) continue;
            var top = canvasHeight - (note.End - baseTime) * PixelsPerSecond;
            if (top < -fallHeight || top > canvasHeight) continue;
            var degree = Key21Layout.KeyIndexForPitch(note.Pitch) % 7;
            var rect = new Rectangle
            {
                Width = Math.Max(6, keyWidth - 18),
                Height = Math.Max(6, (note.End - note.Start) * PixelsPerSecond),
                RadiusX = 6,
                RadiusY = 6,
                Fill = noteFill,
                Opacity = 0.92,
                Tag = note,
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 128, 128, 128)),
                StrokeThickness = 1,
            };
            Canvas.SetLeft(rect, offsetX + degree * keyWidth + (keyWidth - rect.Width) / 2);
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
        var (keyFill, keyBorder, _) = ThemeKeys();
        var activeBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            ?? (Brush)Application.Current.Resources["AccentBlueBrush"];
        foreach (var keyIndex in next)
        {
            if (litKeys.Contains(keyIndex)) continue;
            SetKeyBrush(keyIndex, activeBrush, null);
        }
        foreach (var keyIndex in litKeys)
        {
            if (next.Contains(keyIndex)) continue;
            SetKeyBrush(keyIndex, keyFill, keyBorder);
        }
        litKeys.Clear();
        foreach (var keyIndex in next) litKeys.Add(keyIndex);
    }

    private void SetKeyBrush(int keyIndex, Brush fill, Brush? stroke)
    {
        if (keyCanvas is null) return;
        foreach (var child in keyCanvas.Children.OfType<Ellipse>())
        {
            if (child.Tag is int index && index == keyIndex)
            {
                child.Fill = fill;
                if (stroke is not null) child.Stroke = stroke;
                return;
            }
        }
    }

    private void ClearLitKeys()
    {
        if (litKeys.Count == 0) return;
        var (keyFill, keyBorder, _) = ThemeKeys();
        foreach (var keyIndex in litKeys) SetKeyBrush(keyIndex, keyFill, keyBorder);
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
