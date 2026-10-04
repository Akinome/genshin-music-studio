using GenshinMusicStudio_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GenshinMusicStudio_WinUI.Pages;

public sealed partial class EnvironmentPage : Page
{
    private static readonly string[] ToolKeys = { "yt-dlp", "ffmpeg", "uv", "AI环境", "soundfile" };

    public EnvironmentPage()
    {
        InitializeComponent();
        Loaded += EnvironmentPage_Loaded;
        Unloaded += EnvironmentPage_Unloaded;
    }

    private async void EnvironmentPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.Backend.RunningChanged += SetRunning;
        await RefreshDependenciesAsync();
    }

    private void EnvironmentPage_Unloaded(object sender, RoutedEventArgs e) => App.Backend.RunningChanged -= SetRunning;

    private void SetRunning(bool running)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            RefreshButton.IsEnabled = !running;
            InstallButton.IsEnabled = !running;
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDependenciesAsync();

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.Backend.RunAsync(new BackendRequest { Action = "install_ai" });
            await RefreshDependenciesAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("安装失败", ex.Message);
        }
    }

    private async Task RefreshDependenciesAsync()
    {
        if (App.Backend.IsRunning) return;
        try
        {
            var result = await App.Backend.RunAsync(new BackendRequest { Action = "status" });
            RenderDependencies(result);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("检测失败", ex.Message);
        }
    }

    private void RenderDependencies(Dictionary<string, string?>? values)
    {
        DependencyPanel.Children.Clear();
        ModelPanel.Children.Clear();
        if (values is null) return;
        foreach (var pair in values.Where(p => ToolKeys.Contains(p.Key)))
        {
            DependencyPanel.Children.Add(CreateStatusRow(pair.Key, pair.Value));
        }
        foreach (var pair in values.Where(p => !ToolKeys.Contains(p.Key)))
        {
            ModelPanel.Children.Add(CreateStatusRow(pair.Key, pair.Value));
        }
    }

    private static Grid CreateStatusRow(string key, string? value)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = key, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var installed = !string.IsNullOrWhiteSpace(value);
        var state = new TextBlock
        {
            Text = installed ? "已安装" : "未安装",
            Foreground = (Brush)Application.Current.Resources[installed ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush"],
        };
        Grid.SetColumn(state, 1);
        row.Children.Add(state);
        var path = new TextBlock { Text = value ?? "未找到", TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        Grid.SetColumn(path, 2);
        row.Children.Add(path);
        return row;
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
