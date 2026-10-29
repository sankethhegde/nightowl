using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static NightOwl.Native;

namespace NightOwl.Wallpaper;

/// <summary>
/// The wallpaper process (nightOwl.exe --wallpaper). Kept separate from the core so a crash or a
/// heavy web page never takes down the tray app; reads commands from stdin and exits when the core goes away.
/// </summary>
internal sealed class WallpaperHost : ApplicationContext
{
    readonly Control _invoker = new();
    readonly Dictionary<string, Surface> _surfaces = new();
    readonly InputSink _input;
    CoreWebView2Environment? _env;
    bool _quitting;

    public WallpaperHost(int parentPid)
    {
        _invoker.CreateControl();
        _input = new InputSink(this);

        var reader = new Thread(ReadLoop) { IsBackground = true, Name = "stdin" };
        reader.Start();

        if (parentPid > 0)
        {
            try
            {
                var parent = Process.GetProcessById(parentPid);
                parent.EnableRaisingEvents = true;
                parent.Exited += (_, _) => Post(() => Quit("core exited"));
            }
            catch { Post(() => Quit("core not running")); }
        }
        Log.Info($"wallpaper host started, desktop layering will be probed on first surface");
    }

    void Post(Action a)
    {
        try { if (!_invoker.IsDisposed) _invoker.BeginInvoke(a); } catch { }
    }

    void ReadLoop()
    {
        try
        {
            string? line;
            while ((line = Console.In.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                WallpaperCommand? cmd;
                try { cmd = JsonSerializer.Deserialize<WallpaperCommand>(line, ConfigStore.Json); }
                catch (Exception ex) { Log.Error("bad command", ex); continue; }
                if (cmd != null) Post(() => Enqueue(cmd));
            }
        }
        catch (Exception ex) { Log.Error("stdin read failed", ex); }
        Post(() => Quit("stdin closed"));
    }

    readonly Queue<WallpaperCommand> _queue = new();
    bool _busy;

    /// <summary>Commands run one at a time: a "state" must not overtake the "set" that creates its surface.</summary>
    async void Enqueue(WallpaperCommand cmd)
    {
        _queue.Enqueue(cmd);
        if (_busy) return;
        _busy = true;
        while (_queue.Count > 0) await Handle(_queue.Dequeue());
        _busy = false;
    }

    async Task Handle(WallpaperCommand cmd)
    {
        try
        {
            switch (cmd.Cmd)
            {
                case "set": await SetSurfaces(cmd.Surfaces ?? new()); break;
                case "state":
                    foreach (var (id, state) in cmd.States ?? new())
                        if (_surfaces.TryGetValue(id, out var s)) await s.SetState(state);
                    break;
                case "quit": Quit("asked to quit"); break;
            }
        }
        catch (Exception ex) { Log.Error($"command {cmd.Cmd} failed", ex); }
    }

    async Task SetSurfaces(List<SurfaceSpec> specs)
    {
        var args = "--autoplay-policy=no-user-gesture-required --disable-features=msSmartScreenProtection";
        if (Environment.GetEnvironmentVariable("NIGHTOWL_DEBUGPORT") is { Length: > 0 } port) args += " --remote-debugging-port=" + port;
        _env ??= await CoreWebView2Environment.CreateAsync(null, Paths.WebViewData("wallpaper"), new CoreWebView2EnvironmentOptions(args));

        foreach (var id in _surfaces.Keys.Except(specs.Select(s => s.Id)).ToList())
        {
            _surfaces[id].Dispose();
            _surfaces.Remove(id);
        }
        foreach (var spec in specs)
        {
            if (!_surfaces.TryGetValue(spec.Id, out var s))
            {
                s = new Surface(spec.Id);
                _surfaces[spec.Id] = s;
                await s.Init(_env);
            }
            await s.Apply(spec);
        }
        Log.Info($"surfaces: {string.Join(", ", specs.Select(s => $"{s.Id}={System.IO.Path.GetFileName(s.Path)}"))} ({DesktopLayer.LastMode})");
        _input.Enable(_surfaces.Values.Any(s => s.IsInteractive));
    }

    internal IEnumerable<Surface> Surfaces => _surfaces.Values;

    /// <summary>Report an event to the core (one JSON object per stdout line).</summary>
    internal static void Emit(object evt)
    {
        try
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(evt));
            Console.Out.Flush();
        }
        catch { }
    }

    void Quit(string why)
    {
        if (_quitting) return;
        _quitting = true;
        Log.Info($"wallpaper host quitting: {why}");
        foreach (var s in _surfaces.Values) s.Dispose();
        _surfaces.Clear();
        _input.Dispose();
        RepaintStaticWallpaper();
        ExitThread();
    }
}

