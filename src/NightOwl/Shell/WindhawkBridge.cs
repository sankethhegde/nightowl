using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;

namespace NightOwl.Shell;

/// <summary>
/// Drives a portable Windhawk install under %LocalAppData%\nightOwl\windhawk. Portable storage keeps
/// every mod's config in AppData\Engine\Mods\&lt;id&gt;.ini, writable without admin, and the running
/// engine watches that folder, so a write plus a new SettingsChangeTime applies live.
/// nightOwl never loads code into Explorer itself; Windhawk and its mods do.
/// </summary>
public sealed class WindhawkBridge
{
    public static readonly string Root = Path.Combine(Paths.Local, "windhawk");
    public static string Exe => Path.Combine(Root, "windhawk.exe");
    static string ModsDir => Path.Combine(Root, "AppData", "Engine", "Mods");
    static string ModIni(string id) => Path.Combine(ModsDir, id + ".ini");

    public bool IsInstalled => File.Exists(Exe) && File.Exists(Path.Combine(Root, "windhawk.ini"));

    public bool IsRunning => RunningProcess() != null;

    Process? RunningProcess()
    {
        foreach (var p in Process.GetProcessesByName("windhawk"))
        {
            try
            {
                if (string.Equals(p.MainModule?.FileName, Exe, StringComparison.OrdinalIgnoreCase)) return p;
            }
            catch { }
            p.Dispose();
        }
        return null;
    }

    /// <summary>Installed means Windhawk compiled it: the [Mod] section names a library file.</summary>
    public bool IsModInstalled(string id) =>
        File.Exists(ModIni(id)) && Read(ModIni(id), "Mod", "LibraryFileName") is { Length: > 0 };

    public string? ModVersion(string id) => File.Exists(ModIni(id)) ? Read(ModIni(id), "Mod", "Version") : null;

    public bool IsModEnabled(string id) => IsModInstalled(id) && Read(ModIni(id), "Mod", "Disabled") != "1";

    public void StartTrayOnly()
    {
        if (!IsInstalled || IsRunning) return;
        Process.Start(new ProcessStartInfo(Exe, "-tray-only") { UseShellExecute = false, WorkingDirectory = Root });
        Log.Info("windhawk started (tray only)");
    }

    /// <summary>Stops and starts Windhawk, which makes its engine load into every running target process again.</summary>
    public void Restart()
    {
        Exit();
        StartTrayOnly();
    }

    /// <summary>
    /// ShellExperienceHost (notification center) and StartMenuExperienceHost are AppContainer processes: they
    /// can only load DLLs from folders that grant "ALL APPLICATION PACKAGES" access. Installed Windhawk sets this
    /// up on its own folder; the portable copy doesn't, so grant read/execute on the whole folder and write on
    /// ModsWritable (where mods running inside those processes report their status). Returns true if it changed.
    /// </summary>
    public bool EnsureSandboxAccess()
    {
        bool changed = false;
        changed |= Grant(Root, FileSystemRights.ReadAndExecute);
        var writable = Path.Combine(Root, "AppData", "Engine", "ModsWritable");
        if (Directory.Exists(writable)) changed |= Grant(writable, FileSystemRights.Modify);
        if (changed) Log.Info("granted sandboxed shell processes access to the Windhawk folder");
        return changed;
    }

    static bool Grant(string dir, FileSystemRights rights)
    {
        var info = new DirectoryInfo(dir);
        var acl = info.GetAccessControl();
        bool changed = false;
        foreach (var sid in new[] { "S-1-15-2-1", "S-1-15-2-2" }) // ALL (RESTRICTED) APPLICATION PACKAGES
        {
            var id = new System.Security.Principal.SecurityIdentifier(sid);
            bool has = acl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Any(r => r.IdentityReference == id && r.AccessControlType == AccessControlType.Allow && (r.FileSystemRights & rights) == rights);
            if (has) continue;
            acl.AddAccessRule(new FileSystemAccessRule(id, rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            changed = true;
        }
        if (changed) info.SetAccessControl(acl);
        return changed;
    }

    /// <summary>Opens Windhawk's own window, for installing or updating mods.</summary>
    public void OpenUi()
    {
        if (!IsInstalled) return;
        Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = false, WorkingDirectory = Root });
    }

