using GenshinMusicStudio_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GenshinMusicStudio_WinUI.Pages;

public sealed partial class LibraryPage : Page
{
    private AppSettingsData settings = new();

    public LibraryPage()
    {
        InitializeComponent();
        Loaded += LibraryPage_Loaded;
    }

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        settings = AppSettings.Load();
        LoadSettingsIntoBoxes();
        if (settings.LibraryFolders is null)
        {
            settings.LibraryFolders = AppSettings.DefaultLibraryFolders(settings, StudioBackendClient.RepoRoot);
            AppSettings.Save(settings);
        }
        RenderLibrary();
    }

    private void LoadSettingsIntoBoxes()
    {
        var settings = AppSettings.Load();
        var root = StudioBackendClient.RepoRoot;
        PlayableBox.Text = settings.PlayableDir
            ?? Environment.GetEnvironmentVariable("GENSHIN_PLAYABLE_DIR")
            ?? Path.Combine(root, "示例谱库", "成熟的原琴");
        BackupBox.Text = settings.BackupDir
            ?? Environment.GetEnvironmentVariable("GENSHIN_BACKUP_DIR")
            ?? Path.Combine(root, "示例谱库", "不可播备份");
        OutputDirBox.Text = settings.OutputDir ?? Path.Combine(root, "优化完成_原神可用");
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        settings.PlayableDir = PlayableBox.Text.Trim();
        settings.BackupDir = BackupBox.Text.Trim();
        settings.OutputDir = OutputDirBox.Text.Trim();
        AppSettings.Save(settings);
        RenderLibrary();
    }

    private async void AddLibrary_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFolderAsync();
        if (path is null) return;
        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        settings.LibraryFolders ??= new List<LibraryFolderData>();
        if (settings.LibraryFolders.Any(f => string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            await ShowMessageAsync("目录已存在", "该目录已在谱库列表中。");
            return;
        }
        settings.LibraryFolders.Add(new LibraryFolderData
        {
            Name = Path.GetFileName(normalized),
            Path = normalized,
        });
        AppSettings.Save(settings);
        RenderLibrary();
    }

    private void DeleteLibraryFolder(LibraryFolderData folder)
    {
        settings.LibraryFolders?.Remove(folder);
        AppSettings.Save(settings);
        RenderLibrary();
    }
        {
            PlayableDir = PlayableBox.Text.Trim(),
            BackupDir = BackupBox.Text.Trim(),
            OutputDir = OutputDirBox.Text.Trim(),
        });
        RenderLibrary();
    }

    private async void BrowsePlayable_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFolderAsync();
        if (path is not null) PlayableBox.Text = path;
    }

    private async void BrowseBackup_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFolderAsync();
        if (path is not null) BackupBox.Text = path;
    }

    private async void BrowseOutputDir_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFolderAsync();
        if (path is not null) OutputDirBox.Text = path;
    }

    private void OpenPlayable_Click(object sender, RoutedEventArgs e) => OpenFolder(PlayableBox.Text);
    private void OpenBackup_Click(object sender, RoutedEventArgs e) => OpenFolder(BackupBox.Text);
    private void OpenOutputDir_Click(object sender, RoutedEventArgs e) => OpenFolder(OutputDirBox.Text);

    private async void BrowseMigrateFrom_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFolderAsync();
        if (path is not null) MigrateFromBox.Text = path;
    }

    private async void BrowseMigrateTo_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFolderAsync();
        if (path is not null) MigrateToBox.Text = path;
    }

    private async void Migrate_Click(object sender, RoutedEventArgs e)
    {
        var from = MigrateFromBox.Text.Trim();
        var to = MigrateToBox.Text.Trim();
        var move = (MigrateModeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "移动";
        if (string.IsNullOrWhiteSpace(from) || !Directory.Exists(from))
        {
            await ShowMessageAsync("缺少源目录", "请选择有效的源目录。");
            return;
        }
        if (string.IsNullOrWhiteSpace(to))
        {
            await ShowMessageAsync("缺少目标目录", "请选择目标目录。");
            return;
        }
        if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync("路径相同", "源目录和目标目录不能相同。");
            return;
        }

        var files = Directory.EnumerateFiles(from, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Count == 0)
        {
            await ShowMessageAsync("没有找到 MIDI", "源目录中没有 MIDI 文件。");
            return;
        }

        var confirm = await new ContentDialog
        {
            Title = "确认迁移",
            Content = string.Format("将在目标目录中{0} {1} 个 MIDI 文件，同名文件跳过。是否继续？", move ? "移动" : "复制", files.Count),
            PrimaryButtonText = "开始",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
        }.ShowAsync();
        if (confirm != ContentDialogResult.Primary) return;

        MigrateButton.IsEnabled = false;
        var copied = 0;
        var skipped = 0;
        var failed = 0;
        try
        {
            Directory.CreateDirectory(to);
            foreach (var file in files)
            {
                var target = Path.Combine(to, Path.GetFileName(file));
                try
                {
                    if (File.Exists(target))
                    {
                        skipped++;
                        continue;
                    }
                    if (move) File.Move(file, target);
                    else File.Copy(file, target);
                    copied++;
                }
                catch
                {
                    failed++;
                }
            }
        }
        finally
        {
            MigrateButton.IsEnabled = true;
        }

        await ShowMessageAsync("迁移完成", string.Format("{0} {1} 个，跳过 {2} 个，失败 {3} 个。", move ? "移动" : "复制", copied, skipped, failed));
        RenderLibrary();
    }


    private void Refresh_Click(object sender, RoutedEventArgs e) => RenderLibrary();
    private void OpenRecommended_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(AppSettings.Load().OutputDir ?? Path.Combine(StudioBackendClient.RepoRoot, "优化完成_原神可用"));

    private void SettingsNav_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var container = LibraryScroll.Content as FrameworkElement;
            if (container is null) return;
            var point = SettingsCard.TransformToVisual(container).TransformPoint(new Windows.Foundation.Point(0, 0));
            LibraryScroll.ChangeView(null, point.Y, null, true);
        }
        catch
        {
        }
    }

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
        var folders = settings.LibraryFolders;
        if (folders is null || folders.Count == 0)
        {
            folders = AppSettings.DefaultLibraryFolders(settings, StudioBackendClient.RepoRoot);
            settings.LibraryFolders = folders;
        }

        foreach (var folder in folders.ToList())
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

            var delete = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D" },
                Padding = new Thickness(10, 6, 10, 6),
                ToolTipService.ToolTip = "从列表移除",
            };
            delete.Click += (_, _) => DeleteLibraryFolder(folder);
            Grid.SetColumn(delete, 5);
            grid.Children.Add(delete);

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
