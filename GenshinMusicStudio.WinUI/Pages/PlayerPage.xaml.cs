using GenshinMusicStudio_WinUI.Services;
using GenshinMusicStudio.Core;
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
    private double lastKeyAnimation;
    private bool gameWarningShown;
    private DispatcherQueueTimer? renderTimer;
    private DispatcherQueueTimer? keyTimer;
    private IMidiVisualization? visualization;
    private readonly WaterfallVisualization waterfallMode = new();
    private readonly KeyboardVisualization keyboardMode = new();
    private readonly PlaybackProgressState progressState = new();
    private string? renderedSheetFile;
    private double renderedSheetOffset = -1;

    public PlayerPage()
    {
        InitializeComponent();
        renderTimer = DispatcherQueue.CreateTimer();
        renderTimer.Interval = TimeSpan.FromMilliseconds(16);
        renderTimer.Tick += (_, _) => OnFrame();
        keyTimer = DispatcherQueue.CreateTimer();
        keyTimer.Interval = TimeSpan.FromMilliseconds(33);
        keyTimer.Tick += (_, _) => OnKeyFrame();
        RollViewport.SizeChanged += (_, args) =>
        {
            RollViewport.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height),
            };
            visualization?.Resize();
        };
        Loaded += PlayerPage_Loaded;
        Unloaded += PlayerPage_Unloaded;
    }

    private void PlayerPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.Player.PlayingChanged += Player_PlayingChanged;
        App.Player.ProgressChanged += Player_ProgressChanged;
        App.InputGuard.StatusChanged += InputGuard_StatusChanged;
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
        var settings = AppSettings.Load();
        App.GameKeys.BackgroundMode = settings.PlayInGameBackground ?? false;
        BackgroundModeCheck.IsChecked = App.GameKeys.BackgroundMode;
        InGameButton.IsChecked = settings.PlayInGame ?? false;
        GuardCheck.IsChecked = settings.PlayInGameGuard ?? false;
        ApplyGameOutput();
        UpdateGameWindowStatus();
        var pageContext = PlaybackSheetResolver.ResolvePageContext(
            App.Player.PlayingFolder,
            App.Player.CurrentFile,
            App.Player.CurrentFileStart,
            App.Player.CurrentDuration);
        if (pageContext is not null && File.Exists(pageContext.FilePath))
        {
            FilePathBox.Text = pageContext.FilePath;
            RenderRoll(
                pageContext.FilePath,
                pageContext.TimeOffset,
                pageContext.TimelineDuration,
                preservePlayback: true);
        }
        else if (FilePathBox.Text.Length > 0)
        {
            RenderRoll(FilePathBox.Text);
        }
        SetMode(waterfallMode);
        if (App.Player.IsPlaying)
        {
            StartTimers();
        }
        UpdateControls();
    }

    private void PlayerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        StopTimers();
        App.GameKeys.ReleaseAll();
        App.InputGuard.Stop();
        App.InputGuard.StatusChanged -= InputGuard_StatusChanged;
        App.Player.PlayingChanged -= Player_PlayingChanged;
        App.Player.ProgressChanged -= Player_ProgressChanged;
    }

    private void InputGuard_StatusChanged(string status)
    {
        DispatcherQueue.TryEnqueue(() => GuardText.Text = status);
    }

    /// <summary>Pushes the current in-game playback selection to the player.</summary>
    private void ApplyGameOutput()
    {
        App.GameKeys.BackgroundMode = BackgroundModeCheck.IsChecked == true;
        App.Player.PlayInGame = InGameButton.IsChecked == true;
        App.Player.GameOutput = App.GameKeys;
        App.InputGuard.Enabled = GuardCheck.IsChecked == true;
        gameWarningShown = false;
        App.GameKeys.ClearFailure();
        // 守卫只在原神内弹奏时有意义；已经暂停或停止时启动它也不会有副作用。
        if (App.Player.PlayInGame && App.InputGuard.Enabled && App.Player.IsPlaying)
        {
            App.InputGuard.Start();
        }
        else
        {
            App.InputGuard.Stop();
        }

        UpdateGuardStatus();
    }

    private void GuardCheck_Changed(object sender, RoutedEventArgs e)
    {
        ApplyGameOutput();
        SaveGameSettings();
    }

    /// <summary>刷新守卫相关的界面状态与说明文字。</summary>
    private void UpdateGuardStatus()
    {
        if (InGameButton.IsChecked != true)
        {
            GuardText.Text = "仅原神内弹奏时可用";
            return;
        }

        if (!App.InputGuard.HasSignature)
        {
            GuardText.Text = "播放时自动取样：请先打开乐器界面";
            return;
        }

        GuardText.Text = App.InputGuard.Enabled
            ? $"已启用（{App.InputGuard.SampleCount} 个取样点，退出乐器界面会自动暂停）"
            : "已关闭";
    }

    private void InGameButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyGameOutput();
        UpdateGameWindowStatus();
        SaveGameSettings();
    }

    private void BackgroundMode_Changed(object sender, RoutedEventArgs e)
    {
        ApplyGameOutput();
        UpdateGameWindowStatus();
        SaveGameSettings();
    }

    private void DetectGame_Click(object sender, RoutedEventArgs e) => UpdateGameWindowStatus();

    private void SaveGameSettings()
    {
        var settings = AppSettings.Load();
        settings.PlayInGame = InGameButton.IsChecked == true;
        settings.PlayInGameBackground = BackgroundModeCheck.IsChecked == true;
        settings.PlayInGameGuard = GuardCheck.IsChecked == true;
        AppSettings.Save(settings);
    }

    private void UpdateGameWindowStatus()
    {
        var elevated = GameKeySender.IsProcessElevated();
        ElevateButton.Visibility = elevated ? Visibility.Collapsed : Visibility.Visible;
        if (App.GameKeys.RefreshWindow())
        {
            GameWindowText.Text = elevated
                ? "已连接：" + App.GameKeys.GameWindowTitle
                : "已连接，但需要管理员权限";
            return;
        }
        GameWindowText.Text = InGameButton.IsChecked == true ? "未检测到原神窗口" : string.Empty;
    }

    /// <summary>Relaunches elevated so input can reach the elevated game window.</summary>
    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync("无法提权", ex.Message);
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        string? path;
        try
        {
            path = await PickerHelper.PickFileAsync(".mid", ".midi");
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync("无法打开文件选择器", ex.Message);
            return;
        }
        if (path is null) return;
        FilePathBox.Text = path;
        RenderRoll(path);
    }

    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        var path = FilePathBox.Text.Trim();
        if (path.Length == 0)
        {
            _ = ShowMessageAsync("没有文件", "请先选择一个 MIDI 文件。");
            return;
        }
        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            _ = ShowMessageAsync("目录不存在", "文件所在目录在磁盘上不存在。");
            return;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        });
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (App.Player.IsPlaying)
        {
            App.Player.Pause();
            return;
        }
        if (App.Player.IsPaused)
        {
            App.Player.Resume();
            return;
        }
        var path = FilePathBox.Text.Trim();
        if (path.Length == 0 || !File.Exists(path))
        {
            _ = ShowMessageAsync("缺少文件", "请选择有效的 MIDI 文件。");
            return;
        }
        if (notes.Count == 0)
        {
            RenderRoll(path);
        }
        if (App.Player.PlayInGame)
        {
            var startResult = GamePlaybackPrerequisites.Check(
                App.GameKeys,
                App.Player.PlayInGame,
                App.GameKeys.BackgroundMode);
            if (startResult == GamePlaybackStartResult.RequiresAdministrator)
            {
                _ = ShowMessageAsync(
                    "需要管理员权限",
                    "原神正在以管理员身份运行，本程序需要同等权限才能把按键送进游戏。请点击工具条上的「以管理员身份重启」，重启后再播放。");
                return;
            }
            if (startResult == GamePlaybackStartResult.GameWindowNotFound)
            {
                _ = ShowMessageAsync("未找到游戏窗口", "没有检测到原神窗口，无法进行前台弹奏。可改用后台模式，或用「检测游戏窗口」重试。");
                return;
            }
        }
        App.Player.Play(new[] { path });
        if (visualization is null) SetMode(waterfallMode);
        else visualization.Reset();
        StartTimers();
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
        var instrumentsRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Instruments");
        var selectedFolder = AudioInstrumentDefaults.Resolve(
            AppSettings.Load().AudioInstrument,
            instrumentsRoot);
        AudioBox.SelectedIndex = 0;
        if (selectedFolder.Length > 0)
        {
            foreach (var item in AudioBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), selectedFolder, StringComparison.OrdinalIgnoreCase))
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
        if (SelectionMatchesPlayback(folder)) return;
        var continuation = PlaybackContinuation.Capture(
            App.Player.IsPlaying,
            App.Player.IsPaused);
        if (continuation.ShouldPause)
        {
            // Keep the native pause state and playback event index intact.
            // Replaying from a time offset resets the event stream and makes
            // the waterfall timeline jump after an instrument change.
            App.Player.Pause();
        }
        var sequence = App.Instruments.NextLoadSequence();
        if (folder.Length == 0)
        {
            App.Player.SetInstrument(null, sequence);
            ResumeAfterInstrumentChange(continuation);
            return;
        }
        PlayButton.IsEnabled = false;
        try
        {
            var applied = await Task.Run(() => App.Instruments.LoadInstrument(folder, sequence));
            if (!applied) return;
            App.Player.SetInstrument(App.Instruments, sequence);
            App.Instruments.SetDeviceVolume(App.Player.Volume);
            if (!IsLoaded) return;
            if (App.Instruments.Layout.IsDrumKit)
            {
                SetMode(keyboardMode);
            }
            else if (visualization == keyboardMode)
            {
                visualization.Initialize(RollViewport, notes, duration);
            }
            KeyboardModeButton.Content = keyboardMode.Name;
            ResumeAfterInstrumentChange(continuation);
        }
        catch (Exception ex)
        {
            App.Player.SetInstrument(null, sequence);
            _ = ShowMessageAsync("乐器加载失败", ex.Message);
        }
        finally
        {
            if (IsLoaded)
            {
                PlayButton.IsEnabled = true;
            }
        }
        var settings = AppSettings.Load();
        settings.AudioInstrument = folder;
        AppSettings.Save(settings);
    }

    private bool SelectionMatchesPlayback(string folder)
    {
        if (!App.Player.HasSelection) return false;
        if (folder.Length == 0) return App.Player.Instrument is null;
        return App.Player.Instrument is not null
            && string.Equals(
                Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)),
                App.Instruments.CurrentInstrument,
                StringComparison.OrdinalIgnoreCase);
    }

    private void ResumeAfterInstrumentChange(PlaybackContinuation continuation)
    {
        if (!continuation.ShouldResume || !App.Player.IsPaused) return;
        App.Player.Resume();
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
            if (playing)
            {
                StartTimers();
                // 守卫跟着演奏跑：暂停、停止或切到本地播放时立即停下的检测。
                if (App.Player.PlayInGame && App.InputGuard.Enabled)
                {
                    App.InputGuard.Start();
                }
            }
            else if (App.Player.IsPaused)
            {
                StopTimers();
                App.InputGuard.Stop();
            }
            else if (!App.Player.IsPlaying)
            {
                StopTimers();
                App.InputGuard.Stop();
                visualization?.Reset();
            }
        });
    }

    private void Player_ProgressChanged(string file, double position, double total)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            progressState.UpdateFromPlayer(total);
            var directoryContext = PlaybackSheetResolver.ResolveDirectoryContext(
                App.Player.PlayingFolder,
                App.Player.CurrentFile,
                App.Player.CurrentFileStart,
                total);
            if (directoryContext is not null
                && File.Exists(directoryContext.FilePath)
                && (!string.Equals(
                        renderedSheetFile,
                        directoryContext.FilePath,
                        StringComparison.OrdinalIgnoreCase)
                    || Math.Abs(renderedSheetOffset - directoryContext.TimeOffset) > 0.001))
            {
                FilePathBox.Text = directoryContext.FilePath;
                RenderRoll(
                    directoryContext.FilePath,
                    directoryContext.TimeOffset,
                    directoryContext.TimelineDuration,
                    preservePlayback: true);
            }
            UpdatePlaybackProgress(position, total);
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
                var total = progressState.ResolveFrameDuration(
                    App.Player.CurrentDuration,
                    duration);
                UpdatePlaybackProgress(position, total);
            });
        }
    }

    private void UpdatePlaybackProgress(double position, double total)
    {
        var displayDuration = total > 0
            ? total
            : progressState.ResolveFrameDuration(App.Player.CurrentDuration, duration);
        TimeText.Text = FormatTime(position) + " / " + FormatTime(displayDuration);
        PlayProgress.Value = progressState.CalculatePercent(position, displayDuration);
    }

    private void OnKeyFrame()
    {
        if (!App.Player.IsPlaying) return;
        if (!gameWarningShown && App.Player.PlayInGame && App.GameKeys.LastFailure is { } failure)
        {
            gameWarningShown = true;
            App.GameKeys.ClearFailure();
            _ = ShowMessageAsync("无法在原神中弹奏", failure);
        }
        var position = App.Player.CurrentPosition;
        if (position - lastKeyAnimation < 0.033) return;
        lastKeyAnimation = position;
        visualization?.UpdateKeyAnimation(position);
    }

    private void StartTimers()
    {
        // Playback may restart from 0, so the throttle markers must not keep the
        // previous sheet's timeline or every update would be skipped.
        lastKeyAnimation = -1;
        lastUiUpdate = -1;
        renderTimer?.Start();
        keyTimer?.Start();
    }

    private void StopTimers()
    {
        renderTimer?.Stop();
        keyTimer?.Stop();
    }

    private void UpdateControls()
    {
        var playing = App.Player.IsPlaying;
        var paused = App.Player.IsPaused;
        PlayIcon.Glyph = playing ? "\uE769" : "\uE768";
        PlayText.Text = playing ? "暂停" : paused ? "继续" : "播放";
        StopButton.IsEnabled = playing || paused;
    }

    private void RenderRoll(
        string path,
        double timeOffset = 0,
        double timelineDuration = 0,
        bool preservePlayback = false)
    {
        // Loading a different sheet must not leave the previous one playing:
        // the old thread would keep reporting progress against the new timeline.
        if (!preservePlayback
            && !string.Equals(App.Player.CurrentFile, path, StringComparison.OrdinalIgnoreCase))
        {
            App.Player.Stop();
            PlayProgress.Value = 0;
        }
        try
        {
            var (parsedNotes, fileDuration) = MidiFileParser.ParseNotes(path);
            var timelineNotes = PlaybackSheetResolver.OffsetNotes(parsedNotes, timeOffset);
            notes = timelineNotes
                .Select(note => new MidiPlayer.MidiNote(
                    note.Start,
                    note.End,
                    note.Pitch,
                    note.Velocity))
                .ToList();
            duration = timelineDuration > 0 ? timelineDuration : fileDuration;
            renderedSheetFile = path;
            renderedSheetOffset = Math.Max(0, timeOffset);
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync("无法读取 MIDI", ex.Message);
            return;
        }
        if (notes.Count == 0)
        {
            PlayabilityText.Text = "0.0%";
            PlayabilityDetailText.Text = "没有可统计音符";
            _ = ShowMessageAsync("没有音符", "该 MIDI 文件中没有识别到音符。");
            return;
        }
        // 选中文件后立即显示，不依赖播放或谱面渲染是否成功。
        var playability = MidiPlayer.CalculatePlayability(notes);
        PlayabilityText.Text = playability.RatioText;
        PlayabilityDetailText.Text = playability.DetailText;
        if (visualization is null) SetMode(waterfallMode);
        else visualization.Initialize(RollViewport, notes, duration);
        LogState("RenderRoll: " + Path.GetFileName(path));
        if (preservePlayback && App.Player.IsPlaying)
        {
            visualization?.Update(App.Player.CurrentPosition);
            UpdatePlaybackProgress(App.Player.CurrentPosition, duration);
        }
        else
        {
            TimeText.Text = "0:00 / " + FormatTime(duration);
        }
    }

    private void SetMode(IMidiVisualization mode)
    {
        visualization = mode;
        WaterfallModeButton.IsChecked = mode == waterfallMode;
        KeyboardModeButton.IsChecked = mode == keyboardMode;
        KeyboardModeButton.Content = keyboardMode.Name;
        mode.Initialize(RollViewport, notes, duration);
        if (notes.Count > 0)
        {
            LogState("SetMode: " + mode.Name);
        }
    }

    private void LogState(string label)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GenshinMusicStudio");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss}] STATE {label}: notes={notes.Count} duration={duration:F2}s viewport={RollViewport.ActualWidth:F0}x{RollViewport.ActualHeight:F0} children={RollViewport.Children.Count}{Environment.NewLine}");
        }
        catch
        {
        }
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