    /// <summary>Stops Windhawk; its engine unloads every mod, which returns the shell to stock.</summary>
    public void Exit()
    {
        if (!IsRunning) return;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Exe, "-exit -wait") { UseShellExecute = false, WorkingDirectory = Root });
            p?.WaitForExit(10_000);
            Log.Info("windhawk exited");
        }
        catch (Exception ex) { Log.Error("stopping windhawk failed", ex); }
    }

    /// <summary>Read a mod's [Settings] section (flat keys such as controlStyles[0].target).</summary>
    public Dictionary<string, string> ReadSettings(string id)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = ModIni(id);
        if (!File.Exists(path)) return result;
        foreach (var line in ReadSection(path, "Settings"))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            result[line[..eq]] = Unquote(line[(eq + 1)..]);
        }
        return result;
    }

    /// <summary>
    /// Bring a mod to the wanted state. Keys nightOwl doesn't manage keep their stored value; keys
    /// under <paramref name="ownedPrefixes"/> (arrays nightOwl generates) are replaced as a whole.
    /// Returns true when something was written.
    /// </summary>
    public bool Apply(string id, bool enabled, IReadOnlyDictionary<string, string>? settings, IEnumerable<string>? ownedPrefixes = null)
    {
        if (!IsModInstalled(id)) return false;
        var path = ModIni(id);
        bool changed = false;

        if (settings != null)
        {
            var current = ReadSettings(id);
            var next = new Dictionary<string, string>(current, StringComparer.Ordinal);
            foreach (var prefix in ownedPrefixes ?? Array.Empty<string>())
                foreach (var key in current.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
                    next.Remove(key);
            foreach (var (k, v) in settings) next[k] = v;

            bool same = next.Count == current.Count && next.All(kv => current.TryGetValue(kv.Key, out var v) && v == kv.Value);
            if (!same)
            {
                // Rewrite the whole section so removed array entries really disappear.
                WritePrivateProfileString("Settings", null, null, path);
                foreach (var (k, v) in next) WriteString(path, "Settings", k, v);
                changed = true;
            }
        }

        bool isEnabled = Read(path, "Mod", "Disabled") != "1";
        if (isEnabled != enabled)
        {
            WriteString(path, "Mod", "Disabled", enabled ? "0" : "1");
            changed = true;
        }

        if (changed)
        {
            // The engine reloads a mod's settings when this value changes; it only has to differ.
            var prev = long.TryParse(Read(path, "Mod", "SettingsChangeTime"), out var p) ? p : 0;
            var stamp = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds() & 0x7FFFFFFF, prev + 1);
            WriteString(path, "Mod", "SettingsChangeTime", stamp.ToString());
            Log.Info($"windhawk mod {id}: {(enabled ? "on" : "off")}, settings {(settings == null ? "unchanged" : "written")}");
        }
        return changed;
    }

    /// <summary>Engine-wide process limits (spec NF.10: only the shell processes the features need).</summary>
    public void LimitProcesses(string include)
    {
        var path = Path.Combine(Root, "AppData", "Engine", "settings.ini");
        if (!File.Exists(path)) return;
        if (Read(path, "Settings", "Include") != include) WriteString(path, "Settings", "Include", include);
        if (Read(path, "Settings", "Exclude") != "*") WriteString(path, "Settings", "Exclude", "*");
    }

    /// <summary>Hide Windhawk's own tray icon: nightOwl is the one place to control things.</summary>
    public void HideTrayIcon(bool hide)
    {
        var path = Path.Combine(Root, "AppData", "settings.ini");
        if (File.Exists(path) && Read(path, "Settings", "HideTrayIcon") != (hide ? "1" : "0"))
            WriteString(path, "Settings", "HideTrayIcon", hide ? "1" : "0");
    }

    // ---------- ini helpers (same Win32 profile API Windhawk uses, same quoting rules) ----------

    static string? Read(string path, string section, string key)
    {
        var sb = new StringBuilder(4096);
        const string missing = "\u0001";
        GetPrivateProfileString(section, key, missing, sb, sb.Capacity, path);
        var s = sb.ToString();
        return s == missing ? null : s;
    }

    static IEnumerable<string> ReadSection(string path, string section)
    {
        for (int size = 32 * 1024; ; size *= 2)
        {
            var buf = new char[size];
            int n = GetPrivateProfileSection(section, buf, size, path);
            if (n < size - 2) return new string(buf, 0, n).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
    }

    static void WriteString(string path, string section, string key, string value)
    {
        if (value.Contains('\r') || value.Contains('\n')) value = value.Replace("\r", " ").Replace("\n", " ");
        bool trimmable = value.Length > 0 && (value[0] <= ' ' || value[^1] <= ' ');
        bool quoted = value.Length >= 2 && value[0] == value[^1] && (value[0] == '"' || value[0] == '\'');
        if (trimmable || quoted) value = "\"" + value + "\"";
        if (!WritePrivateProfileString(section, key, value, path))
            throw new IOException($"writing {key} to {Path.GetFileName(path)} failed ({Marshal.GetLastWin32Error()})");
    }

    /// <summary>Section lines come back raw, so undo the quoting GetPrivateProfileString would strip.</summary>
    static string Unquote(string v) =>
        v.Length >= 2 && v[0] == v[^1] && (v[0] == '"' || v[0] == '\'') ? v[1..^1] : v;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WritePrivateProfileString(string section, string? key, string? value, string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder ret, int size, string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPrivateProfileSection(string section, char[] ret, int size, string path);
}
