using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace GenshinMusicStudio_WinUI.Services;

/// <summary>
/// Falling-note view with a standard piano keybed. Lanes follow real keyboard
/// geometry: seven wide white keys per octave with narrower black keys in the
/// gaps, and the keybed is drawn into the same scrolling canvas as the notes so
/// every falling note lands exactly on the key that plays it.
/// </summary>
public sealed class WaterfallVisualization : IMidiVisualization
{
    private const double PixelsPerSecond = 140;
    private const double RespawnInterval = 0.5;
    private const double HighlightInterval = 0.25;
    private const double MinWhiteKeyWidth = 11;
    private const double MaxWhiteKeyWidth = 40;
    private const double BlackKeyWidthRatio = 0.6;
    private const double BlackKeyHeightRatio = 0.62;
    private const int DefaultLowPitch = 48;
    private const int DefaultHighPitch = 83;

    private static readonly Windows.UI.Color WhiteKeyFill = Windows.UI.Color.FromArgb(255, 247, 248, 251);
    private static readonly Windows.UI.Color WhiteKeyBorder = Windows.UI.Color.FromArgb(255, 186, 194, 208);
    private static readonly Windows.UI.Color BlackKeyFill = Windows.UI.Color.FromArgb(255, 30, 34, 44);
    private static readonly Windows.UI.Color BlackKeyBorder = Windows.UI.Color.FromArgb(255, 12, 15, 22);
    private static readonly Windows.UI.Color PressedWhite = Windows.UI.Color.FromArgb(255, 232, 163, 61);
    private static readonly Windows.UI.Color PressedBlack = Windows.UI.Color.FromArgb(255, 198, 132, 36);

    private IReadOnlyList<MidiPlayer.MidiNote> notes = Array.Empty<MidiPlayer.MidiNote>();
    private readonly Dictionary<int, List<MidiPlayer.MidiNote>> notesByPitch = new();
    private readonly Dictionary<int, Rectangle> keyRects = new();
    private readonly HashSet<int> pressedKeys = new();
    private double duration;
    private double minPitch = DefaultLowPitch;
    private double maxPitch = DefaultHighPitch;
    private double whiteKeyWidth = 24;
    private double keyboardHeight = 48;
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
        ApplyPitchRange();

        notesByPitch.Clear();
        foreach (var note in notes)
        {
            if (!notesByPitch.TryGetValue(note.Pitch, out var list))
            {
                list = new List<MidiPlayer.MidiNote>();
                notesByPitch[note.Pitch] = list;
            }
            list.Add(note);
        }

        owner.Children.Clear();
        canvas = new Canvas { VerticalAlignment = VerticalAlignment.Bottom };
        transform = new TranslateTransform();
        canvas.RenderTransform = transform;
        owner.Children.Add(canvas);

