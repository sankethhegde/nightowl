using System.Diagnostics;
using Microsoft.Win32;

namespace NightOwl;

/// <summary>
/// Uninstall cleanup (NF.7), run by the installer's uninstaller as <c>nightOwl.exe --uninstall-cleanup [--purge]</c>.
/// Stops nightOwl and its helpers, stops nightOwl's Windhawk (which unloads every mod from Explorer), shows any
/// hidden taskbar, removes the Run key and, with --purge, deletes nightOwl's AppData. No reboot needed.
/// </summary>
internal static class Uninstaller
{
    public const string QuitEventName = @"Local\nightOwl.quit";
    const string CoreMutexName = @"Local\nightOwl.core";

    public static int Run(bool purge)
    {
        var report = new List<string>();
        StopCore(report);
        StopHelpers(report);
        StopWindhawk(report);
        TaskbarHider.RunGuard(0); // shows any taskbar still hidden
        Native.RepaintStaticWallpaper();
        Startup.Set(false);
        report.Add("start-with-Windows entry removed");
        if (purge)
            foreach (var dir in new[] { Paths.Local, Paths.Roaming })
                report.Add(DeleteDir(dir) ? $"deleted {dir}" : $"could not fully delete {dir}");
        try { File.WriteAllLines(Path.Combine(Path.GetTempPath(), "nightOwl-uninstall.txt"), report); } catch { }
        return 0;
    }

    static void StopCore(List<string> report)
    {
        if (!Mutex.TryOpenExisting(CoreMutexName, out var mutex)) { report.Add("nightOwl was not running"); return; }
        mutex.Dispose();
        if (EventWaitHandle.TryOpenExisting(QuitEventName, out var quit)) { quit.Set(); quit.Dispose(); }
        else ControlPipe.Send("{\"cmd\":\"action\",\"name\":\"quit\"}"); // builds before 0.3.0
        var until = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < until && Mutex.TryOpenExisting(CoreMutexName, out var m)) { m.Dispose(); Thread.Sleep(200); }
        report.Add(Mutex.TryOpenExisting(CoreMutexName, out var left) ? "nightOwl did not quit in time" : "nightOwl quit");
        left?.Dispose();
    }

    /// <summary>Any other nightOwl process (wallpaper, guard, a copy from another folder that didn't quit).</summary>
    static void StopHelpers(List<string> report)
    {
        foreach (var p in Process.GetProcessesByName("nightOwl"))
        {
            using (p)
            {
                if (p.Id == Environment.ProcessId) continue;
                try { p.Kill(); p.WaitForExit(5000); report.Add($"stopped nightOwl process {p.Id}"); } catch { }
            }
        }
    }

    static void StopWindhawk(List<string> report)
    {
        var bridge = new Shell.WindhawkBridge();
        if (!bridge.IsRunning) return;
        bridge.Exit();
        if (bridge.IsRunning)
            foreach (var p in Process.GetProcessesByName("windhawk"))
                using (p)
                    try { if (p.MainModule?.FileName?.StartsWith(Paths.Local, StringComparison.OrdinalIgnoreCase) == true) p.Kill(); } catch { }
        report.Add("Windhawk stopped; mods unloaded");
    }

    static bool DeleteDir(string dir)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                return true;
            }
            catch (Exception) { Thread.Sleep(250); } // WebView2 or Windhawk files still closing
        }
        return !Directory.Exists(dir);
    }
}
