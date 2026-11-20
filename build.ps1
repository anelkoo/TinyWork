# Builds the release packages into export/:
#   TinyWorks-linux-x86_64.tar.gz   (unpack, run ./TinyWorks.x86_64)
#   TinyWorks-windows-x86_64.zip    (unpack, run TinyWorks.exe)
# Needs Godot 4.7.2 .NET with its export templates installed, and Python 3 (for a tar that keeps the Linux exec bit).
param([string]$Godot = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe")
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

python -c @"
import tarfile, pathlib
with tarfile.open('export/TinyWorks-linux-x86_64.tar.gz', 'w:gz') as t:
    for p in sorted(pathlib.Path('export/linux').rglob('*')):
        info = t.gettarinfo(str(p), 'TinyWorks/' + p.relative_to('export/linux').as_posix())
        info.mode = 0o755 if p.is_dir() or p.name == 'TinyWorks.x86_64' else 0o644
        t.addfile(info, open(p, 'rb') if p.is_file() else None)
"@
Compress-Archive -Path export/windows/* -DestinationPath export/TinyWorks-windows-x86_64.zip -Force
Get-ChildItem export/*.tar.gz, export/*.zip | ForEach-Object { "{0}  {1:N0} MB" -f $_.Name, ($_.Length / 1MB) }
