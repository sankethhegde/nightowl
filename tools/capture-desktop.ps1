# Test helper: minimises every window, screenshots the desktop, then restores the windows.
param([string]$Out = "$env:TEMP\nightowl-desktop.png", [int]$Wait = 2)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace NightOwlTest -Name Dpi -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[void][NightOwlTest.Dpi]::SetProcessDPIAware()
$shell = New-Object -ComObject Shell.Application
$shell.MinimizeAll()
Start-Sleep -Seconds $Wait
$b = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
$shell.UndoMinimizeAll()
$Out
