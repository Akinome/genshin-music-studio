using GenshinMusicStudio_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GenshinMusicStudio_WinUI.Pages;

public sealed partial class DownloadPage : Page
{
    private Dictionary<string, string?>? modelStatus;
    private List<BilibiliVideo> searchResults = new();

    public DownloadPage()
    {
        InitializeComponent();
        WorkBox.Text = Path.Combine(StudioBackendClient.RepoRoot, "工作区");
        OutputBox.Text = Path.Combine(StudioBackendClient.RepoRoot, "AI扒谱输出");
        Loaded += DownloadPage_Loaded;
        Unloaded += DownloadPage_Unloaded;
    }

    private async void DownloadPage_Loaded(object sender, RoutedEventArgs e)
    {
        AppendYuanQinBox.IsChecked = AppSettings.Load().SearchDefaultYuanQin ?? true;
        App.Backend.RunningChanged += SetRunning;
        await RefreshModelStatusAsync();
    }
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

    private async void ModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateModelStatusHint();

    private async void Search_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private void AppendYuanQin_Changed(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Load();
        settings.SearchDefaultYuanQin = AppendYuanQinBox.IsChecked == true;
        AppSettings.Save(settings);
    }

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        var raw = SearchBox.Text.Trim();
        if (raw.Length == 0) return;

        // 直接输入 BV 号或链接时不必搜索，填进下方即可。
        var direct = BilibiliSearch.TryNormalizeUrl(raw);
        if (direct is not null)
        {
            UrlBox.Text = direct;
            SearchStatusText.Text = "已识别为视频链接，可直接用下方按钮处理。";
            return;
        }

        var keyword = BilibiliSearch.BuildKeyword(raw, AppendYuanQinBox.IsChecked == true);
        SearchButton.IsEnabled = false;
        SearchStatusText.Text = $"正在搜索「{keyword}」…";
        try
        {
            searchResults = await BilibiliSearch.SearchAsync(keyword, 1, CookiesBox.Text.Trim());
            var hasResults = searchResults.Count > 0;
            SearchResultsView.ItemsSource = searchResults;
            SearchResultsView.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
            SearchActions.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
            if (hasResults) SearchResultsView.SelectedIndex = 0;
            SearchStatusText.Text = hasResults
                ? $"按「{keyword}」找到 {searchResults.Count} 个视频，选中后点「扒谱选中视频」。"
                : $"没有找到「{keyword}」相关视频。";
            _ = LoadCoversAsync(searchResults);
        }
        catch (Exception ex)
        {
            SearchResultsView.Visibility = Visibility.Collapsed;
            SearchActions.Visibility = Visibility.Collapsed;
            SearchStatusText.Text = "搜索失败：" + ex.Message;
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private static async Task LoadCoversAsync(List<BilibiliVideo> videos)
    {
        using var gate = new SemaphoreSlim(4);
        var tasks = videos.Where(v => !string.IsNullOrWhiteSpace(v.Pic)).Select(async video =>
        {
            await gate.WaitAsync();
            try
            {
                video.Cover = await BilibiliSearch.LoadCoverAsync(video.Pic);
            }
            catch
            {
                // 单张封面失败不影响搜索结果本身。
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);
    }

    private BilibiliVideo? SelectedSearchResult => SearchResultsView.SelectedItem as BilibiliVideo;

    private void UseSearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSearchResult is not { } video) return;
        UrlBox.Text = video.Url;
        SearchStatusText.Text = "已填入链接：" + video.Title;
    }

    private void SearchResultsView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => UseSearchResult_Click(sender, e);

    private async void TranscribeSearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSearchResult is not { } video) return;
        UrlBox.Text = video.Url;
        await RunAsync("full_pipeline", video.SuggestedName);
    }

    private async Task RefreshModelStatusAsync()
    {
        if (App.Backend.IsRunning) return;
        try
        {
            modelStatus = await App.Backend.RunAsync(new BackendRequest { Action = "status" });
        }
        catch (Exception)
        {
            modelStatus = null;
        }
        UpdateModelStatusHint();
    }

    private void UpdateModelStatusHint()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var model = (ModelBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var (installed, text) = ModelInstallStatus.Evaluate(model, modelStatus);
            ModelStatusText.Text = text;
            ModelStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                installed ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush"];
        });
    }

    private async Task RunAsync(string action, string? fileNameHint = null)
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
            FileNameHint = fileNameHint,
        };

        try
        {
            await App.Backend.RunAsync(request);
            await RefreshModelStatusAsync();
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
