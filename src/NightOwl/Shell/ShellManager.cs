namespace NightOwl.Shell;

/// <summary>A Windhawk mod nightOwl manages, and the feature it implements.</summary>
public record ShellMod(string Id, string Name, string Feature);

/// <summary>
/// Phases 2–4 through Windhawk: turns the active profile into mod settings, and holds the
/// safeguards for the two shell risks:
///  - Windows updates (NF.8): on a build the user hasn't confirmed, every shell mod stays off and Home
///    asks to turn them back on; mods that fail on a new build can then be updated in Windhawk.
///  - Explorer crashes (NF.6): two Explorer restarts within a minute while mods are on switches every
///    mod off, stops Windhawk and enters safe mode until the user retries.
/// Pause all, the reset hotkey and High Contrast also switch every mod off.
/// </summary>
public sealed class ShellManager
{
    public static readonly ShellMod[] Mods =
    {
        new("taskbar-background-helper", "Taskbar Background Helper", "Translucent taskbar"),
        new("windows-11-notification-center-styler", "Windows 11 Notification Center Styler", "Notification center"),
        new("windows-11-start-menu-styler", "Windows 11 Start Menu Styler", "Start menu"),
        new("taskbar-icon-size", "Taskbar height and icon size", "Resize the taskbar"),
        new("taskbar-dock-animation-plus", "Taskbar Dock Animation Plus", "Hover animation"),
        new("windows-11-taskbar-styler", "Windows 11 Taskbar Styler", "Taskbar styler"),
        new("windows-11-file-explorer-styler", "Windows 11 File Explorer Styler", "Translucent Explorer"),
        new("windows-11-settings-styler", "Windows 11 Settings Styler", "Translucent Settings"),
        new("translucent-windows", "Translucent Windows", "Not used (kept off)"),
    };

    /// <summary>
    /// The processes Windhawk may load into: spec NF.10, plus ShellHost.exe (Quick Settings on 24H2+) and the
    /// Start menu and Search hosts.
    /// </summary>
    public const string ShellProcesses = "explorer.exe|ShellExperienceHost.exe|ShellHost.exe|SystemSettings.exe|StartMenuExperienceHost.exe|SearchHost.exe";

    readonly ConfigStore _store;
    readonly WindhawkBridge _wh = new();
    readonly List<DateTime> _explorerRestarts = new();
    bool _anyOn;

    public ShellManager(ConfigStore store) { _store = store; }

    public WindhawkBridge Windhawk => _wh;
    public string Status { get; private set; } = "Off";
    public bool IsActive => _anyOn;
    public List<string> Warnings { get; } = new();

    /// <summary>True when the current build hasn't been confirmed for shell mods yet.</summary>
    public bool BuildUnconfirmed => _store.Config.ShellConfirmedBuild != WinInfo.Build;

    static bool AnyFeature(Profile p) =>
        p.TaskbarLook.Enabled || p.NotificationCenter.Enabled || p.StartMenu.Enabled || p.Resize.Enabled || p.Hover.Enabled
        || p.Styler.Enabled || p.Explorer.Enabled;

