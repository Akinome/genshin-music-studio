using GenshinMusicStudio_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GenshinMusicStudio_WinUI.Pages;

public sealed partial class DownloadPage : Page
{
    public DownloadPage()
    {
        InitializeComponent();
        WorkBox.Text = Path.Combine(StudioBackendClient.RepoRoot, "工作区");
        OutputBox.Text = Path.Combine(StudioBackendClient.RepoRoot, "AI扒谱输出");
        Loaded += DownloadPage_Loaded;
        Unloaded += DownloadPage_Unloaded;
    }

    private void DownloadPage_Loaded(object sender, RoutedEventArgs e) => App.Backend.RunningChanged += SetRunning;
    private void DownloadPage_Unloaded(object sender, RoutedEventArgs e) => App.Backend.RunningChanged -= SetRunning;

    private void SetRunning(bool running)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            DownloadAudioButton.IsEnabled = !running;
            DownloadVideoButton.IsEnabled = !running;
            TranscribeOnlyButton.IsEnabled = !running;
            FullPipelineButton.IsEnabled = !running;
        });
    }

    private async void BrowseWork_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFolderAsync();
        if (path is not null) WorkBox.Text = path;
    }

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFolderAsync();
        if (path is not null) OutputBox.Text = path;
    }

    private async void BrowseCookies_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFileAsync(".txt");
        if (path is not null) CookiesBox.Text = path;
    }

    private void OpenWork_Click(object sender, RoutedEventArgs e) => OpenFolder(WorkBox.Text);
    private void OpenOutput_Click(object sender, RoutedEventArgs e) => OpenFolder(OutputBox.Text);

    private async void DownloadAudio_Click(object sender, RoutedEventArgs e) => await RunAsync("download_audio");
    private async void DownloadVideo_Click(object sender, RoutedEventArgs e) => await RunAsync("download_video");
    private async void TranscribeOnly_Click(object sender, RoutedEventArgs e) => await RunAsync("transcribe_url");
    private async void FullPipeline_Click(object sender, RoutedEventArgs e) => await RunAsync("full_pipeline");

    private async Task RunAsync(string action)
    {
        var urls = UrlBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (urls.Count == 0)
        {
            await ShowMessageAsync("缺少链接", "请至少输入一个视频链接。");
            return;
        }

        var request = new BackendRequest
        {
            Action = action,
            Urls = urls,
            WorkDir = WorkBox.Text.Trim(),
            OutDir = OutputBox.Text.Trim(),
            CookiesFile = CookiesBox.Text.Trim(),
            Browser = (BrowserBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "不使用",
            AllowPlaylist = PlaylistBox.IsChecked == true,
            MelodyOnly = MelodyBox.IsChecked == true,
            PreserveDuration = PreserveBox.IsChecked == true,
            MinGapMs = double.IsNaN(MinGapBox.Value) ? 60 : (int)MinGapBox.Value,
            Model = (ModelBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "basic_pitch_onnx",
        };

        try
        {
            await App.Backend.RunAsync(request);
        }
        catch (OperationCanceledException)
        {
            // Stop is already reflected in the shared status bar.
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("任务失败", ex.Message);
        }
    }

    private static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
