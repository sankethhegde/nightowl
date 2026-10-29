using static NightOwl.Native;

namespace NightOwl;

/// <summary>
/// F2: hides a monitor's taskbar while the foreground app fills that monitor, and brings it back
/// as soon as the app leaves full screen or loses focus. No injection: it only shows/hides
/// Explorer's Shell_TrayWnd / Shell_SecondaryTrayWnd windows.
/// </summary>
public sealed class TaskbarHider : IDisposable
{
    readonly HashSet<IntPtr> _hidden = new();
    readonly System.Windows.Forms.Timer _edgeTimer = new() { Interval = 80 };
    IntPtr _revealed;
    AutoHideSettings _settings = new();
    bool _active;
    DesktopSnapshot? _last;

    public TaskbarHider()
    {
        _edgeTimer.Tick += (_, _) => CheckEdge();
        _autoHideTimer.Tick += (_, _) => { _autoHideTimer.Stop(); SetForcedAutoHide(_wantAutoHide); };
        // A previous run that died without its guard may have left Windows' auto-hide switched on.
        RestoreForcedAutoHide();
    }

    // ---- "Also hide when a window is maximised": switches Windows' own auto-hide ----
    // Hiding the bar window alone would leave its strip reserved (maximised windows don't grow into it), so
    // while a maximised window is in front nightOwl turns on Windows' auto-hide instead, and turns it off
    // again afterwards. A marker file records that nightOwl (not the user) switched it on, so the guard,
    // the next start and the uninstaller can always switch it back off.

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
    [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

    static string ForcedMarker => Path.Combine(Paths.Local, "autohide-forced");
    readonly System.Windows.Forms.Timer _autoHideTimer = new() { Interval = 400 };
    // _forced: nightOwl wants auto-hide on (UI thread). _switchedOn: nightOwl actually switched it on, which it
    // skips when the user already had it on in Windows (touched only on the switch chain).
    bool _forced, _wantAutoHide, _switchedOn;
    // Both calls are messages to Explorer, which can stall for many seconds while Explorer is (re)starting,
    // so switches run in order on a background chain instead of blocking the tray app.
    Task _switches = Task.CompletedTask;
    void QueueSwitch(bool on) => _switches = _switches.ContinueWith(_ =>
    {
        try
        {
            if (on && !_switchedOn && !WindowsAutoHide())
            {
                Directory.CreateDirectory(Paths.Local);
                File.WriteAllText(ForcedMarker, "");
                SetWindowsAutoHide(true);
                _switchedOn = true;
                Log.Info("Windows auto-hide on (maximised window)");
            }
            else if (!on && _switchedOn)
            {
                SetWindowsAutoHide(false);
                File.Delete(ForcedMarker);
                _switchedOn = false;
                Log.Info("Windows auto-hide off");
            }
        }
        catch (Exception ex) { Log.Warn("switching Windows auto-hide failed: " + ex.Message); }
    }, TaskScheduler.Default);

    static bool WindowsAutoHide()
    {
        var d = new APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>() };
        return ((int)SHAppBarMessage(ABM_GETSTATE, ref d) & ABS_AUTOHIDE) != 0;
    }

