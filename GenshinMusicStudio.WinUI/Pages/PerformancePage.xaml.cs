using GenshinMusicStudio_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;

namespace GenshinMusicStudio_WinUI.Pages;

public sealed partial class PerformancePage : Page
{
    private readonly KeyboardVisualization keyboard = new();
    private readonly HashSet<int> heldKeys = new();
    private DispatcherQueueTimer? focusTimer;

    public PerformancePage()
    {
        InitializeComponent();
        IsTabStop = true;
        KeyDown += PerformancePage_KeyDown;
        KeyUp += PerformancePage_KeyUp;
        focusTimer = DispatcherQueue.CreateTimer();
        focusTimer.Interval = TimeSpan.FromMilliseconds(150);
        focusTimer.IsRepeating = false;
        focusTimer.Tick += (_, _) => KeyboardViewport.Focus(FocusState.Programmatic);
        Loaded += PerformancePage_Loaded;
        Unloaded += PerformancePage_Unloaded;
        keyboard.KeyPressed += OnKeyPressed;
        keyboard.KeyReleased += OnKeyReleased;
    }

    private void PerformancePage_Loaded(object sender, RoutedEventArgs e)
    {
        AudioBox.SelectionChanged -= AudioBox_SelectionChanged;
        LoadAudioOptions();
        VolumeSlider.ValueChanged -= VolumeSlider_ValueChanged;
        VolumeSlider.Value = App.Player.Volume * 100;
        VolumeText.Text = (int)Math.Round(App.Player.Volume * 100) + "%";
        VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        KeyboardViewport.SizeChanged += (_, args) =>
        {
            KeyboardViewport.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, args.NewSize.Height),
            };
        };
        keyboard.Initialize(KeyboardViewport, Array.Empty<MidiPlayer.MidiNote>(), 0);
        KeyboardViewport.IsTabStop = true;
        KeyboardViewport.Tapped += (_, _) => KeyboardViewport.Focus(FocusState.Programmatic);
        KeyboardViewport.Focus(FocusState.Programmatic);
        focusTimer?.Start();
    }

    private void PerformancePage_Unloaded(object sender, RoutedEventArgs e)
    {
        focusTimer?.Stop();
        KeyDown -= PerformancePage_KeyDown;
        KeyUp -= PerformancePage_KeyUp;
    }

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
        AudioBox.Items.Clear();
        AudioBox.Items.Add(new ComboBoxItem { Content = "Windows 合成器", Tag = "" });
        var instrumentsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Instruments");
        if (Directory.Exists(instrumentsDir))
        {
            foreach (var folder in Directory.EnumerateDirectories(instrumentsDir)
                .OrderBy(p => InstrumentNames.TryGetValue(Path.GetFileName(p), out var mapped) ? mapped : Path.GetFileName(p), StringComparer.CurrentCulture))
            {
                var name = Path.GetFileName(folder);
                var display = InstrumentNames.TryGetValue(name, out var mapped) ? mapped : name;
                AudioBox.Items.Add(new ComboBoxItem { Content = display, Tag = folder });
            }
        }
        var saved = AppSettings.Load().AudioInstrument;
        AudioBox.SelectedIndex = 0;
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
        App.Player.Stop();
        if (folder.Length == 0)
        {
            App.Player.Instrument = null;
            return;
        }
        try
        {
            await Task.Run(() => App.Instruments.LoadInstrument(folder));
            App.Player.Instrument = App.Instruments;
            App.Instruments.SetDeviceVolume(App.Player.Volume);
            keyboard.Initialize(KeyboardViewport, Array.Empty<MidiPlayer.MidiNote>(), 0);
        }
        catch (Exception ex)
        {
            App.Player.Instrument = null;
            _ = ShowMessageAsync("乐器加载失败", ex.Message);
        }
        var settings = AppSettings.Load();
        settings.AudioInstrument = folder;
        AppSettings.Save(settings);
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var volume = e.NewValue / 100.0;
        App.Player.Volume = volume;
        App.Instruments.SetDeviceVolume(volume);
        VolumeText.Text = (int)Math.Round(volume * 100) + "%";
    }

    private void OnKeyPressed(int keyIndex)
    {
        if (heldKeys.Add(keyIndex))
        {
            var pitch = PitchForKeyIndex(keyIndex);
            LogDiag("key press: keyIndex=" + keyIndex + " pitch=" + pitch);
            App.Player.ManualNoteOn(pitch);
        }
    }

    private void OnKeyReleased(int keyIndex)
    {
        if (heldKeys.Remove(keyIndex))
        {
            App.Player.ManualNoteOff(PitchForKeyIndex(keyIndex));
        }
    }

    private int PitchForKeyIndex(int keyIndex)
    {
        return App.Instruments.Layout.KeyCount > 0
            ? App.Instruments.Layout.PitchForKeyIndex(keyIndex)
            : new InstrumentLayout().PitchForKeyIndex(keyIndex);
    }

    private void LogDiag(string text)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GenshinMusicStudio");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss}] DIAG {text}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void PerformancePage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (FocusManager.GetFocusedElement() is TextBox) return;
        var keyIndex = PhysicalKeyToIndex(e.Key);
        if (keyIndex < 0) return;
        if (e.KeyStatus.WasKeyDown || !heldKeys.Add(keyIndex)) return;
        keyboard.PressKeyVisual(keyIndex);
        App.Player.ManualNoteOn(PitchForKeyIndex(keyIndex));
        e.Handled = true;
    }

    private void PerformancePage_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        var keyIndex = PhysicalKeyToIndex(e.Key);
        if (keyIndex < 0 || !heldKeys.Remove(keyIndex)) return;
        keyboard.ReleaseKeyVisual(keyIndex);
        App.Player.ManualNoteOff(PitchForKeyIndex(keyIndex));
        e.Handled = true;
    }

    private static readonly string[] KeyboardRows = { "QWERTYU", "ASDFGHJ", "ZXCVBNM" };

    private static int PhysicalKeyToIndex(Windows.System.VirtualKey key)
    {
        var letter = key.ToString();
        if (letter.Length != 1) return -1;
        for (var row = 0; row < KeyboardRows.Length; row++)
        {
            var column = KeyboardRows[row].IndexOf(letter, StringComparison.OrdinalIgnoreCase);
            if (column >= 0) return row * 7 + column;
        }
        return -1;
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