        renderBase = 0;
        RenderWindow(0);
    }

    public void RenderIdle()
    {
        if (canvas is null) return;
        minPitch = DefaultLowPitch;
        maxPitch = DefaultHighPitch;
        renderBase = 0;
        if (transform is not null) transform.Y = 0;
        canvas.Children.Clear();
        keyRects.Clear();
        pressedKeys.Clear();
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
        ReleasePressedKeys();
        renderBase = 0;
        if (transform is not null) transform.Y = 0;
        RenderWindow(0);
    }

    private void ApplyPitchRange()
    {
        if (notes.Count > 0)
        {
            minPitch = Math.Floor((double)notes.Min(n => n.Pitch));
            maxPitch = Math.Ceiling((double)notes.Max(n => n.Pitch));
            if (minPitch > DefaultLowPitch) minPitch = DefaultLowPitch;
            if (maxPitch < DefaultHighPitch) maxPitch = DefaultHighPitch;
        }
        else
        {
            minPitch = DefaultLowPitch;
            maxPitch = DefaultHighPitch;
        }
    }

    private void RenderWindow(double baseTime)
    {
        if (canvas is null) return;
        var viewportWidth = SafeWidth();
        var viewportHeight = SafeHeight();
        var contentWidth = GetContentWidth(viewportWidth);
        var canvasWidth = Math.Max(viewportWidth, contentWidth);
        var left = (canvasWidth - contentWidth) / 2;
        var canvasHeight = viewportHeight;
        var windowSeconds = canvasHeight / PixelsPerSecond;

        canvas.Width = canvasWidth;
        canvas.Height = canvasHeight;
        canvas.Children.Clear();
        keyRects.Clear();
        pressedKeys.Clear();

        var laneBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 128, 128, 128));
        var landmarkBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(55, 128, 128, 128));
        for (var pitch = (int)minPitch; pitch <= (int)maxPitch; pitch++)
        {
            var line = new Rectangle
            {
                Width = 1,
                Height = canvasHeight,
                Fill = pitch % 12 == 0 ? landmarkBrush : laneBrush,
            };
            Canvas.SetLeft(line, left + PitchToX(pitch) + LaneWidth(pitch) - 1);
            Canvas.SetTop(line, 0);
            canvas.Children.Add(line);
        }

        var noteBrush = (Brush)Application.Current.Resources["AccentBlueBrush"];
        foreach (var note in notes)
        {
            if (note.End < baseTime - 0.1) continue;
            if (note.Start > baseTime + windowSeconds) continue;
            var top = canvasHeight - (note.End - baseTime) * PixelsPerSecond;
            if (top > canvasHeight) continue;
            var rect = new Rectangle
            {
                Width = Math.Max(3, LaneWidth(note.Pitch) - 3),
                Height = Math.Max(6, (note.End - note.Start) * PixelsPerSecond),
                RadiusX = 3,
                RadiusY = 3,
                Fill = noteBrush,
                Opacity = 0.88,
                Tag = note,
            };
            Canvas.SetLeft(rect, left + PitchToX(note.Pitch) + 1.5);
            Canvas.SetTop(rect, top);
            canvas.Children.Add(rect);
        }

        DrawKeyboard(left, canvasHeight);
    }

    /// <summary>Paints the piano keybed at the bottom of the same canvas.</summary>
    private void DrawKeyboard(double left, double canvasHeight)
    {
        if (canvas is null) return;
        keyboardHeight = Math.Clamp(whiteKeyWidth * 2.2, 26, 54);
        var top = canvasHeight - keyboardHeight;
        var low = (int)Math.Floor(minPitch);
        var high = (int)Math.Ceiling(maxPitch);

        var backing = new Rectangle
        {
            Width = GetContentWidth(SafeWidth()),
            Height = keyboardHeight,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 22, 26, 34)),
        };
        Canvas.SetLeft(backing, left);
        Canvas.SetTop(backing, top);
        canvas.Children.Add(backing);

        for (var pitch = low; pitch <= high; pitch++)
        {
            if (IsSharp(pitch)) continue;
            var rect = new Rectangle
            {
                Width = Math.Max(3, whiteKeyWidth - 1.5),
                Height = keyboardHeight - 2,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(WhiteKeyFill),
                Stroke = new SolidColorBrush(WhiteKeyBorder),
                StrokeThickness = 1.1,
                Tag = pitch,
            };
            Canvas.SetLeft(rect, left + PitchToX(pitch) + 0.75);
            Canvas.SetTop(rect, top + 1);
            canvas.Children.Add(rect);
            keyRects[pitch] = rect;
        }

        var blackWidth = Math.Max(3, whiteKeyWidth * BlackKeyWidthRatio);
        var blackHeight = keyboardHeight * BlackKeyHeightRatio;
        for (var pitch = low; pitch <= high; pitch++)
        {
            if (!IsSharp(pitch)) continue;
            var centerX = left + BlackKeyCenterX(pitch);
            var rect = new Rectangle
            {
                Width = blackWidth,
                Height = blackHeight,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(BlackKeyFill),
                Stroke = new SolidColorBrush(BlackKeyBorder),
                StrokeThickness = 1.1,
                Tag = pitch,
            };
            Canvas.SetLeft(rect, centerX - blackWidth / 2);
            Canvas.SetTop(rect, top);
            canvas.Children.Add(rect);
            keyRects[pitch] = rect;
        }
    }

    public void UpdateKeyAnimation(double position)
    {
        foreach (var pitch in keyRects.Keys)
        {
            var pressed = false;
            if (notesByPitch.TryGetValue(pitch, out var list))
            {
                foreach (var note in list)
                {
                    if (position >= note.Start - 0.02 && position <= note.End)
                    {
                        pressed = true;
                        break;
                    }
                }
            }

            if (pressed)
            {
                if (!pressedKeys.Add(pitch)) continue;
            }
            else if (!pressedKeys.Remove(pitch))
            {
                continue;
            }
            ApplyKeyVisual(pitch, keyRects[pitch], pressed);
        }
    }

    private static void ApplyKeyVisual(int pitch, Rectangle key, bool pressed)
    {
        var sharp = IsSharp(pitch);
        key.Fill = new SolidColorBrush(pressed
            ? sharp ? PressedBlack : PressedWhite
            : sharp ? BlackKeyFill : WhiteKeyFill);
    }

    private void ReleasePressedKeys()
    {
        foreach (var pitch in pressedKeys.ToList())
        {
            if (keyRects.TryGetValue(pitch, out var key))
            {
                ApplyKeyVisual(pitch, key, false);
            }
        }
        pressedKeys.Clear();
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

    private double GetContentWidth(double viewportWidth)
    {
        var low = (int)Math.Floor(minPitch);
        var high = (int)Math.Ceiling(maxPitch);
        var whiteCount = 0;
        for (var pitch = low; pitch <= high; pitch++)
        {
            if (!IsSharp(pitch)) whiteCount++;
        }
        if (whiteCount < 1) whiteCount = 1;
        whiteKeyWidth = Math.Clamp(viewportWidth / whiteCount, MinWhiteKeyWidth, MaxWhiteKeyWidth);
        var width = 0.0;
        for (var pitch = low; pitch <= high; pitch++)
        {
            width += LaneWidth(pitch);
        }
        return width;
    }

    private double LaneWidth(int pitch) => IsSharp(pitch) ? whiteKeyWidth * BlackKeyWidthRatio : whiteKeyWidth;

    private double PitchToX(int pitch)
    {
        var x = 0.0;
        var low = (int)Math.Floor(minPitch);
        for (var p = low; p < pitch; p++)
        {
            x += LaneWidth(p);
        }
        return x;
    }

    /// <summary>Black keys are centred in the white-key gap they sound between.</summary>
    private double BlackKeyCenterX(int pitch)
    {
        var lowerWhite = PreviousWhitePitch(pitch);
        return (PitchToX(lowerWhite) + PitchToX(lowerWhite + 1)) / 2;
    }

    private static int PreviousWhitePitch(int pitch)
    {
        var candidate = pitch - 1;
        while (candidate >= 0 && IsSharp(candidate))
        {
            candidate--;
        }
        return candidate;
    }

    private static bool IsSharp(int pitch)
    {
        return ((pitch % 12) + 12) % 12 is 1 or 3 or 6 or 8 or 10;
    }

    private double SafeWidth()
    {
        var width = viewport?.ActualWidth ?? 0;
        return double.IsNaN(width) || width < 100 ? 1100 : width;
    }

    private double SafeHeight()
    {
        var height = viewport?.ActualHeight ?? 0;
        return double.IsNaN(height) || height < 100 ? 420 : height;
    }
}
