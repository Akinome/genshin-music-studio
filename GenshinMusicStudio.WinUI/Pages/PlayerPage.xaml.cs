using GenshinMusicStudio_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = System.IO.Path;
using Windows.Foundation;

namespace GenshinMusicStudio_WinUI.Pages;

public sealed partial class PlayerPage : Page
{
    private const double PixelsPerSecond = 140;
    private const double RespawnInterval = 0.5;

    private List<MidiPlayer.MidiNote> notes = new();
    private double duration;
    private double minPitch = 48;
    private double maxPitch = 83;
    private double renderBase;
    private double lastUiUpdate;
    private double lastHighlight;
    private TranslateTransform? waterfallTransform;
    private DispatcherQueueTimer? renderTimer;

    public PlayerPage()
    {
        InitializeComponent();
        BoostSlider.Minimum = 100;
        renderTimer = DispatcherQueue.CreateTimer();
        renderTimer.Interval = TimeSpan.FromMilliseconds(16);
        renderTimer.Tick += (_, _) => OnFrame();
        waterfallTransform = new TranslateTransform();
        RollCanvas.RenderTransform = waterfallTransform;
        RollViewport.SizeChanged += (_, args) =>
        {
            RollViewport.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height),
            };
        };
        Loaded += PlayerPage_Loaded;
        Unloaded += PlayerPage_Unloaded;
    }

    private void PlayerPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.Player.PlayingChanged += Player_PlayingChanged;
        App.Player.ProgressChanged += Player_ProgressChanged;
        VolumeSlider.ValueChanged -= VolumeSlider_ValueChanged;
        VolumeSlider.Value = App.Player.Volume * 100;
        VolumeText.Text = (int)Math.Round(App.Player.Volume * 100) + "%";
        VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        BoostSlider.ValueChanged -= BoostSlider_ValueChanged;
        BoostSlider.Value = App.Player.VelocityBoost * 100;
        BoostText.Text = (int)Math.Round(App.Player.VelocityBoost * 100) + "%";
        BoostSlider.ValueChanged += BoostSlider_ValueChanged;
        if (FilePathBox.Text.Length > 0)
        {
            RenderRoll(FilePathBox.Text);
        }
        UpdateControls();
    }

    private void PlayerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        renderTimer?.Stop();
        App.Player.PlayingChanged -= Player_PlayingChanged;
        App.Player.ProgressChanged -= Player_ProgressChanged;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFileAsync(".mid", ".midi");
        if (path is null) return;
        FilePathBox.Text = path;
        RenderRoll(path);
    }

    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(FilePathBox.Text);
        if (folder is null || !Directory.Exists(folder)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        var path = FilePathBox.Text.Trim();
        if (App.Player.IsPlaying)
        {
            App.Player.Stop();
            return;
        }
        if (path.Length == 0 || !File.Exists(path))
        {
            _ = ShowMessageAsync("缺少文件", "请选择有效的 MIDI 文件。");
            return;
        }
        if (notes.Count == 0)
        {
            RenderRoll(path);
        }
        renderBase = 0;
        App.Player.Play(new[] { path });
        renderTimer?.Start();
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => App.Player.Stop();

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var volume = e.NewValue / 100.0;
        App.Player.Volume = volume;
        VolumeText.Text = (int)Math.Round(volume * 100) + "%";
        var settings = AppSettings.Load();
        settings.Volume = volume;
        AppSettings.Save(settings);
    }

    private void BoostSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var boost = e.NewValue / 100.0;
        App.Player.VelocityBoost = boost;
        BoostText.Text = (int)Math.Round(boost * 100) + "%";
        var settings = AppSettings.Load();
        settings.VelocityBoost = boost;
        AppSettings.Save(settings);
    }

    private void Player_PlayingChanged(bool playing)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateControls();
            if (!playing)
            {
                renderTimer?.Stop();
                renderBase = 0;
                RenderNotesWindow(0);
                if (waterfallTransform is not null) waterfallTransform.Y = 0;
            }
        });
    }

    private void Player_ProgressChanged(string file, double position, double total)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            TimeText.Text = FormatTime(position) + " / " + FormatTime(Math.Max(duration, total));
            PlayProgress.Value = total > 0 ? Math.Clamp(position / total * 100, 0, 100) : 0;
        });
    }

    private void OnFrame()
    {
        if (!App.Player.IsPlaying) return;
        var position = App.Player.CurrentPosition;
        if (position - renderBase >= RespawnInterval)
        {
            renderBase = Math.Floor(position / RespawnInterval) * RespawnInterval;
            RenderNotesWindow(renderBase);
        }
        if (waterfallTransform is not null)
        {
            waterfallTransform.Y = (position - renderBase) * PixelsPerSecond;
        }
        if (position - lastUiUpdate >= 0.25)
        {
            lastUiUpdate = position;
            DispatcherQueue.TryEnqueue(() =>
            {
                TimeText.Text = FormatTime(position) + " / " + FormatTime(duration);
                PlayProgress.Value = duration > 0 ? Math.Clamp(position / duration * 100, 0, 100) : 0;
            });
        }
        if (position - lastHighlight >= 0.25)
        {
            lastHighlight = position;
            HighlightPlayingNotes(position);
        }
    }

    private void UpdateControls()
    {
        var playing = App.Player.IsPlaying;
        PlayIcon.Glyph = playing ? "\uE769" : "\uE768";
        PlayText.Text = playing ? "暂停" : "播放";
        StopButton.IsEnabled = playing;
    }

    private void RenderRoll(string path)
    {
        try
        {
            (notes, duration) = MidiPlayer.ParseNotes(path);
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync("无法读取 MIDI", ex.Message);
            return;
        }
        if (notes.Count == 0)
        {
            _ = ShowMessageAsync("没有音符", "该 MIDI 文件中没有识别到音符。");
            return;
        }
        renderBase = 0;
        RenderNotesWindow(0);
        TimeText.Text = "0:00 / " + FormatTime(duration);
    }

    private void RenderNotesWindow(double baseTime)
    {
        if (waterfallTransform is null) return;
        var range = (int)(maxPitch - minPitch);
        if (range < 35) range = 35;
        var viewportWidth = RollViewport.ActualWidth;
        if (double.IsNaN(viewportWidth) || viewportWidth < 100) viewportWidth = 1100;
        var keyWidth = Math.Clamp(viewportWidth / (range + 1), 10, 26);
        var viewportHeight = RollViewport.ActualHeight;
        if (double.IsNaN(viewportHeight) || viewportHeight < 100) viewportHeight = 420;
        var windowSeconds = viewportHeight / PixelsPerSecond + 1.5;
        var canvasHeight = windowSeconds * PixelsPerSecond;
        var canvasWidth = (range + 1) * keyWidth;
        RollCanvas.Width = canvasWidth;
        RollCanvas.Height = canvasHeight;
        RollCanvas.Children.Clear();

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
            RollCanvas.Children.Add(line);
        }

        var noteBrush = (Brush)Application.Current.Resources["AccentBlueBrush"];
        var halfViewport = viewportHeight;
        foreach (var note in notes)
        {
            if (note.End < baseTime - 0.1) continue;
            if (note.Start > baseTime + windowSeconds) continue;
            var top = canvasHeight - (note.End - baseTime) * PixelsPerSecond;
            if (top < -halfViewport || top > canvasHeight) continue;
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
            RollCanvas.Children.Add(rect);
        }
    }

    private void HighlightPlayingNotes(double position)
    {
        var normal = (Brush)Application.Current.Resources["AccentBlueBrush"];
        var active = new SolidColorBrush(Microsoft.UI.Colors.White);
        foreach (var child in RollCanvas.Children.OfType<Rectangle>())
        {
            if (child.Tag is not MidiPlayer.MidiNote note) continue;
            child.Fill = position >= note.Start - 0.02 && position <= note.End ? active : normal;
        }
    }

    private static string FormatTime(double seconds)
    {
        return string.Format("{0:0}:{1:00}", (int)(seconds / 60), (int)seconds % 60);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        await new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
        }.ShowAsync();
    }
}