    public void Apply(bool suppressed)
    {
        var cfg = _store.Config;
        var p = cfg.Active;
        Warnings.Clear();

        if (Sandbox.Package != null)
        {
            // Windhawk installed or run from here would be invisible to Explorer.
            Status = "Unavailable (started from inside another app)";
            if (AnyFeature(p)) Warnings.Add("Taskbar and Explorer styling can't run while nightOwl is started from inside another app's sandbox. Quit nightOwl and start it from its folder or the Start menu.");
            return;
        }

        if (!_wh.IsInstalled)
        {
            Status = AnyFeature(p) ? "Windhawk is not installed" : "Off";
            if (AnyFeature(p)) Warnings.Add("Taskbar and Explorer styling need Windhawk. Install it from the Taskbar page.");
            return;
        }

        var missing = Mods.Where(m => m.Id != "translucent-windows" && !_wh.IsModInstalled(m.Id)).ToList();
        bool wanted = AnyFeature(p);
        string? blockReason =
            cfg.PausedAll ? "Paused" :
            suppressed ? "Off (High Contrast)" :
            cfg.ShellSafeMode ? "Safe mode" :
            wanted && BuildUnconfirmed ? $"Waiting for you to confirm Windows build {WinInfo.Build}" : null;

        if (cfg.ShellSafeMode)
            Warnings.Add($"Taskbar and Explorer styling were switched off to protect Explorer: {cfg.ShellSafeModeReason ?? "Explorer kept restarting"}. Retry from Home once mods are updated in Windhawk.");
        else if (wanted && BuildUnconfirmed && !cfg.PausedAll)
            Warnings.Add(cfg.ShellConfirmedBuild == 0
                ? $"Taskbar and Explorer styling are ready but held off until you confirm them on Windows build {WinInfo.Build}."
                : $"Windows changed from build {cfg.ShellConfirmedBuild} to {WinInfo.Build}, so taskbar and Explorer styling were held off. Update the mods in Windhawk, then turn them back on.");
        foreach (var m in missing)
            if (FeatureOn(p, m.Id)) Warnings.Add($"“{m.Feature}” needs the Windhawk mod “{m.Name}”. Install it in Windhawk.");

        bool active = blockReason == null;
        int on = 0;
        try
        {
            _wh.LimitProcesses(ShellProcesses);
            foreach (var m in Mods)
            {
                bool enable = active && FeatureOn(p, m.Id);
                if (enable) on++;
                _wh.Apply(m.Id, enable, enable ? SettingsFor(p, m.Id) : null, OwnedPrefixes(m.Id));
            }
        }
        catch (Exception ex)
        {
            Log.Error("applying Windhawk mods failed", ex);
            Warnings.Add("Couldn't update Windhawk's mod settings: " + ex.Message);
        }

        // Start Windhawk when mods are wanted; stop it only on the way from on to off, so an
        // open Windhawk window (installing mods) isn't closed by unrelated setting changes.
        bool wasOn = _anyOn;
        _anyOn = on > 0;
        if (_anyOn)
        {
            bool granted = false;
            try { granted = _wh.EnsureSandboxAccess(); }
            catch (Exception ex) { Log.Error("granting sandboxed processes access to Windhawk failed", ex); }
            if (granted && _wh.IsRunning) _wh.Restart(); // reload into the processes that were refused before
            else _wh.StartTrayOnly();
        }
        else if (wasOn) _wh.Exit();
        Status = blockReason ?? (on == 0 ? "Off" : $"{on} mod(s) active through Windhawk");
    }

    static bool FeatureOn(Profile p, string id) => id switch
    {
        "taskbar-background-helper" => p.TaskbarLook.Enabled,
        "windows-11-notification-center-styler" => p.NotificationCenter.Enabled,
        "windows-11-start-menu-styler" => p.StartMenu.Enabled,
        "taskbar-icon-size" => p.Resize.Enabled,
        "taskbar-dock-animation-plus" => p.Hover.Enabled,
        // The translucent taskbar needs the styler too: it clears the taskbar's own XAML background so the
        // Background Helper's blur/acrylic/tint (drawn behind it) shows through.
        "windows-11-taskbar-styler" => p.Styler.Enabled || p.TaskbarLook.Enabled,
        "windows-11-file-explorer-styler" => p.Explorer.Enabled,
        "windows-11-settings-styler" => p.Explorer.Enabled && p.Explorer.SettingsApp,
        _ => false,
    };

    static IEnumerable<string> OwnedPrefixes(string id) => id switch
    {
        "windows-11-taskbar-styler" => new[] { "controlStyles[", "styleConstants[" },
        _ => Array.Empty<string>(),
    };

    static string B(bool v) => v ? "1" : "0";

