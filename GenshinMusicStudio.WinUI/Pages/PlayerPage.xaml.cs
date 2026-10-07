using GenshinMusicStudio_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = System.IO.Path;
using Windows.Foundation;

namespace GenshinMusicStudio_WinUI.Pages;

public sealed partial class PlayerPage : Page
{
    private const double PixelsPerSecond = 180;
    private const double RowHeight = 12;

    private List<MidiPlayer.MidiNote> notes = new();
    private double duration;
    private double minPitch = 48;
    private double maxPitch = 83;
    private double playheadSeconds;
    private double lastHighlight;
    private Rectangle? playhead;

    public PlayerPage()
    {
        InitializeComponent();
        BoostSlider.Minimum = 100;
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
        App.Player.Play(new[] { path });
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => App.Player.Stop();

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var volume = e.NewValue / 100.0;
        App.Player.Volume = volume;
        VolumeText.Text = (int)Math.Round(volume * 100) + "%";
        var settings = AppSettings.Load();
        settings.Volume = volume;
        AppSettings.Save(settings);
    }

    private void BoostSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
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
        DispatcherQueue.TryEnqueue(UpdateControls);
    }

    private void Player_ProgressChanged(string file, double position, double total)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            playheadSeconds = position;
            duration = Math.Max(duration, total);
            TimeText.Text = FormatTime(position) + " / " + FormatTime(duration);
            PlayProgress.Value = duration > 0 ? position / duration * 100 : 0;
            MovePlayhead(position);
            if (position - lastHighlight >= 0.2)
            {
                lastHighlight = position;
                HighlightPlayingNotes(position);
            }
        });
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

        minPitch = Math.Floor((double)notes.Min(n => n.Pitch));
        maxPitch = Math.Ceiling((double)notes.Max(n => n.Pitch));
        if (maxPitch - minPitch < 23)
        {
            maxPitch = minPitch + 23;
        }

        var width = Math.Max(1200, duration * PixelsPerSecond + 120);
        var height = (maxPitch - minPitch + 1) * RowHeight;
        RollCanvas.Width = width;
        RollCanvas.Height = height;
        RollCanvas.Children.Clear();

        var gridBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(36, 128, 128, 128));
        var cLineBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 128, 128, 128));
        for (var second = 0; second <= duration; second += 5)
        {
            var line = new Rectangle
            {
                Width = 1,
                Height = height,
                Fill = gridBrush,
            };
            Canvas.SetLeft(line, second * PixelsPerSecond);
            Canvas.SetTop(line, 0);
            RollCanvas.Children.Add(line);
        }
        for (var pitch = (int)minPitch; pitch <= (int)maxPitch; pitch++)
        {
            if (pitch % 12 != 0) continue;
            var line = new Rectangle
            {
                Width = width,
                Height = 1,
                Fill = cLineBrush,
            };
            Canvas.SetLeft(line, 0);
            Canvas.SetTop(line, (maxPitch - pitch) * RowHeight + RowHeight);
            RollCanvas.Children.Add(line);
        }

        var noteBrush = (Brush)Application.Current.Resources["AccentBlueBrush"];
        foreach (var note in notes)
        {
            var rect = new Rectangle
            {
                Width = Math.Max(3, (note.End - note.Start) * PixelsPerSecond),
                Height = RowHeight - 2,
                RadiusX = 2,
                RadiusY = 2,
                Fill = noteBrush,
                Opacity = 0.85,
                Tag = note,
            };
            Canvas.SetLeft(rect, note.Start * PixelsPerSecond);
            Canvas.SetTop(rect, (maxPitch - note.Pitch) * RowHeight + 1);
            RollCanvas.Children.Add(rect);
        }

        playhead = new Rectangle
        {
            Width = 2,
            Height = height,
            Fill = new SolidColorBrush(Microsoft.UI.Colors.Orange),
        };
        Canvas.SetLeft(playhead, 0);
        Canvas.SetTop(playhead, 0);
        RollCanvas.Children.Add(playhead);
        TimeText.Text = "0:00 / " + FormatTime(duration);
    }

    private void MovePlayhead(double position)
    {
        if (playhead is null) return;
        var x = Math.Clamp(position * PixelsPerSecond, 0, RollCanvas.Width - 2);
        Canvas.SetLeft(playhead, x);
        var viewport = RollScroll.ViewportWidth;
        var offset = RollScroll.HorizontalOffset;
        if (x < offset + 40 || x > offset + viewport - 60)
        {
            RollScroll.ChangeView(Math.Max(0, x - viewport / 2), null, null, true);
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
