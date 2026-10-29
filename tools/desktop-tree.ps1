# Test helper: prints Progman's child windows (class, visibility, rect, owning process) to check wallpaper layering.
Add-Type -Namespace NightOwlTest -Name Tree -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string t);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
public struct RECT { public int L, T, R, B; }
'@
[void][NightOwlTest.Tree]::SetProcessDPIAware()
function Describe($h, $indent) {
    $sb = New-Object System.Text.StringBuilder 256
    [void][NightOwlTest.Tree]::GetClassName($h, $sb, 256)
    $r = New-Object NightOwlTest.Tree+RECT
    [void][NightOwlTest.Tree]::GetWindowRect($h, [ref]$r)
    $procId = 0; [void][NightOwlTest.Tree]::GetWindowThreadProcessId($h, [ref]$procId)
    $name = (Get-Process -Id $procId -ErrorAction SilentlyContinue).ProcessName
    $ex = '{0:X}' -f [NightOwlTest.Tree]::GetWindowLong($h, -20)
    "{0}{1} 0x{2:X} vis={3} rect={4},{5},{6}x{7} ex={8} [{9}]" -f $indent, $sb, [int64]$h, [NightOwlTest.Tree]::IsWindowVisible($h), $r.L, $r.T, ($r.R-$r.L), ($r.B-$r.T), $ex, $name
}
$progman = [NightOwlTest.Tree]::FindWindow("Progman", [NullString]::Value)
Describe $progman ""
# Children in z-order (GW_CHILD then GW_HWNDNEXT)
$c = [NightOwlTest.Tree]::GetWindow($progman, 5)
while ($c -ne [IntPtr]::Zero) { Describe $c "  "; $c = [NightOwlTest.Tree]::GetWindow($c, 2) }
