using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace GenshinMusicStudio_WinUI.Services;

/// <summary>
/// Falling-note view with a piano keybed pinned to the bottom. The keybed is
/// drawn inside the note canvas but carries an inverse transform, so it stays
/// fixed on screen while the notes scroll past it.
/// </summary>
public sealed class WaterfallVisualization : IMidiVisualization
{
    private const double PixelsPerSecond = 140;
    private const double RespawnInterval = 0.5;
    private const double HighlightInterval = 0.25;
    private const double FixedWhiteKeyWidth = 34;
    private const int DefaultLowPitch = 48;
    private const int DefaultHighPitch = 83;

    private static readonly Windows.UI.Color WhiteKeyFill = Windows.UI.Color.FromArgb(255, 242, 242, 245);
    private static readonly Windows.UI.Color WhiteKeyBorder = Windows.UI.Color.FromArgb(255, 172, 172, 180);
    private static readonly Windows.UI.Color WhiteKeyText = Windows.UI.Color.FromArgb(255, 46, 46, 52);
    private static readonly Windows.UI.Color BlackKeyFill = Windows.UI.Color.FromArgb(255, 8, 8, 10);
    private static readonly Windows.UI.Color BlackKeyBorder = Windows.UI.Color.FromArgb(255, 0, 0, 0);
    private static readonly Windows.UI.Color BlackKeyText = Windows.UI.Color.FromArgb(255, 236, 146, 46);
    private static readonly Windows.UI.Color PressedWhite = Windows.UI.Color.FromArgb(255, 232, 163, 61);
    private static readonly Windows.UI.Color PressedBlack = Windows.UI.Color.FromArgb(255, 120, 78, 20);

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private IReadOnlyList<MidiPlayer.MidiNote> notes = Array.Empty<MidiPlayer.MidiNote>();
    private readonly Dictionary<int, List<MidiPlayer.MidiNote>> notesByPitch = new();
    private readonly Dictionary<int, Rectangle> keyRects = new();
    private readonly HashSet<int> pressedKeys = new();
    private double duration;
    private double minPitch = DefaultLowPitch;
    private double maxPitch = DefaultHighPitch;
    private double whiteKeyWidth = 24;
    private double keyboardHeight = 48;
    private Canvas? contentHost;
    private Canvas? canvas;
    private Canvas? keybed;
    private TranslateTransform? keybedCounter;
    private ScrollViewer? scroller;
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

        scroller = new ScrollViewer
        {
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            ZoomMode = ZoomMode.Disabled,
        };
        scroller.ViewChanged += (_, _) => SyncKeybedPan();
        owner.Children.Add(scroller);

        contentHost = new Canvas();
        scroller.Content = contentHost;

        // Notes scroll vertically inside the host.
        canvas = new Canvas();
        transform = new TranslateTransform();
        canvas.RenderTransform = transform;
        contentHost.Children.Add(canvas);

