# Builds Linux packages from the Godot export in export/linux:
#   tinyworks_<ver>_amd64.deb        Debian, Ubuntu, Mint, Pop!_OS ...
#   tinyworks-<ver>-1-x86_64.pkg.tar.zst   Arch, Manjaro, EndeavourOS ...
# Plain Python 3.14+ (tarfile, gzip, compression.zstd), so it runs on Windows too and file modes are set by hand.
# Usage: python packaging/linux_packages.py <version>
import gzip, hashlib, io, sys, tarfile, time
from compression import zstd
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXPORT, OUT, PKG = ROOT / "export" / "linux", ROOT / "export", ROOT / "packaging"
VERSION = sys.argv[1]
NOW = int(time.time())
DESC = "A small, relaxing factory game that can sit in a corner of your screen"
URL = "https://github.com/anelkoo/TinyWork"

LAUNCHER = b'#!/bin/sh\nexec /opt/tinyworks/TinyWorks.x86_64 "$@"\n'
DESKTOP = (PKG / "tinyworks.desktop").read_bytes()
ICON = (PKG / "icon.png").read_bytes()

# Installed tree: path -> (bytes, mode). Directories are added automatically.
files = {f"opt/tinyworks/{p.relative_to(EXPORT).as_posix()}": (p.read_bytes(), 0o755 if p.name == "TinyWorks.x86_64" else 0o644)
         for p in sorted(EXPORT.rglob("*")) if p.is_file()}
files["usr/bin/tinyworks"] = (LAUNCHER, 0o755)
files["usr/share/applications/tinyworks.desktop"] = (DESKTOP, 0o644)
files["usr/share/icons/hicolor/256x256/apps/tinyworks.png"] = (ICON, 0o644)
installed_size = sum(len(b) for b, _ in files.values())

def dirs_of(paths):
    out = set()
    for p in paths:
        parts = p.split("/")[:-1]
        for i in range(1, len(parts) + 1): out.add("/".join(parts[:i]))
    return sorted(out)

def add(t, name, data=None, mode=0o644):
    info = tarfile.TarInfo(name)
    info.mtime, info.uid, info.gid, info.uname, info.gname = NOW, 0, 0, "root", "root"
    if data is None:
        info.type, info.mode = tarfile.DIRTYPE, 0o755
        t.addfile(info)
    else:
        info.size, info.mode = len(data), mode
        t.addfile(info, io.BytesIO(data))

def tree_tar(prefix, fmt):
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w", format=fmt) as t:
        for d in dirs_of(files): add(t, prefix + d)
        for p, (b, m) in files.items(): add(t, prefix + p, b, m)
    return buf.getvalue()

# ---------- .deb: an "ar" archive of debian-binary, control.tar.gz, data.tar.gz ----------
def deb():
    control = f"""Package: tinyworks
Version: {VERSION}
Architecture: amd64
Maintainer: anelkoo <anelkoo@users.noreply.github.com>
Installed-Size: {installed_size // 1024}
Depends: libc6, libgl1, libx11-6
Recommends: libxcursor1, libxinerama1, libxrandr2, libxi6, libasound2 | libpulse0
Section: games
Priority: optional
Homepage: {URL}
Description: {DESC}
 Peaceful factory builder: mine, smelt, belt and build. Mini mode keeps it
 in a corner of the screen, F8 hides it from anywhere.
""".encode()
    md5 = "".join(f"{hashlib.md5(b).hexdigest()}  {p}\n" for p, (b, _) in files.items()).encode()
    cbuf = io.BytesIO()
    with tarfile.open(fileobj=cbuf, mode="w:gz", format=tarfile.GNU_FORMAT) as t:
        add(t, "./control", control)
        add(t, "./md5sums", md5)
    data = gzip.compress(tree_tar("./", tarfile.GNU_FORMAT), 9, mtime=NOW)
    out = io.BytesIO()
    out.write(b"!<arch>\n")
    for name, body in (("debian-binary", b"2.0\n"), ("control.tar.gz", cbuf.getvalue()), ("data.tar.gz", data)):
        out.write(f"{name:<16}{NOW:<12}0     0     100644  {len(body):<10}`\n".encode())
        out.write(body + (b"\n" if len(body) % 2 else b""))
    path = OUT / f"tinyworks_{VERSION}_amd64.deb"
    path.write_bytes(out.getvalue())
    return path

# ---------- Arch: zstd tar with .PKGINFO and .MTREE first ----------
def arch():
    pkgver = f"{VERSION}-1"
    pkginfo = f"""pkgname = tinyworks
pkgbase = tinyworks
pkgver = {pkgver}
pkgdesc = {DESC}
url = {URL}
builddate = {NOW}
packager = anelkoo
size = {installed_size}
arch = x86_64
license = custom
depend = glibc
depend = libx11
depend = libglvnd
optdepend = libxcursor
optdepend = libxinerama
optdepend = libxrandr
optdepend = libxi
optdepend = alsa-lib: sound
""".encode()
    lines = ["#mtree", "/set type=file uid=0 gid=0 mode=644"]
    lines.append(f"./.PKGINFO time={NOW}.0 size={len(pkginfo)} md5digest={hashlib.md5(pkginfo).hexdigest()} sha256digest={hashlib.sha256(pkginfo).hexdigest()}")
    for d in dirs_of(files): lines.append(f"./{d} time={NOW}.0 mode=755 type=dir")
    for p, (b, m) in files.items():
        lines.append(f"./{p} time={NOW}.0{' mode=755' if m == 0o755 else ''} size={len(b)} md5digest={hashlib.md5(b).hexdigest()} sha256digest={hashlib.sha256(b).hexdigest()}")
    mtree = gzip.compress(("\n".join(lines) + "\n").encode(), 9, mtime=NOW)
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w", format=tarfile.PAX_FORMAT) as t:
        add(t, ".PKGINFO", pkginfo)
        add(t, ".MTREE", mtree)
        for d in dirs_of(files): add(t, d)
        for p, (b, m) in files.items(): add(t, p, b, m)
    path = OUT / f"tinyworks-{pkgver}-x86_64.pkg.tar.zst"
    path.write_bytes(zstd.compress(buf.getvalue(), 19))
    return path

for p in (deb(), arch()): print(f"{p.name}  {p.stat().st_size / 2**20:.0f} MB")
