# Test helper: send one --control command to the running nightOwl and print the JSON reply.
# Usage: & tools\ctl.ps1 '{"cmd":"status"}'
param([Parameter(Mandatory)][string]$Json, [string]$Exe = "$env:LOCALAPPDATA\Programs\nightOwl\nightOwl.exe")
$out = Join-Path $env:TEMP "nightowl-ctl-$PID.txt"
Start-Process $Exe -ArgumentList '--control', ('"' + $Json.Replace('"', '\"') + '"') -Wait -NoNewWindow -RedirectStandardOutput $out
Get-Content $out -Raw
Remove-Item $out -ErrorAction SilentlyContinue
