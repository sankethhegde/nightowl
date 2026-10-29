# Test helper: screenshots the bottom strip of the primary monitor (the taskbar), in physical pixels.
param([string]$Out = "$env:TEMP\nightowl-taskbar.png", [int]$Height = 140, [switch]$ShowDesktop)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace NightOwlTest -Name TbDpi -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[void][NightOwlTest.TbDpi]::SetProcessDPIAware()
$shell = $null
if ($ShowDesktop) { $shell = New-Object -ComObject Shell.Application; $shell.MinimizeAll(); Start-Sleep -Seconds 2 }
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.X, $b.Bottom - $Height, 0, 0, (New-Object System.Drawing.Size $b.Width, $Height))
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
if ($shell) { $shell.UndoMinimizeAll() }
$Out
