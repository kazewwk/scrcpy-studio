#!/usr/bin/env python3
"""Pack the rebuilt client, server and runtime notices for the portable EXE."""

import hashlib
import json
import os
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo


ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / ".build"
SOURCE = BUILD / "upstream"
SOURCES = SOURCE / "app/deps/work/sources"


def runtime_pack(assets, name):
    for framework in assets["project"]["frameworks"].values():
        for package in framework.get("downloadDependencies", []):
            if package["name"] != name:
                continue
            version = package["version"].lstrip("[").split(",", 1)[0]
            for folder in assets["packageFolders"]:
                path = Path(folder) / name.lower() / version
                if path.is_dir():
                    return path
    raise FileNotFoundError(f"Could not locate restored runtime pack {name}")


def main():
    assets = json.loads((ROOT / "app/obj/project.assets.json").read_text())
    net = runtime_pack(assets, "Microsoft.NETCore.App.Runtime.win-x64")
    desktop = runtime_pack(assets, "Microsoft.WindowsDesktop.App.Runtime.win-x64")
    dist = SOURCE / "release/work/build-win64/dist"
    zlib = Path(os.environ.get("ZLIB_NOTICE", "/usr/share/doc/libz-mingw-w64-dev/copyright"))

    files = {
        file.name: file for file in dist.iterdir()
        if file.suffix.lower() in (".exe", ".dll", ".png")
    }
    files.update({
        "scrcpy-server": BUILD / "scrcpy-server",
        "LICENSES/scrcpy.txt": SOURCE / "LICENSE",
        "LICENSES/studio.txt": ROOT / "LICENSE",
        "LICENSES/SDL.txt": SOURCES / "sdl-3.4.18/LICENSE.txt",
        "LICENSES/dav1d.txt": SOURCES / "dav1d-1.5.4/COPYING",
        "LICENSES/FFmpeg.txt": SOURCES / "ffmpeg-9.0.2/COPYING.LGPLv2.1",
        "LICENSES/libusb.txt": SOURCES / "libusb-1.0.30/COPYING",
        "LICENSES/zlib.txt": zlib,
        "LICENSES/dotnet.txt": net / "LICENSE.TXT",
        "LICENSES/dotnet-notices.txt": net / "THIRD-PARTY-NOTICES.TXT",
        "LICENSES/WindowsDesktop.txt": desktop / "LICENSE",
        "LICENSES/third-party.md": ROOT / "THIRD_PARTY.md",
    })
    payload = {name: path.read_bytes() for name, path in files.items()}
    with ZipFile(SOURCES / "platform-tools-37.0.1-windows.zip") as adb:
        payload["LICENSES/adb-notices.txt"] = adb.read("platform-tools/NOTICE.txt")
    out = BUILD / "backend.zip"
    with ZipFile(out, "w") as package:
        for name, content in sorted(payload.items()):
            package.writestr(ZipInfo(name, (2026, 10, 8, 0, 0, 0)), content, compress_type=ZIP_DEFLATED)
    print(f"Runtime ZIP SHA-256 {hashlib.sha256(out.read_bytes()).hexdigest()}")


if __name__ == "__main__":
    main()
