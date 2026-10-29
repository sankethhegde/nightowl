# Test helper: hides the taskbar with a full-screen window, force-kills nightOwl, and checks the guard restores it.
param([string]$Exe)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace NightOwlTest -Name Guard -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
'@
[void][NightOwlTest.Guard]::SetProcessDPIAware()
$tray = [NightOwlTest.Guard]::FindWindow("Shell_TrayWnd", [NullString]::Value)
function Pump([int]$ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 10 } }
Start-Process $Exe; Pump 5000
Start-Process $Exe -ArgumentList '--control', '"{\"cmd\":\"set\",\"path\":\"autoHide.enabled\",\"value\":true}"' -Wait -NoNewWindow -RedirectStandardOutput "$env:TEMP\guard-ctl.txt"
"enable auto-hide: $(Get-Content "$env:TEMP\guard-ctl.txt")"; Pump 500
$f = New-Object System.Windows.Forms.Form
$f.FormBorderStyle = 'None'; $f.StartPosition = 'Manual'; $f.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$f.Show(); [void][NightOwlTest.Guard]::SetForegroundWindow($f.Handle); Pump 800
"taskbar hidden by nightOwl: $(-not [NightOwlTest.Guard]::IsWindowVisible($tray))"
"guard processes: $((Get-CimInstance Win32_Process -Filter "Name='nightOwl.exe'" | Where-Object { $_.CommandLine -match '--guard' } | Measure-Object).Count)"
Get-CimInstance Win32_Process -Filter "Name='nightOwl.exe'" | Where-Object { $_.CommandLine -notmatch '--guard|--wallpaper' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not [NightOwlTest.Guard]::IsWindowVisible($tray) -and $sw.ElapsedMilliseconds -lt 3000) { Pump 10 }
"after force-kill, taskbar visible: $([NightOwlTest.Guard]::IsWindowVisible($tray)) after $($sw.ElapsedMilliseconds) ms (full-screen window still open)"
$f.Close(); $f.Dispose()
Get-Process nightOwl -ErrorAction SilentlyContinue | Stop-Process -Force
