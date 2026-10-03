using GenshinMusicStudio_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GenshinMusicStudio_WinUI.Pages;

public sealed partial class LibraryPage : Page
{
    public LibraryPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RenderLibrary();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RenderLibrary();
    private void OpenRecommended_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(StudioBackendClient.RepoRoot, "优化完成_原神可用"));

    private async void BrowseReference_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFileAsync(".mid", ".midi");
        if (path is not null) ReferenceBox.Text = path;
    }

    private async void BrowsePrediction_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFileAsync(".mid", ".midi");
        if (path is not null) PredictionBox.Text = path;
    }

    private async void Evaluate_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(ReferenceBox.Text) || !File.Exists(PredictionBox.Text))
        {
            await ShowMessageAsync("缺少文件", "请选择有效的参考 MIDI 和生成 MIDI。");
            return;
        }
        EvaluateButton.IsEnabled = false;
        try
        {
            var result = await App.Backend.RunAsync(new BackendRequest
            {
                Action = "evaluate",
                ReferencePath = ReferenceBox.Text.Trim(),
                PredictionPath = PredictionBox.Text.Trim(),
            });
            var message = result is null
                ? "没有返回评估结果"
                : string.Join(Environment.NewLine, result.Select(pair => $"{pair.Key}: {pair.Value}"));
            await ShowMessageAsync("模型评估结果", message);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("评估失败", ex.Message);
        }
        finally
        {
            EvaluateButton.IsEnabled = true;
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

    private void RenderLibrary()
    {
        LibraryPanel.Children.Clear();
        var root = StudioBackendClient.RepoRoot;
        var playableSource = Environment.GetEnvironmentVariable("GENSHIN_PLAYABLE_DIR")
            ?? Path.Combine(root, "示例谱库", "成熟的原琴");
        var backupSource = Environment.GetEnvironmentVariable("GENSHIN_BACKUP_DIR")
            ?? Path.Combine(root, "示例谱库", "不可播备份");
        var folders = new (string Name, string Path)[]
        {
            ("推荐纯旋律", Path.Combine(root, "优化完成_原神可用")),
            ("和弦简化", Path.Combine(root, "优化完成_和弦简化_时长匹配")),
            ("成熟原琴源", playableSource),
            ("不可播备份源", backupSource),
            ("零丢音旧版", Path.Combine(root, "旧_零丢音_时长会变")),
        };

        foreach (var folder in folders)
        {
            var count = Directory.Exists(folder.Path)
                ? Directory.EnumerateFiles(folder.Path, "*.mid", SearchOption.AllDirectories).Count()
                : 0;
            var row = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(10),
                Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeBrush"],
                BorderThickness = new Thickness(1),
            };
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBorder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["AccentBlueBrush"],
            };
            iconBorder.Child = new FontIcon { Glyph = "\uE8D6", Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(iconBorder);

            var title = new StackPanel { Spacing = 2 };
            title.Children.Add(new TextBlock { Text = folder.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            title.Children.Add(new TextBlock { Text = "MIDI  ·  风物之诗琴  ·  推荐谱库", Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], FontSize = 11 });
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            var countText = new TextBlock { Text = $"{count} 个 MIDI", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(countText, 2);
            grid.Children.Add(countText);

            var pathText = new TextBlock
            {
                Text = folder.Path,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(pathText, 3);
            grid.Children.Add(pathText);

            var open = new Button { Content = new FontIcon { Glyph = "\uE8A7" }, Padding = new Thickness(10, 6, 10, 6) };
            open.Click += (_, _) => OpenFolder(folder.Path);
            Grid.SetColumn(open, 4);
            grid.Children.Add(open);

            row.Child = grid;
            LibraryPanel.Children.Add(row);
        }
    }

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }
}
