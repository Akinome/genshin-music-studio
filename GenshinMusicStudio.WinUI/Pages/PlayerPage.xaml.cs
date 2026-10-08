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
    private DispatcherQueueTimer? renderTimer;
    private IMidiVisualization? visualization;
    private readonly WaterfallVisualization waterfallMode = new();
    private readonly KeyboardVisualization keyboardMode = new();

    public PlayerPage()
    {
        InitializeComponent();
        renderTimer = DispatcherQueue.CreateTimer();
        renderTimer.Interval = TimeSpan.FromMilliseconds(16);
        renderTimer.Tick += (_, _) => OnFrame();
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
        LoadAudioOptions();
        VolumeSlider.ValueChanged -= VolumeSlider_ValueChanged;
        VolumeSlider.Value = App.Player.Volume * 100;
        VolumeText.Text = (int)Math.Round(App.Player.Volume * 100) + "%";
        VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        BoostSlider.ValueChanged -= BoostSlider_ValueChanged;
        BoostSlider.Minimum = 100;
        BoostSlider.Value = App.Player.VelocityBoost * 100;
        BoostText.Text = (int)Math.Round(App.Player.VelocityBoost * 100) + "%";
        BoostSlider.ValueChanged += BoostSlider_ValueChanged;
        if (FilePathBox.Text.Length > 0)
        {
            RenderRoll(FilePathBox.Text);
        }
        SetMode(waterfallMode);
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
        App.Player.Play(new[] { path });
        if (visualization is null) SetMode(waterfallMode);
        else visualization.Reset();
        renderTimer?.Start();
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => App.Player.Stop();

    private static readonly Dictionary<string, string> InstrumentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Lyre"] = "风物之诗琴",
        ["DunDun"] = "豪鼓",
        ["Zither"] = "镜花之琴",
        ["Old-Zither"] = "镜花之琴(旧版)",
        ["DjemDjemDrum"] = "聚聚鼓",
        ["Vintage-Lyre"] = "老旧的诗琴",
        ["NightwindHorn"] = "晚风圆号",
        ["Vodyanitsa"] = "沃雅妮莎",
        ["HarmonicKey"] = "谐律键琴",
        ["Ukulele"] = "悠可琴",
        ["LingeringEuphonia"] = "余音",
        ["LeapingSpiritPiano"] = "跃律琴",
    };

    private void LoadAudioOptions()
    {
        AudioBox.SelectionChanged -= AudioBox_SelectionChanged;
        AudioBox.Items.Clear();
        AudioBox.Items.Add(new ComboBoxItem { Content = "Windows 合成器", Tag = "" });
        var instrumentsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Instruments");
        if (Directory.Exists(instrumentsDir))
        {
            foreach (var folder in Directory.EnumerateDirectories(instrumentsDir)
                .OrderBy(p => InstrumentNames.TryGetValue(System.IO.Path.GetFileName(p), out var mapped) ? mapped : System.IO.Path.GetFileName(p), StringComparer.CurrentCulture))
            {
                var name = System.IO.Path.GetFileName(folder);
                var display = InstrumentNames.TryGetValue(name, out var mapped) ? mapped : name;
                AudioBox.Items.Add(new ComboBoxItem { Content = display, Tag = folder });
            }
        }
        AudioBox.SelectedIndex = 0;
        var saved = AppSettings.Load().AudioInstrument;
        if (!string.IsNullOrEmpty(saved))
        {
            foreach (var item in AudioBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), saved, StringComparison.OrdinalIgnoreCase))
                {
                    AudioBox.SelectedItem = item;
                    break;
                }
            }
        }
        AudioBox.SelectionChanged += AudioBox_SelectionChanged;
    }

    private async void AudioBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AudioBox.SelectedItem is not ComboBoxItem item) return;
        var folder = item.Tag?.ToString() ?? string.Empty;
        if (folder.Length == 0)
        {
            App.Player.Instrument = null;
            return;
        }
        PlayButton.IsEnabled = false;
        try
        {
            await Task.Run(() => App.Instruments.LoadInstrument(folder));
            App.Player.Instrument = App.Instruments;
            App.Instruments.SetDeviceVolume(App.Player.Volume);
            if (App.Instruments.Layout.IsDrumKit)
            {
                SetMode(keyboardMode);
            }
        }
        catch (Exception ex)
        {
            App.Player.Instrument = null;
            _ = ShowMessageAsync("乐器加载失败", ex.Message);
        }
        finally
        {
            PlayButton.IsEnabled = true;
        }
        var settings = AppSettings.Load();
        settings.AudioInstrument = folder;
        AppSettings.Save(settings);
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var volume = e.NewValue / 100.0;
        App.Player.Volume = volume;
        App.Instruments.SetDeviceVolume(volume);
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
            if (!playing && !App.Player.IsPlaying)
            {
                renderTimer?.Stop();
                visualization?.Reset();
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
        var position = App.Player.CurrentPosition;
        if (!App.Player.IsPlaying) return;
        visualization?.Update(position);
        if (position - lastUiUpdate >= 0.25)
        {
            lastUiUpdate = position;
            DispatcherQueue.TryEnqueue(() =>
            {
                TimeText.Text = FormatTime(position) + " / " + FormatTime(duration);
                PlayProgress.Value = duration > 0 ? Math.Clamp(position / duration * 100, 0, 100) : 0;
            });
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
        if (visualization is null) SetMode(waterfallMode);
        else visualization.Initialize(RollViewport, notes, duration);
        TimeText.Text = "0:00 / " + FormatTime(duration);
    }

    private void SetMode(IMidiVisualization mode)
    {
        visualization = mode;
        WaterfallModeButton.IsChecked = mode == waterfallMode;
        KeyboardModeButton.IsChecked = mode == keyboardMode;
        if (notes.Count == 0)
        {
            RollViewport.Children.Clear();
            return;
        }
        mode.Initialize(RollViewport, notes, duration);
    }

    private void WaterfallMode_Click(object sender, RoutedEventArgs e) => SetMode(waterfallMode);

    private void KeyboardMode_Click(object sender, RoutedEventArgs e) => SetMode(keyboardMode);

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
