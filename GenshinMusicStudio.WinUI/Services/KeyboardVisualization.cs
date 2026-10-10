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
    private const double ApproachSeconds = 1.0;

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
    private readonly Dictionary<int, List<MidiPlayer.MidiNote>> keyNotes = new();
    private readonly HashSet<int> litKeys = new();
    private double keyWidth = 70;
    private InstrumentLayout layout = new();
    public event Action<int>? KeyPressed;
    public event Action<int>? KeyReleased;

    public string Name => $"{layout.KeyCount}键键盘";

    private bool IsDarkTheme => Application.Current.RequestedTheme == ApplicationTheme.Dark;

    public void Initialize(Grid owner, IReadOnlyList<MidiPlayer.MidiNote> notes, double duration)
    {
        this.notes = notes;
        this.duration = duration;
        viewport = owner;
        layout = App.Instruments.Layout;
        keyNotes.Clear();
        foreach (var note in notes)
        {
            var key = layout.KeyIndexForPitch(note.Pitch);
            if (!keyNotes.TryGetValue(key, out var list))
            {
                list = new List<MidiPlayer.MidiNote>();
                keyNotes[key] = list;
            }
            list.Add(note);
        }

        owner.Children.Clear();
        keyCanvas = new Canvas
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        owner.Children.Add(keyCanvas);
        BuildKeyboard();
        lastHighlight = -1;
    }

    public void Update(double position)
    {
        UpdateApproach(position);
        if (position - lastHighlight >= HighlightInterval)
        {
            lastHighlight = position;
            Highlight(position);
        }
    }

    public void Reset()
    {
        lastHighlight = -1;
        ClearLitKeys();
        HideAllRings();
    }

    private void BuildKeyboard()
    {
        if (keyCanvas is null || viewport is null) return;
        var viewportWidth = SafeWidth();
        var columns = layout.Columns;
        keyWidth = Math.Clamp((viewportWidth - 32) / columns, 48, 120);
        var keyboardWidth = keyWidth * columns;
        var viewportHeight = SafeHeight();
        var rowHeight = Math.Min((viewportHeight - 20) / 3, keyWidth * 1.2);
        keyCanvas.Width = keyboardWidth;
        keyCanvas.Height = rowHeight * layout.Octaves + 14;
        keyCanvas.Children.Clear();

        var (fill, border, text) = ThemeKeys();
        for (var keyIndex = 0; keyIndex < layout.KeyCount; keyIndex++)
        {
            var row = keyIndex / columns;
            var column = keyIndex % columns;
            var capturedIndex = keyIndex;
            var centerX = column * keyWidth + keyWidth / 2;
            var centerY = row * rowHeight + rowHeight / 2 + 7;
            var diameter = Math.Min(keyWidth - 12, rowHeight - 10);

            var ring = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Stroke = RingBrushForRow(row),
                StrokeThickness = 3,
                Opacity = 0,
                IsHitTestVisible = false,
                Tag = 4000 + keyIndex,
                RenderTransform = new ScaleTransform
                {
                    ScaleX = 2.3,
                    ScaleY = 2.3,
                    CenterX = diameter / 2,
                    CenterY = diameter / 2,
                },
            };
            Canvas.SetLeft(ring, centerX - diameter / 2);
            Canvas.SetTop(ring, centerY - diameter / 2);
            keyCanvas.Children.Add(ring);

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
            circle.PointerPressed += (_, args) =>
            {
                args.Handled = true;
                circle.Opacity = 1;
                circle.CapturePointer(args.Pointer);
                PressKeyVisual(capturedIndex);
                KeyPressed?.Invoke(capturedIndex);
            };
            circle.PointerReleased += (_, args) =>
            {
                circle.ReleasePointerCapture(args.Pointer);
                ReleaseKeyVisual(capturedIndex);
                KeyReleased?.Invoke(capturedIndex);
            };
            circle.PointerCaptureLost += (_, args) =>
            {
                ReleaseKeyVisual(capturedIndex);
                KeyReleased?.Invoke(capturedIndex);
            };
            keyCanvas.Children.Add(circle);

            var labelText = layout.IsDrumKit
                ? (row == 0 ? "A" : "B") + (column + 1)
                : Key21Layout.DegreeNames[column];
            var label = new TextBlock
            {
                Text = labelText,
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

    private Brush RingBrushForRow(int row)
    {
        // genshin.music approach circles: outer rows use the accent, the middle row its complement.
        if (row == 1)
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 163, 61));
        }
        return (Brush)Application.Current.Resources["AccentBlueBrush"];
    }

    private void UpdateApproach(double position)
    {
        if (keyCanvas is null) return;
        foreach (var pair in keyNotes)
        {
            double? nextStart = null;
            foreach (var note in pair.Value)
            {
                if (note.Start >= position - 0.02 && (nextStart is null || note.Start < nextStart))
                {
                    nextStart = note.Start;
                }
            }
            var keyIndex = pair.Key;
            if (nextStart is double start && start - position <= ApproachSeconds && position < start)
            {
                var progress = 1.0 - (start - position) / ApproachSeconds;
                var scale = 2.3 - 1.5 * progress;
                var opacity = progress < 0.5 ? progress * 0.6 : 0.3 + (progress - 0.5) * 1.0;
                SetRing(keyIndex, scale, Math.Clamp(opacity, 0, 0.8));
            }
            else
            {
                SetRing(keyIndex, 2.3, 0);
            }
        }
    }

    private void SetRing(int keyIndex, double scale, double opacity)
    {
        if (keyCanvas is null) return;
        foreach (var child in keyCanvas.Children.OfType<Ellipse>())
        {
            if (child.Tag is int index && index == 4000 + keyIndex)
            {
                child.Opacity = opacity;
                if (child.RenderTransform is ScaleTransform transform)
                {
                    transform.ScaleX = scale;
                    transform.ScaleY = scale;
                }
                return;
            }
        }
    }

    private void HideAllRings()
    {
        if (keyCanvas is null) return;
        foreach (var child in keyCanvas.Children.OfType<Ellipse>())
        {
            if (child.Tag is int index && index >= 4000)
            {
                child.Opacity = 0;
            }
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
            if (newlyOnset.Contains(keyIndex) || !litKeys.Contains(keyIndex))
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

    public void PressKeyVisual(int keyIndex)
    {
        PressKey(keyIndex);
    }

    public void ReleaseKeyVisual(int keyIndex)
    {
        ReleaseKey(keyIndex);
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
