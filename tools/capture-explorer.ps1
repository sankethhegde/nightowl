# Test helper: opens a File Explorer window, screenshots it (scaled), and closes it.
param([string]$Out = "$env:TEMP\nightowl-explorer.png", [string]$Folder = "C:\Users\Sanketh\Desktop\Claude\nightowl", [double]$Scale = 0.5)
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace NightOwlX -Name W -MemberDefinition '[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s); [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n); [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l); public struct RECT { public int L, T, R, B; }'
[void][NightOwlX.W]::SetProcessDPIAware()
Start-Process explorer.exe $Folder; Start-Sleep 3
$h = [NightOwlX.W]::GetForegroundWindow(); $sb = New-Object System.Text.StringBuilder 64; [void][NightOwlX.W]::GetClassName($h, $sb, 64)
if ("$sb" -ne "CabinetWClass") { "foreground is $sb, not Explorer"; exit 1 }
$r = New-Object NightOwlX.W+RECT; [void][NightOwlX.W]::DwmGetWindowAttribute($h, 9, [ref]$r, 16)
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$small = New-Object System.Drawing.Bitmap $bmp, ([int]($bmp.Width * $Scale)), ([int]($bmp.Height * $Scale)); $small.Save($Out)
$g.Dispose(); $bmp.Dispose(); $small.Dispose()
[void][NightOwlX.W]::PostMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
$Out
