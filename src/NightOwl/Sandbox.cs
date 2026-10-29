using System.Runtime.InteropServices;
using System.Text;

namespace NightOwl;

/// <summary>
/// nightOwl started from inside another app's MSIX package (e.g. launched by a packaged app) inherits
/// that package's file virtualization: its writes to %AppData% / %LocalAppData% land in the package's
/// private LocalCache, which Explorer and the rest of Windows can't see. That breaks Windhawk (Explorer
/// can't load an engine it can't see) and loses settings on the next normal start. This detects that
/// case, and on a normal start moves data stranded in such a private copy back to the real folders.
/// </summary>
internal static class Sandbox
{
    const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);

    /// <summary>The package whose private AppData nightOwl's writes land in, or null when they reach the real AppData.</summary>
    public static readonly string? Package = CurrentPackage() ?? RedirectingPackage();

    static string? CurrentPackage()
    {
        try
        {
            int len = 0;
            int rc = GetCurrentPackageFullName(ref len, null);
            if (rc == APPMODEL_ERROR_NO_PACKAGE) return null;
            var sb = new StringBuilder(len);
            return GetCurrentPackageFullName(ref len, sb) == 0 ? sb.ToString() : "unknown package";
        }
        catch { return null; }
    }

    /// <summary>
    /// A process started by a packaged app can have its AppData writes redirected without having a package
    /// identity itself, so test it: write a probe file and look for it in every package's private copy.
    /// </summary>
    static string? RedirectingPackage()
    {
        var probeName = $"redirect-probe-{Environment.ProcessId}.tmp";
        var probe = Path.Combine(Paths.Local, probeName);
        try
        {
            Directory.CreateDirectory(Paths.Local);
            File.WriteAllText(probe, "probe");
            var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (!Directory.Exists(packages)) return null;
            foreach (var pkg in Directory.EnumerateDirectories(packages))
                if (File.Exists(Path.Combine(pkg, "LocalCache", "Local", "nightOwl", probeName))) return Path.GetFileName(pkg);
            return null;
        }
        catch { return null; }
        finally { try { File.Delete(probe); } catch { } }
    }

    /// <summary>
    /// Copy nightOwl data stranded in a package's private AppData into the real one. Only folders the
    /// real side doesn't have yet are copied, so nothing real is ever overwritten. Returns what moved.
    /// </summary>
    public static List<string> RescueStrandedData()
    {
        var moved = new List<string>();
        if (Package != null) return moved;
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        if (!Directory.Exists(packages)) return moved;

        foreach (var pkg in Directory.GetDirectories(packages).OrderByDescending(Directory.GetLastWriteTime))
        {
            var roaming = Path.Combine(pkg, "LocalCache", "Roaming", "nightOwl");
            var local = Path.Combine(pkg, "LocalCache", "Local", "nightOwl");
            try
            {
                if (File.Exists(Path.Combine(roaming, "config.json")) && !File.Exists(Paths.Config))
                {
                    CopyDir(roaming, Paths.Roaming);
                    moved.Add($"settings from {Path.GetFileName(pkg)}");
                }
                foreach (var sub in new[] { "windhawk", "thumbs" })
                {
                    var src = Path.Combine(local, sub);
                    var dst = Path.Combine(Paths.Local, sub);
                    if (Directory.Exists(src) && !Directory.Exists(dst))
                    {
                        CopyDir(src, dst);
                        moved.Add($"{sub} from {Path.GetFileName(pkg)}");
                    }
                }
            }
            catch (Exception ex) { moved.Add($"failed for {Path.GetFileName(pkg)}: {ex.Message}"); }
        }
        return moved;
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(dst, Path.GetRelativePath(src, file)), overwrite: false);
    }
}
