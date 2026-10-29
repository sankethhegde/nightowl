using NightOwl.Wallpaper;

namespace NightOwl;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        if (args.Contains("--wallpaper")) return RunWallpaper(args);
        var guard = Array.IndexOf(args, "--guard");
        if (guard >= 0 && guard + 1 < args.Length && int.TryParse(args[guard + 1], out var corePid)) return TaskbarHider.RunGuard(corePid);
        var upd = Array.IndexOf(args, "--apply-update");
        if (upd >= 0 && upd + 2 < args.Length) return ApplyUpdate(args[upd + 1], int.Parse(args[upd + 2]));
        if (args.Contains("--uninstall-cleanup")) return Uninstaller.Run(args.Contains("--purge"));
        var ctl = Array.IndexOf(args, "--control");
        if (ctl >= 0) return ControlPipe.Send(ctl + 1 < args.Length ? args[ctl + 1] : "{\"cmd\":\"status\"}");
        try { return RunCore(args); }
        catch (Exception ex) { WriteCrashNote(ex); throw; }
    }

    /// <summary>Self-update: wait for the old core (and its helpers) to exit, copy this build over it, start it.</summary>
    static int ApplyUpdate(string targetDir, int oldPid)
    {
        try { System.Diagnostics.Process.GetProcessById(oldPid).WaitForExit(15_000); } catch { }
        var source = Paths.AppDir.TrimEnd('\\');
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(source, file);
                    if (rel.Equals("last-crash.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    var dst = Path.Combine(targetDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(file, dst, true);
                }
                break;
            }
            catch (IOException) when (attempt < 40) { Thread.Sleep(250); } // helpers still closing
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(targetDir, "nightOwl.exe")) { UseShellExecute = false, WorkingDirectory = targetDir });
        return 0;
    }

    /// <summary>A crash note next to the exe: readable even when AppData (and so the log) is out of reach.</summary>
    static void WriteCrashNote(Exception? ex)
    {
        try { File.WriteAllText(Path.Combine(Paths.AppDir, "last-crash.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{ex}"); }
        catch { }
    }

    static int RunWallpaper(string[] args)
    {
        Log.Init("wallpaper");
        int parent = 0;
        var i = Array.IndexOf(args, "--parent");
        if (i >= 0 && i + 1 < args.Length) int.TryParse(args[i + 1], out parent);
        Application.ThreadException += (_, e) => Log.Error("wallpaper UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("wallpaper crashed", e.ExceptionObject as Exception);
            Native.RepaintStaticWallpaper();
        };
        Application.Run(new WallpaperHost(parent));
        return 0;
    }

    static int RunCore(string[] args)
    {
        Log.Init("nightowl");
        using var mutex = new Mutex(true, @"Local\nightOwl.core", out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\nightOwl.showSettings");
        using var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Uninstaller.QuitEventName);
        if (!first)
        {
            // Already running: ask that instance to open its settings window.
            showSignal.Set();
            return 0;
        }

        if (Sandbox.Package != null)
            Log.Warn($"running inside package {Sandbox.Package}: AppData writes are private to it");
        foreach (var m in Sandbox.RescueStrandedData()) Log.Info("rescued stranded data: " + m);

        CoreApp? app = null;
        Application.ThreadException += (_, e) => Log.Error("UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("core crashed", e.ExceptionObject as Exception);
            WriteCrashNote(e.ExceptionObject as Exception);
            app?.EmergencyRestore();
        };

        app = new CoreApp(args.Contains("--startup"));
        var listener = new Thread(() =>
        {
            // The installer and uninstaller set the quit event to close nightOwl cleanly.
            while (true)
            {
                if (WaitHandle.WaitAny(new WaitHandle[] { showSignal, quitSignal }) == 1) { app.InvokeOnUi(app.Quit); return; }
                app.InvokeOnUi(app.ShowSettings);
            }
        }) { IsBackground = true, Name = "single-instance" };
        listener.Start();
        ControlPipe.StartServer(app);

        Application.Run(app);
        return 0;
    }
}
