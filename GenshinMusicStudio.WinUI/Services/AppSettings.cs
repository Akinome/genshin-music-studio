namespace GenshinMusicStudio_WinUI.Services;

/// <summary>Reads and writes the user settings file for the WinUI shell.</summary>
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
                    cached = AppSettingsJson.Deserialize(json);
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
            File.WriteAllText(SettingsPath, AppSettingsJson.Serialize(data));
            cached = data;
        }
    }

    public static List<LibraryFolderData> DefaultLibraryFolders(AppSettingsData settings, string repoRoot)
        => AppSettingsDefaults.LibraryFolders(settings, repoRoot);
}
