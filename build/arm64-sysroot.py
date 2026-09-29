#!/usr/bin/env python3
"""Builds the linux-arm64 sysroot the r36s and linux-arm64 presets link against when cross-compiling from linux-x64.

ArkOS (the R36S) is based on Ubuntu 19.10 (glibc 2.30), so the executable must not need a newer glibc than that. This
downloads Ubuntu 18.04's arm64 C and C++ runtime packages (glibc 2.27) from ports.ubuntu.com, extracts them and makes
their absolute symbolic links relative, which is what clang's --sysroot needs.

    python3 build/arm64-sysroot.py [destination]      (default: ./artifacts/sysroot-bionic-arm64)
    dotnet publish <game> -p:IonTarget=r36s -p:IonArm64SysRoot=<destination>

Also usable as the -L root of qemu-aarch64 to run the result on an x64 machine (docs/platforms/r36s.md).
"""
import gzip
import os
import subprocess
import sys
import urllib.request

MIRROR = "https://ports.ubuntu.com/ubuntu-ports/"
INDEXES = ["dists/bionic-updates/main/binary-arm64/Packages.gz", "dists/bionic/main/binary-arm64/Packages.gz"]
PACKAGES = ["libc6", "libc6-dev", "linux-libc-dev", "libgcc-7-dev", "libgcc1", "libstdc++-7-dev", "libstdc++6", "zlib1g", "zlib1g-dev"]


def main() -> int:
    dest = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join("artifacts", "sysroot-bionic-arm64"))
    cache = dest + ".debs"
    os.makedirs(dest, exist_ok=True)
    os.makedirs(cache, exist_ok=True)

    found = {}
    for index in INDEXES:
        text = gzip.decompress(urllib.request.urlopen(MIRROR + index).read()).decode()
        for block in text.split("\n\n"):
            fields = dict(line.split(": ", 1) for line in block.splitlines() if ": " in line and not line.startswith(" "))
            name = fields.get("Package")
            if name in PACKAGES and name not in found:
                found[name] = fields["Filename"]

    missing = [p for p in PACKAGES if p not in found]
    if missing:
        print("missing packages: " + ", ".join(missing), file=sys.stderr)
        return 1

    for name in PACKAGES:
        deb = os.path.join(cache, os.path.basename(found[name]))
        if not os.path.exists(deb):
            print("fetching " + found[name])
            with urllib.request.urlopen(MIRROR + found[name]) as response, open(deb, "wb") as out:
                out.write(response.read())
        subprocess.run(["dpkg-deb", "-x", deb, dest], check=True)

    # Absolute symbolic links (/lib/aarch64-linux-gnu/libm.so.6) would point into the build machine: make them relative.
    for dirpath, dirnames, filenames in os.walk(dest):
        for entry in dirnames + filenames:
            path = os.path.join(dirpath, entry)
            if os.path.islink(path):
                target = os.readlink(path)
                if target.startswith("/"):
                    os.remove(path)
                    os.symlink(os.path.relpath(dest + target, dirpath), path)

    print(dest)
    return 0


if __name__ == "__main__":
    sys.exit(main())
