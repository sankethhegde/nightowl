using System.Diagnostics;
using static NightOwl.Native;

namespace NightOwl;

public record MonitorInfo(string Id, IntPtr Handle, RECT Bounds, RECT Work, bool Primary, int Index);

public class MonitorState
{
    public required MonitorInfo Monitor { get; init; }
    /// <summary>Some visible window covers the whole monitor (full screen or borderless).</summary>
    public bool HasFullscreen { get; set; }
    /// <summary>Some visible window on this monitor is maximised.</summary>
    public bool HasMaximized { get; set; }
}

public class DesktopSnapshot
{
    public List<MonitorState> Monitors { get; } = new();
    public IntPtr Foreground { get; set; }
    public string ForegroundExe { get; set; } = "";
    /// <summary>Monitor the foreground window fills completely, if any (F2.1).</summary>
    public IntPtr ForegroundFullscreenMonitor { get; set; }
    public IntPtr ForegroundMonitor { get; set; }
    public bool ForegroundIsShell { get; set; }
    /// <summary>The foreground window is an app window in the maximised state.</summary>
    public bool ForegroundMaximized { get; set; }
    /// <summary>The desktop itself (or nothing) is in front.</summary>
    public bool ForegroundIsDesktop { get; set; }
}

/// <summary>
/// Watches foreground and window-placement changes (WinEvent hooks, plus a slow safety poll)
/// and works out, per monitor, whether something full screen or maximised covers it.
/// Feeds both F2 (auto-hide taskbar) and F6.4 (pause wallpaper when covered).
/// </summary>
public sealed class DesktopWatcher : IDisposable
{
    static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow",
        "Shell_InputSwitchTopLevelWindow", "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd",
        "XamlWindow", "ControlCenterWindow", "LauncherTipWnd", "#32768", "SysShadow", "tooltips_class32",
    };

    public event Action<DesktopSnapshot>? Changed;
    public DesktopSnapshot Current { get; private set; } = new();

    readonly List<IntPtr> _hooks = new();
    readonly WinEventProc _proc;
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = 40 };
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 1000 };
    readonly uint _ownPid = (uint)Environment.ProcessId;
    readonly HashSet<uint> _ignorePids = new();
    readonly Dictionary<uint, string> _exeCache = new();
    List<MonitorInfo> _monitors = new();

    public DesktopWatcher()
    {
        _proc = OnWinEvent;
        _debounce.Tick += (_, _) => { _debounce.Stop(); Evaluate(); };
        _poll.Tick += (_, _) => Evaluate();
        RefreshMonitors();
    }

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    /// <summary>Processes whose windows never count as covering (our wallpaper host).</summary>
    public void IgnoreProcess(int pid) { lock (_ignorePids) _ignorePids.Add((uint)pid); }

    public void Start()
    {
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        Hook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND);
        Hook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE);
        Hook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE);
        Hook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED);
        _poll.Start();
        Evaluate();
    }

    void Hook(uint min, uint max)
    {
        var h = SetWinEventHook(min, max, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (h != IntPtr.Zero) _hooks.Add(h); else Log.Warn($"SetWinEventHook {min:x} failed");
    }

    void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero) return;
        if (evt != EVENT_SYSTEM_FOREGROUND && GetAncestor(hwnd, GA_ROOT) != hwnd) return;
        if (evt == EVENT_SYSTEM_FOREGROUND) { _debounce.Stop(); Evaluate(); return; }
        if (!_debounce.Enabled) _debounce.Start();
    }

    public void RefreshMonitors()
    {
        var list = new List<MonitorInfo>();
        int i = 0;
        foreach (var s in Screen.AllScreens.OrderByDescending(s => s.Primary).ThenBy(s => s.Bounds.X))
        {
            var pt = new POINT { X = s.Bounds.X + s.Bounds.Width / 2, Y = s.Bounds.Y + s.Bounds.Height / 2 };
            var h = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            GetMonitorInfo(h, ref mi);
            list.Add(new MonitorInfo(s.DeviceName, h, mi.rcMonitor, mi.rcWork, s.Primary, ++i));
        }
        _monitors = list;
    }

    public void Evaluate()
    {
        try
        {
            var snap = Scan();
            Current = snap;
            Changed?.Invoke(snap);
        }
        catch (Exception ex) { Log.Error("desktop scan failed", ex); }
    }

    DesktopSnapshot Scan()
    {
        var snap = new DesktopSnapshot();
        var byHandle = new Dictionary<IntPtr, MonitorState>();
        foreach (var m in _monitors)
        {
            var st = new MonitorState { Monitor = m };
            snap.Monitors.Add(st);
            byHandle[m.Handle] = st;
        }

        EnumWindows((hwnd, _) =>
        {
            if (!IsCandidate(hwnd)) return true;
            var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONULL);
            if (!byHandle.TryGetValue(mon, out var st)) return true;
            if (IsZoomed(hwnd)) st.HasMaximized = true;
            if (CoversMonitor(hwnd, st.Monitor.Bounds)) st.HasFullscreen = true;
            return true;
        }, IntPtr.Zero);

        var fg = GetForegroundWindow();
        snap.Foreground = fg;
        snap.ForegroundIsDesktop = fg == IntPtr.Zero || ClassOf(fg) is "Progman" or "WorkerW";
        if (fg != IntPtr.Zero)
        {
            var mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            snap.ForegroundMonitor = mon;
            snap.ForegroundIsShell = ShellClasses.Contains(ClassOf(fg));
            snap.ForegroundExe = ExeOf(fg);
            snap.ForegroundMaximized = !snap.ForegroundIsShell && IsZoomed(fg) && IsCandidate(fg);
            if (!snap.ForegroundIsShell && byHandle.TryGetValue(mon, out var st) && CoversMonitor(fg, st.Monitor.Bounds))
                snap.ForegroundFullscreenMonitor = mon;
        }
        return snap;
    }

    bool IsCandidate(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsCloaked(hwnd)) return false;
        var pid = ProcessIdOf(hwnd);
        if (pid == _ownPid) return false;
        lock (_ignorePids) if (_ignorePids.Contains(pid)) return false;
        var ex = Style(hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_TRANSPARENT) != 0) return false;
        return !ShellClasses.Contains(ClassOf(hwnd));
    }

    static bool CoversMonitor(IntPtr hwnd, RECT mon)
    {
        GetWindowRect(hwnd, out var r);
        if (r.Contains(mon)) return true;
        var v = VisibleBounds(hwnd);
        return v.Contains(mon);
    }

    string ExeOf(IntPtr hwnd)
    {
        var pid = ProcessIdOf(hwnd);
        if (_exeCache.TryGetValue(pid, out var name)) return name;
        try { name = Process.GetProcessById((int)pid).ProcessName + ".exe"; }
        catch { name = ""; }
        if (_exeCache.Count > 200) _exeCache.Clear();
        _exeCache[pid] = name;
        return name;
    }

    public void Dispose()
    {
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
        _poll.Dispose();
        _debounce.Dispose();
    }
}
