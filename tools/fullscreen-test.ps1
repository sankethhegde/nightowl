# Test helper for F2: shows a borderless window covering the primary monitor (like a borderless game),
# measures how long until the taskbar hides, then closes it and measures how long until it returns.
param([int]$HoldMs = 1500, [switch]$EdgeTest)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace NightOwlTest -Name Fs -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
'@
[void][NightOwlTest.Fs]::SetProcessDPIAware()
$tray = [NightOwlTest.Fs]::FindWindow("Shell_TrayWnd", [NullString]::Value)
function WaitFor([bool]$visible, [int]$timeoutMs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        [System.Windows.Forms.Application]::DoEvents()
        if ([NightOwlTest.Fs]::IsWindowVisible($tray) -eq $visible) { return $sw.ElapsedMilliseconds }
        Start-Sleep -Milliseconds 5
    }
    return -1
}
"taskbar visible before: $([NightOwlTest.Fs]::IsWindowVisible($tray))"
$f = New-Object System.Windows.Forms.Form
$f.FormBorderStyle = 'None'; $f.StartPosition = 'Manual'; $f.BackColor = [System.Drawing.Color]::FromArgb(30, 27, 75)
$f.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $f.Text = "nightOwl fullscreen test"
$f.Show(); [void][NightOwlTest.Fs]::SetForegroundWindow($f.Handle); $f.Activate()
"hide after ms: $(WaitFor $false 3000)"
$sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $HoldMs) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
"still hidden after hold: $(-not [NightOwlTest.Fs]::IsWindowVisible($tray))"
if ($EdgeTest) {
    $saved = [System.Windows.Forms.Cursor]::Position
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point ($b.X + $b.Width / 2), ($b.Bottom - 1)
    "edge reveal after ms: $(WaitFor $true 2000)"
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point ($b.X + $b.Width / 2), ($b.Y + $b.Height / 2)
    "re-hidden after leaving ms: $(WaitFor $false 2000)"
    [System.Windows.Forms.Cursor]::Position = $saved
}
$f.Close(); $f.Dispose()
"show after ms: $(WaitFor $true 3000)"
