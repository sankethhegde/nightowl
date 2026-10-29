using System.Text.Json;
using NightOwl.Shell;
using static NightOwl.Native;

namespace NightOwl;

/// <summary>
/// The tray app and core service: owns config, the desktop watcher, the taskbar hider and the
/// wallpaper manager, and applies every config change live.
/// </summary>
internal sealed class CoreApp : ApplicationContext
{
    public static readonly Icon AppIcon = LoadIcon();
    public static string Version => typeof(CoreApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public ConfigStore Store { get; }
    public WallpaperManager Wallpaper { get; }
    public ShellManager Shell { get; }
    readonly DesktopWatcher _desktop;
    readonly TaskbarHider _hider = new();
    readonly PowerWatcher _power = new();
    readonly NotifyIcon _tray;
    readonly MessageWindow _msgWindow;
    readonly Control _invoker = new();
    SettingsWindow? _settings;
    string _lastPushed = "";
    bool _exiting;

    public CoreApp(bool startup)
    {
        _invoker.CreateControl();
        Store = new ConfigStore();
        _desktop = new DesktopWatcher();
        Wallpaper = new WallpaperManager(Store, _desktop, _power) { UiInvoke = a => _invoker.BeginInvoke(a) };
        Shell = new ShellManager(Store);

        _tray = new NotifyIcon { Icon = AppIcon, Text = "nightOwl", Visible = true };
        _tray.DoubleClick += (_, _) => ShowSettings();
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        BuildMenu();

        _msgWindow = new MessageWindow(this);

        Store.Changed += () => { SyncStartup(); Apply(); };
        _desktop.Changed += snap => { _hider.Apply(snap); Wallpaper.Refresh(Suppressed); PushIfChanged(); };
        _power.Changed += () => { Wallpaper.Refresh(Suppressed); PushIfChanged(); };
        Wallpaper.StatusChanged += PushIfChanged;

        Log.Info($"nightOwl {Version} starting on Windows {WinInfo.DisplayVersion} build {WinInfo.Build}");
        if (!WinInfo.VerifiedBuilds.Contains(WinInfo.Build)) Log.Warn($"build {WinInfo.Build} not verified");

        // Re-check every video: a file may have been converted (e.g. HEVC → H.264) since it was added.
        var codecs = Store.Config.Library.ToDictionary(i => i.Id, i => i.Type == "video" ? MediaProbe.VideoCodec(i.Path) : "");
        if (Store.Config.Library.Any(i => i.Codec != codecs[i.Id]))
            Store.Update(c => { foreach (var i in c.Library) if (codecs.TryGetValue(i.Id, out var codec)) i.Codec = codec; });

        if (!Store.Config.FirstRunDone)
        {
            Store.Update(c => { c.FirstRunDone = true; c.StartWithWindows = true; });
            ShowSettings();
        }
        SyncStartup();
        _desktop.Start();
        Apply();
        if (!startup && Store.Config.FirstRunDone && _settings == null && Environment.GetCommandLineArgs().Contains("--settings")) ShowSettings();
    }

    static Icon LoadIcon()
    {
        try { return new Icon(Path.Combine(Paths.AppDir, "assets", "nightowl.ico")); }
        catch { return SystemIcons.Application; }
    }

    bool Suppressed => WinInfo.HighContrast;

    void Apply()
    {
        var cfg = Store.Config;
        bool hideActive = cfg.Active.AutoHide.Enabled && !cfg.PausedAll && !Suppressed;
        _hider.Configure(cfg.Active.AutoHide, hideActive);
        _hider.Apply(_desktop.Current);
        Wallpaper.Refresh(Suppressed);
        Shell.Apply(Suppressed);
        _tray.Text = cfg.PausedAll ? "nightOwl (paused)" : $"nightOwl — {cfg.ActiveProfile}";
        PushState();
    }

    void SyncStartup()
    {
        bool want = Store.Config.StartWithWindows;
        if (want != Startup.IsSet()) Startup.Set(want);
    }

    // ---------- tray ----------

    void BuildMenu()
    {
        var cfg = Store.Config;
        var m = _tray.ContextMenuStrip!;
        m.Items.Clear();
        var open = new ToolStripMenuItem("Open settings", null, (_, _) => ShowSettings()) { Font = new Font(m.Font, FontStyle.Bold) };
        m.Items.Add(open);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("Pause all effects", null, (_, _) => SetPausedAll(!Store.Config.PausedAll)) { Checked = cfg.PausedAll });
        m.Items.Add(new ToolStripMenuItem("Next wallpaper", null, (_, _) => Wallpaper.Next()) { Enabled = cfg.Library.Count > 0 });
        var profiles = new ToolStripMenuItem("Profile");
        foreach (var name in cfg.Profiles.Keys)
        {
            var n = name;
            profiles.DropDownItems.Add(new ToolStripMenuItem(n, null, (_, _) => ProfileAction("switch", n, null)) { Checked = n == cfg.ActiveProfile });
        }
        m.Items.Add(profiles);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("Reset to stock", null, (_, _) => ResetToStock()) { ShortcutKeyDisplayString = "Ctrl+Alt+Shift+R" });
        m.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => Quit()));
    }

    public void ShowSettings()
    {
        if (_settings is { IsDisposed: false })
        {
            if (_settings.WindowState == FormWindowState.Minimized) _settings.WindowState = FormWindowState.Normal;
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(this);
        _settings.FormClosed += (_, _) => { _settings = null; GC.Collect(); };
        _settings.Show();
        _settings.Activate();
    }

    public void InvokeOnUi(Action a) { try { _invoker.BeginInvoke(a); } catch { } }

    // ---------- actions ----------

    public void SetPausedAll(bool value) => Store.Update(c => c.PausedAll = value);

    public void SetStartup(bool value) => Store.Update(c => c.StartWithWindows = value);

    /// <summary>UI.6 — everything back to stock Windows, from the hotkey, tray or settings.</summary>
    public void ResetToStock()
    {
        Log.Info("reset to stock");
        _hider.ShowAll();
        Wallpaper.StopProcess();
        Wallpaper.ResetCrashGuard();
        Shell.EmergencyOff();
        Store.Update(c =>
        {
            c.PausedAll = false;
            c.Active.AutoHide.Enabled = false;
            c.Active.Wallpaper.Enabled = false;
            var p = c.Active;
            p.TaskbarLook.Enabled = p.NotificationCenter.Enabled = p.Resize.Enabled = p.Hover.Enabled = false;
            p.Styler.Enabled = p.Explorer.Enabled = p.StartMenu.Enabled = false;
        });
        Store.SaveNow();
        _tray.ShowBalloonTip(3000, "nightOwl", "Everything is back to stock Windows. Turn features on again in settings.", ToolTipIcon.Info);
    }

    public void AddToLibrary(IEnumerable<string> paths)
    {
        var added = new List<LibraryItem>();
        var skipped = new List<string>();
        foreach (var raw in paths)
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            var type = WallpaperManager.TypeOf(p);
            if (type == "") { skipped.Add(Path.GetFileName(p)); continue; }
            if (Store.Config.Library.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            string name = type == "url" ? (Uri.TryCreate(p, UriKind.Absolute, out var u) ? u.Host : p) : Path.GetFileNameWithoutExtension(p);
            added.Add(new LibraryItem { Path = p, Type = type, Name = name, Codec = type == "video" ? MediaProbe.VideoCodec(p) : "" });
        }
        if (added.Count > 0)
        {
            Store.Update(c =>
            {
                c.Library.AddRange(added);
                var wp = c.Active.Wallpaper;
                // First wallpaper ever: put it on every monitor straight away.
                if (wp.Monitors.Count == 0 && wp.SpanItem == null)
                {
                    foreach (var m in _desktop.Monitors) wp.Monitors[m.Id] = added[0].Id;
                    wp.SpanItem = added[0].Id;
                }
            });
            Log.Info($"library: added {added.Count}");
        }
        if (skipped.Count > 0) _tray.ShowBalloonTip(3000, "nightOwl", "Not a supported wallpaper: " + string.Join(", ", skipped), ToolTipIcon.Warning);
    }

    public void RemoveFromLibrary(string id)
    {
        Store.Update(c =>
        {
            c.Library.RemoveAll(i => i.Id == id);
            foreach (var p in c.Profiles.Values)
            {
                var wp = p.Wallpaper;
                foreach (var k in wp.Monitors.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList()) wp.Monitors.Remove(k);
                if (wp.SpanItem == id) wp.SpanItem = null;
                wp.Playlist.Items.Remove(id);
                wp.Playlist.Schedule.RemoveAll(s => s.ItemId == id);
            }
        });
        Thumbnails.Delete(id);
    }

    public void ProfileAction(string action, string? name, string? newName)
    {
        var cfg = Store.Config;
        newName = newName?.Trim();
        switch (action)
        {
            case "switch" when name != null && cfg.Profiles.ContainsKey(name):
                Store.Update(c => c.ActiveProfile = name);
                Log.Info($"profile -> {name}");
                break;
            case "duplicate" when name != null && !string.IsNullOrEmpty(newName) && cfg.Profiles.ContainsKey(name) && !cfg.Profiles.ContainsKey(newName):
                var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(cfg.Profiles[name], ConfigStore.Json), ConfigStore.Json)!;
                Store.Update(c => c.Profiles[newName] = copy);
                break;
            case "rename" when name != null && !string.IsNullOrEmpty(newName) && cfg.Profiles.ContainsKey(name) && !cfg.Profiles.ContainsKey(newName):
                Store.Update(c =>
                {
                    c.Profiles = c.Profiles.ToDictionary(kv => kv.Key == name ? newName : kv.Key, kv => kv.Value);
                    if (c.ActiveProfile == name) c.ActiveProfile = newName;
                });
                break;
            case "delete" when name != null && cfg.Profiles.Count > 1 && cfg.Profiles.ContainsKey(name):
                Store.Update(c =>
                {
                    c.Profiles.Remove(name);
                    if (c.ActiveProfile == name) c.ActiveProfile = c.Profiles.Keys.First();
                });
                break;
        }
    }

    // ---------- state for the settings UI ----------

    public object BuildState()
    {
        var cfg = Store.Config;
        var warnings = new List<string>(Wallpaper.Warnings);
        warnings.AddRange(Shell.Warnings);
        if (Sandbox.Package != null)
            warnings.Insert(0, "nightOwl was started from inside another app, so Windows keeps its settings in that app's private storage. Quit and start nightOwl from its folder or the Start menu.");
        if (Suppressed) warnings.Add("High Contrast is on, so every effect is switched off (NF.11).");
        if (!WinInfo.TransparencyOn) warnings.Add("Windows transparency effects are off (Settings › Personalization › Colors). Later translucency features need them.");
        if (!WinInfo.VerifiedBuilds.Contains(WinInfo.Build)) warnings.Add($"Windows build {WinInfo.Build} has not been verified with nightOwl yet.");
        foreach (var t in WinInfo.ConflictingTools()) warnings.Add($"{t} is running and may overlap with nightOwl.");

        var hideOn = cfg.Active.AutoHide.Enabled && !cfg.PausedAll && !Suppressed;
        return new
        {
            type = "state",
            config = cfg,
            monitors = _desktop.Monitors.Select(m => new
            {
                id = m.Id, index = m.Index, primary = m.Primary,
                x = m.Bounds.Left, y = m.Bounds.Top, w = m.Bounds.Width, h = m.Bounds.Height,
            }),
            status = new
            {
                version = Version,
                build = WinInfo.Build,
                displayVersion = WinInfo.DisplayVersion,
                autoHide = !hideOn ? "Off" : _hider.HiddenCount > 0 ? $"Hiding {_hider.HiddenCount} taskbar(s) now" : "Watching for full-screen apps",
                autoHideOn = hideOn,
                wallpaper = Wallpaper.Status,
                wallpaperOn = cfg.Active.Wallpaper.Enabled && !cfg.PausedAll && !Suppressed,
                shell = Shell.Status,
                shellOn = Shell.IsActive,
                windhawkInstalled = Shell.Windhawk.IsInstalled,
                windhawkRunning = Shell.Windhawk.IsRunning,
                buildUnconfirmed = Shell.BuildUnconfirmed,
                mods = Shell.ModStatus(),
                onBattery = _power.OnBattery,
                batterySaver = _power.BatterySaver,
                hasBattery = _power.HasBattery,
                startupSet = Startup.IsSet(),
                warnings,
                logs = Paths.Logs,
                configPath = Paths.Config,
            },
        };
    }

    void PushState()
    {
        if (_settings is not { IsDisposed: false }) return;
        _lastPushed = StatusKey();
        _settings.PushState();
    }

    string StatusKey() => $"{Wallpaper.Status}|{Shell.Status}|{string.Join(";", Shell.Warnings)}|{string.Join(";", Wallpaper.Warnings)}|{_hider.HiddenCount}|{_power.OnBattery}|{_power.BatterySaver}";

    void PushIfChanged()
    {
        if (StatusKey() != _lastPushed) PushState();
    }

    // ---------- system events ----------

    internal void OnHotkey() => ResetToStock();

    internal void OnExplorerRestarted()
    {
        Log.Warn("Explorer restarted (TaskbarCreated); re-applying");
        _hider.ExplorerRestarted();
        if (Shell.OnExplorerRestarted())
            _tray.ShowBalloonTip(5000, "nightOwl", "Explorer restarted twice in a minute, so taskbar and Explorer styling were switched off to keep Windows stable.", ToolTipIcon.Warning);
        Wallpaper.Restart();
        var t = new System.Windows.Forms.Timer { Interval = 1500 };
        t.Tick += (_, _) => { t.Dispose(); _desktop.RefreshMonitors(); _desktop.Evaluate(); Apply(); };
        t.Start();
    }

    internal void OnDisplayChanged()
    {
        var t = new System.Windows.Forms.Timer { Interval = 800 };
        t.Tick += (_, _) =>
        {
            t.Dispose();
            _desktop.RefreshMonitors();
            Log.Info($"displays changed: {string.Join(", ", _desktop.Monitors.Select(m => $"{m.Id} {m.Bounds}"))}");
            _desktop.Evaluate();
            Apply();
        };
        t.Start();
    }

    internal void OnSettingChanged() => Apply();

    public void Quit()
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info("quitting");
        _settings?.Close();
        _hider.Dispose();
        Wallpaper.Dispose();
        Shell.Windhawk.Exit();
        _desktop.Dispose();
        _power.Dispose();
        Store.SaveNow();
        _msgWindow.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }

    /// <summary>Emergency cleanup when the core is crashing: never leave the shell broken (NF.6).</summary>
    public void EmergencyRestore()
    {
        try { _hider.ShowAll(); } catch { }
        try { Wallpaper.StopProcess(); } catch { }
    }

    /// <summary>Hidden top-level window: receives the reset hotkey and shell/display broadcasts.</summary>
    sealed class MessageWindow : NativeWindow, IDisposable
    {
        const int HotkeyId = 0x4E4F;
        readonly CoreApp _app;
        readonly int _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        public MessageWindow(CoreApp app)
        {
            _app = app;
            CreateHandle(new CreateParams { Caption = "nightOwl core", ExStyle = (int)WS_EX_TOOLWINDOW });
            if (!RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.R))
                Log.Warn("reset hotkey Ctrl+Alt+Shift+R is taken by another app");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam == HotkeyId) _app.OnHotkey();
            else if (m.Msg == _taskbarCreated) _app.OnExplorerRestarted();
            else if (m.Msg == WM_DISPLAYCHANGE) _app.OnDisplayChanged();
            else if (m.Msg == WM_SETTINGCHANGE && Marshal_GetString(m.LParam) is "ImmersiveColorSet" or "WindowsThemeElement") _app.OnSettingChanged();
            base.WndProc(ref m);
        }

        static string? Marshal_GetString(IntPtr p) => p == IntPtr.Zero ? null : System.Runtime.InteropServices.Marshal.PtrToStringUni(p);

        public void Dispose()
        {
            UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }
    }
}
