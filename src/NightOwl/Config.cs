using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NightOwl;

public class AppConfig
{
    public int Version { get; set; } = 1;
    public string ActiveProfile { get; set; } = "Default";
    public bool StartWithWindows { get; set; } = true;
    public bool FirstRunDone { get; set; }
    public bool PausedAll { get; set; }
    /// <summary>Windows build the user last confirmed the shell mods on (risk: updates break mods).</summary>
    public int ShellConfirmedBuild { get; set; }
    /// <summary>Set by the Explorer crash watchdog; shell mods stay off until the user retries.</summary>
    public bool ShellSafeMode { get; set; }
    public string? ShellSafeModeReason { get; set; }
    public List<LibraryItem> Library { get; set; } = new();
    public Dictionary<string, Profile> Profiles { get; set; } = new();

    [JsonIgnore] public Profile Active => Profiles.TryGetValue(ActiveProfile, out var p) ? p : Profiles.Values.First();
}

public class LibraryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "";
    /// <summary>File path, or an http(s) URL for web wallpapers.</summary>
    public string Path { get; set; } = "";
    /// <summary>video | image | html | url</summary>
    public string Type { get; set; } = "video";
    /// <summary>Sniffed video codec (hevc, h264, av1, vp9…), null until probed.</summary>
    public string? Codec { get; set; }
    public DateTime Added { get; set; } = DateTime.Now;
}

public class Profile
{
    public AutoHideSettings AutoHide { get; set; } = new();
    public WallpaperSettings Wallpaper { get; set; } = new();
    public TaskbarLookSettings TaskbarLook { get; set; } = new();
    public NotificationCenterSettings NotificationCenter { get; set; } = new();
    public StartMenuSettings StartMenu { get; set; } = new();
    public ResizeSettings Resize { get; set; } = new();
    public HoverSettings Hover { get; set; } = new();
    public StylerSettings Styler { get; set; } = new();
    public ExplorerSettings Explorer { get; set; } = new();
}

/// <summary>F1: taskbar background (Taskbar Background Helper mod, by Windhawk's author).</summary>
public class TaskbarLookSettings
{
    public bool Enabled { get; set; }
    /// <summary>clear | blur | acrylic | color</summary>
    public string Style { get; set; } = "acrylic";
    /// <summary>F1.2 tint, used by the color style.</summary>
    public string Color { get; set; } = "#202040";
    /// <summary>Tint opacity 0–100 (0 = fully clear).</summary>
    public int Opacity { get; set; } = 50;
    public bool AccentColor { get; set; }
    /// <summary>F1.3 (partial): apply only while a window is maximised on that monitor; stock otherwise.</summary>
    public bool OnlyWhenMaximized { get; set; }
}

/// <summary>F1.4: notification center and quick settings (Notification Center Styler mod).</summary>
public class NotificationCenterSettings
{
    public bool Enabled { get; set; }
    public string Theme { get; set; } = "TranslucentShell";
}

/// <summary>F1.3: Start menu and Search (Windows 11 Start Menu Styler mod).</summary>
public class StartMenuSettings
{
    public bool Enabled { get; set; }
    public string Theme { get; set; } = "WindowGlass";
    /// <summary>The mod's disableNewStartMenuLayout option: "", default, forceNewLayout or newLayoutSideBySide.</summary>
    public string Layout { get; set; } = "";
}

/// <summary>F3: taskbar height and icon size (Taskbar height and icon size mod).</summary>
public class ResizeSettings
{
    public bool Enabled { get; set; }
    public int Height { get; set; } = 48;
    public int IconSize { get; set; } = 24;
    public int ButtonWidth { get; set; } = 44;
}

/// <summary>F4: dock-style magnification (Taskbar Dock Animation Plus mod).</summary>
public class HoverSettings
{
    public bool Enabled { get; set; }
    /// <summary>Peak scale in percent, 100–160 (spec F4.1: 1.0x–1.6x).</summary>
    public int MaxScale { get; set; } = 125;
    public int Radius { get; set; } = 100;
    /// <summary>0 smooth (cosine), 1 linear, 2 snappy (exponential).</summary>
    public int Curve { get; set; }
    public int Spacing { get; set; } = 50;
}

/// <summary>F5: taskbar styler (Windows 11 Taskbar Styler mod): a built-in theme plus custom rules.</summary>
public class StylerSettings
{
    public bool Enabled { get; set; }
    public string Theme { get; set; } = "";
    public List<StyleRule> Rules { get; set; } = new();
    public List<string> Constants { get; set; } = new();
}

/// <summary>F5.3: a shareable plain-JSON taskbar theme.</summary>
public class ThemeFile
{
    public string Name { get; set; } = "";
    public string? BaseTheme { get; set; }
    public List<StyleRule>? Rules { get; set; }
    public List<string>? Constants { get; set; }
}

public class StyleRule
{
    public string Target { get; set; } = "";
    public List<string> Styles { get; set; } = new();
}

/// <summary>F7: File Explorer and Settings app (File Explorer Styler + Settings Styler mods).</summary>
public class ExplorerSettings
{
    public bool Enabled { get; set; }
    public string ExplorerTheme { get; set; } = "Translucent Explorer11";
    /// <summary>"" theme default | mica | micaAlt | acrylic | acrylicblur | none</summary>
    public string Backdrop { get; set; } = "mica";
    public bool SettingsApp { get; set; } = true;
    public string SettingsTheme { get; set; } = "Translucent_Settings11";
}

public class AutoHideSettings
{
    public bool Enabled { get; set; } = true;
    public bool EdgeReveal { get; set; } = true;
    /// <summary>Also hide while a maximised window is in front, by switching on Windows' own auto-hide.</summary>
    public bool WhenMaximized { get; set; }
    public List<string> AlwaysHide { get; set; } = new();
    public List<string> NeverHide { get; set; } = new();
}

