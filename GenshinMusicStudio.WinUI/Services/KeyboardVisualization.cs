using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace GenshinMusicStudio_WinUI.Services;

public sealed class KeyboardVisualization : IMidiVisualization
{
    private const double HighlightInterval = 0.05;
    private const double PulseDuration = 0.45;

    private static readonly Windows.UI.Color KeyFill = Windows.UI.Color.FromArgb(255, 224, 223, 209);
    private static readonly Windows.UI.Color GlowNear = Windows.UI.Color.FromArgb(255, 212, 212, 196);
    private static readonly Windows.UI.Color GlowFar = Windows.UI.Color.FromArgb(255, 223, 223, 198);
    private static readonly Windows.UI.Color KeyText = Windows.UI.Color.FromArgb(255, 150, 148, 120);
    private static readonly Windows.UI.Color HitFill = Windows.UI.Color.FromArgb(255, 144, 249, 227);
    private static readonly Windows.UI.Color HitGlow = Windows.UI.Color.FromArgb(204, 144, 249, 227);

    private IReadOnlyList<MidiPlayer.MidiNote> notes = Array.Empty<MidiPlayer.MidiNote>();
    private double duration;
    private Canvas? keyCanvas;
    private Grid? viewport;
    private double lastHighlight;
    private readonly Dictionary<int, List<MidiPlayer.MidiNote>> keyNotes = new();
    private readonly HashSet<int> litKeys = new();
    private readonly List<(int KeyIndex, double Start)> pulses = new();
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

