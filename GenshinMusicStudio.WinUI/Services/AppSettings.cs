using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenshinMusicStudio_WinUI.Services;

public sealed class AppSettingsData
{
    [JsonPropertyName("playable_dir")] public string? PlayableDir { get; set; }
    [JsonPropertyName("backup_dir")] public string? BackupDir { get; set; }
    [JsonPropertyName("output_dir")] public string? OutputDir { get; set; }
}

public static class AppSettings
{
    private static readonly object Gate = new();
    private static AppSettingsData? cached;

    public static string SettingsPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GenshinMusicStudio");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettingsData Load()
    {
        lock (Gate)
        {
            if (cached is not null) return cached;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    cached = JsonSerializer.Deserialize<AppSettingsData>(json) ?? new AppSettingsData();
                }
            }
            catch
            {
                cached = new AppSettingsData();
            }
            cached ??= new AppSettingsData();
            return cached;
        }
    }

    public static void Save(AppSettingsData data)
    {
        lock (Gate)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data, options));
            cached = data;
        }
    }
}
