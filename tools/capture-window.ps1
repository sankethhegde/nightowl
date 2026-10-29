# Test helper: screenshots the on-screen area of the first visible top-level window with the given title.
param([string]$Title = "nightOwl", [string]$Out = "$env:TEMP\nightowl-window.png")
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace NightOwlTest -Name Win -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
public struct RECT { public int L, T, R, B; }
'@
[void][NightOwlTest.Win]::SetProcessDPIAware()
$h = [NightOwlTest.Win]::FindWindow([NullString]::Value, $Title)
if ($h -eq [IntPtr]::Zero) { "window '$Title' not found"; exit 1 }
[void][NightOwlTest.Win]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 600
$r = New-Object NightOwlTest.Win+RECT
[void][NightOwlTest.Win]::DwmGetWindowAttribute($h, 9, [ref]$r, 16)
$w = $r.R - $r.L; $hgt = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $hgt))
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
$Out
