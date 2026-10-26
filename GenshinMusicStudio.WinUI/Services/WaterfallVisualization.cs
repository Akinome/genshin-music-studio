using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private const double FixedWhiteKeyWidth = 34;
    private const int DefaultLowPitch = 48;
    private const int DefaultHighPitch = 83;

    private static readonly Windows.UI.Color WhiteKeyFill = Windows.UI.Color.FromArgb(255, 242, 242, 245);
    private static readonly Windows.UI.Color WhiteKeyBorder = Windows.UI.Color.FromArgb(255, 172, 172, 180);
    private static readonly Windows.UI.Color WhiteKeyText = Windows.UI.Color.FromArgb(255, 46, 46, 52);
    private static readonly Windows.UI.Color BlackKeyFill = Windows.UI.Color.FromArgb(255, 8, 8, 10);
    private static readonly Windows.UI.Color BlackKeyBorder = Windows.UI.Color.FromArgb(255, 0, 0, 0);
    private static readonly Windows.UI.Color BlackKeyText = Windows.UI.Color.FromArgb(255, 236, 146, 46);

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private IReadOnlyList<MidiPlayer.MidiNote> notes = Array.Empty<MidiPlayer.MidiNote>();
    private readonly List<WaterfallNoteTiming> noteTimings = new();
    private readonly Dictionary<int, List<MidiPlayer.MidiNote>> notesByPitch = new();
    private readonly Dictionary<int, Rectangle> keyRects = new();
    private readonly Dictionary<int, Rectangle> noteRects = new();
    private readonly HashSet<int> activeNotes = new();
    private readonly HashSet<int> pressedKeys = new();
    private readonly WaterfallRenderWindow renderWindow = new();
    private double duration;
    private double minPitch = DefaultLowPitch;
    private double maxPitch = DefaultHighPitch;
    private double whiteKeyWidth = 24;
    private double keyboardHeight = 48;
    private Canvas? contentHost;
    private Canvas? laneCanvas;
    private Canvas? canvas;
    private Canvas? keybed;
    private TranslateTransform? keybedCounter;
    private TranslateTransform? panTransform;
    private double panX;
    private double panMin;
    private double panMax;
    private Grid? viewport;
    private double lastPosition;
    private Brush? normalNoteBrush;
    private Brush? activeNoteBrush;
    private bool dragging;
    private double dragStartX;
    private double dragStartPanX;

    public string Name => "瀑布流";

    public void Initialize(Grid owner, IReadOnlyList<MidiPlayer.MidiNote> notes, double duration)
    {
        this.notes = notes;
        this.duration = duration;
        viewport = owner;
        lastPosition = 0;
        panX = 0;
        ApplyPitchRange();

        notesByPitch.Clear();
        noteTimings.Clear();
        noteRects.Clear();
        activeNotes.Clear();
        renderWindow.Clear();
        foreach (var note in notes)
        {
            if (!notesByPitch.TryGetValue(note.Pitch, out var list))
            {
                list = new List<MidiPlayer.MidiNote>();
                notesByPitch[note.Pitch] = list;
            }
            list.Add(note);
            noteTimings.Add(new WaterfallNoteTiming(note.Start, note.End));
        }

        owner.Children.Clear();

        // The viewport itself handles dragging, so no ScrollViewer is needed and
        // nothing can swallow the pointer gestures.
        owner.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        owner.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Scroller_PointerPressed), true);
        owner.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Scroller_PointerMoved), true);
        owner.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Scroller_PointerReleased), true);
        owner.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Scroller_PointerCaptureLost), true);

        contentHost = new Canvas();
        contentHost.HorizontalAlignment = HorizontalAlignment.Left;
        contentHost.VerticalAlignment = VerticalAlignment.Top;
        panTransform = new TranslateTransform();
        contentHost.RenderTransform = panTransform;
        owner.Children.Add(contentHost);

        // Lane guides stay fixed while the notes move over them.
        laneCanvas = new Canvas();
        contentHost.Children.Add(laneCanvas);

        // Note shapes are pooled and repositioned in place. Rebuilding the
        // whole canvas every half second made active notes flash between the
        // normal and playing colors.
        canvas = new Canvas();
        contentHost.Children.Add(canvas);

        // The keybed sits outside the scrolling content so it never moves
        // vertically; it mirrors the horizontal pan to stay aligned.
        keybed = new Canvas
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
        };
        keybedCounter = new TranslateTransform();
        keybed.RenderTransform = keybedCounter;
        owner.Children.Add(keybed);

        RenderLanes();
        RenderWindow(0);
        RenderKeybed();
    }

    public void RenderIdle()
    {
        if (canvas is null) return;
        minPitch = DefaultLowPitch;
        maxPitch = DefaultHighPitch;
        lastPosition = 0;
        renderWindow.Clear();
        noteRects.Clear();
        activeNotes.Clear();
        var viewportWidth = SafeWidth();
        var contentWidth = GetContentWidth(viewportWidth);
        if (contentHost is not null)
        {
            contentHost.Width = Math.Max(viewportWidth, contentWidth);
            contentHost.Height = SafeHeight();
        }
        if (laneCanvas is not null)
        {
            laneCanvas.Width = Math.Max(viewportWidth, contentWidth);
            laneCanvas.Height = SafeHeight();
        }
        canvas.Width = Math.Max(viewportWidth, contentWidth);
        canvas.Height = SafeHeight();
        canvas.Children.Clear();
        RenderLanes();
        RenderKeybed();
    }

    public void Update(double position)
    {
        if (canvas is null || viewport is null) return;
        lastPosition = position;
        RenderWindow(position);
    }

    public void Reset()
    {
        ReleasePressedKeys();
        lastPosition = 0;
        renderWindow.Clear();
        noteRects.Clear();
        activeNotes.Clear();
        canvas?.Children.Clear();
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
        RenderLanes();
        RenderWindow(lastPosition);
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
        if (canvas is null || laneCanvas is null) return;
        var viewportWidth = SafeWidth();
        var viewportHeight = SafeHeight();
        var contentWidth = GetContentWidth(viewportWidth);
        var canvasWidth = Math.Max(viewportWidth, contentWidth);
        // Centre the sheet when it fits, otherwise clamp the pan to its edges.
        panMin = Math.Min(0, viewportWidth - contentWidth);
        panMax = Math.Max(0, (viewportWidth - contentWidth) / 2);
        if (contentWidth <= viewportWidth)
        {
            panX = panMax;
        }
        panX = Math.Clamp(panX, panMin, panMax);
        if (panTransform is not null) panTransform.X = panX;
        var noteLeft = 0.0;
        var canvasHeight = viewportHeight;
        // Notes stop at the top edge of the keybed, so that edge is the hit line.
        var hitY = canvasHeight - keyboardHeight;
        var windowSeconds = hitY / PixelsPerSecond;
        var rasterizationScale = viewport?.XamlRoot?.RasterizationScale ?? 1.0;
        if (rasterizationScale <= 0) rasterizationScale = 1.0;

        if (contentHost is not null)
        {
            if (Math.Abs(contentHost.Width - canvasWidth) > 0.5) contentHost.Width = canvasWidth;
            contentHost.Height = canvasHeight;
        }
        if (Math.Abs(laneCanvas.Width - canvasWidth) > 0.5) laneCanvas.Width = canvasWidth;
        laneCanvas.Height = canvasHeight;
        if (Math.Abs(canvas.Width - canvasWidth) > 0.5) canvas.Width = canvasWidth;
        canvas.Height = canvasHeight;
        if (keybed is not null)
        {
            keybed.Width = viewportWidth;
            keybed.Height = keyboardHeight;
        }

        var change = renderWindow.Update(noteTimings, baseTime, windowSeconds);
        foreach (var index in change.Removed)
        {
            if (noteRects.Remove(index, out var removed))
            {
                canvas.Children.Remove(removed);
            }
            activeNotes.Remove(index);
        }

        normalNoteBrush ??= (Brush)Application.Current.Resources["AccentBlueBrush"];
        activeNoteBrush ??= new SolidColorBrush(Microsoft.UI.Colors.White);

        foreach (var index in change.Added)
        {
            if (index < 0 || index >= notes.Count) continue;
            var note = notes[index];
            var rect = new Rectangle
            {
                Width = Math.Max(3, LaneWidth(note.Pitch) - 3),
                Height = Math.Max(6, (note.End - note.Start) * PixelsPerSecond),
                RadiusX = 3,
                RadiusY = 3,
                Fill = normalNoteBrush,
                Opacity = 0.88,
                Tag = note,
            };
            Canvas.SetLeft(rect, noteLeft + PitchToX(note.Pitch) + 1.5);
            canvas.Children.Add(rect);
            noteRects[index] = rect;
        }

        foreach (var (index, rect) in noteRects)
        {
            if (index < 0 || index >= notes.Count) continue;
            var note = notes[index];
            var top = hitY - (note.End - baseTime) * PixelsPerSecond;
            if (rect.RenderTransform is not TranslateTransform noteTransform)
            {
                noteTransform = new TranslateTransform();
                rect.RenderTransform = noteTransform;
            }
            noteTransform.Y = SnapToPhysicalPixel(top, rasterizationScale);

            var isActive = baseTime >= note.Start - 0.02 && baseTime <= note.End;
            if (isActive == activeNotes.Contains(index)) continue;
            if (isActive)
            {
                activeNotes.Add(index);
            }
            else
            {
                activeNotes.Remove(index);
            }
            rect.Fill = isActive ? activeNoteBrush : normalNoteBrush;
        }

        SyncKeybedPan();
    }

    /// <summary>
    /// Renders the vertical pitch guides once per layout. They do not scroll,
    /// so rebuilding them during playback only introduces unnecessary work.
    /// </summary>
    private void RenderLanes()
    {
        if (laneCanvas is null) return;
        var viewportWidth = SafeWidth();
        var contentWidth = GetContentWidth(viewportWidth);
        var canvasWidth = Math.Max(viewportWidth, contentWidth);
        var canvasHeight = SafeHeight();

        laneCanvas.Width = canvasWidth;
        laneCanvas.Height = canvasHeight;
        laneCanvas.Children.Clear();

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
            Canvas.SetLeft(line, PitchToX(pitch) + LaneWidth(pitch) - 1);
            Canvas.SetTop(line, 0);
            laneCanvas.Children.Add(line);
        }
    }

    private static double SnapToPhysicalPixel(double value, double rasterizationScale) =>
        Math.Round(value * rasterizationScale, MidpointRounding.AwayFromZero) / rasterizationScale;

    /// <summary>Keeps the keybed aligned with the horizontally scrolled sheet.</summary>
    private void SyncKeybedPan()
    {
        if (keybedCounter is null) return;
        keybedCounter.X = panX;
        keybedCounter.Y = 0;
    }

    /// <summary>Press anywhere on the sheet to start dragging it sideways.</summary>
    private void Scroller_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (viewport is null || panMax <= panMin) return;
        var point = e.GetCurrentPoint(viewport);
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) return;
        if (!point.Properties.IsLeftButtonPressed) return;
        dragging = true;
        dragStartX = point.Position.X;
        dragStartPanX = panX;
        viewport.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Scroller_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!dragging || viewport is null) return;
        var x = e.GetCurrentPoint(viewport).Position.X;
        panX = Math.Clamp(dragStartPanX + (x - dragStartX), panMin, panMax);
        if (panTransform is not null) panTransform.X = panX;
        SyncKeybedPan();
        e.Handled = true;
    }

    private void Scroller_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!dragging || viewport is null) return;
        dragging = false;
        viewport.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void Scroller_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => dragging = false;

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
        // Centring is handled by the shared pan offset, so content coordinates start at 0.
        const double left = 0;
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
        // Use the Windows accent colour so the highlight matches the accent
        // styled buttons (play/pause) instead of a separate hard-coded colour.
        var accent = AccentColor();
        key.Fill = new SolidColorBrush(pressed
            ? sharp ? Darken(accent, 0.68) : accent
            : sharp ? BlackKeyFill : WhiteKeyFill);
    }

    private static Windows.UI.Color AccentColor()
    {
        if (Application.Current.Resources.TryGetValue("AccentBlueBrush", out var value)
            && value is SolidColorBrush brush)
        {
            return brush.Color;
        }
        return Windows.UI.Color.FromArgb(255, 59, 130, 246);
    }

    private static Windows.UI.Color Darken(Windows.UI.Color color, double factor) =>
        Windows.UI.Color.FromArgb(
            color.A,
            (byte)(color.R * factor),
            (byte)(color.G * factor),
            (byte)(color.B * factor));

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
