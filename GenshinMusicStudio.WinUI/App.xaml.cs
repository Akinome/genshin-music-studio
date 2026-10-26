using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using GenshinMusicStudio_WinUI.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GenshinMusicStudio_WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public static MainWindow MainWindow { get; private set; } = null!;
    public static StudioBackendClient Backend { get; } = new();
    public static MidiPlayer Player { get; } = new();
    public static InstrumentPlayer Instruments { get; } = new();
    public static GameKeySender GameKeys { get; } = new();
    public static GameInputGuardService InputGuard { get; } = new(Player, GameKeys);
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        this.UnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GenshinMusicStudio");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss}] {e.Message}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
        e.Handled = true;
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        ApplySystemAccent();
        RestoreInGamePlayback();
        MainWindow = new MainWindow();
        _window = MainWindow;
        _window.Activate();
        RestoreDefaultInstrument();
    }

    /// <summary>
    /// Wires the shared game-key sender into the player so in-game playback works
    /// no matter which page started it.
    /// </summary>
    private static void RestoreInGamePlayback()
    {
        try
        {
            var settings = AppSettings.Load();
            GameKeys.BackgroundMode = settings.PlayInGameBackground ?? false;
            Player.PlayInGame = settings.PlayInGame ?? false;
            Player.GameOutput = GameKeys;
            InputGuard.Enabled = settings.PlayInGameGuard ?? false;
        }
        catch
        {
        }
    }

    /// <summary>
    /// Publishes the Windows accent colour as the app's accent brush so custom
    /// drawing (waterfall notes, key highlights) matches the built-in
    /// accent-styled buttons instead of a hard-coded colour.
    /// </summary>
    private static void ApplySystemAccent()
    {
        try
        {
            var settings = new Windows.UI.ViewManagement.UISettings();
            var accent = settings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            Application.Current.Resources["AccentBlueBrush"] =
                new Microsoft.UI.Xaml.Media.SolidColorBrush(accent);
        }
        catch
        {
            // Fall back to the brush defined in App.xaml.
        }
    }

    private static void RestoreDefaultInstrument()
    {
        try
        {
            var instrumentsRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "Instruments");
            var selected = AudioInstrumentDefaults.Resolve(
                AppSettings.Load().AudioInstrument,
                instrumentsRoot);
            if (selected.Length == 0 || !Directory.Exists(selected)) return;
            var sequence = App.Instruments.NextLoadSequence();
            Task.Run(() =>
            {
                if (!App.Instruments.LoadInstrument(selected, sequence)) return;
                App.Player.SetInstrument(App.Instruments, sequence);
                App.Instruments.SetDeviceVolume(App.Player.Volume);
            });
        }
        catch
        {
        }
    }
}
