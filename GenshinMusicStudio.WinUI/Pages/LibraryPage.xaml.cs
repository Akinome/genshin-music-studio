using GenshinMusicStudio_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GenshinMusicStudio_WinUI.Pages;

public sealed partial class LibraryPage : Page
{
    private AppSettingsData settings = new();
    private string? playingFolder;

    public LibraryPage()
    {
        InitializeComponent();
        Loaded += LibraryPage_Loaded;
        Unloaded += LibraryPage_Unloaded;
    }

    private void LibraryPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.Player.PlayingChanged -= Player_PlayingChanged;
    }

    private void Player_PlayingChanged(bool playing)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!playing) playingFolder = null;
            RenderLibrary();
        });
    }

    private void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.Player.PlayingChanged += Player_PlayingChanged;
        settings = AppSettings.Load();
        if (settings.LibraryFolders is null)
        {
            settings.LibraryFolders = AppSettings.DefaultLibraryFolders(settings, StudioBackendClient.RepoRoot);
            AppSettings.Save(settings);
        }
        RenderLibrary();
    }

    private async void AddLibrary_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickerHelper.PickFolderAsync();
        if (path is null) return;
        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(folderName)) folderName = normalized;
        settings.LibraryFolders ??= new List<LibraryFolderData>();
        if (settings.LibraryFolders.Any(f => string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            await ShowMessageAsync("目录已存在", "该目录已在谱库列表中。");
            return;
        }
        settings.LibraryFolders.Add(new LibraryFolderData
        {
            Name = folderName,
            Path = normalized,
        });
        AppSettings.Save(settings);
        RenderLibrary();
    }

    private void RemoveLibraryFolder(LibraryFolderData folder)
    {
        settings.LibraryFolders?.Remove(folder);
        AppSettings.Save(settings);
        RenderLibrary();
    }

    private void TogglePlay(LibraryFolderData folder)
    {
        if (playingFolder == folder.Path && App.Player.IsPlaying)
        {
            App.Player.Stop();
            return;
        }
        if (!Directory.Exists(folder.Path))
        {
            _ = ShowMessageAsync("目录不存在", "该目录在磁盘上不存在。");
            return;
        }
        var files = Directory.EnumerateFiles(folder.Path, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            _ = ShowMessageAsync("没有找到 MIDI", "该目录中没有 MIDI 文件。");
            return;
        }
        playingFolder = folder.Path;
        App.Player.Play(files, folder.Path);
        RenderLibrary();
    }

    private void SetLibraryRole(LibraryFolderData folder, Action<AppSettingsData> apply)
    {
        apply(settings);
        AppSettings.Save(settings);
        RenderLibrary();
    }

    private async Task MigrateFolderAsync(LibraryFolderData folder)
    {
        if (!Directory.Exists(folder.Path))
        {
            await ShowMessageAsync("目录不存在", "该目录在磁盘上不存在，请先移除或重新添加。");
            return;
        }
        var target = await PickerHelper.PickFolderAsync();
        if (target is null) return;
        target = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(Path.GetFullPath(folder.Path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync("路径相同", "目标目录不能和当前目录相同。");
            return;
        }

        var files = Directory.EnumerateFiles(folder.Path, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Count == 0)
        {
            await ShowMessageAsync("没有找到 MIDI", "该目录中没有 MIDI 文件。");
            return;
        }

        var confirm = await new ContentDialog
        {
            Title = "迁移 MIDI 文件",
            Content = string.Format(
                "将把 {0} 个 MIDI 文件从 {1} 迁移到 {2}，同名文件跳过。",
                files.Count, folder.Name, Path.GetFileName(target)),
            PrimaryButtonText = "移动",
            SecondaryButtonText = "复制",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
        }.ShowAsync();
        if (confirm == ContentDialogResult.None) return;
        var move = confirm == ContentDialogResult.Primary;

        var copied = 0;
        var skipped = 0;
        var failed = 0;
        Directory.CreateDirectory(target);
        foreach (var file in files)
        {
            var targetPath = Path.Combine(target, Path.GetFileName(file));
            try
            {
                if (File.Exists(targetPath))
                {
                    skipped++;
                    continue;
                }
                if (move) File.Move(file, targetPath);
                else File.Copy(file, targetPath);
                copied++;
            }
            catch
            {
                failed++;
            }
        }

        await ShowMessageAsync(
            "迁移完成",
            string.Format("{0} {1} 个，跳过 {2} 个，失败 {3} 个。", move ? "移动" : "复制", copied, skipped, failed));
        RenderLibrary();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RenderLibrary();

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
            LibraryPanel.Children.Add(new TextBlock
            {
                Text = "列表为空，点击“添加目录”添加谱库目录。",
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(4, 8, 4, 8),
            });
            return;
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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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
            title.Children.Add(new TextBlock
            {
                Text = "MIDI  ·  风物之诗琴",
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                FontSize = 11,
            });
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

            var isPlayingThis = playingFolder == folder.Path && App.Player.IsPlaying;
            var play = new Button
            {
                Content = new FontIcon { Glyph = isPlayingThis ? "\uE71A" : "\uE768" },
                Padding = new Thickness(10, 6, 10, 6),
            };
            ToolTipService.SetToolTip(play, isPlayingThis ? "停止播放" : "播放该目录 MIDI");
            play.Click += (_, _) => TogglePlay(folder);
            Grid.SetColumn(play, 4);
            grid.Children.Add(play);

            var open = new Button
            {
                Content = new FontIcon { Glyph = "\uE8A7" },
                Padding = new Thickness(10, 6, 10, 6),
            };
            ToolTipService.SetToolTip(open, "打开目录");
            open.Click += (_, _) => OpenFolder(folder.Path);
            Grid.SetColumn(open, 5);
            grid.Children.Add(open);

            var remove = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D" },
                Padding = new Thickness(10, 6, 10, 6),
            };
            ToolTipService.SetToolTip(remove, "从列表移除");
            remove.Click += (_, _) => RemoveLibraryFolder(folder);
            Grid.SetColumn(remove, 6);
            grid.Children.Add(remove);

            var more = new Button
            {
                Content = new FontIcon { Glyph = "\uE712" },
                Padding = new Thickness(10, 6, 10, 6),
            };
            ToolTipService.SetToolTip(more, "更多操作");
            var menu = new MenuFlyout();
            menu.Items.Add(CreateMenuItem("设为推荐谱库", () => SetLibraryRole(folder, s => s.PlayableDir = folder.Path)));
            menu.Items.Add(CreateMenuItem("设为备份谱库", () => SetLibraryRole(folder, s => s.BackupDir = folder.Path)));
            menu.Items.Add(CreateMenuItem("迁移 MIDI 到新目录...", () => _ = MigrateFolderAsync(folder)));
            more.Flyout = menu;
            Grid.SetColumn(more, 7);
            grid.Children.Add(more);

            row.Child = grid;
            LibraryPanel.Children.Add(row);
        }
    }

    private static MenuFlyoutItem CreateMenuItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private async void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            await ShowMessageAsync("目录不存在", "该目录在磁盘上不存在。");
            return;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }
}
