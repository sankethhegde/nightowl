# Publishes nightOwl self-contained (runs without a .NET install) and builds dist\nightOwl-Setup-<version>.exe.
# Needs Inno Setup 6 (winget install JRSoftware.InnoSetup --scope user).
param([string]$Out = "$PSScriptRoot\..\dist\publish")
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$proj = "$root\src\NightOwl\NightOwl.csproj"
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained true -o $Out -p:DebugType=none
if ($LASTEXITCODE) { throw "publish failed" }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found" }
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$(Resolve-Path $Out)" "$root\installer\nightOwl.iss"
if ($LASTEXITCODE) { throw "ISCC failed" }
Get-Item "$root\dist\nightOwl-Setup-$version.exe"