    static void SetWindowsAutoHide(bool on)
    {
        var d = new APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            hWnd = FindWindow("Shell_TrayWnd", null),
            lParam = on ? ABS_AUTOHIDE : ABS_ALWAYSONTOP,
        };
        SHAppBarMessage(ABM_SETSTATE, ref d);
    }

    /// <summary>Waits briefly before switching, so moving between windows doesn't bounce the taskbar.</summary>
    void WantAutoHide(bool want)
    {
        _wantAutoHide = want;
        if (want == _forced) { _autoHideTimer.Stop(); return; }
        if (!_autoHideTimer.Enabled) _autoHideTimer.Start();
    }

    void SetForcedAutoHide(bool on)
    {
        if (on == _forced) return;
        _forced = on;
        QueueSwitch(on);
        UpdateGuard();
    }

    /// <summary>Switch Windows' auto-hide back off if nightOwl left it on (guard, startup, uninstall).</summary>
    public static void RestoreForcedAutoHide()
    {
        if (!File.Exists(ForcedMarker)) return;
        SetWindowsAutoHide(false);
        try { File.Delete(ForcedMarker); } catch { }
    }

    public int HiddenCount => _hidden.Count;

    System.Diagnostics.Process? _guard;

    /// <summary>
    /// While any taskbar is hidden, a tiny guard process (nightOwl.exe --guard) waits for the core to exit
    /// and shows every taskbar again, so a crash or a forced kill can never leave the taskbar missing.
    /// </summary>
    void UpdateGuard()
    {
        bool need = _hidden.Count > 0 || _forced;
        if (need && _guard is not { HasExited: false })
        {
            try
            {
                _guard = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    Environment.ProcessPath!, $"--guard {Environment.ProcessId}") { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception ex) { Log.Warn("starting taskbar guard failed: " + ex.Message); }
        }
        else if (!need && _guard is { HasExited: false })
        {
            try { _guard.Kill(); } catch { }
            _guard = null;
        }
    }

    /// <summary>Guard mode: wait for the core, then make sure every taskbar is visible.</summary>
    public static int RunGuard(int corePid)
    {
        try { System.Diagnostics.Process.GetProcessById(corePid).WaitForExit(); } catch { }
        foreach (var (bar, _) in Taskbars())
            if (!IsWindowVisible(bar)) ShowWindow(bar, SW_SHOWNA);
        RestoreForcedAutoHide();
        return 0;
    }

    public void Configure(AutoHideSettings settings, bool active)
    {
        _settings = settings;
        _active = active;
        if (!_active) ShowAll();
        else if (_last != null) Apply(_last);
    }

    public void Apply(DesktopSnapshot snap)
    {
        _last = snap;
        if (!_active) return;

        var want = new HashSet<IntPtr>();
        var exe = snap.ForegroundExe;
        bool never = Matches(_settings.NeverHide, exe);
        bool always = Matches(_settings.AlwaysHide, exe);
        // Maximised windows go through Windows' auto-hide. Keep the current state while the shell itself (the
        // revealed taskbar, Start, a flyout) is in front, or revealing the bar would switch auto-hide off.
        if (!snap.ForegroundIsShell || snap.ForegroundIsDesktop) WantAutoHide(_settings.WhenMaximized && !never && snap.ForegroundMaximized);
        bool byAutoHide = _forced && snap.ForegroundMaximized;

        if (!never && !snap.ForegroundIsShell && !byAutoHide)
        {
            if (snap.ForegroundFullscreenMonitor != IntPtr.Zero) want.Add(snap.ForegroundFullscreenMonitor);
            else if (always && snap.ForegroundMonitor != IntPtr.Zero) want.Add(snap.ForegroundMonitor);
        }

        foreach (var (bar, mon) in Taskbars())
        {
            bool hide = want.Contains(mon) && mon != _revealed;
            if (hide)
            {
                if (IsWindowVisible(bar)) ShowWindow(bar, SW_HIDE);
                if (_hidden.Add(bar)) Log.Info($"taskbar hidden on monitor {mon} for {exe}");
            }
            else if (_hidden.Remove(bar))
            {
                ShowWindow(bar, SW_SHOWNA);
                Log.Info($"taskbar restored on monitor {mon}");
            }
        }
        UpdateGuard();
        // A revealed bar whose app is no longer full screen goes back to normal tracking.
        if (_revealed != IntPtr.Zero && !want.Contains(_revealed)) _revealed = IntPtr.Zero;

        bool needEdge = _settings.EdgeReveal && (want.Count > 0);
        if (needEdge && !_edgeTimer.Enabled) _edgeTimer.Start();
        else if (!needEdge && _edgeTimer.Enabled) _edgeTimer.Stop();
    }

    /// <summary>F2.5: mouse at the bottom edge of a monitor whose taskbar is hidden reveals it until the mouse leaves.</summary>
    void CheckEdge()
    {
        if (_last == null || !GetCursorPos(out var pt)) return;
        var mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var st = _last.Monitors.FirstOrDefault(m => m.Monitor.Handle == mon);
        if (st == null) return;
        var b = st.Monitor.Bounds;

        if (_revealed == IntPtr.Zero)
        {
            if (pt.Y >= b.Bottom - 2 && _last.ForegroundFullscreenMonitor == mon)
            {
                _revealed = mon;
                Apply(_last);
            }
        }
        else
        {
            var rb = _last.Monitors.FirstOrDefault(m => m.Monitor.Handle == _revealed)?.Monitor.Bounds;
            int barHeight = BarHeight(_revealed);
            if (rb == null || mon != _revealed || pt.Y < rb.Value.Bottom - barHeight - 24)
            {
                _revealed = IntPtr.Zero;
                Apply(_last);
            }
        }
    }

    int BarHeight(IntPtr mon)
    {
        foreach (var (bar, m) in Taskbars())
            if (m == mon) { GetWindowRect(bar, out var r); return Math.Max(r.Height, 32); }
        return 48;
    }

    static bool Matches(List<string> list, string exe) =>
        exe.Length > 0 && list.Any(x => string.Equals(x.Trim(), exe, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(x.Trim() + ".exe", exe, StringComparison.OrdinalIgnoreCase));

    public static List<(IntPtr bar, IntPtr monitor)> Taskbars()
    {
        var list = new List<(IntPtr, IntPtr)>();
        var primary = FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) list.Add((primary, MonitorFromWindow(primary, MONITOR_DEFAULTTONEAREST)));
        IntPtr after = IntPtr.Zero;
        while ((after = FindWindowEx(IntPtr.Zero, after, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            list.Add((after, MonitorFromWindow(after, MONITOR_DEFAULTTONEAREST)));
        return list;
    }

    /// <summary>Restore every taskbar we hid (feature off, reset hotkey, exit).</summary>
    public void ShowAll()
    {
        _edgeTimer.Stop();
        _revealed = IntPtr.Zero;
        foreach (var (bar, _) in Taskbars())
            if (_hidden.Contains(bar) || !IsWindowVisible(bar)) ShowWindow(bar, SW_SHOWNA);
        _hidden.Clear();
        _autoHideTimer.Stop();
        _wantAutoHide = false;
        SetForcedAutoHide(false);
        UpdateGuard();
    }

    /// <summary>Explorer restarted: old handles are gone.</summary>
    public void ExplorerRestarted() { _hidden.Clear(); _revealed = IntPtr.Zero; }

    public void Dispose()
    {
        ShowAll();
        _switches.Wait(5000); // switch auto-hide back off before nightOwl exits
        _edgeTimer.Dispose();
        _autoHideTimer.Dispose();
    }
}
