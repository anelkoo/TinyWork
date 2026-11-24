# Builds every release package into export/:
#   TinyWorks-<ver>-windows-setup.exe        Windows installer (Inno Setup)
#   TinyWorks-<ver>-windows-x86_64.zip       Windows portable (unpack, run TinyWorks.exe)
#   tinyworks_<ver>_amd64.deb                Debian, Ubuntu, Mint ...
#   tinyworks-<ver>-1-x86_64.pkg.tar.zst     Arch, Manjaro, EndeavourOS ...
#   TinyWorks-<ver>-x86_64.AppImage          any other Linux (Fedora, openSUSE ...)
#   TinyWorks-<ver>-linux-x86_64.tar.gz      Linux portable (unpack, run ./TinyWorks/TinyWorks.x86_64)
# Needs: Godot 4.7.2 .NET with export templates, Python 3.14+, Inno Setup 6, WSL (for the AppImage)
# and tools/appimagetool-x86_64.AppImage. Usage: .\build.ps1 -Version 0.0.2
param(
    [string]$Version = "0.0.2",
    [string]$Godot = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
Remove-Item -Recurse -Force export -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force export/linux, export/windows | Out-Null

dotnet build -c Release | Out-Null
foreach ($preset in "Linux", "Windows") {
    $out = if ($preset -eq "Linux") { "export/linux/TinyWorks.x86_64" } else { "export/windows/TinyWorks.exe" }
    & $Godot --headless --path . --export-release $preset $out
    if (-not (Test-Path $out)) { throw "$preset export failed, see the output above" }
}

# Jumpscare frames and sound are loaded from a FramesAndSound folder next to the game (gitignored, local only).
if (-not (Test-Path FramesAndSound)) { throw "FramesAndSound folder missing, the jumpscare would not work" }
foreach ($d in "export/linux", "export/windows") { Copy-Item FramesAndSound "$d/FramesAndSound" -Recurse }

# Linux portable: Python's tar keeps the exec bit that a Windows tar would lose.
python -c @"
import tarfile, pathlib
with tarfile.open('export/TinyWorks-$Version-linux-x86_64.tar.gz', 'w:gz') as t:
    for p in sorted(pathlib.Path('export/linux').rglob('*')):
        info = t.gettarinfo(str(p), 'TinyWorks/' + p.relative_to('export/linux').as_posix())
        info.mode = 0o755 if p.is_dir() or p.name == 'TinyWorks.x86_64' else 0o644
        t.addfile(info, open(p, 'rb') if p.is_file() else None)
"@
python packaging/linux_packages.py $Version
if ($LASTEXITCODE) { throw "Linux packages failed" }
wsl --cd $PSScriptRoot -e sh packaging/appimage.sh $Version
if ($LASTEXITCODE) { throw "AppImage failed" }

Compress-Archive -Path export/windows/* -DestinationPath "export/TinyWorks-$Version-windows-x86_64.zip" -Force
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }
& $iscc /Q "/DVersion=$Version" packaging/TinyWorks.iss
if ($LASTEXITCODE) { throw "Installer failed" }

Get-ChildItem export -File | ForEach-Object { "{0}  {1:N0} MB" -f $_.Name, ($_.Length / 1MB) }
