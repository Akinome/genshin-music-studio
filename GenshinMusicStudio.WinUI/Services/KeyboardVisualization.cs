using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace GenshinMusicStudio_WinUI.Services;

public sealed class KeyboardVisualization : IMidiVisualization
{
    private const double HighlightInterval = 0.05;
    private const double PixelsPerSecond = 120;

    // genshin.music palette: #fff9ef / #eae5ce keys in light, #495466 in dark.
    private static readonly Windows.UI.Color KeyFillLight = Windows.UI.Color.FromArgb(255, 255, 249, 239);
    private static readonly Windows.UI.Color KeyBorderLight = Windows.UI.Color.FromArgb(255, 234, 229, 206);
    private static readonly Windows.UI.Color KeyTextLight = Windows.UI.Color.FromArgb(255, 74, 62, 48);
    private static readonly Windows.UI.Color KeyFillDark = Windows.UI.Color.FromArgb(255, 73, 84, 102);
    private static readonly Windows.UI.Color KeyBorderDark = Windows.UI.Color.FromArgb(255, 92, 104, 126);
    private static readonly Windows.UI.Color KeyTextDark = Windows.UI.Color.FromArgb(255, 234, 232, 230);

    private IReadOnlyList<MidiPlayer.MidiNote> notes = Array.Empty<MidiPlayer.MidiNote>();
    private double duration;
    private Canvas? keyCanvas;
    private Grid? viewport;
    private double lastHighlight;
    private double lastPosition;
    private Canvas? fallCanvas;
    private TranslateTransform? transform;
    private double renderBase;
    private double keyboardHeight = 150;
    private readonly Dictionary<int, List<MidiPlayer.MidiNote>> keyNotes = new();
    private readonly HashSet<int> litKeys = new();
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

        owner.Children.Clear();
        fallCanvas = new Canvas();
        transform = new TranslateTransform();
        fallCanvas.RenderTransform = transform;
        owner.Children.Add(fallCanvas);

