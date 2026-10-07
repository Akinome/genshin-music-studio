using GenshinMusicStudio_WinUI.Pages;
using GenshinMusicStudio_WinUI.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace GenshinMusicStudio_WinUI;

public sealed partial class MainWindow : Window
{
    private bool logExpanded = false;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Resize(new SizeInt32(1280, 800));
        AppWindow.Move(new PointInt32(320, 140));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
        ApplyTheme(Application.Current.RequestedTheme == ApplicationTheme.Dark);
        UpdateLogToggleStyle();
        AppTitleBar.Loaded += (_, _) => UpdateTitleBarDragRegion();
        App.Backend.EventReceived += Backend_EventReceived;
        App.Backend.RunningChanged += Backend_RunningChanged;
        App.Player.ProgressChanged += Player_ProgressChanged;
        App.Player.PlayingChanged += Player_PlayingChanged;
        NavFrame.Navigate(typeof(DownloadPage));
    }

    private void Player_PlayingChanged(bool playing)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!playing && StatusText.Text.StartsWith("正在播放", StringComparison.Ordinal))
            {
                StatusText.Text = "就绪";
                RunProgress.Value = 0;
            }
        });
    }

    private void Player_ProgressChanged(string file, double position, double duration)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (string.IsNullOrWhiteSpace(file)) return;
            var name = Path.GetFileName(file);
            StatusText.Text = string.Format("正在播放: {0}  ({1:0}:{2:00} / {3:0}:{4:00})",
                name, (int)(position / 60), (int)position % 60, (int)(duration / 60), (int)duration % 60);
            RunProgress.Value = duration > 0 ? Math.Clamp(position / duration * 100, 0, 100) : 0;
        });
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(ThemeToggleButton.IsChecked == true);
        UpdateTitleBarDragRegion();
    }

    private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTitleBarDragRegion();

    private void ApplyTheme(bool dark)
    {
        RootGrid.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        ThemeToggleButton.IsChecked = dark;
        var titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = dark ? Color.FromArgb(255, 27, 27, 31) : Color.FromArgb(255, 245, 246, 248);
        titleBar.ForegroundColor = dark ? Colors.White : Color.FromArgb(255, 32, 32, 32);
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Color.FromArgb(255, 32, 32, 32);
        titleBar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(255, 160, 160, 160) : Color.FromArgb(255, 120, 120, 120);
        titleBar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(25, 0, 0, 0);
        titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        UpdateLogToggleStyle();
    }

    private void UpdateTitleBarDragRegion()
    {
        try
        {
            var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            var regions = new List<RectInt32>();
            foreach (var element in new FrameworkElement[] { ThemeToggleButton })
            {
                if (element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
                var bounds = element.TransformToVisual(AppTitleBar).TransformBounds(
                    new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                regions.Add(new RectInt32(
                    (int)Math.Floor(bounds.X),
                    (int)Math.Floor(bounds.Y),
                    (int)Math.Ceiling(bounds.Width),
                    (int)Math.Ceiling(bounds.Height)));
            }
            if (regions.Count > 0)
            {
                source.SetRegionRects(NonClientRegionKind.Passthrough, regions.ToArray());
            }
        }
        catch
        {
            // Custom title bar regions are best-effort; the app remains usable if unsupported.
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        switch (item.Tag)
        {
            case "download":
                NavFrame.Navigate(typeof(DownloadPage));
                break;
            case "local":
                NavFrame.Navigate(typeof(LocalPage));
                break;
            case "environment":
                NavFrame.Navigate(typeof(EnvironmentPage));
                break;
            case "library":
                NavFrame.Navigate(typeof(LibraryPage));
                break;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "正在停止...";
        AppendLog("请求停止：正在终止 Python 后端及所有子进程...");
        App.Backend.Cancel();
    }

    private void LogToggle_Click(object sender, RoutedEventArgs e)
    {
        logExpanded = !logExpanded;
        LogBox.Visibility = logExpanded ? Visibility.Visible : Visibility.Collapsed;
        BottomPanelRow.Height = new GridLength(logExpanded ? 300 : 118);
        UpdateLogToggleStyle();
    }

    private void UpdateLogToggleStyle()
    {
        if (LogToggleButton is null) return;
        LogToggleButton.Content = logExpanded ? "收起运行日志" : "运行日志";
    }

    private void Backend_RunningChanged(bool running)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StopButton.IsEnabled = running;
            if (running)
            {
                RunProgress.Value = 0;
            }
        });
    }

    private void Backend_EventReceived(StudioEvent evt)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (evt.Type == "error")
            {
                StatusText.Text = "失败";
                AppendLog("失败：" + (evt.Text ?? "未知错误"));
                return;
            }

            if (evt.Type == "done")
            {
                RunProgress.Value = 100;
                StatusText.Text = "完成";
                if (!string.IsNullOrWhiteSpace(evt.Output))
                {
                    AppendLog("完成：" + evt.Output);
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(evt.Text))
            {
                StatusText.Text = evt.Text;
                AppendLog(evt.Text);
            }
            if (evt.Value is not null)
            {
                RunProgress.Value = Math.Clamp(evt.Value.Value, 0, 100);
            }
        });
    }

    private void AppendLog(string text)
    {
        LogBox.Text += $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}";
        if (LogBox.Text.Length > 200_000)
        {
            LogBox.Text = LogBox.Text[^120_000..];
        }
        LogBox.SelectionStart = LogBox.Text.Length;
        LogBox.SelectionLength = 0;
        LogBox.Focus(FocusState.Programmatic);
    }
}
