using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace NightOwl;

/// <summary>Battery and battery-saver state (F6.4, NF.4).</summary>
public sealed class PowerWatcher : IDisposable
{
    public bool OnBattery { get; private set; }
    public bool BatterySaver { get; private set; }
    public bool HasBattery { get; private set; }
    public event Action? Changed;
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 10_000 };

    public PowerWatcher()
    {
        Read();
        SystemEvents.PowerModeChanged += OnPowerMode;
        _poll.Tick += (_, _) => { if (Read()) Changed?.Invoke(); };
        _poll.Start();
    }

    void OnPowerMode(object? s, PowerModeChangedEventArgs e) { if (Read()) Changed?.Invoke(); }

    bool Read()
    {
        if (!Native.GetSystemPowerStatus(out var st)) return false;
        bool battery = st.ACLineStatus == 0;
        bool saver = st.SystemStatusFlag == 1;
        HasBattery = st.BatteryFlag != 128 && st.BatteryFlag != 255;
        bool changed = battery != OnBattery || saver != BatterySaver;
        OnBattery = battery;
        BatterySaver = saver;
        return changed;
    }

    public void Dispose() { SystemEvents.PowerModeChanged -= OnPowerMode; _poll.Dispose(); }
}

/// <summary>Start with Windows (UI.7) through the per-user Run key.</summary>
public static class Startup
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "nightOwl";

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, true) ?? Registry.CurrentUser.CreateSubKey(Key);
            if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\" --startup");
            else if (k.GetValue(Name) != null) k.DeleteValue(Name);
        }
        catch (Exception ex) { Log.Error("updating Run key failed", ex); }
    }

    public static bool IsSet()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(Name) is string s && s.Contains(Environment.ProcessPath ?? "nightOwl.exe", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Windows facts the Home page and the compatibility check need (NF.8, NF.11).</summary>
public static class WinInfo
{
    /// <summary>Builds the Phase 0–1 features have been checked on.</summary>
    public static readonly int[] VerifiedBuilds = { 22631, 26100, 26200, 26300 };

    public static int Build => Environment.OSVersion.Version.Build;

    public static string DisplayVersion =>
        Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", "") as string ?? "";

    public static bool TransparencyOn =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) is int v && v != 0;

    public static bool AppsDark =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v == 0;

    public static bool HighContrast => SystemInformation.HighContrast;

    /// <summary>Other shell tools that overlap with nightOwl features (NF.9).</summary>
    public static List<string> ConflictingTools()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lively"] = "Lively Wallpaper (live wallpapers)",
            ["Livelywpf"] = "Lively Wallpaper (live wallpapers)",
            ["wallpaper32"] = "Wallpaper Engine (live wallpapers)",
            ["wallpaper64"] = "Wallpaper Engine (live wallpapers)",
            ["TranslucentTB"] = "TranslucentTB (taskbar)",
            ["windhawk"] = "Windhawk (taskbar mods)",
            ["RoundedTB"] = "RoundedTB (taskbar)",
            ["TaskbarX"] = "TaskbarX (taskbar)",
            ["MicaForEveryone"] = "Mica For Everyone (window backdrops)",
            ["Start11Srv"] = "Start11 (taskbar and Start)",
            ["Start11_64"] = "Start11 (taskbar and Start)",
            ["WindowBlinds"] = "WindowBlinds (window styling)",
            ["wbconfig"] = "WindowBlinds (window styling)",
        };
        var found = new List<string>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (!names.TryGetValue(p.ProcessName, out var n) || found.Contains(n)) continue;
                // nightOwl's own portable Windhawk is how Phases 2–4 run, not a conflict.
                if (p.MainModule?.FileName?.StartsWith(Paths.Local, StringComparison.OrdinalIgnoreCase) == true) continue;
                found.Add(n);
            }
            catch { }
            finally { p.Dispose(); }
        }
        // These run inside Explorer with no process of their own, so look for their install folders.
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (Directory.Exists(Path.Combine(pf, "ExplorerPatcher"))) found.Add("ExplorerPatcher (taskbar and Explorer)");
        if (Directory.Exists(Path.Combine(pf, "StartAllBack")) ||
            Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StartAllBack"))) found.Add("StartAllBack (taskbar and Explorer)");
        return found;
    }
}

/// <summary>Library thumbnails from the Windows shell (the same ones Explorer shows), cached as PNG.</summary>
public static class Thumbnails
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Native.POINT size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

    public static string PathFor(string id) => Path.Combine(Paths.Thumbs, id + ".png");

    /// <summary>Creates the thumbnail on a background STA thread; returns false when Windows has none for the file.</summary>
    public static Task<bool> EnsureAsync(LibraryItem item)
    {
        var target = PathFor(item.Id);
        if (File.Exists(target)) return Task.FromResult(true);
        if (item.Type == "url") return Task.FromResult(false);
        var tcs = new TaskCompletionSource<bool>();
        var t = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(Paths.Thumbs);
                SHCreateItemFromParsingName(item.Path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var f);
                // SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY: a real thumbnail or nothing (the UI draws a type icon).
                int hr = f.GetImage(new Native.POINT { X = 320, Y = 180 }, 0x1 | 0x8, out var hbm);
                Marshal.ReleaseComObject(f);
                if (hr != 0 || hbm == IntPtr.Zero) { tcs.SetResult(false); return; }
                using (var bmp = Image.FromHbitmap(hbm)) bmp.Save(target, System.Drawing.Imaging.ImageFormat.Png);
                DeleteObject(hbm);
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                Log.Warn($"thumbnail failed for {item.Path}: {ex.Message}");
                tcs.TrySetResult(false);
            }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    public static void Delete(string id) { try { File.Delete(PathFor(id)); } catch { } }
}