    /// <summary>The flat Windhawk settings for one mod (keys as the mod declares them).</summary>
    public static Dictionary<string, string> SettingsFor(Profile p, string id)
    {
        var s = new Dictionary<string, string>(StringComparer.Ordinal);
        switch (id)
        {
            case "taskbar-background-helper":
            {
                var t = p.TaskbarLook;
                var (r, g, bl) = ParseColor(t.Color);
                bool clear = t.Style == "clear";
                // "Clear" is the color style at alpha 0; the mod's transparency value is the color's alpha.
                s["backgroundStyle"] = t.Style switch { "blur" => "blur", "acrylic" => "acrylicBlur", _ => "color" };
                s["color.red"] = r.ToString();
                s["color.green"] = g.ToString();
                s["color.blue"] = bl.ToString();
                s["color.accentColor"] = B(t.AccentColor && !clear);
                s["color.transparency"] = clear ? "0" : ((int)Math.Round(t.Opacity * 2.55)).ToString();
                s["onlyWhenMaximized"] = B(t.OnlyWhenMaximized);
                s["styleForDarkMode.use"] = "0";
                break;
            }
            case "windows-11-notification-center-styler":
                s["theme"] = p.NotificationCenter.Theme;
                break;
            case "windows-11-start-menu-styler":
                s["theme"] = p.StartMenu.Theme;
                s["disableNewStartMenuLayout"] = p.StartMenu.Layout;
                break;
            case "taskbar-icon-size":
                s["TaskbarHeight"] = p.Resize.Height.ToString();
                s["IconSize"] = p.Resize.IconSize.ToString();
                s["TaskbarButtonWidth"] = p.Resize.ButtonWidth.ToString();
                break;
            case "taskbar-dock-animation-plus":
                s["AnimationType"] = p.Hover.Curve.ToString();
                s["MaxScale"] = p.Hover.MaxScale.ToString();
                s["EffectRadius"] = p.Hover.Radius.ToString();
                s["SpacingFactor"] = p.Hover.Spacing.ToString();
                break;
            case "windows-11-taskbar-styler":
            {
                var st = p.Styler;
                s["theme"] = st.Enabled ? st.Theme : "";
                var rules = st.Enabled ? st.Rules.Where(r => r.Target.Trim().Length > 0).ToList() : new List<StyleRule>();
                if (p.TaskbarLook.Enabled) rules.InsertRange(0, ClearBackgroundRules);
                for (int i = 0; i < rules.Count; i++)
                {
                    s[$"controlStyles[{i}].target"] = rules[i].Target.Trim();
                    var styles = rules[i].Styles.Where(x => x.Trim().Length > 0).ToList();
                    for (int j = 0; j < styles.Count; j++) s[$"controlStyles[{i}].styles[{j}]"] = styles[j].Trim();
                }
                var constants = st.Enabled ? st.Constants.Where(c => c.Trim().Length > 0).ToList() : new List<string>();
                for (int i = 0; i < constants.Count; i++) s[$"styleConstants[{i}]"] = constants[i].Trim();
                break;
            }
            case "windows-11-file-explorer-styler":
                s["theme"] = p.Explorer.ExplorerTheme;
                s["backgroundTranslucentEffect"] = p.Explorer.Backdrop;
                break;
            case "windows-11-settings-styler":
                s["theme"] = p.Explorer.SettingsTheme;
                break;
        }
        return s;
    }

    /// <summary>Makes the taskbar's XAML background transparent (Background Helper paints behind it).</summary>
    static readonly StyleRule[] ClearBackgroundRules =
    {
        new() { Target = "Taskbar.TaskbarBackground > Grid > Rectangle#BackgroundFill", Styles = { "Fill=Transparent" } },
        new() { Target = "Taskbar.TaskbarBackground > Grid > Rectangle#BackgroundStroke", Styles = { "Fill=Transparent" } },
    };

    static (int r, int g, int b) ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return ((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        return (32, 32, 64);
    }

    // ---------- safeguards ----------

    /// <summary>The user confirmed the shell mods on this Windows build (after an update, or the first time).</summary>
    public void ConfirmBuild() => _store.Update(c => { c.ShellConfirmedBuild = WinInfo.Build; c.ShellSafeMode = false; c.ShellSafeModeReason = null; });

    public void LeaveSafeMode() => ConfirmBuild();

    /// <summary>Explorer restarted (TaskbarCreated). Two restarts in a minute with mods on means a crash loop.</summary>
    public bool OnExplorerRestarted()
    {
        if (!_anyOn) return false;
        _explorerRestarts.Add(DateTime.Now);
        _explorerRestarts.RemoveAll(t => t < DateTime.Now.AddMinutes(-1));
        if (_explorerRestarts.Count < 2) return false;
        Log.Error($"Explorer restarted {_explorerRestarts.Count} times in a minute with Windhawk mods on; entering safe mode");
        EmergencyOff();
        _store.Update(c => { c.ShellSafeMode = true; c.ShellSafeModeReason = $"Explorer restarted {_explorerRestarts.Count} times in a minute on {DateTime.Now:g}"; });
        _store.SaveNow();
        _explorerRestarts.Clear();
        return true;
    }

    /// <summary>Every managed mod off and Windhawk stopped: stock shell, whatever the config says.</summary>
    public void EmergencyOff()
    {
        try
        {
            foreach (var m in Mods) _wh.Apply(m.Id, false, null);
        }
        catch (Exception ex) { Log.Error("disabling mods failed", ex); }
        _wh.Exit();
        _anyOn = false;
    }

    public object ModStatus() => Mods.Select(m => new
    {
        id = m.Id,
        name = m.Name,
        feature = m.Feature,
        installed = _wh.IsModInstalled(m.Id),
        enabled = _wh.IsModEnabled(m.Id),
        version = _wh.ModVersion(m.Id),
    });
}