        // The keybed sits outside the scroller so it can never move vertically;
        // it follows horizontal scrolling via its own offset instead.
        keybed = new Canvas
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
        };
        keybedCounter = new TranslateTransform();
        keybed.RenderTransform = keybedCounter;
        owner.Children.Add(keybed);

        renderBase = 0;
        RenderWindow(0);
        RenderKeybed();
    }

    public void RenderIdle()
    {
        if (canvas is null) return;
        minPitch = DefaultLowPitch;
        maxPitch = DefaultHighPitch;
        renderBase = 0;
        if (transform is not null) transform.Y = 0;
        var viewportWidth = SafeWidth();
        var contentWidth = GetContentWidth(viewportWidth);
        canvas.Width = Math.Max(viewportWidth, contentWidth);
        canvas.Height = SafeHeight();
        canvas.Children.Clear();
        RenderKeybed();
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
        RenderKeybed();
    }

    /// <summary>
    /// Re-lays out after a viewport resize without rebuilding the scroller or
    /// dropping the per-key press animation state.
    /// </summary>
    public void Resize()
    {
        if (canvas is null) return;
        RenderWindow(renderBase);
        RenderKeybed();
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
        var scrollOffset = scroller?.HorizontalOffset ?? 0;
        var viewportWidth = SafeWidth();
        var viewportHeight = SafeHeight();
        var contentWidth = GetContentWidth(viewportWidth);
        var canvasWidth = Math.Max(viewportWidth, contentWidth);
        var noteLeft = contentWidth <= viewportWidth ? (viewportWidth - contentWidth) / 2 : 0;
        var canvasHeight = viewportHeight;
        // Notes stop at the top edge of the keybed, so that edge is the hit line.
        var hitY = canvasHeight - keyboardHeight;
        var windowSeconds = hitY / PixelsPerSecond;

        if (contentHost is not null)
        {
            if (Math.Abs(contentHost.Width - canvasWidth) > 0.5) contentHost.Width = canvasWidth;
            contentHost.Height = canvasHeight;
        }
        if (Math.Abs(canvas.Width - canvasWidth) > 0.5) canvas.Width = canvasWidth;
        canvas.Height = canvasHeight;
        if (keybed is not null)
        {
            keybed.Width = viewportWidth;
            keybed.Height = keyboardHeight;
        }
        canvas.Children.Clear();

        var laneBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 128, 128, 128));
        var landmarkBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(52, 128, 128, 128));
        for (var pitch = (int)minPitch; pitch <= (int)maxPitch; pitch++)
        {
            var line = new Rectangle
            {
                Width = 1,
                Height = canvasHeight,
                Fill = pitch % 12 == 0 ? landmarkBrush : laneBrush,
            };
            Canvas.SetLeft(line, noteLeft + PitchToX(pitch) + LaneWidth(pitch) - 1);
            Canvas.SetTop(line, 0);
            canvas.Children.Add(line);
        }

        var noteBrush = (Brush)Application.Current.Resources["AccentBlueBrush"];
        foreach (var note in notes)
        {
            if (note.End < baseTime - 0.1) continue;
            if (note.Start > baseTime + windowSeconds) continue;
            var top = hitY - (note.End - baseTime) * PixelsPerSecond;
            if (top > hitY) continue;
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
            Canvas.SetLeft(rect, noteLeft + PitchToX(note.Pitch) + 1.5);
            Canvas.SetTop(rect, top);
            canvas.Children.Add(rect);
        }

        if (scroller is not null && scrollOffset > 0)
        {
            scroller.ChangeView(scrollOffset, null, null, true);
        }
    }

    /// <summary>Keeps the keybed aligned with the horizontally scrolled sheet.</summary>
    private void SyncKeybedPan()
    {
        if (keybedCounter is null) return;
        keybedCounter.X = -(scroller?.HorizontalOffset ?? 0);
        keybedCounter.Y = 0;
    }

    /// <summary>
    /// Draws the keybed at the bottom of the viewport. It lives outside the
    /// scroller so it never moves vertically, and mirrors the horizontal scroll
    /// offset so it stays lined up with the notes.
    /// </summary>
    private void RenderKeybed()
    {
        if (keybed is null) return;
        var viewportWidth = SafeWidth();
        var viewportHeight = SafeHeight();
        var contentWidth = GetContentWidth(viewportWidth);
        var left = contentWidth <= viewportWidth ? (viewportWidth - contentWidth) / 2 : 0;
        keyboardHeight = Math.Clamp(whiteKeyWidth * 2.4, 28, 58);
        const double top = 0;
        SyncKeybedPan();

        keybed.Width = viewportWidth;
        keybed.Height = keyboardHeight;
        keybed.Children.Clear();
        keyRects.Clear();

        var low = (int)Math.Floor(minPitch);
        var high = (int)Math.Ceiling(maxPitch);

        var backing = new Rectangle
        {
            Width = contentWidth,
            Height = keyboardHeight,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 236, 236, 240)),
        };
        Canvas.SetLeft(backing, left);
        Canvas.SetTop(backing, top);
        keybed.Children.Add(backing);

        // Every semitone gets one equal-sized key, laid side by side. Sharps are
        // simply darker, so nothing overlaps and the row stays uniform.
        for (var pitch = low; pitch <= high; pitch++)
        {
            var sharp = IsSharp(pitch);
            var rect = new Rectangle
            {
                Width = Math.Max(3, whiteKeyWidth - 2),
                Height = keyboardHeight - 2,
                Fill = new SolidColorBrush(sharp ? BlackKeyFill : WhiteKeyFill),
                Stroke = new SolidColorBrush(sharp ? BlackKeyBorder : WhiteKeyBorder),
                StrokeThickness = 1,
                Tag = pitch,
            };
            Canvas.SetLeft(rect, left + PitchToX(pitch) + 1);
            Canvas.SetTop(rect, top + 1);
            keybed.Children.Add(rect);
            keyRects[pitch] = rect;
        }

        RenderKeyLabels(left, top);
        // Re-apply the press animation to any key that is still held, so a
        // rebuild (resize) never makes the highlight disappear.
        foreach (var pitch in pressedKeys.ToList())
        {
            if (keyRects.TryGetValue(pitch, out var held))
            {
                ApplyKeyVisual(pitch, held, true);
            }
        }
    }

    /// <summary>Every key carries its note name, centred in the key.</summary>
    private void RenderKeyLabels(double left, double top)
    {
        if (keybed is null) return;
        var fontSize = Math.Clamp(whiteKeyWidth * 0.3, 8, 12);
        var low = (int)Math.Floor(minPitch);
        var high = (int)Math.Ceiling(maxPitch);

        for (var pitch = low; pitch <= high; pitch++)
        {
            var label = new TextBlock
            {
                Text = NoteName(pitch),
                FontSize = fontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(IsSharp(pitch) ? BlackKeyText : WhiteKeyText),
                IsHitTestVisible = false,
            };
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var centerX = left + PitchToX(pitch) + whiteKeyWidth / 2;
            Canvas.SetLeft(label, centerX - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, top + (keyboardHeight - label.DesiredSize.Height) / 2);
            keybed.Children.Add(label);
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
        // A fixed key width keeps the keyboard identical no matter which sheet
        // is open; wider ranges simply extend the content and scroll.
        whiteKeyWidth = FixedWhiteKeyWidth;
        keyboardHeight = Math.Clamp(whiteKeyWidth * 2.4, 28, 58);
        var width = 0.0;
        for (var pitch = low; pitch <= high; pitch++)
        {
            width += LaneWidth(pitch);
        }
        return width;
    }

    /// <summary>Every semitone gets the same lane width, matching the uniform keys.</summary>
    private double LaneWidth(int pitch) => whiteKeyWidth;

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

    private static bool IsSharp(int pitch)
    {
        return ((pitch % 12) + 12) % 12 is 1 or 3 or 6 or 8 or 10;
    }

    private static string NoteName(int pitch)
    {
        var pc = ((pitch % 12) + 12) % 12;
        return NoteNames[pc] + (pitch / 12 - 1);
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
