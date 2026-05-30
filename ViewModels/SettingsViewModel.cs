using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using oru.Services;

namespace oru.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;

        // Hydrate from persisted values
        RunAtStartup = _settings.RunAtStartup;
        MinimizeToTrayOnClose = _settings.MinimizeToTrayOnClose;
        AutoResumeMirroring = _settings.AutoResumeMirroring;
        ShowDisconnectedBluetooth = _settings.ShowDisconnectedBluetooth;
        SurfaceTheme = _settings.SurfaceTheme;
    }

    // ── General ───────────────────────────────────────────────

    [ObservableProperty]
    public partial bool RunAtStartup { get; set; }

    partial void OnRunAtStartupChanged(bool value)
    {
        _settings.RunAtStartup = value;
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (value)
                {
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue("OruAudioUtility", $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue("OruAudioUtility", false);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogService.Write($"Failed to set RunAtStartup: {ex.Message}");
        }
    }

    [ObservableProperty]
    public partial bool MinimizeToTrayOnClose { get; set; }

    partial void OnMinimizeToTrayOnCloseChanged(bool value) => _settings.MinimizeToTrayOnClose = value;

    [ObservableProperty]
    public partial bool AutoResumeMirroring { get; set; }

    partial void OnAutoResumeMirroringChanged(bool value) => _settings.AutoResumeMirroring = value;

    [ObservableProperty]
    public partial bool ShowDisconnectedBluetooth { get; set; }

    partial void OnShowDisconnectedBluetoothChanged(bool value) => _settings.ShowDisconnectedBluetooth = value;

    // ── System ────────────────────────────────────────────────

    [RelayCommand]
    private static void OpenSoundSettings()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ms-settings:sound",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private static void OpenBluetoothSettings()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ms-settings:bluetooth",
            UseShellExecute = true
        });
    }

    // ── Appearance ────────────────────────────────────────────

    [ObservableProperty]
    public partial string SurfaceTheme { get; set; }

    partial void OnSurfaceThemeChanged(string value)
    {
        _settings.SurfaceTheme = value;
        ThemeChanged?.Invoke(value);
    }

    // ── Events ────────────────────────────────────────────────

    /// <summary>Raised when the surface material changes. Host window should apply.</summary>
    public event Action<string>? ThemeChanged;

    // ── Diagnostics ───────────────────────────────────────────

    public string AppVersion => $"oru v{GetAssemblyVersion()}";

    public string LogFilePath => DebugLogService.GetLogPath();

    [RelayCommand]
    private static void ExportLogs()
    {
        var logPath = DebugLogService.GetLogPath();
        if (System.IO.File.Exists(logPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{logPath}\"",
                UseShellExecute = true
            });
        }
    }

    private static string GetAssemblyVersion()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        return ver is not null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
    }
}
