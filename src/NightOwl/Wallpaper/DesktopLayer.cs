using static NightOwl.Native;

namespace NightOwl.Wallpaper;

/// <summary>
/// Puts a window behind the desktop icons. Windows has two layouts (see spec risk table):
///  - before 24H2: Progman spawns a top-level WorkerW behind the icons' WorkerW; we become its child.
///  - 24H2 and later: SHELLDLL_DefView and a WorkerW are both children of Progman; we become a
///    child of Progman, z-ordered between the icons (DefView) and the WorkerW that paints the static wallpaper.
/// </summary>
internal static class DesktopLayer
{
    public static string LastMode { get; private set; } = "none";

    static readonly bool UseLayered = Environment.GetEnvironmentVariable("NIGHTOWL_LAYERED") != "0";

    public static IntPtr Progman() => FindWindow("Progman", null);

    static void SpawnWorkerW(IntPtr progman)
    {
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), SMTO_NORMAL, 1000, out _);
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);
    }

    /// <summary>Re-parent <paramref name="hwnd"/> into the desktop and place it over <paramref name="screen"/> (physical px).</summary>
    public static bool Attach(IntPtr hwnd, RECT screen)
    {
        var progman = Progman();
        if (progman == IntPtr.Zero) { Log.Warn("Progman not found"); return false; }
        SpawnWorkerW(progman);

        var style = Style(hwnd, GWL_STYLE);
        style = (style & ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME)) | WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS;
        SetStyle(hwnd, GWL_STYLE, style);
        var ex = Style(hwnd, GWL_EXSTYLE) & ~WS_EX_APPWINDOW;
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;

        var defViewInProgman = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        var workerInProgman = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);

        if (defViewInProgman != IntPtr.Zero && workerInProgman != IntPtr.Zero)
        {
            // Windows 11 24H2+ layering.
            if (UseLayered) ex |= WS_EX_LAYERED;
            SetStyle(hwnd, GWL_EXSTYLE, ex);
            if (UseLayered) SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
            SetParent(hwnd, progman);
            Place(hwnd, progman, screen, defViewInProgman);
            SetWindowPos(workerInProgman, hwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            LastMode = "progman-24h2";
            return true;
        }

        // Legacy layering: find the WorkerW that sits behind the one hosting the icons.
        IntPtr workerw = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                workerw = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        if (workerw == IntPtr.Zero)
        {
            Log.Warn("no WorkerW behind the icons; falling back to Progman");
            workerw = progman;
        }
        SetStyle(hwnd, GWL_EXSTYLE, ex);
        SetParent(hwnd, workerw);
        Place(hwnd, workerw, screen, IntPtr.Zero);
        LastMode = workerw == progman ? "progman-fallback" : "workerw-legacy";
        return true;
    }

    static void Place(IntPtr hwnd, IntPtr parent, RECT screen, IntPtr insertAfter)
    {
        var r = screen;
        MapWindowPoints(IntPtr.Zero, parent, ref r, 2);
        uint flags = SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED;
        if (insertAfter == IntPtr.Zero) flags |= SWP_NOZORDER;
        SetWindowPos(hwnd, insertAfter, r.Left, r.Top, screen.Width, screen.Height, flags);
    }

    /// <summary>True when the window under the cursor is the desktop itself (icons view or wallpaper layer).</summary>
    public static bool IsDesktopAt(POINT pt)
    {
        var w = WindowFromPoint(pt);
        if (w == IntPtr.Zero) return false;
        var cls = ClassOf(w);
        if (cls is "SysListView32" or "SHELLDLL_DefView" or "WorkerW" or "Progman") return true;
        var root = GetAncestor(w, GA_ROOT);
        return ClassOf(root) is "Progman" or "WorkerW";
    }
}
