using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NightOwl;

/// <summary>
/// The settings window (UI.2–UI.8): a Mica window hosting the web UI in WebView2. It is created on
/// demand and fully disposed on close, so the browser processes only exist while it is open (NF.1).
/// </summary>
internal sealed class SettingsWindow : Form
{
    readonly CoreApp _app;
    readonly WebView2 _web;
    bool _ready;

    public SettingsWindow(CoreApp app)
    {
        _app = app;
        Text = "nightOwl";
        Icon = CoreApp.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = LogicalToDeviceUnits(new Size(1040, 700));
        MinimumSize = LogicalToDeviceUnits(new Size(760, 520));
        BackColor = Color.Black;
        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
        Controls.Add(_web);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnPrefsChanged;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyBackdrop();
    }

    void ApplyBackdrop()
    {
        Native.UseDarkTitleBar(Handle, WinInfo.AppsDark);
        var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Native.DwmExtendFrameIntoClientArea(Handle, ref m);
        int mica = 2; // DWMSBT_MAINWINDOW
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
    }

    void OnPrefsChanged(object? s, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (IsHandleCreated) BeginInvoke(() => { ApplyBackdrop(); PushState(); });
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            var options = new CoreWebView2EnvironmentOptions();
            if (int.TryParse(Environment.GetEnvironmentVariable("NIGHTOWL_DEBUGPORT"), out var port))
                options.AdditionalBrowserArguments = "--remote-debugging-port=" + (port + 1);
            var env = await CoreWebView2Environment.CreateAsync(null, Paths.WebViewData("settings"), options);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("NIGHTOWL_DEVTOOLS") == "1";
            core.Settings.IsZoomControlEnabled = false;
#if !DEBUG
            core.Settings.AreDefaultContextMenusEnabled = false;
#endif
            core.SetVirtualHostNameToFolderMapping("ui.nightowl", Path.Combine(Paths.AppDir, "ui"), CoreWebView2HostResourceAccessKind.Allow);
            Directory.CreateDirectory(Paths.Thumbs);
            core.SetVirtualHostNameToFolderMapping("thumbs.nightowl", Paths.Thumbs, CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += OnMessage;
            core.NewWindowRequested += (_, a) =>
            {
                a.Handled = true;
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(a.Uri) { UseShellExecute = true }); } catch { }
            };
            core.Navigate("https://ui.nightowl/index.html");
        }
        catch (Exception ex)
        {
            Log.Error("settings window failed to start WebView2", ex);
            MessageBox.Show(this, "The settings window needs the Microsoft Edge WebView2 Runtime.\n\n" + ex.Message, "nightOwl",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    public void PushState()
    {
        if (!_ready || _web.CoreWebView2 == null) return;
        Post(_app.BuildState());
    }

    void Post(object msg)
    {
        try { _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(msg, ConfigStore.Wire)); }
        catch (Exception ex) { Log.Error("posting to settings UI failed", ex); }
    }

    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonNode? msg;
        try { msg = JsonNode.Parse(e.WebMessageAsJson); }
        catch { return; }
        if (msg == null) return;
        var type = (string?)msg["type"];
        try
        {
            switch (type)
            {
                case "ready":
                    _ready = true;
                    PushState();
                    break;
                case "setConfig":
                    _app.Store.Replace(msg["config"]!);
                    break;
                case "addFiles":
                    using (var dlg = new OpenFileDialog
                    {
                        Multiselect = true,
                        Title = "Add wallpapers",
                        Filter = "Wallpapers|*.mp4;*.webm;*.mkv;*.mov;*.m4v;*.gif;*.webp;*.png;*.jpg;*.jpeg;*.avif;*.html;*.htm|All files|*.*",
                    })
                    {
                        if (dlg.ShowDialog(this) == DialogResult.OK) _app.AddToLibrary(dlg.FileNames);
                    }
                    break;
                case "dropFiles":
                    var paths = new List<string>();
                    if (e.AdditionalObjects != null)
                        foreach (var o in e.AdditionalObjects)
                            if (o is CoreWebView2File f) paths.Add(f.Path);
                    _app.AddToLibrary(paths);
                    break;
                case "addUrl":
                    _app.AddToLibrary(new[] { (string)msg["url"]! });
                    break;
                case "removeItem":
                    _app.RemoveFromLibrary((string)msg["id"]!);
                    break;
                case "thumb":
                    var id = (string)msg["id"]!;
                    var item = _app.Store.Config.Library.FirstOrDefault(i => i.Id == id);
                    bool ok = item != null && await Thumbnails.EnsureAsync(item);
                    Post(new { type = "thumb", id, ok });
                    break;
                case "nextWallpaper": _app.Wallpaper.Next(); break;
                case "pauseAll": _app.SetPausedAll((bool)msg["value"]!); break;
                case "resetAll": _app.ResetToStock(); break;
                case "setStartup": _app.SetStartup((bool)msg["value"]!); break;
                case "profile": _app.ProfileAction((string)msg["action"]!, (string?)msg["name"], (string?)msg["newName"]); break;
                case "shell":
                    switch ((string?)msg["action"])
                    {
                        case "confirmBuild": _app.Shell.ConfirmBuild(); break;
                        case "leaveSafeMode": _app.Shell.LeaveSafeMode(); break;
                        case "openWindhawk": _app.Shell.Windhawk.OpenUi(); break;
                        case "refresh": _app.Store.Update(_ => { }); break;
                    }
                    break;
                case "exportTheme":
                    using (var dlg = new SaveFileDialog { Title = "Export taskbar theme", Filter = "nightOwl theme|*.json", FileName = "my-taskbar-theme.json" })
                    {
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            var st = _app.Store.Config.Active.Styler;
                            var theme = new ThemeFile { Name = Path.GetFileNameWithoutExtension(dlg.FileName), BaseTheme = st.Theme, Rules = st.Rules, Constants = st.Constants };
                            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(theme, ConfigStore.Json));
                            Post(new { type = "toast", message = "Theme exported." });
                        }
                    }
                    break;
                case "importTheme":
                    using (var dlg = new OpenFileDialog { Title = "Import taskbar theme", Filter = "nightOwl theme|*.json|All files|*.*" })
                    {
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            var theme = JsonSerializer.Deserialize<ThemeFile>(File.ReadAllText(dlg.FileName), ConfigStore.Json)
                                ?? throw new InvalidDataException("Not a theme file.");
                            _app.Store.Update(c =>
                            {
                                var st = c.Active.Styler;
                                st.Theme = theme.BaseTheme ?? "";
                                st.Rules = theme.Rules ?? new();
                                st.Constants = theme.Constants ?? new();
                                st.Enabled = true;
                            });
                            Post(new { type = "toast", message = $"Imported “{theme.Name}”." });
                        }
                    }
                    break;
                case "openFolder":
                    var which = (string?)msg["which"];
                    var dir = which == "logs" ? Paths.Logs : Paths.Roaming;
                    Directory.CreateDirectory(dir);
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"settings message {type} failed", ex);
            Post(new { type = "error", message = ex.Message });
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnPrefsChanged;
        _web.Dispose();
        base.OnFormClosed(e);
    }
}
