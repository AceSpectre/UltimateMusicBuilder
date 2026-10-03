#!/usr/bin/env bash
# Build pinned vgmstream with static codec libraries.
set -euo pipefail

[[ "$(uname -s)" == Darwin ]] || { echo 'This builder requires macOS.' >&2; exit 1; }
command -v brew >/dev/null || { echo 'Install Homebrew before building vgmstream.' >&2; exit 1; }
xcrun --find clang >/dev/null

tag="${1:-r2083}"
target="${2:-$(cd "$(dirname "$0")/.." && pwd)/Tools/vgmstream-cli/vgmstream-cli}"
ffmpeg_version=7.1.1 # Matches r2083's upstream codec build.
for formula in cmake pkg-config mpg123 libvorbis libogg speex; do
    brew list --versions "$formula" >/dev/null 2>&1 || brew install "$formula"
done
if [[ "$(uname -m)" == x86_64 ]]; then
    brew list --versions nasm >/dev/null 2>&1 || brew install nasm
fi

work="$(mktemp -d -t umb-vgmstream.XXXXXX)"
trap 'rm -rf "$work"' EXIT
curl -fL --retry 3 "https://github.com/vgmstream/vgmstream/archive/refs/tags/$tag.tar.gz" -o "$work/vgmstream.tar.gz"
curl -fL --retry 3 "https://ffmpeg.org/releases/ffmpeg-$ffmpeg_version.tar.xz" -o "$work/ffmpeg.tar.xz"
tar -xf "$work/vgmstream.tar.gz" -C "$work"
tar -xf "$work/ffmpeg.tar.xz" -C "$work"
source_dir="$work/vgmstream-$tag"

# Adapt the pinned recipe for Apple's linker and static codec libraries.
python3 - "$source_dir" <<'PY'
from pathlib import Path
import sys
root = Path(sys.argv[1])
patches = {
    'cmake/vgmstream.cmake': {
        'PUBLIC mpg123 m': 'PUBLIC ${MPG123_LIBRARIES} m',
        'PUBLIC vorbisfile vorbis ogg m': 'PUBLIC ${VORBISFILE_LIBRARIES} m',
        'PUBLIC speex m': 'PUBLIC ${SPEEX_LIBRARIES} m',
    },
    'cmake/dependencies/ffmpeg.cmake': {
        'if(PC_OPUS_FOUND)': 'if(FALSE)',
        '--extra-libs=-static': '',
        '--extra-cflags=--static': '',
        '--disable-everything': '--disable-everything\n                --disable-autodetect\n                --disable-videotoolbox',
    },
    'cmake/dependencies/celt.cmake': {
        'if(CMAKE_SYSTEM_PROCESSOR STREQUAL "aarch64")':
            'if(CMAKE_SYSTEM_PROCESSOR MATCHES "^(aarch64|arm64)$")',
    },
}
for name, replacements in patches.items():
    path = root / name
    contents = path.read_text()
    for before, after in replacements.items():
        if contents.count(before) != 1:
            raise SystemExit(f'Unexpected upstream recipe: {name}: {before}')
        contents = contents.replace(before, after)
    path.write_text(contents)
PY

cmake -S "$source_dir" -B "$work/build" \
    -DCMAKE_BUILD_TYPE=Release -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
    -DBUILD_CLI=ON -DBUILD_V123=OFF -DBUILD_AUDACIOUS=OFF \
    -DVGMSTREAM_VERSION="$tag" -DFFMPEG_PATH="$work/ffmpeg-$ffmpeg_version" \
    -DCMAKE_PREFIX_PATH="$(brew --prefix)" \
    -DMPG123_LIBRARIES="$(brew --prefix mpg123)/lib/libmpg123.a" \
    -DVORBISFILE_LIBRARY="$(brew --prefix libvorbis)/lib/libvorbisfile.a" \
    -DVORBIS_LIBRARY="$(brew --prefix libvorbis)/lib/libvorbis.a" \
    -DOGG_LIBRARY="$(brew --prefix libogg)/lib/libogg.a" \
    -DSPEEX_LIBRARY="$(brew --prefix speex)/lib/libspeex.a"
cmake --build "$work/build" --target vgmstream_cli --parallel "$(sysctl -n hw.ncpu)"
binary="$work/build/cli/vgmstream-cli"

# Reject non-system dylib dependencies.
otool -L "$binary" | tail -n +2 | while read -r library rest; do
    case "$library" in
        /usr/lib/*|/System/Library/*) ;;
        *) echo "Nonportable dependency: $library" >&2; exit 1 ;;
    esac
done
# A valid version probe returns 1; also validate its JSON.
"$binary" -V > "$work/version.json" || [[ $? == 1 ]]
python3 - "$work/version.json" "$tag" <<'PY'
import json, sys
with open(sys.argv[1]) as stream:
    info = json.load(stream)
assert info['version'] == sys.argv[2], info
PY
mkdir -p "$(dirname "$target")"
cp "$binary" "$target"
chmod +x "$target"
# Retain redistribution notices alongside the bundled executable.
cp "$source_dir/COPYING" "$(dirname "$target")/COPYING-vgmstream"
cp "$work/ffmpeg-$ffmpeg_version/COPYING.GPLv2" "$(dirname "$target")/COPYING-FFmpeg-GPLv2"
echo "Built $target ($tag, FFmpeg $ffmpeg_version; static codecs)."
