using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NightOwl;

/// <summary>
/// Command-line control of the running nightOwl (scripts, testing): `nightOwl.exe --control "{json}"`.
/// The pipe only admits the current Windows account. Commands:
///   {"cmd":"status"}                              the same state the settings window gets, plus extras
///   {"cmd":"set","path":"resize.height","value":64}   profile-relative path (or a top-level config key)
///   {"cmd":"action","name":"confirmBuild|reset|pause|resume|leaveSafeMode|openSettings|nextWallpaper"}
///   {"cmd":"log","lines":40}                       tail of today's log
/// </summary>
internal static class ControlPipe
{
    const string Name = "nightOwl.control";

    public static void StartServer(CoreApp app)
    {
        var t = new Thread(() => ServeLoop(app)) { IsBackground = true, Name = "control-pipe" };
        t.Start();
    }

    static void ServeLoop(CoreApp app)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        while (true)
        {
            try
            {
                using var server = NamedPipeServerStreamAcl.Create(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.None, 0, 0, security);
                server.WaitForConnection();
                using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                var line = reader.ReadLine();
                string reply;
                try
                {
                    var tcs = new TaskCompletionSource<string>();
                    app.InvokeOnUi(() =>
                    {
                        try { tcs.SetResult(Handle(app, line ?? "")); }
                        catch (Exception ex) { tcs.SetResult(JsonSerializer.Serialize(new { ok = false, error = ex.Message })); }
                    });
                    reply = tcs.Task.Wait(15_000) ? tcs.Task.Result : "{\"ok\":false,\"error\":\"timeout\"}";
                }
                catch (Exception ex) { reply = JsonSerializer.Serialize(new { ok = false, error = ex.Message }); }
                writer.WriteLine(reply);
            }
            catch (Exception ex)
            {
                Log.Warn("control pipe: " + ex.Message);
                Thread.Sleep(500);
            }
        }
    }

    static string Handle(CoreApp app, string line)
    {
        var msg = JsonNode.Parse(line) ?? throw new InvalidDataException("empty command");
        switch ((string?)msg["cmd"])
        {
            case "status":
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    package = Sandbox.Package,
                    process = Environment.ProcessId,
                    windhawkProcesses = InjectedProcesses(),
                    state = app.BuildState(),
                }, ConfigStore.Wire);
            case "set":
            {
                var path = (string?)msg["path"] ?? throw new InvalidDataException("path missing");
                var value = msg["value"]?.DeepClone();
                var root = JsonSerializer.SerializeToNode(app.Store.Config, ConfigStore.Json)!.AsObject();
                bool topLevel = root.ContainsKey(path.Split('.')[0]);
                JsonNode target = topLevel ? root : root["profiles"]![app.Store.Config.ActiveProfile]!;
                var keys = path.Split('.');
                foreach (var k in keys[..^1]) target = target[k] ?? throw new InvalidDataException($"no such setting: {path}");
                if (target is not JsonObject obj || !obj.ContainsKey(keys[^1])) throw new InvalidDataException($"no such setting: {path}");
                obj[keys[^1]] = value;
                app.Store.Replace(root);
                return JsonSerializer.Serialize(new { ok = true });
            }
            case "action":
                switch ((string?)msg["name"])
                {
                    case "confirmBuild": app.Shell.ConfirmBuild(); break;
                    case "leaveSafeMode": app.Shell.LeaveSafeMode(); break;
                    case "reset": app.ResetToStock(); break;
                    case "pause": app.SetPausedAll(true); break;
                    case "resume": app.SetPausedAll(false); break;
                    case "openSettings": app.ShowSettings(); break;
                    case "nextWallpaper": app.Wallpaper.Next(); break;
                    case "quit": app.InvokeOnUi(app.Quit); break;
                    case "update":
                    {
                        // Install a new build from a folder: its exe waits for us to exit, copies itself over
                        // our folder and starts the result. Started by this process, so it runs in the same context.
                        var from = (string?)msg["from"] ?? throw new InvalidDataException("from missing");
                        var newExe = Path.Combine(from, "nightOwl.exe");
                        if (!File.Exists(newExe)) throw new FileNotFoundException("no nightOwl.exe in " + from);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(newExe,
                            $"--apply-update \"{Paths.AppDir.TrimEnd('\\')}\" {Environment.ProcessId}") { UseShellExecute = false });
                        app.InvokeOnUi(app.Quit);
                        break;
                    }
                    default: throw new InvalidDataException("unknown action");
                }
                return JsonSerializer.Serialize(new { ok = true });
            case "log":
            {
                int n = (int?)msg["lines"] ?? 40;
                var file = Path.Combine(Paths.Logs, $"nightowl-{DateTime.Now:yyyyMMdd}.log");
                var lines = File.Exists(file) ? ReadShared(file).TakeLast(n).ToArray() : Array.Empty<string>();
                return JsonSerializer.Serialize(new { ok = true, lines });
            }
            default:
                throw new InvalidDataException("unknown cmd");
        }
    }

    static IEnumerable<string> ReadShared(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        var all = new List<string>();
        while (sr.ReadLine() is { } l) all.Add(l);
        return all;
    }

    /// <summary>Shell processes that have Windhawk's engine loaded (proves mods can run).</summary>
    static List<string> InjectedProcesses()
    {
        var result = new List<string>();
        foreach (var name in new[] { "explorer", "ShellExperienceHost", "ShellHost", "SystemSettings", "StartMenuExperienceHost", "SearchHost" })
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.Modules.Cast<System.Diagnostics.ProcessModule>().Any(m => m.ModuleName.Equals("windhawk.dll", StringComparison.OrdinalIgnoreCase)))
                        result.Add($"{name}:{p.Id}");
                }
                catch { }
                finally { p.Dispose(); }
            }
        return result;
    }

    /// <summary>Client side: send one command to the running nightOwl and print the reply.</summary>
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    public static int Send(string json)
    {
        // A GUI-subsystem exe has no console; borrow the terminal it was started from, if any.
        if (!Console.IsOutputRedirected) AttachConsole(-1);
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.InOut);
            client.Connect(3000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, false, 4096, leaveOpen: true);
            writer.WriteLine(json.Replace("\r", " ").Replace("\n", " "));
            Console.Out.WriteLine(reader.ReadLine());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new { ok = false, error = "nightOwl isn't running or didn't answer: " + ex.Message }));
            return 1;
        }
    }
}