public class WallpaperSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>perMonitor | span</summary>
    public string Layout { get; set; } = "perMonitor";
    /// <summary>Monitor device name → library item id.</summary>
    public Dictionary<string, string> Monitors { get; set; } = new();
    public string? SpanItem { get; set; }
    /// <summary>fill | fit | stretch | centre</summary>
    public string Fit { get; set; } = "fill";
    public bool Muted { get; set; } = true;
    public int Volume { get; set; } = 30;
    public bool Interactive { get; set; } = true;
    public bool PauseWhenFullscreen { get; set; } = true;
    public bool PauseWhenMaximized { get; set; } = true;
    public bool PauseOnBattery { get; set; } = true;
    public bool PauseOnBatterySaver { get; set; } = true;
    public PlaylistSettings Playlist { get; set; } = new();
}

public class PlaylistSettings
{
    public bool Enabled { get; set; }
    /// <summary>interval | timeOfDay</summary>
    public string Mode { get; set; } = "interval";
    public int IntervalMinutes { get; set; } = 30;
    public bool Shuffle { get; set; }
    public List<string> Items { get; set; } = new();
    public List<ScheduleSlot> Schedule { get; set; } = new();
}

public class ScheduleSlot
{
    public string Time { get; set; } = "07:00";
    public string ItemId { get; set; } = "";
}

/// <summary>Loads and saves config.json; every change goes through <see cref="Update"/> so listeners apply it live.</summary>
public class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Single-line JSON for the stdin protocol and WebView messages.</summary>
    public static readonly JsonSerializerOptions Wire = new(Json) { WriteIndented = false };

    public AppConfig Config { get; private set; }
    public event Action? Changed;
    readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 400 };

    public ConfigStore()
    {
        Config = Load();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };
    }

    static AppConfig Load()
    {
        AppConfig? cfg = null;
        try
        {
            if (File.Exists(Paths.Config))
                cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Paths.Config), Json);
        }
        catch (Exception ex)
        {
            Log.Error("config.json unreadable, starting from defaults", ex);
            try { File.Copy(Paths.Config, Paths.Config + ".bad", true); } catch { }
        }
        cfg ??= new AppConfig();
        Normalize(cfg);
        return cfg;
    }

    static void Normalize(AppConfig cfg)
    {
        if (cfg.Profiles.Count == 0)
        {
            cfg.Profiles["Default"] = new Profile();
            cfg.Profiles["Work"] = new Profile();
            var gaming = new Profile();
            gaming.AutoHide.EdgeReveal = false;
            cfg.Profiles["Gaming"] = gaming;
            var battery = new Profile();
            battery.Wallpaper.Enabled = false;
            cfg.Profiles["Battery"] = battery;
        }
        if (!cfg.Profiles.ContainsKey(cfg.ActiveProfile)) cfg.ActiveProfile = cfg.Profiles.Keys.First();
        foreach (var p in cfg.Profiles.Values)
        {
            p.AutoHide ??= new();
            p.Wallpaper ??= new();
            p.TaskbarLook ??= new();
            p.NotificationCenter ??= new();
            p.StartMenu ??= new();
            p.Resize ??= new();
            p.Hover ??= new();
            p.Styler ??= new();
            p.Styler.Rules ??= new();
            p.Styler.Constants ??= new();
            p.Explorer ??= new();
            p.Resize.Height = Math.Clamp(p.Resize.Height, 32, 80);
            p.Resize.IconSize = Math.Clamp(p.Resize.IconSize, 16, 48);
            p.Resize.ButtonWidth = Math.Clamp(p.Resize.ButtonWidth, 24, 80);
            p.Hover.MaxScale = Math.Clamp(p.Hover.MaxScale, 100, 160);
            p.Hover.Radius = Math.Clamp(p.Hover.Radius, 20, 400);
            p.Hover.Curve = Math.Clamp(p.Hover.Curve, 0, 2);
            p.TaskbarLook.Opacity = Math.Clamp(p.TaskbarLook.Opacity, 0, 100);
            p.Wallpaper.Playlist ??= new();
            p.Wallpaper.Monitors ??= new();
            p.Wallpaper.Volume = Math.Clamp(p.Wallpaper.Volume, 0, 100);
            p.Wallpaper.Playlist.IntervalMinutes = Math.Clamp(p.Wallpaper.Playlist.IntervalMinutes, 1, 24 * 60);
        }
    }

    public void Update(Action<AppConfig> change)
    {
        change(Config);
        Normalize(Config);
        _saveTimer.Stop();
        _saveTimer.Start();
        Changed?.Invoke();
    }

    /// <summary>Replace the whole config with one sent by the settings UI (live preview, no Apply button).</summary>
    public void Replace(JsonNode node)
    {
        var next = node.Deserialize<AppConfig>(Json);
        if (next == null) return;
        Update(c =>
        {
            c.ActiveProfile = next.ActiveProfile;
            c.StartWithWindows = next.StartWithWindows;
            c.PausedAll = next.PausedAll;
            c.ShellConfirmedBuild = next.ShellConfirmedBuild;
            c.ShellSafeMode = next.ShellSafeMode;
            c.ShellSafeModeReason = next.ShellSafeModeReason;
            c.Library = next.Library;
            c.Profiles = next.Profiles;
        });
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Roaming);
            var tmp = Paths.Config + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Config, Json));
            File.Move(tmp, Paths.Config, true);
        }
        catch (Exception ex) { Log.Error("saving config failed", ex); }
    }

    public void SaveNow() { _saveTimer.Stop(); Save(); }
}
