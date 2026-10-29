namespace NightOwl;

internal static class Paths
{
    public static readonly string Roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nightOwl");
    public static readonly string Local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nightOwl");
    public static string Config => Path.Combine(Roaming, "config.json");
    public static string Logs => Path.Combine(Local, "logs");
    public static string Thumbs => Path.Combine(Local, "thumbs");
    public static string WebViewData(string name) => Path.Combine(Local, "webview-" + name);
    public static string AppDir => AppContext.BaseDirectory;
}

/// <summary>Daily log files under %LocalAppData%\nightOwl\logs, kept for 7 days (NF.13).</summary>
internal static class Log
{
    static readonly object Gate = new();
    static string _prefix = "nightowl";

    public static void Init(string prefix)
    {
        _prefix = prefix;
        try
        {
            Directory.CreateDirectory(Paths.Logs);
            foreach (var f in Directory.GetFiles(Paths.Logs, "*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7)) File.Delete(f);
        }
        catch { }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex == null ? msg : $"{msg}: {ex}");

    static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        lock (Gate)
        {
            try { File.AppendAllText(Path.Combine(Paths.Logs, $"{_prefix}-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine); }
            catch { }
        }
    }
}
