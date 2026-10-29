# Test helper for interactive web wallpapers: shows the desktop, injects mouse moves and a click
# over an empty part of it with SendInput, and prints what the wallpaper page received.
param([int]$X = 900, [int]$Y = 500)
Add-Type -Namespace NightOwlTest -Name Input -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
public struct POINT { public int X, Y; }
'@
[void][NightOwlTest.Input]::SetProcessDPIAware()
$env:NIGHTOWL_DEBUGPORT = "9333"
$probe = "C:\Users\Sanketh\Desktop\Claude\nightowl\tools\probe-wallpaper.mjs"
node $probe "window.__no = {move: 0, down: 0, last: null}; addEventListener('mousemove', e => { __no.move++; __no.last = [e.clientX, e.clientY]; }); addEventListener('mousedown', () => __no.down++); 'armed'"
$saved = New-Object NightOwlTest.Input+POINT; [void][NightOwlTest.Input]::GetCursorPos([ref]$saved)
$shell = New-Object -ComObject Shell.Application
$shell.MinimizeAll(); Start-Sleep -Seconds 2
[void][NightOwlTest.Input]::SetCursorPos($X, $Y); Start-Sleep -Milliseconds 100
for ($i = 0; $i -lt 20; $i++) { [NightOwlTest.Input]::mouse_event(0x1, 3, 1, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 15 }
# A right-button click would open the desktop menu; a left click on empty desktop only clears selection.
[NightOwlTest.Input]::mouse_event(0x2, 0, 0, 0, [IntPtr]::Zero); [NightOwlTest.Input]::mouse_event(0x4, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 400
node $probe "JSON.stringify(window.__no)"
$shell.UndoMinimizeAll()
[void][NightOwlTest.Input]::SetCursorPos($saved.X, $saved.Y)
