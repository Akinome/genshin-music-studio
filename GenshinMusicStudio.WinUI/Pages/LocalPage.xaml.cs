using GenshinMusicStudio_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GenshinMusicStudio_WinUI.Pages;

public sealed partial class LocalPage : Page
{
    private bool outputManuallySelected;
    private Dictionary<string, string?>? modelStatus;

    public LocalPage()
    {
        InitializeComponent();
        OutputBox.Text = Path.Combine(StudioBackendClient.RepoRoot, "本地处理输出");
        Loaded += LocalPage_Loaded;
        Unloaded += LocalPage_Unloaded;
    }

    private async void LocalPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.Backend.RunningChanged += SetRunning;
        await RefreshModelStatusAsync();
    }
    private void LocalPage_Unloaded(object sender, RoutedEventArgs e) => App.Backend.RunningChanged -= SetRunning;

    private void SetRunning(bool running)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            FullButton.IsEnabled = !running;
            TranscribeButton.IsEnabled = !running;
            MidiButton.IsEnabled = !running;
        });
    }

    private async void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFileAsync(".mid", ".midi", ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".mp4", ".mkv", ".webm");
        if (path is not null)
        {
            InputBox.Text = path;
            if (!outputManuallySelected)
            {
                var inputDirectory = Path.GetDirectoryName(path) ?? StudioBackendClient.RepoRoot;
                OutputBox.Text = Path.Combine(inputDirectory, "原神转换输出");
            }
        }
    }

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFolderAsync();
        if (path is not null)
        {
            OutputBox.Text = path;
            outputManuallySelected = true;
        }
    }

    private async void Full_Click(object sender, RoutedEventArgs e) => await RunAsync("local_full");
    private async void Transcribe_Click(object sender, RoutedEventArgs e) => await RunAsync("transcribe");
    private async void Midi_Click(object sender, RoutedEventArgs e) => await RunAsync("convert_midi");

    private async void ModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateModelStatusHint();

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

    private async Task RunAsync(string action)
    {
        if (string.IsNullOrWhiteSpace(InputBox.Text) || !File.Exists(InputBox.Text))
        {
            await ShowMessageAsync("缺少文件", "请选择有效的音频、视频或 MIDI 文件。");
            return;
        }

        var request = new BackendRequest
        {
            Action = action,
            Path = InputBox.Text.Trim(),
            OutDir = OutputBox.Text.Trim(),
            MelodyOnly = MelodyBox.IsChecked == true,
            PreserveDuration = PreserveBox.IsChecked == true,
            MinGapMs = double.IsNaN(MinGapBox.Value) ? 60 : (int)MinGapBox.Value,
            Model = (ModelBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "basic_pitch_onnx",
        };
        try
        {
            await App.Backend.RunAsync(request);
            await RefreshModelStatusAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("任务失败", ex.Message);
        }
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
