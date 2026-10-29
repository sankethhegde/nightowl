# Test helper: a window in its own process that comes to the front, then after 3 s maximises, after 6 s
# restores and after 9 s closes. Run it with Start-Process and watch nightOwl's log meanwhile.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace NightOwlWin -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, int f, IntPtr e);
public static void Front(IntPtr h) { keybd_event(0x12, 0, 0, IntPtr.Zero); keybd_event(0x12, 0, 2, IntPtr.Zero); SetForegroundWindow(h); }
'@
$f = New-Object System.Windows.Forms.Form -Property @{ Text = 'nightOwl maximise test'; Width = 600; Height = 400; StartPosition = 'CenterScreen' }
$step = 0
$t = New-Object System.Windows.Forms.Timer -Property @{ Interval = 3000 }
$t.Add_Tick({
  $script:step++
  switch ($script:step) { 1 { $f.WindowState = 'Maximized' } 2 { $f.WindowState = 'Normal' } default { $t.Stop(); $f.Close() } }
})
$f.Add_Shown({ [NightOwlWin.W]::Front($f.Handle); $t.Start() })
[System.Windows.Forms.Application]::Run($f)
