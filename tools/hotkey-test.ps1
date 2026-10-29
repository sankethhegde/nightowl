# Test helper for UI.6: presses Ctrl+Alt+Shift+R (the reset-to-stock hotkey).
Add-Type -Namespace NightOwlTest -Name Keys -MemberDefinition '[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);'
$keys = 0x11, 0x12, 0x10, 0x52   # Ctrl, Alt, Shift, R
foreach ($k in $keys) { [NightOwlTest.Keys]::keybd_event($k, 0, 0, [IntPtr]::Zero) }
[array]::Reverse($keys)
foreach ($k in $keys) { [NightOwlTest.Keys]::keybd_event($k, 0, 2, [IntPtr]::Zero) }
"pressed Ctrl+Alt+Shift+R"