        keyCanvas = new Canvas
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 10),
        };
        owner.Children.Add(keyCanvas);
        BuildKeyboard();
        var hitLine = new Rectangle
        {
            Height = 2,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, keyCanvas.Height + 12),
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 218, 165, 82)),
            IsHitTestVisible = false,
        };
        owner.Children.Add(hitLine);
        lastHighlight = -1;
        renderBase = 0;
        RenderFallWindow(0);
    }

    public void Update(double position)
    {
        if (fallCanvas is not null && transform is not null)
        {
            if (position - renderBase >= 0.5)
            {
                renderBase = Math.Floor(position / 0.5) * 0.5;
                RenderFallWindow(renderBase);
            }
            transform.Y = (position - renderBase) * PixelsPerSecond;
        }
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
        lastHighlight = -1;
        ClearLitKeys();
    }

    private void RenderFallWindow(double baseTime)
    {
        if (fallCanvas is null || viewport is null || transform is null) return;
        var viewportWidth = SafeWidth();
        var fallHeight = SafeHeight() - keyCanvas.Height - 14;
        if (fallHeight < 60) fallHeight = 60;
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

        var noteFill = IsDarkTheme
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 73, 84, 102))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 249, 239));
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

    private void BuildKeyboard()
    {
        if (keyCanvas is null || viewport is null) return;
        var viewportWidth = SafeWidth();
        keyWidth = Math.Clamp((viewportWidth - 32) / 7, 48, 120);
        var keyboardWidth = keyWidth * 7;
        var viewportHeight = SafeHeight();
        var rowHeight = Math.Min((viewportHeight - 20) / 3, keyWidth * 1.2);
        keyCanvas.Width = keyboardWidth;
        keyCanvas.Height = rowHeight * 3 + 14;
        keyCanvas.Children.Clear();

        var (fill, border, text) = ThemeKeys();
        for (var keyIndex = 0; keyIndex < Key21Layout.KeyCount; keyIndex++)
        {
            var (row, column) = Key21Layout.KeyPosition(keyIndex);
            var centerX = column * keyWidth + keyWidth / 2;
            var centerY = row * rowHeight + rowHeight / 2 + 7;
            var diameter = Math.Min(keyWidth - 12, rowHeight - 10);

            var circle = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = fill,
                Stroke = border,
                StrokeThickness = IsDarkTheme ? 3 : 4,
                Tag = keyIndex,
                RenderTransform = new ScaleTransform
                {
                    ScaleX = 1,
                    ScaleY = 1,
                    CenterX = diameter / 2,
                    CenterY = diameter / 2,
                },
            };
            Canvas.SetLeft(circle, centerX - diameter / 2);
            Canvas.SetTop(circle, centerY - diameter / 2);
            keyCanvas.Children.Add(circle);

            var label = new TextBlock
            {
                Text = Key21Layout.DegreeNames[keyIndex % 7],
                FontSize = Math.Max(12, diameter * 0.24),
                Foreground = text,
                IsHitTestVisible = false,
                Tag = 3000 + keyIndex,
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
            ? (new SolidColorBrush(KeyFillDark), new SolidColorBrush(KeyBorderDark), new SolidColorBrush(KeyTextDark))
            : (new SolidColorBrush(KeyFillLight), new SolidColorBrush(KeyBorderLight), new SolidColorBrush(KeyTextLight));
    }

    private void Highlight(double position)
    {
        var previous = lastPosition;
        var next = new HashSet<int>();
        var newlyOnset = new HashSet<int>();
        foreach (var pair in keyNotes)
        {
            foreach (var note in pair.Value)
            {
                if (position >= note.Start - 0.02 && position <= note.End)
                {
                    next.Add(pair.Key);
                    if (note.Start > previous && note.Start <= position + 0.02)
                    {
                        newlyOnset.Add(pair.Key);
                    }
                    break;
                }
            }
        }
        foreach (var keyIndex in next)
        {
            if (newlyOnset.Contains(keyIndex))
            {
                PressKey(keyIndex);
            }
            else if (!litKeys.Contains(keyIndex))
            {
                PressKey(keyIndex);
            }
        }
        foreach (var keyIndex in litKeys)
        {
            if (next.Contains(keyIndex)) continue;
            ReleaseKey(keyIndex);
        }
        litKeys.Clear();
        foreach (var keyIndex in next) litKeys.Add(keyIndex);
        lastPosition = position;
    }

    private void PressKey(int keyIndex)
    {
        // genshin.music .click-event: accent background, accent border, white note text, scale(0.9).
        var accent = (Brush)Application.Current.Resources["AccentBlueBrush"];
        SetKeyStyle(keyIndex, accent, accent, true);
        AnimateKeyScale(keyIndex, 1.0, 0.9, 90);
    }

    private void ReleaseKey(int keyIndex)
    {
        var (fill, border, _) = ThemeKeys();
        SetKeyStyle(keyIndex, fill, border, false);
        AnimateKeyScale(keyIndex, 0.9, 1.0, 140);
    }

    private void SetKeyStyle(int keyIndex, Brush fill, Brush border, bool pressed)
    {
        if (keyCanvas is null) return;
        var textColor = pressed
            ? new SolidColorBrush(Microsoft.UI.Colors.White)
            : (SolidColorBrush)(IsDarkTheme ? new SolidColorBrush(KeyTextDark) : new SolidColorBrush(KeyTextLight));
        foreach (var child in keyCanvas.Children.OfType<Ellipse>())
        {
            if (child.Tag is int index && index == keyIndex)
            {
                child.Fill = fill;
                child.Stroke = border;
            }
        }
        foreach (var child in keyCanvas.Children.OfType<TextBlock>())
        {
            if (child.Tag is int index && index == 3000 + keyIndex)
            {
                child.Foreground = textColor;
            }
        }
    }

    private void AnimateKeyScale(int keyIndex, double from, double to, int milliseconds)
    {
        var key = FindKeyEllipse(keyIndex);
        if (key?.RenderTransform is not ScaleTransform scale) return;
        scale.ScaleX = from;
        scale.ScaleY = from;
        var storyboard = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(animation, scale);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }
        storyboard.Begin();
    }

    private Ellipse? FindKeyEllipse(int keyIndex)
    {
        if (keyCanvas is null) return null;
        foreach (var child in keyCanvas.Children.OfType<Ellipse>())
        {
            if (child.Tag is int index && index == keyIndex) return child;
        }
        return null;
    }

    private void ClearLitKeys()
    {
        if (litKeys.Count == 0) return;
        foreach (var keyIndex in litKeys) ReleaseKey(keyIndex);
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
