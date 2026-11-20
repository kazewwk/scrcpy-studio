#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
mkdir -p .build dist

source_dir="$root/.build/upstream"
expected_commit="$(cat upstream/commit.txt)"
if [[ ! -d "$source_dir/.git" ]]; then
    git clone --depth 1 --branch v5.0.1 https://github.com/Genymobile/scrcpy.git "$source_dir"
fi
actual_commit="$(git -C "$source_dir" rev-parse HEAD)"
if [[ "$actual_commit" != "$expected_commit" ]]; then
    printf 'Expected scrcpy %s, found %s\n' "$expected_commit" "$actual_commit" >&2
    exit 1
fi
patch="$root/upstream/gui-control.patch"
if ! git -C "$source_dir" apply --reverse --check "$patch" 2>/dev/null; then
    git -C "$source_dir" apply "$patch"
fi

python3 scripts/generate_options.py "$source_dir" .build/options.json
dotnet restore app/ScrcpyStudio.csproj
scripts/build_server.sh "$source_dir" .build/scrcpy-server
(cd "$source_dir" && release/build_windows.sh 64)
python3 scripts/pack_runtime.py

dotnet publish app/ScrcpyStudio.csproj -c Release --no-restore -o .build/publish
asset=ScrcpyStudio-1.0.0-win-x64.exe
install -m 755 .build/publish/ScrcpyStudio.exe "dist/$asset"
tar -czf dist/third-party-source.tar.gz -C "$source_dir/app/deps/work/sources" \
    ffmpeg-9.0.2.tar.xz dav1d-1.5.4.tar.xz libusb-1.0.30.tar.gz sdl-3.4.18.tar.gz
(cd dist && sha256sum "$asset" third-party-source.tar.gz > SHA256SUMS.txt)
