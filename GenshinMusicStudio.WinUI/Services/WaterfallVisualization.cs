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
    private const double KeyStripHeight = 26;
    private const double KeyCapWidth = 32;
    private const double KeyCapGap = 3;

    private static readonly string[] DegreeNames = { "do", "re", "mi", "fa", "sol", "la", "si" };
    private static readonly Dictionary<int, string> AccidentalNames = new()
    {
        [1] = "reb",
        [3] = "mib",
        [6] = "fab",
        [8] = "lab",
        [10] = "tib",
    };

    private IReadOnlyList<MidiPlayer.MidiNote> notes = Array.Empty<MidiPlayer.MidiNote>();
    private double duration;
    private double minPitch = 48;
    private double maxPitch = 83;
    private Canvas? canvas;
    private Canvas? keyStrip;
    private readonly Dictionary<int, Border> keyCaps = new();
    private readonly HashSet<int> pressedKeys = new();
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
        if (canvas is null || viewport is null) return;
        var (range, keyWidth, _, viewportHeight) = LaneMetrics();
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
        RenderKeyStrip();
    }

    private (int Range, double KeyWidth, double ViewportWidth, double ViewportHeight) LaneMetrics()
    {
        var range = (int)(maxPitch - minPitch);
        if (range < 35) range = 35;
        var viewportWidth = viewport?.ActualWidth ?? 0;
        if (double.IsNaN(viewportWidth) || viewportWidth < 100) viewportWidth = 1100;
        var viewportHeight = viewport?.ActualHeight ?? 0;
        if (double.IsNaN(viewportHeight) || viewportHeight < 100) viewportHeight = 420;
        return (range, Math.Clamp(viewportWidth / (range + 1), 10, 26), viewportWidth, viewportHeight);
    }

    private void RenderKeyStrip()
    {
        if (keyStrip is null || canvas is null) return;
        var (_, _, viewportWidth, _) = LaneMetrics();
        keyStrip.Width = viewportWidth;
        keyStrip.Height = KeyStripHeight;
        keyStrip.Children.Clear();
        keyCaps.Clear();
        pressedKeys.Clear();

        var total = 21 * KeyCapWidth + 20 * KeyCapGap;
        var startX = Math.Max(8, (viewportWidth - total) / 2);
        var background = new Rectangle
        {
            Width = total + 16,
            Height = KeyStripHeight,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 24, 28, 36)),
            RadiusX = 7,
            RadiusY = 7,
        };
        Canvas.SetLeft(background, startX - 8);
        Canvas.SetTop(background, 0);
        keyStrip.Children.Add(background);

        for (var button = 0; button < 21; button++)
        {
            var pitch = PitchForButton(button);
            var cap = new Border
            {
                Width = KeyCapWidth,
                Height = KeyStripHeight - 6,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(KeyCapFill(pitch)),
                Child = new TextBlock
                {
                    Text = KeyNameForPitch(pitch),
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Canvas.SetLeft(cap, startX + button * (KeyCapWidth + KeyCapGap));
            Canvas.SetTop(cap, 3);
            keyStrip.Children.Add(cap);
            keyCaps[pitch] = cap;
        }
    }

    private static int PitchForButton(int button)
    {
        var octave = button / 7;
        var degree = button % 7;
        return 48 + octave * 12 + Key21Layout.DegreeToSemitone(degree);
    }

    public void UpdateKeyAnimation(double position)
    {
        foreach (var pair in keyCaps)
        {
            var pressed = false;
            foreach (var note in notes)
            {
                if (note.Pitch != pair.Key) continue;
                if (position >= note.Start - 0.02 && position <= note.End)
                {
                    pressed = true;
                    break;
                }
            }

            if (pressed)
            {
                if (!pressedKeys.Add(pair.Key)) continue;
            }
            else if (!pressedKeys.Remove(pair.Key))
            {
                continue;
            }
            pair.Value.Background = new SolidColorBrush(KeyCapFill(pair.Key, pressed));
        }
    }

    private static Windows.UI.Color KeyCapFill(int pitch, bool pressed = false)
    {
        if (pressed) return Windows.UI.Color.FromArgb(255, 232, 163, 61);
        var sharp = pitch % 12 is 1 or 3 or 6 or 8 or 10;
        return sharp
            ? Windows.UI.Color.FromArgb(255, 88, 96, 112)
            : Windows.UI.Color.FromArgb(255, 58, 64, 78);
    }

    private static string KeyNameForPitch(int pitch)
    {
        var pc = ((pitch % 12) + 12) % 12;
        if (AccidentalNames.TryGetValue(pc, out var accidental)) return accidental;
        var index = (int)(pc * 7.0 / 12.0);
        if (index > 6) index = 6;
        return DegreeNames[index];
    }

    private void HighlightPlayingNotes(double position)
    {
        if (canvas is null) return;
        var normal = (Brush)Application.Current.Resources["AccentBlueBrush"];
        var active = new SolidColorBrush(Microsoft.UI.Colors.White);
        var activeText = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 44, 54));
        var normalText = new SolidColorBrush(Microsoft.UI.Colors.White);
        foreach (var child in canvas.Children)
        {
            if (child is not FrameworkElement element || element.Tag is not MidiPlayer.MidiNote note) continue;
            var lit = position >= note.Start - 0.02 && position <= note.End;
            switch (element)
            {
                case Rectangle rect:
                    rect.Fill = lit ? active : normal;
                    break;
                case TextBlock caption:
                    caption.Foreground = lit ? activeText : normalText;
                    break;
            }
        }
    }
}