        owner.Children.Clear();
        keyCanvas = new Canvas
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 8),
        };
        owner.Children.Add(keyCanvas);
        BuildKeyboard();
        lastHighlight = -1;
    }

    public void Update(double position)
    {
        UpdatePulses(position);
        if (position - lastHighlight >= HighlightInterval)
        {
            lastHighlight = position;
            Highlight(position);
        }
    }

    public void Reset()
    {
        lastHighlight = -1;
        pulses.Clear();
        ClearLitKeys();
    }

    private void BuildKeyboard()
    {
        if (keyCanvas is null || viewport is null) return;
        var viewportWidth = SafeWidth();
        keyWidth = Math.Clamp((viewportWidth - 24) / 7, 44, 110);
        var keyboardWidth = keyWidth * 7;
        var viewportHeight = SafeHeight();
        var rowHeight = Math.Min((viewportHeight - 16) / 3, keyWidth * 1.25);
        keyCanvas.Width = keyboardWidth;
        keyCanvas.Height = rowHeight * 3 + 12;
        keyCanvas.Children.Clear();

        for (var keyIndex = 0; keyIndex < Key21Layout.KeyCount; keyIndex++)
        {
            var (row, column) = Key21Layout.KeyPosition(keyIndex);
            var centerX = column * keyWidth + keyWidth / 2;
            var centerY = row * rowHeight + rowHeight / 2 + 6;
            var diameter = Math.Min(keyWidth - 12, rowHeight - 10);

            var glow = new Ellipse
            {
                Width = diameter + 14,
                Height = diameter + 14,
                Fill = new SolidColorBrush(GlowNear),
                Opacity = 0,
                IsHitTestVisible = false,
                Tag = 1000 + keyIndex,
            };
            Canvas.SetLeft(glow, centerX - glow.Width / 2);
            Canvas.SetTop(glow, centerY - glow.Height / 2);
            keyCanvas.Children.Add(glow);

            var halo = new Ellipse
            {
                Width = diameter + 26,
                Height = diameter + 26,
                Fill = new SolidColorBrush(GlowFar),
                Opacity = 0,
                IsHitTestVisible = false,
                Tag = 2000 + keyIndex,
            };
            Canvas.SetLeft(halo, centerX - halo.Width / 2);
            Canvas.SetTop(halo, centerY - halo.Height / 2);
            keyCanvas.Children.Add(halo);

            var circle = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = new SolidColorBrush(KeyFill),
                Stroke = new SolidColorBrush(GlowNear),
                StrokeThickness = 2,
                Tag = keyIndex,
            };
            Canvas.SetLeft(circle, centerX - diameter / 2);
            Canvas.SetTop(circle, centerY - diameter / 2);
            keyCanvas.Children.Add(circle);

            var label = new TextBlock
            {
                Text = Key21Layout.DegreeNames[keyIndex % 7],
                FontSize = Math.Max(11, diameter * 0.22),
                Foreground = new SolidColorBrush(KeyText),
                IsHitTestVisible = false,
                Tag = 3000 + keyIndex,
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, centerX - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, centerY - label.DesiredSize.Height / 2);
            keyCanvas.Children.Add(label);
        }

        AddTriangles(rowHeight);
    }

    private void AddTriangles(double rowHeight)
    {
        if (keyCanvas is null) return;
        var size = Math.Max(5, keyWidth / 14);
        var gap = 6.0;
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 6; column++)
            {
                var x = (column + 1) * keyWidth;
                var y = row * rowHeight + rowHeight / 2 + 6;
                var left = new Polygon
                {
                    Points = new PointCollection { new(0, -size), new(0, size), new(gap, 0) },
                    Fill = new SolidColorBrush(KeyFill),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(left, x - gap - 2);
                Canvas.SetTop(left, y);
                keyCanvas.Children.Add(left);
                var right = new Polygon
                {
                    Points = new PointCollection { new(gap, -size), new(gap, size), new(0, 0) },
                    Fill = new SolidColorBrush(KeyFill),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(right, x + 2);
                Canvas.SetTop(right, y);
                keyCanvas.Children.Add(right);
            }
        }
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 7; column++)
            {
                var x = column * keyWidth + keyWidth / 2;
                var y = (row + 1) * rowHeight + 6;
                var up = new Polygon
                {
                    Points = new PointCollection { new(-size, 0), new(size, 0), new(0, gap) },
                    Fill = new SolidColorBrush(KeyFill),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(up, x);
                Canvas.SetTop(up, y - gap - 2);
                keyCanvas.Children.Add(up);
                var down = new Polygon
                {
                    Points = new PointCollection { new(-size, gap), new(size, gap), new(0, 0) },
                    Fill = new SolidColorBrush(KeyFill),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(down, x);
                Canvas.SetTop(down, y + 2);
                keyCanvas.Children.Add(down);
            }
        }
    }

    private void Highlight(double position)
    {
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
        foreach (var keyIndex in next)
        {
            if (litKeys.Contains(keyIndex)) continue;
            PressKey(keyIndex);
            pulses.Add((keyIndex, position));
        }
        foreach (var keyIndex in litKeys)
        {
            if (next.Contains(keyIndex)) continue;
            ReleaseKey(keyIndex);
        }
        litKeys.Clear();
        foreach (var keyIndex in next) litKeys.Add(keyIndex);
    }

    private void PressKey(int keyIndex)
    {
        SetKeyBrush(keyIndex, HitFill, HitFill, HitGlow);
        SetLabelBrush(keyIndex, true);
    }

    private void ReleaseKey(int keyIndex)
    {
        SetKeyBrush(keyIndex, KeyFill, GlowNear, Windows.UI.Color.FromArgb(0, 255, 255, 255));
        SetLabelBrush(keyIndex, false);
    }

    private void UpdatePulses(double position)
    {
        if (pulses.Count == 0) return;
        for (var i = pulses.Count - 1; i >= 0; i--)
        {
            var (keyIndex, start) = pulses[i];
            var progress = (position - start) / PulseDuration;
            if (progress >= 1.0)
            {
                SetPulseOpacity(keyIndex, 0, 1.0);
                pulses.RemoveAt(i);
                continue;
            }
            var scale = 1.0 + 0.4 * progress;
            var opacity = 0.55 * (1.0 - progress);
            SetPulseOpacity(keyIndex, opacity, scale);
        }
    }

    private void SetPulseOpacity(int keyIndex, double opacity, double scale)
    {
        if (keyCanvas is null) return;
        foreach (var child in keyCanvas.Children.OfType<Ellipse>())
        {
            if (child.Tag is int index && index == 1000 + keyIndex)
            {
                child.Opacity = opacity;
                return;
            }
        }
    }

    private void SetKeyBrush(int keyIndex, Windows.UI.Color fill, Windows.UI.Color stroke, Windows.UI.Color glow)
    {
        if (keyCanvas is null) return;
        foreach (var child in keyCanvas.Children.OfType<Ellipse>())
        {
            if (child.Tag is not int index) continue;
            if (index == keyIndex)
            {
                child.Fill = new SolidColorBrush(fill);
                child.Stroke = new SolidColorBrush(stroke);
            }
            else if (index == 1000 + keyIndex)
            {
                child.Fill = new SolidColorBrush(glow);
            }
            else if (index == 2000 + keyIndex)
            {
                child.Fill = new SolidColorBrush(glow);
            }
        }
    }

    private void SetLabelBrush(int keyIndex, bool pressed)
    {
        if (keyCanvas is null) return;
        var color = pressed
            ? Windows.UI.Color.FromArgb(204, 255, 255, 255)
            : KeyText;
        foreach (var child in keyCanvas.Children.OfType<TextBlock>())
        {
            if (child.Tag is int index && index == 3000 + keyIndex)
            {
                child.Foreground = new SolidColorBrush(color);
                return;
            }
        }
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
