using System.IO;
using System.Text.Json;

namespace oru.Services;

/// <summary>
/// Centralised persistence layer for all user-facing settings.
/// Uses a local JSON file since the app runs unpackaged (no ApplicationData available).
/// </summary>
public sealed class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "oru");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private readonly object _lockObj = new();
    private Dictionary<string, object?> _cache;

    public SettingsService()
    {
        _cache = Load();
    }

    // ── Keys ──────────────────────────────────────────────────
    private const string KeyRunAtStartup = "RunAtStartup";
    private const string KeyMinimizeToTray = "MinimizeToTrayOnClose";
    private const string KeyAutoResumeMirroring = "AutoResumeMirroring";
    private const string KeyShowDisconnectedBt = "ShowDisconnectedBluetooth";
    private const string KeySurfaceTheme = "FlyoutSurfaceTheme";

    // ── General ───────────────────────────────────────────────

    public bool RunAtStartup
    {
        get => ReadBool(KeyRunAtStartup, false);
        set { WriteBool(KeyRunAtStartup, value); Save(); }
    }

    public bool MinimizeToTrayOnClose
    {
        get => ReadBool(KeyMinimizeToTray, true);
        set { WriteBool(KeyMinimizeToTray, value); Save(); }
    }

    public bool AutoResumeMirroring
    {
        get => ReadBool(KeyAutoResumeMirroring, false);
        set { WriteBool(KeyAutoResumeMirroring, value); Save(); }
    }

    public bool ShowDisconnectedBluetooth
    {
        get => ReadBool(KeyShowDisconnectedBt, true);
        set { WriteBool(KeyShowDisconnectedBt, value); Save(); }
    }

    // ── Appearance ────────────────────────────────────────────

    public string SurfaceTheme
    {
        get => ReadString(KeySurfaceTheme, "Acrylic");
        set { _cache[KeySurfaceTheme] = value; Save(); }
    }

    // ── Helpers ───────────────────────────────────────────────

    private bool ReadBool(string key, bool fallback)
    {
        if (_cache.TryGetValue(key, out var v))
        {
            if (v is bool b) return b;
            if (v is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
            if (v is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
        }
        return fallback;
    }

    private int ReadInt(string key, int fallback)
    {
        if (_cache.TryGetValue(key, out var v))
        {
            if (v is int n) return n;
            if (v is JsonElement je && je.TryGetInt32(out var i)) return i;
        }
        return fallback;
    }

    private string ReadString(string key, string fallback)
    {
        if (_cache.TryGetValue(key, out var v))
        {
            if (v is string s) return s;
            if (v is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? fallback;
        }
        return fallback;
    }

    private void WriteBool(string key, bool value) => _cache[key] = value;

    private Dictionary<string, object?> Load()
    {
        lock (_lockObj)
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
                           ?? new Dictionary<string, object?>();
                }
            }
            catch
            {
                DebugLogService.Write("Failed to load settings file, using defaults.");
            }
            return new Dictionary<string, object?>();
        }
    }

    private void Save()
    {
        lock (_lockObj)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                SettingsChanged?.Invoke();
            }
            catch
            {
                DebugLogService.Write("Failed to save settings file.");
            }
        }
    }

    public event Action? SettingsChanged;
}
