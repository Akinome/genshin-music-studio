using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class AppSettingsJsonTests
{
    [Fact]
    public void SerializeAndDeserialize_RoundTripsAllSettings()
    {
        var settings = new AppSettingsData
        {
            PlayableDir = "playable",
            BackupDir = "backup",
            OutputDir = "output",
            LibraryFolders = new List<LibraryFolderData>
            {
                new() { Name = "folder", Path = "path" },
            },
            Volume = 0.75,
            VelocityBoost = 1.5,
            AudioInstrument = "lyre",
            PlayInGame = true,
            PlayInGameBackground = true,
            SearchDefaultYuanQin = true,
            PlayInGameGuard = true,
        };

        var json = AppSettingsJson.Serialize(settings);
        var restored = AppSettingsJson.Deserialize(json);

        Assert.Contains("\"playable_dir\":\"playable\"", json);
        Assert.Contains("\"play_in_game_guard\":true", json);
        Assert.Equal(settings.PlayableDir, restored.PlayableDir);
        Assert.Equal(settings.BackupDir, restored.BackupDir);
        Assert.Equal(settings.OutputDir, restored.OutputDir);
        Assert.Equal(settings.LibraryFolders![0].Name, restored.LibraryFolders![0].Name);
        Assert.Equal(settings.Volume, restored.Volume);
        Assert.Equal(settings.VelocityBoost, restored.VelocityBoost);
        Assert.Equal(settings.AudioInstrument, restored.AudioInstrument);
        Assert.True(restored.PlayInGame);
        Assert.True(restored.PlayInGameBackground);
        Assert.True(restored.SearchDefaultYuanQin);
        Assert.True(restored.PlayInGameGuard);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void Deserialize_ReturnsEmptySettingsForMissingOrInvalidJson(string? json)
    {
        var settings = AppSettingsJson.Deserialize(json);

        Assert.NotNull(settings);
        Assert.Null(settings.PlayableDir);
        Assert.Null(settings.LibraryFolders);
    }

    [Fact]
    public void Serialize_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => AppSettingsJson.Serialize(null!));
    }

    [Fact]
    public void LibraryFolders_ReturnsConfiguredFoldersInDisplayOrder()
    {
        var settings = new AppSettingsData
        {
            PlayableDir = "playable",
            BackupDir = "backup",
            OutputDir = "output",
        };

        var folders = AppSettingsDefaults.LibraryFolders(settings, "repo");

        Assert.Equal(3, folders.Count);
        Assert.Equal("推荐谱库", folders[0].Name);
        Assert.Equal("playable", folders[0].Path);
        Assert.Equal("备份谱库", folders[1].Name);
        Assert.Equal("backup", folders[1].Path);
        Assert.Equal("推荐输出", folders[2].Name);
        Assert.Equal("output", folders[2].Path);
    }

    [Fact]
    public void LibraryFolders_UsesFallbackOutputAndSkipsEmptyOptionalFolders()
    {
        var folders = AppSettingsDefaults.LibraryFolders(new AppSettingsData(), "repo");

        var folder = Assert.Single(folders);
        Assert.Equal("推荐输出", folder.Name);
        Assert.Equal(Path.Combine("repo", "优化完成_原神可用"), folder.Path);
    }

    [Fact]
    public void LibraryFolders_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => AppSettingsDefaults.LibraryFolders(null!, "repo"));
        Assert.Throws<ArgumentException>(() =>
            AppSettingsDefaults.LibraryFolders(new AppSettingsData(), ""));
    }
}
