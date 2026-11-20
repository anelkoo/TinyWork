#!/bin/sh
# Builds export/TinyWorks-<ver>-x86_64.AppImage from export/linux. Runs in Linux or WSL; needs
# tools/appimagetool-x86_64.AppImage (it fetches the AppImage runtime from the AppImage project on first use).
# Usage: sh packaging/appimage.sh <version>
set -e
cd "$(dirname "$0")/.."
ver="$1"
dir=$(mktemp -d)
cp -r export/linux/. "$dir/"
chmod 755 "$dir/TinyWorks.x86_64"
cp packaging/tinyworks.desktop "$dir/tinyworks.desktop"
cp packaging/icon.png "$dir/tinyworks.png"
printf '#!/bin/sh\nexec "$(dirname "$(readlink -f "$0")")/TinyWorks.x86_64" "$@"\n' > "$dir/AppRun"
chmod 755 "$dir/AppRun"
chmod -R a+rX "$dir" # mktemp makes it 0700; AppImages are run by other users
tool=$(mktemp)
cp tools/appimagetool-x86_64.AppImage "$tool" && chmod 755 "$tool"
ARCH=x86_64 "$tool" --appimage-extract-and-run --no-appstream "$dir" "export/TinyWorks-$ver-x86_64.AppImage" >/dev/null
rm -rf "$dir" "$tool"
ls -la "export/TinyWorks-$ver-x86_64.AppImage"