/// <summary>A borderless window parented into the desktop layer, hosting one WebView2.</summary>
internal sealed class Surface : IDisposable
{
    public string Id { get; }
    readonly Form _form;
    readonly WebView2 _web;
    SurfaceSpec? _spec;
    string _state = "play";
    IntPtr _renderHwnd;
    bool _attached;

    public Surface(string id)
    {
        Id = id;
        _form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            BackColor = Color.Black,
            Text = "nightOwl wallpaper " + id,
        };
        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
        _form.Controls.Add(_web);
    }

    public bool IsInteractive => _spec is { Interactive: true, Type: "html" or "url" };
    public bool WantsInput => IsInteractive && _state == "play";
    public Rectangle ScreenBounds => _spec == null ? Rectangle.Empty : new(_spec.X, _spec.Y, _spec.W, _spec.H);

    public async Task Init(CoreWebView2Environment env)
    {
        _form.CreateControl();
        _ = _form.Handle;
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        var st = core.Settings;
        st.AreDefaultContextMenusEnabled = false;
        st.AreDevToolsEnabled = false;
        st.IsStatusBarEnabled = false;
        st.IsZoomControlEnabled = false;
        st.AreBrowserAcceleratorKeysEnabled = false;
        st.IsSwipeNavigationEnabled = false;
        core.SetVirtualHostNameToFolderMapping("app.nightowl", Path.Combine(Paths.AppDir, "player"), CoreWebView2HostResourceAccessKind.Allow);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.ProcessFailed += (_, e) => Log.Warn($"surface {Id}: webview process failed ({e.ProcessFailedKind})");
        core.NavigationCompleted += (_, _) => { _renderHwnd = IntPtr.Zero; SendPlayerSource(); };
        core.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                if (doc.RootElement.GetProperty("event").GetString() == "error" && _spec != null)
                {
                    int code = doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
                    Log.Warn($"surface {Id}: media error {code} for {_spec.Path}");
                    WallpaperHost.Emit(new { @event = "error", id = Id, path = _spec.Path, code });
                }
            }
            catch { }
        };
    }

    public async Task Apply(SurfaceSpec spec)
    {
        var prev = _spec;
        _spec = spec;
        var screen = new RECT { Left = spec.X, Top = spec.Y, Right = spec.X + spec.W, Bottom = spec.Y + spec.H };

        if (!_attached || prev == null || prev.X != spec.X || prev.Y != spec.Y || prev.W != spec.W || prev.H != spec.H)
        {
            _attached = DesktopLayer.Attach(_form.Handle, screen);
            if (!_form.Visible) _form.Show();
        }

        var core = _web.CoreWebView2;
        core.IsMuted = spec.Muted || spec.Volume == 0;
        bool sameSource = prev != null && prev.Path == spec.Path && prev.Type == spec.Type;
        if (!sameSource)
        {
            if (spec.Type == "url")
            {
                core.Navigate(spec.Path);
            }
            else if (spec.Type == "html")
            {
                core.SetVirtualHostNameToFolderMapping("site.nightowl", Path.GetDirectoryName(spec.Path)!, CoreWebView2HostResourceAccessKind.Allow);
                core.Navigate("https://site.nightowl/" + Uri.EscapeDataString(Path.GetFileName(spec.Path)));
            }
            else
            {
                core.SetVirtualHostNameToFolderMapping("media.nightowl", Path.GetDirectoryName(spec.Path)!, CoreWebView2HostResourceAccessKind.Allow);
                if (core.Source?.StartsWith("https://app.nightowl/player.html") == true) SendPlayerSource();
                else core.Navigate("https://app.nightowl/player.html");
            }
        }
        else SendPlayerSource();
        await Task.CompletedTask;
    }

    void SendPlayerSource()
    {
        if (_spec == null || _spec.Type is "html" or "url") return;
        var msg = new
        {
            cmd = "load",
            src = "https://media.nightowl/" + Uri.EscapeDataString(Path.GetFileName(_spec.Path)),
            type = _spec.Type,
            fit = _spec.Fit,
            muted = _spec.Muted,
            volume = _spec.Volume / 100.0,
            playing = _state == "play",
        };
        try { _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(msg)); } catch { }
    }

    void SendPlayer(string cmd)
    {
        try { _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { cmd })); } catch { }
    }

    /// <summary>play | pause (video keeps its frame) | hide (covered: drop to ~0 GPU, static wallpaper shows).</summary>
    public async Task SetState(string state)
    {
        if (_spec == null || state == _state) return;
        var core = _web.CoreWebView2;
        bool isVideo = _spec.Type == "video";
        bool hideAll = state == "hide" || (state == "pause" && !isVideo);
        Log.Info($"surface {Id}: {_state} -> {state}");
        _state = state;

        if (hideAll)
        {
            SendPlayer("pause");
            ShowWindow(_form.Handle, SW_HIDE);
            _web.Visible = false;
            try { await core.TrySuspendAsync(); } catch { }
            return;
        }

        // play, or a paused video that keeps showing its current frame.
        if (core.IsSuspended) core.Resume();
        _web.Visible = true;
        ShowWindow(_form.Handle, SW_SHOWNA);
        SendPlayer(state == "play" ? "play" : "pause");
    }

    /// <summary>Chromium's input window, for forwarding desktop mouse input to interactive web wallpapers.</summary>
    public IntPtr RenderWindow()
    {
        if (_renderHwnd != IntPtr.Zero && IsWindow(_renderHwnd)) return _renderHwnd;
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(_form.Handle, (h, _) =>
        {
            if (ClassOf(h) == "Chrome_RenderWidgetHostHWND") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return _renderHwnd = found;
    }

    public void Dispose()
    {
        try { _web.Dispose(); } catch { }
        try { _form.Close(); _form.Dispose(); } catch { }
    }
}

/// <summary>
/// Raw mouse input (RIDEV_INPUTSINK) so we see the mouse while the desktop has focus, then forward
/// it to the interactive web wallpaper under the cursor (desktop icons keep their own clicks).
/// </summary>
internal sealed class InputSink : NativeWindow, IDisposable
{
    readonly WallpaperHost _host;
    bool _enabled;

    public InputSink(WallpaperHost host)
    {
        _host = host;
        CreateHandle(new CreateParams { Caption = "nightOwl input", Style = 0, ExStyle = (int)WS_EX_TOOLWINDOW });
    }

    public void Enable(bool on)
    {
        if (on == _enabled) return;
        var dev = new RAWINPUTDEVICE[]
        {
            new() { UsagePage = 1, Usage = 2, Flags = on ? RIDEV_INPUTSINK : 0x1 /* RIDEV_REMOVE */, Target = on ? Handle : IntPtr.Zero },
        };
        if (RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>())) _enabled = on;
        else Log.Warn("RegisterRawInputDevices failed");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT) { OnInput(m.LParam); }
        base.WndProc(ref m);
    }

    void OnInput(IntPtr hRaw)
    {
        uint size = 0;
        uint header = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(hRaw, RID_INPUT, IntPtr.Zero, ref size, header);
        if (size == 0) return;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(hRaw, RID_INPUT, buf, ref size, header) != size) return;
            var hdr = Marshal.PtrToStructure<RAWINPUTHEADER>(buf);
            if (hdr.Type != RIM_TYPEMOUSE) return;
            var mouse = Marshal.PtrToStructure<RAWMOUSE>(buf + (int)header);
            Forward(mouse.ButtonFlags);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    void Forward(ushort buttons)
    {
        if (!GetCursorPos(out var pt) || !DesktopLayer.IsDesktopAt(pt)) return;
        var surface = _host.Surfaces.FirstOrDefault(s => s.WantsInput && s.ScreenBounds.Contains(pt.X, pt.Y));
        if (surface == null) return;
        var target = surface.RenderWindow();
        if (target == IntPtr.Zero) return;
        var cp = pt;
        ScreenToClient(target, ref cp);
        var lp = MakeLParam(cp.X, cp.Y);

        if ((buttons & RI_MOUSE_LEFT_BUTTON_DOWN) != 0) PostMessage(target, WM_LBUTTONDOWN, new IntPtr(1), lp);
        else if ((buttons & RI_MOUSE_LEFT_BUTTON_UP) != 0) PostMessage(target, WM_LBUTTONUP, IntPtr.Zero, lp);
        else PostMessage(target, WM_MOUSEMOVE, IntPtr.Zero, lp);
    }

    public void Dispose()
    {
        Enable(false);
        DestroyHandle();
    }
}
