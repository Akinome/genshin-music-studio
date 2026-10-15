using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenshinMusicStudio.Core;

/// <summary>Serializable user settings shared by the WinUI shell and tests.</summary>
public sealed class AppSettingsData
{
    /// <summary>Directory containing playable reference sheets.</summary>
    [JsonPropertyName("playable_dir")] public string? PlayableDir { get; set; }

    /// <summary>Directory containing backup sheets.</summary>
    [JsonPropertyName("backup_dir")] public string? BackupDir { get; set; }

    /// <summary>Default output directory.</summary>
    [JsonPropertyName("output_dir")] public string? OutputDir { get; set; }

    /// <summary>User-managed library folders.</summary>
    [JsonPropertyName("library_folders")] public List<LibraryFolderData>? LibraryFolders { get; set; }

    /// <summary>Playback volume between zero and one.</summary>
    [JsonPropertyName("volume")] public double? Volume { get; set; }

    /// <summary>Playback velocity multiplier.</summary>
    [JsonPropertyName("velocity_boost")] public double? VelocityBoost { get; set; }

    /// <summary>Selected instrument identifier.</summary>
    [JsonPropertyName("audio_instrument")] public string? AudioInstrument { get; set; }

    /// <summary>True when playback should send keys to the game window.</summary>
    [JsonPropertyName("play_in_game")] public bool? PlayInGame { get; set; }

    /// <summary>True when in-game playback may use background key messages.</summary>
    [JsonPropertyName("play_in_game_background")] public bool? PlayInGameBackground { get; set; }

    /// <summary>True when Bilibili searches append the original-lyre hint by default.</summary>
    [JsonPropertyName("search_default_yuanqin")] public bool? SearchDefaultYuanQin { get; set; }
}

/// <summary>One folder shown in the user's sheet library.</summary>
public sealed class LibraryFolderData
{
    /// <summary>Display name.</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    /// <summary>Absolute or application-relative folder path.</summary>
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
}

/// <summary>Source-generated JSON context for settings serialization.</summary>
[JsonSerializable(typeof(AppSettingsData))]
[JsonSerializable(typeof(LibraryFolderData))]
public sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}

/// <summary>Stable JSON helpers used by the settings store.</summary>
public static class AppSettingsJson
{
    /// <summary>Serializes settings using the source-generated context.</summary>
    /// <param name="data">Settings to serialize.</param>
    /// <returns>JSON text.</returns>
    public static string Serialize(AppSettingsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return JsonSerializer.Serialize(data, AppSettingsJsonContext.Default.AppSettingsData);
    }

    /// <summary>Deserializes settings and falls back to empty settings for invalid JSON.</summary>
    /// <param name="json">JSON text.</param>
    /// <returns>Deserialized settings, never null.</returns>
    public static AppSettingsData Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AppSettingsData();
        try
        {
            return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettingsData) ?? new AppSettingsData();
        }
        catch (JsonException)
        {
            return new AppSettingsData();
        }
    }
}

/// <summary>Builds default library entries when no user list has been saved.</summary>
public static class AppSettingsDefaults
{
    /// <summary>Creates the initial playable, backup and output folder list.</summary>
    /// <param name="settings">Current settings.</param>
    /// <param name="repoRoot">Application root used for the fallback output path.</param>
    /// <returns>Library folders in display order.</returns>
    public static List<LibraryFolderData> LibraryFolders(AppSettingsData settings, string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var folders = new List<LibraryFolderData>();
        if (!string.IsNullOrWhiteSpace(settings.PlayableDir))
        {
            folders.Add(new LibraryFolderData { Name = "推荐谱库", Path = settings.PlayableDir });
        }

        if (!string.IsNullOrWhiteSpace(settings.BackupDir))
        {
            folders.Add(new LibraryFolderData { Name = "备份谱库", Path = settings.BackupDir });
        }

        folders.Add(new LibraryFolderData
        {
            Name = "推荐输出",
            Path = settings.OutputDir ?? Path.Combine(repoRoot, "优化完成_原神可用"),
        });
        return folders;
    }
}
