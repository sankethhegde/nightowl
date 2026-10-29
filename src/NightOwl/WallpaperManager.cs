using System.Diagnostics;
using System.Text.Json;
using NightOwl.Wallpaper;

namespace NightOwl;

/// <summary>
/// Core side of F6: decides which surfaces should exist and whether each plays, pauses or hides,
/// then drives the wallpaper process over stdin. Restarts it after a crash (and repaints the
/// static wallpaper meanwhile, F6.9), and rotates the playlist (F6.8).
/// </summary>
public sealed class WallpaperManager : IDisposable
{
    public static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".mkv", ".mov", ".m4v" };
    public static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".gif", ".webp", ".png", ".jpg", ".jpeg", ".bmp", ".avif" };
    public static readonly HashSet<string> HtmlExt = new(StringComparer.OrdinalIgnoreCase) { ".html", ".htm" };

    readonly ConfigStore _store;
    readonly DesktopWatcher _desktop;
    readonly PowerWatcher _power;
    readonly System.Windows.Forms.Timer _playlistTimer = new() { Interval = 15_000 };
    readonly List<DateTime> _crashes = new();
    readonly Random _rng = new();

    Process? _proc;
    string _lastSurfaces = "";
    string _lastStates = "";
    bool _expectedExit;
    bool _gaveUp;
    DateTime _lastRotate = DateTime.Now;
    /// <summary>Files the player could not play (path → media error code), reported by the wallpaper process.</summary>
    readonly Dictionary<string, int> _failed = new(StringComparer.OrdinalIgnoreCase);

    public string Status { get; private set; } = "Off";
    public List<string> Warnings { get; } = new();
    public event Action? StatusChanged;

    public WallpaperManager(ConfigStore store, DesktopWatcher desktop, PowerWatcher power)
    {
        _store = store;
        _desktop = desktop;
        _power = power;
        _playlistTimer.Tick += (_, _) => PlaylistTick();
        _playlistTimer.Start();
    }

    public static string TypeOf(string path)
    {
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "url";
        var ext = Path.GetExtension(path);
        if (VideoExt.Contains(ext)) return "video";
        if (ImageExt.Contains(ext)) return "image";
        if (HtmlExt.Contains(ext)) return "html";
        return "";
    }

    /// <summary>Recompute everything from config + desktop + power state.</summary>
    bool _suppressed;

    public void Refresh(bool suppressed)
    {
        _suppressed = suppressed;
        var cfg = _store.Config;
        var wp = cfg.Active.Wallpaper;
        Warnings.Clear();

        var surfaces = suppressed || !wp.Enabled || cfg.PausedAll ? new List<SurfaceSpec>() : BuildSurfaces(cfg, wp);
        if (surfaces.Count == 0)
        {
            StopProcess();
            SetStatus(suppressed ? "Off (high contrast)" : cfg.PausedAll ? "Paused" : !wp.Enabled ? "Off" : "No wallpaper assigned");
            return;
        }
        if (_gaveUp) { SetStatus("Stopped after repeated crashes; toggle the feature to retry"); return; }

        foreach (var s in surfaces.Where(s => _failed.ContainsKey(s.Path)))
            Warnings.Add(FailureText(cfg.Library.FirstOrDefault(i => i.Path == s.Path), s.Path));

        EnsureProcess();
        var surfacesJson = JsonSerializer.Serialize(surfaces, ConfigStore.Wire);
        if (surfacesJson != _lastSurfaces)
        {
            Send(new WallpaperCommand { Cmd = "set", Surfaces = surfaces });
            _lastSurfaces = surfacesJson;
            _lastStates = "";
        }

        var states = ComputeStates(wp, surfaces);
        var statesJson = JsonSerializer.Serialize(states);
        if (statesJson != _lastStates)
        {
            Send(new WallpaperCommand { Cmd = "state", States = states });
            _lastStates = statesJson;
            var covered = string.Join(" ", _desktop.Current.Monitors.Select(m => $"{m.Monitor.Index}:{(m.HasFullscreen ? "F" : "")}{(m.HasMaximized ? "M" : "")}"));
            Log.Info($"wallpaper states {statesJson} (battery={_power.OnBattery}, saver={_power.BatterySaver}, covered {covered})");
        }
        int playing = states.Values.Count(s => s == "play");
        SetStatus(playing == states.Count ? $"Playing on {surfaces.Count} surface(s)"
            : playing == 0 ? "Paused (" + PauseReason(wp) + ")" : $"Playing on {playing} of {states.Count}");
    }

    List<SurfaceSpec> BuildSurfaces(AppConfig cfg, WallpaperSettings wp)
    {
        var list = new List<SurfaceSpec>();
        LibraryItem? Find(string? id) => id == null ? null : cfg.Library.FirstOrDefault(i => i.Id == id);
        bool Usable(LibraryItem item)
        {
            if (item.Type == "url" || File.Exists(item.Path)) return true;
            Warnings.Add($"Wallpaper file missing: {item.Path}");
            return false;
        }

        if (wp.Layout == "span")
        {
            var item = Find(wp.SpanItem);
            if (item != null && Usable(item))
            {
                var vs = SystemInformation.VirtualScreen;
                list.Add(Spec("span", vs.X, vs.Y, vs.Width, vs.Height, item, wp));
            }
            return list;
        }
        foreach (var m in _desktop.Monitors)
        {
            if (!wp.Monitors.TryGetValue(m.Id, out var id)) continue;
            var item = Find(id);
            if (item == null || !Usable(item)) continue;
            var b = m.Bounds;
            list.Add(Spec(m.Id, b.Left, b.Top, b.Width, b.Height, item, wp));
        }
        return list;
    }

    static SurfaceSpec Spec(string id, int x, int y, int w, int h, LibraryItem item, WallpaperSettings wp) =>
        new(id, x, y, w, h, item.Path, item.Type, wp.Fit, wp.Muted, wp.Volume, wp.Interactive);

    Dictionary<string, string> ComputeStates(WallpaperSettings wp, List<SurfaceSpec> surfaces)
    {
        bool powerPause = (wp.PauseOnBattery && _power.OnBattery) || (wp.PauseOnBatterySaver && _power.BatterySaver);
        var snap = _desktop.Current;
        bool Covered(MonitorState st) => (wp.PauseWhenFullscreen && st.HasFullscreen) || (wp.PauseWhenMaximized && st.HasMaximized);

        var states = new Dictionary<string, string>();
        foreach (var s in surfaces)
        {
            bool covered = s.Id == "span"
                ? snap.Monitors.Count > 0 && snap.Monitors.All(Covered)
                : snap.Monitors.FirstOrDefault(m => m.Monitor.Id == s.Id) is { } st && Covered(st);
            states[s.Id] = covered ? "hide" : powerPause ? "pause" : "play";
        }
        return states;
    }

    string PauseReason(WallpaperSettings wp)
    {
        if (wp.PauseOnBatterySaver && _power.BatterySaver) return "battery saver";
        if (wp.PauseOnBattery && _power.OnBattery) return "on battery";
        return "covered by a window";
    }

    void SetStatus(string s)
    {
        if (s == Status) return;
        Status = s;
        StatusChanged?.Invoke();
    }

    void EnsureProcess()
    {
        if (_proc is { HasExited: false }) return;
        var psi = new ProcessStartInfo(Environment.ProcessPath!, $"--wallpaper --parent {Environment.ProcessId}")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        _proc = Process.Start(psi)!;
        _proc.EnableRaisingEvents = true;
        _proc.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) UiInvoke(() => OnEvent(line)); };
        _proc.BeginOutputReadLine();
        var started = _proc;
        _proc.Exited += (_, _) => UiInvoke(() => OnExited(started));
        _desktop.IgnoreProcess(_proc.Id);
        _lastSurfaces = _lastStates = "";
        _expectedExit = false;
        Log.Info($"wallpaper process started (pid {_proc.Id})");
    }

    /// <summary>Marshal back to the UI thread; set by the tray app.</summary>
    public Action<Action> UiInvoke { get; set; } = a => a();

    void OnExited(Process p)
    {
        if (p != _proc) return;
        var code = SafeExitCode(p);
        _proc = null;
        _lastSurfaces = _lastStates = "";
        if (_expectedExit) return;
        Log.Warn($"wallpaper process exited unexpectedly (code {code})");
        Native.RepaintStaticWallpaper();
        _crashes.Add(DateTime.Now);
        _crashes.RemoveAll(t => t < DateTime.Now.AddMinutes(-1));
        if (_crashes.Count >= 3) { _gaveUp = true; Log.Error("wallpaper crashed 3 times in a minute; giving up"); }
        Refresh(_suppressed);
    }

    /// <summary>Events from the wallpaper process (one JSON object per stdout line).</summary>
    void OnEvent(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.GetProperty("event").GetString() != "error") return;
            var path = root.GetProperty("path").GetString() ?? "";
            var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
            if (_failed.TryGetValue(path, out var prev) && prev == code) return;
            _failed[path] = code;
            Log.Warn($"player could not play {path} (media error {code})");
            Refresh(_suppressed);
            StatusChanged?.Invoke();
        }
        catch (Exception ex) { Log.Warn($"bad event from wallpaper process: {ex.Message}"); }
    }

    static string FailureText(LibraryItem? item, string path)
    {
        var name = item?.Name ?? Path.GetFileName(path);
        return item?.Codec == "hevc"
            ? $"“{name}” is HEVC (H.265) video, which this PC can't decode. Install HEVC Video Extensions from the Microsoft Store, or use an H.264, VP9 or AV1 copy."
            : $"“{name}” couldn't be played: its format or codec isn't supported.";
    }

    /// <summary>Forget playback failures, e.g. after a codec was installed.</summary>
    public void ClearFailures() => _failed.Clear();

    static int SafeExitCode(Process p) { try { return p.ExitCode; } catch { return -1; } }

    void Send(WallpaperCommand cmd)
    {
        try
        {
            _proc?.StandardInput.WriteLine(JsonSerializer.Serialize(cmd, ConfigStore.Wire));
            _proc?.StandardInput.Flush();
        }
        catch (Exception ex) { Log.Error("sending to wallpaper process failed", ex); }
    }

    public void StopProcess()
    {
        if (_proc == null) return;
        _expectedExit = true;
        var p = _proc;
        try
        {
            Send(new WallpaperCommand { Cmd = "quit" });
            p.StandardInput.Close();
            if (!p.WaitForExit(3000)) p.Kill();
        }
        catch { }
        _proc = null;
        _lastSurfaces = _lastStates = "";
        Native.RepaintStaticWallpaper();
        Log.Info("wallpaper process stopped");
    }

    /// <summary>Explorer restarted: our windows died with the desktop, so start over.</summary>
    public void Restart()
    {
        StopProcess();
        _gaveUp = false;
        _crashes.Clear();
    }

    public void ResetCrashGuard() { _gaveUp = false; _crashes.Clear(); }

    // ---------- playlist (F6.8) ----------

    void PlaylistTick()
    {
        var wp = _store.Config.Active.Wallpaper;
        if (!wp.Enabled || !wp.Playlist.Enabled || _store.Config.PausedAll) return;
        if (wp.Playlist.Mode == "timeOfDay")
        {
            var slot = CurrentSlot(wp.Playlist);
            if (slot == null) return;
            if (_store.Config.Library.All(i => i.Id != slot.ItemId)) return;
            bool differs = wp.Layout == "span" ? wp.SpanItem != slot.ItemId
                : _desktop.Monitors.Any(m => !wp.Monitors.TryGetValue(m.Id, out var cur) || cur != slot.ItemId);
            if (differs) _store.Update(c => AssignAll(c.Active.Wallpaper, slot.ItemId));
        }
        else if (DateTime.Now - _lastRotate >= TimeSpan.FromMinutes(wp.Playlist.IntervalMinutes))
        {
            Next();
        }
    }

    static ScheduleSlot? CurrentSlot(PlaylistSettings pl)
    {
        var now = DateTime.Now.TimeOfDay;
        var slots = pl.Schedule
            .Select(s => (s, ok: TimeSpan.TryParse(s.Time, out var t), t))
            .Where(x => x.ok && x.s.ItemId.Length > 0)
            .OrderBy(x => x.t).ToList();
        if (slots.Count == 0) return null;
        var current = slots.LastOrDefault(x => x.t <= now);
        return (current.s ?? slots[^1].s);
    }

    void AssignAll(WallpaperSettings wp, string itemId)
    {
        if (wp.Layout == "span") wp.SpanItem = itemId;
        else foreach (var m in _desktop.Monitors) wp.Monitors[m.Id] = itemId;
    }

    /// <summary>Advance every surface to the next wallpaper in the playlist (or the whole library).</summary>
    public void Next()
    {
        _lastRotate = DateTime.Now;
        var cfg = _store.Config;
        var wp = cfg.Active.Wallpaper;
        var pool = (wp.Playlist.Enabled && wp.Playlist.Items.Count > 0
                ? wp.Playlist.Items.Where(id => cfg.Library.Any(i => i.Id == id)).ToList()
                : cfg.Library.Select(i => i.Id).ToList());
        if (pool.Count == 0) return;

        string Pick(string? current)
        {
            if (pool.Count == 1) return pool[0];
            if (wp.Playlist.Shuffle)
            {
                var others = pool.Where(p => p != current).ToList();
                return others[_rng.Next(others.Count)];
            }
            var idx = current == null ? -1 : pool.IndexOf(current);
            return pool[(idx + 1) % pool.Count];
        }

        _store.Update(c =>
        {
            var w = c.Active.Wallpaper;
            if (w.Layout == "span") w.SpanItem = Pick(w.SpanItem);
            else
            {
                var monitors = _desktop.Monitors.ToList();
                foreach (var m in monitors)
                    w.Monitors[m.Id] = Pick(w.Monitors.TryGetValue(m.Id, out var cur) ? cur : null);
            }
        });
        Log.Info("next wallpaper");
    }

    public void Dispose()
    {
        _playlistTimer.Dispose();
        StopProcess();
    }
}
