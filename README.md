# nightOwl

A Windows 11 shell customizer: one tray app and one settings window for every visual tweak in the
[requirements doc](https://claude.ai/artifact/8ra58xWArwz1TMPPyvh5Hf).

| Feature | How | Status |
| --- | --- | --- |
| Tray app, settings window, profiles, logging, reset hotkey (Phase 0) | nightOwl | Done |
| F2 Auto-hide the taskbar when an app is full screen | nightOwl | Done |
| F2 option: also hide while a maximised window is in front (switches Windows' own auto-hide) | nightOwl | Done, tested |
| F6 Live wallpapers: video, GIF/images, HTML and web pages (interactive) | nightOwl | Done |
| F1 Translucent taskbar: clear, blur, acrylic or tint; optionally clear until a window is maximised | Windhawk: Taskbar Background Helper + Taskbar Styler | Done, tested |
| F1.4 Notification center and Quick Settings themes | Windhawk: Notification Center Styler | Loads (needs the folder access below) |
| F1.3 Start menu themes and layout | Windhawk: Start Menu Styler | Done, tested (WindowGlass) |
| F3 Taskbar height, icon size, button width | Windhawk: Taskbar height and icon size | Done, tested |
| F4 Dock-style hover magnification | Windhawk: Taskbar Dock Animation Plus | Loads; check by hovering |
| F5 Styler: built-in themes, custom rules, JSON import/export | Windhawk: Windows 11 Taskbar Styler | Done, tested |
| F7 Translucent File Explorer and Settings app | Windhawk: File Explorer Styler + Settings Styler | Explorer tested; Settings wired |
| Phase 5: build checks, coexistence warnings, installer, clean uninstall (NF.7–NF.9) | nightOwl + Inno Setup | Done; code signing skipped (personal tool) |

## Install

Run `dist\nightOwl-Setup-<version>.exe` from File Explorer. It installs for your account only (no admin prompt) into
`%LocalAppData%\Programs\nightOwl`, adds Start menu and desktop shortcuts, closes any running nightOwl and starts the
new one. It bundles .NET, so nothing else needs installing.

**Uninstall** from Settings > Apps > Installed apps > nightOwl. It quits nightOwl, stops its Windhawk (unloading every
mod from Explorer, so the stock look returns at once), shows the taskbar, removes the start-with-Windows entry, and
optionally deletes your settings, themes and Windhawk mods. No reboot. A report goes to `%TEMP%\nightOwl-uninstall.txt`.

Build the installer with `& tools\build-installer.ps1` (needs Inno Setup 6: `winget install JRSoftware.InnoSetup --scope user`).

Code signing is skipped: this is a personal tool, a trusted certificate costs money, and a self-signed one means
adding a certificate to Windows' trust settings. SmartScreen may warn once about the setup file.

## Run it

Double-click the **nightOwl** shortcut on the desktop or in the Start menu. It lives in the tray;
click the owl to open settings. It registers itself to start with Windows. **Ctrl+Alt+Shift+R** turns every
effect off and restores stock Windows.

Start nightOwl from Explorer, the Start menu or sign-in, **not from inside another app** such as a terminal hosted
by a packaged (MSIX) app. Such apps redirect AppData writes into their private storage, which Explorer can't see.
nightOwl detects this, shows a warning, and keeps the Windhawk features off. On a normal start it moves any data
stranded that way back into the real AppData.

Build from source (needs the .NET 10 SDK):

```bash
dotnet publish src/NightOwl -c Release -o dist/nightOwl
```

To update a running nightOwl without restarting it by hand, publish to another folder (add
`-r win-x64 --self-contained true` when updating the installed copy) and run
`nightOwl.exe --control "{\"cmd\":\"action\",\"name\":\"update\",\"from\":\"C:\\path\\to\\new\\build\"}"`.

## How it works

- **Core** (`nightOwl.exe`): WinForms tray app. It owns the config, watches windows (`DesktopWatcher`), hides
  taskbars for F2 (`TaskbarHider`), drives the wallpaper process (`WallpaperManager`) and drives Windhawk
  (`Shell/`).
- **Settings window**: a Mica window hosting the web UI (`ui/`) in WebView2, created on demand and disposed on
  close.
- **Wallpaper process** (`nightOwl.exe --wallpaper`): WebView2 windows parented behind the desktop icons
  (24H2+ Progman layering or legacy WorkerW).
- **Taskbar guard** (`nightOwl.exe --guard`): runs only while a taskbar is hidden. If the core dies in any way, it
  shows every taskbar again (40 ms in testing).
- **Phases 2–4 through Windhawk**: nightOwl never loads code into Explorer itself. A **portable** Windhawk
  1.7.3 lives in `%LocalAppData%\nightOwl\windhawk`, so its config is plain `.ini` files nightOwl writes without
  admin rights (`Shell/WindhawkBridge.cs`). Each mod has `AppData\Engine\Mods\<id>.ini` with `[Mod]` and
  `[Settings]` sections. Bumping `SettingsChangeTime` makes the running engine apply changes live. The engine
  is limited to `explorer.exe`, `ShellExperienceHost.exe`, `ShellHost.exe`, `SystemSettings.exe`,
  `StartMenuExperienceHost.exe` and `SearchHost.exe` (`Exclude=*` plus an Include list). The notification center
  and Start menu are sandboxed (AppContainer) processes, so nightOwl grants "ALL APPLICATION PACKAGES" read access
  to the Windhawk folder; without it Windows refuses to load the mods there. nightOwl starts Windhawk (`-tray-only`) when a feature is on and stops it
  (`-exit`) when none are.
- **Coexistence check** (NF.9): Home warns about Lively, Wallpaper Engine, TranslucentTB, another Windhawk,
  RoundedTB, TaskbarX, Mica For Everyone, Start11, WindowBlinds, ExplorerPatcher and StartAllBack.
- **Control channel**: `nightOwl.exe --control "<json>"` talks to the running instance over a pipe only your
  account can open (`ControlPipe.cs`). Commands are `status`, `set`, `log`, and actions (`confirmBuild`,
  `reset`, `pause`, `resume`, `leaveSafeMode`, `openSettings`, `nextWallpaper`, `quit`, `update`).

### Mods used

| Mod | Author | Feature |
| --- | --- | --- |
| Taskbar Background Helper | m417z (Windhawk's author) | F1 background behind the taskbar |
| Windows 11 Taskbar Styler | m417z | F1 (clears the XAML background), F5 |
| Taskbar height and icon size | m417z | F3 |
| Windows 11 Notification Center Styler | m417z | F1.4 |
| Windows 11 Start Menu Styler | m417z | F1.3 (Start menu) |
| Windows 11 File Explorer Styler | m417z | F7 |
| Windows 11 Settings Styler | m417z | F7.3 |
| Taskbar Dock Animation Plus | incconutwo (community) | F4 |
| Translucent Windows | Undisputed00x (community) | Installed, kept off |

Mods are installed and updated in Windhawk's own window (the **Open Windhawk** button), because Windhawk 1.7.3
has no command line for installing.

## Risks and how they're handled

| Risk (spec section 8) | Handling |
| --- | --- |
| Windows updates break shell internals (this PC is on an Insider build) | Shell mods stay off on a Windows build you haven't confirmed. After an update, Home asks you to turn them back on; broken mods get updated in Windhawk by their authors. |
| Injected code crashes Explorer | Two Explorer restarts within a minute while mods are on puts nightOwl in safe mode: every mod off, Windhawk stopped, a "Try again" button. Tested by simulating the restart signal. |
| Antivirus flags DLL injection | nightOwl loads nothing into other processes. Windhawk does, and its installer is signed by its author (verified). Five of the seven active mods are by Windhawk's author. |
| Taskbar left hidden after a crash | The taskbar guard process restores it when the core exits. |
| 24H2 desktop layering | Both wallpaper layering paths, detected at runtime. |
| Battery drain | Wallpapers pause on battery and when covered. |
| Translucency hurts readability | Mica is the default backdrop for Explorer and Settings. |

## Known limits

- **HEVC (H.265) videos don't play on this PC.** No HEVC decoder is installed. nightOwl flags these files; install
  *HEVC Video Extensions* from the Microsoft Store, or use an H.264, VP9 or AV1 copy.
- Not tested here: hover magnification by eye (simulated mouse moves don't trigger it), the notification center
  and Settings app themes, multiple monitors, and a real Explorer crash loop.
- F1.3 is partial: Background Helper offers "always" or "only when a window is maximised" (clear otherwise).
  There's no separate look for Start, Search or flyouts.
- F4.3: Dock Animation Plus has a curve setting but no duration setting.

## Measured on the reference laptop (Core Ultra 5 125H, Windows 26H2 build 26300)

| Check | Result | Target |
| --- | --- | --- |
| Core idle (settings closed) | 0.013 % CPU, 57 MB RAM | NF.1: < 1 % CPU, < 80 MB |
| 1080p VP9 video wallpaper playing | 1.35 % CPU (all wallpaper processes) | NF.2: < 5 % CPU |
| Wallpaper covered by a maximised window | 0.02 % CPU | NF.3: near 0 |
| Taskbar hides / returns around a full-screen app | 10–20 ms / 2–62 ms | F2.3: < 300 ms |
| Taskbar restored after nightOwl is force-killed | 40 ms | NF.6 |
| Large preset (64 px): taskbar and work area | 60 → 80 physical px; work area bottom 1140 → 1120 | F3.3 |

## Test helpers (`tools/`)

| Script | What it does |
| --- | --- |
| `ctl.ps1 '<json>'` | Sends a `--control` command to the running nightOwl |
| `build-installer.ps1` | Publishes self-contained and builds `dist\nightOwl-Setup-<version>.exe` |
| `capture-taskbar.ps1` | Screenshots the taskbar strip |
| `capture-desktop.ps1` / `capture-window.ps1` | Screenshot the desktop / the settings window |
| `fullscreen-test.ps1 [-EdgeTest]` | Times taskbar hide/show/edge reveal around a borderless full-screen window |
| `maximize-window.ps1` | A window (own process) that goes normal → maximised → normal → closed, for the maximised auto-hide option |
| `guard-test.ps1 -Exe <path>` | Hides the taskbar, force-kills nightOwl, checks the guard restores it |
| `desktop-tree.ps1` | Prints Progman's child windows (wallpaper layering) |
| `input-test.ps1` / `hotkey-test.ps1` | Mouse forwarding to web wallpapers / presses the reset hotkey |
| `probe-wallpaper.mjs` | Evaluates JS in the WebViews (needs `NIGHTOWL_DEBUGPORT=9333`) |
| `serve-ui.mjs` | Serves `ui/` with mock data for browser previews |
| `IconGen/` | Draws the app icon |
