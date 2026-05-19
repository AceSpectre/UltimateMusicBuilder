#!/usr/bin/env bash
# Fetches external binaries that UltimateMusicBuilder needs at runtime.
# Run from the repo root: ./scripts/fetch-tools.sh
#
# Tools installed into ./Tools/<subfolder>/:
#   - nus3audio          (Rust, jam1garner/nus3audio-rs)
#   - ultimate_tex_cli   (Rust, ScanMountGoat/ultimate_tex)
#   - bgm-property       (Rust, jam1garner/smash-bgm-property — requires `cargo install` on non-Windows)
#   - vgmstream-cli      (vgmstream/vgmstream, r2083 prebuilt)
#
# Tools that should be on PATH (script prints install hints; not bundled):
#   - ffmpeg / ffprobe / ffplay   (Homebrew / apt / dnf)
#   - pymusiclooper                (`pipx install pymusiclooper`)
#
# Re-running is safe: existing binaries are skipped unless --force is passed.

set -euo pipefail

# ─── Pinned versions ────────────────────────────────────────────────────────
NUS3AUDIO_TAG="v1.1.7"
ULTIMATE_TEX_TAG="0.3.1"
VGMSTREAM_TAG="r2083"
BGM_PROPERTY_REPO="https://github.com/jam1garner/smash-bgm-property"
BGM_PROPERTY_BIN="bgm_property_yaml"   # upstream binary name; we rename to bgm-property
BGM_PROPERTY_TAG="v1.2.0"              # pin to match Windows release binary; master has incompatible CLI

# ─── Options ────────────────────────────────────────────────────────────────
FORCE=0
for arg in "$@"; do
    case "$arg" in
        --force|-f) FORCE=1 ;;
        --help|-h)
            cat <<EOF
Usage: $0 [--force]
  --force    Re-download tools even if already present.
EOF
            exit 0 ;;
        *) echo "unknown arg: $arg" >&2; exit 2 ;;
    esac
done

# ─── Locate repo root ───────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TOOLS_DIR="$REPO_ROOT/Tools"
mkdir -p "$TOOLS_DIR"

# ─── Detect OS + arch ───────────────────────────────────────────────────────
case "$(uname -s)" in
    Linux)   OS="linux" ;;
    Darwin)  OS="mac" ;;
    *) echo "Unsupported OS: $(uname -s). Use fetch-tools.ps1 on Windows." >&2; exit 1 ;;
esac

case "$(uname -m)" in
    x86_64|amd64) ARCH="x64" ;;
    arm64|aarch64) ARCH="arm64" ;;
    *) echo "Unsupported arch: $(uname -m)" >&2; exit 1 ;;
esac

echo ">> Detected: $OS-$ARCH"
echo ">> Tools dir: $TOOLS_DIR"

# ─── Helpers ────────────────────────────────────────────────────────────────
have() { command -v "$1" >/dev/null 2>&1; }

download() {
    local url=$1
    local dest=$2
    echo "   ↓ $url"
    if have curl; then
        curl -fL --retry 3 -o "$dest" "$url"
    elif have wget; then
        wget -q -O "$dest" "$url"
    else
        echo "   neither curl nor wget is installed" >&2
        return 1
    fi
}

extract_zip() {
    local zip=$1
    local dest=$2
    if have unzip; then
        unzip -q -o "$zip" -d "$dest"
    else
        echo "   unzip not installed" >&2
        return 1
    fi
}

extract_tar_xz() {
    local tar=$1
    local dest=$2
    tar -xf "$tar" -C "$dest"
}

# Skip if the final target file already exists (idempotent), unless --force.
needs_install() {
    local target=$1
    if [[ -f "$target" && "$FORCE" -eq 0 ]]; then
        echo "   ✓ already present: $target"
        return 1
    fi
    return 0
}

verify() {
    local bin=$1
    local label=$2
    if [[ ! -x "$bin" ]]; then
        chmod +x "$bin" 2>/dev/null || true
    fi
    if "$bin" --help >/dev/null 2>&1 \
       || "$bin" -h >/dev/null 2>&1 \
       || "$bin" --version >/dev/null 2>&1; then
        echo "   ✓ $label OK"
    else
        echo "   ! $label installed but help/version probe failed (may still work)"
    fi
}

# ─── nus3audio ──────────────────────────────────────────────────────────────
install_nus3audio() {
    echo
    echo "── nus3audio ($NUS3AUDIO_TAG) ──"
    local dir="$TOOLS_DIR/Nus3Audio"
    local target="$dir/nus3audio"
    mkdir -p "$dir"
    needs_install "$target" || return 0

    local asset
    case "$OS" in
        linux) asset="nus3audio" ;;
        mac)   asset="nus3audio_mac" ;;  # Intel binary; runs via Rosetta on Apple Silicon
    esac

    download "https://github.com/jam1garner/nus3audio-rs/releases/download/$NUS3AUDIO_TAG/$asset" "$target"
    chmod +x "$target"
    verify "$target" "nus3audio"
}

# ─── ultimate_tex_cli ───────────────────────────────────────────────────────
install_ultimate_tex_cli() {
    echo
    echo "── ultimate_tex_cli ($ULTIMATE_TEX_TAG) ──"
    local dir="$TOOLS_DIR/UltimateTexCli"
    local target="$dir/ultimate_tex_cli"
    mkdir -p "$dir"
    needs_install "$target" || return 0

    local asset
    case "$OS-$ARCH" in
        linux-x64)  asset="ultimate_tex_cli_linux_x64.zip" ;;
        mac-arm64)  asset="ultimate_tex_cli_macos_apple_silicon.zip" ;;
        mac-x64)    asset="ultimate_tex_cli_macos_intel.zip" ;;
        *) echo "   no prebuilt for $OS-$ARCH; skipping"; return 0 ;;
    esac

    local tmpzip
    tmpzip="$(mktemp -t umb_uxtex.XXXXXX.zip)"
    download "https://github.com/ScanMountGoat/ultimate_tex/releases/download/$ULTIMATE_TEX_TAG/$asset" "$tmpzip"
    extract_zip "$tmpzip" "$dir"
    rm -f "$tmpzip"

    # Asset zips contain the binary at the top level (verified for 0.3.1).
    chmod +x "$target" 2>/dev/null || true
    verify "$target" "ultimate_tex_cli"
}

# ─── bgm-property ───────────────────────────────────────────────────────────
install_bgm_property() {
    echo
    echo "── bgm-property (cargo install) ──"
    local dir="$TOOLS_DIR/BgmProperty"
    local target="$dir/bgm-property"
    mkdir -p "$dir"
    needs_install "$target" || return 0

    if ! have cargo; then
        cat <<EOF
   No prebuilt bgm-property binary exists for $OS-$ARCH on GitHub Releases.
   Building requires the Rust toolchain. Install rustup:
       curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
   Then re-run this script.
EOF
        return 0
    fi

    # cargo install drops binaries in --root/bin; we point that at our dir and rename.
    cargo install --git "$BGM_PROPERTY_REPO" --tag "$BGM_PROPERTY_TAG" --bin "$BGM_PROPERTY_BIN" --root "$dir" --quiet
    local built="$dir/bin/$BGM_PROPERTY_BIN"
    if [[ ! -f "$built" ]]; then
        echo "   ! cargo install did not produce $built"
        return 1
    fi
    mv "$built" "$target"
    rm -rf "$dir/bin" "$dir/.crates.toml" "$dir/.crates2.json"
    verify "$target" "bgm-property"
}

# ─── vgmstream-cli ──────────────────────────────────────────────────────────
install_vgmstream_cli() {
    echo
    echo "── vgmstream-cli ($VGMSTREAM_TAG) ──"
    local dir="$TOOLS_DIR/vgmstream-cli"
    local target="$dir/vgmstream-cli"
    mkdir -p "$dir"
    needs_install "$target" || return 0

    local asset
    case "$OS" in
        linux) asset="vgmstream-linux-cli.zip" ;;
        mac)   asset="vgmstream-mac-cli.zip" ;;
    esac

    local tmpzip
    tmpzip="$(mktemp -t umb_vgm.XXXXXX.zip)"
    download "https://github.com/vgmstream/vgmstream/releases/download/$VGMSTREAM_TAG/$asset" "$tmpzip"
    extract_zip "$tmpzip" "$dir"
    rm -f "$tmpzip"

    chmod +x "$target" 2>/dev/null || true
    verify "$target" "vgmstream-cli"
}

# ─── ffmpeg + pymusiclooper hints ───────────────────────────────────────────
print_path_tools() {
    echo
    echo "── PATH-resolved tools (not bundled) ──"

    if have ffmpeg && have ffprobe && have ffplay; then
        echo "   ✓ ffmpeg, ffprobe, ffplay found on PATH"
    else
        case "$OS" in
            mac)
                echo "   Install ffmpeg via Homebrew:"
                echo "       brew install ffmpeg"
                ;;
            linux)
                echo "   Install ffmpeg via your package manager:"
                echo "       sudo apt install ffmpeg        # Debian/Ubuntu"
                echo "       sudo dnf install ffmpeg        # Fedora"
                echo "       sudo pacman -S ffmpeg          # Arch"
                ;;
        esac
    fi

    if have pymusiclooper; then
        echo "   ✓ pymusiclooper found on PATH"
    else
        echo "   Install pymusiclooper via pipx (recommended):"
        echo "       pipx install pymusiclooper"
        echo "   Or via pip:"
        echo "       pip install --user pymusiclooper"
    fi
}

# ─── Run ────────────────────────────────────────────────────────────────────
install_nus3audio
install_ultimate_tex_cli
install_bgm_property
install_vgmstream_cli
print_path_tools

echo
echo "Done. Build UMB with: cd UMB.CLI && dotnet build && dotnet run"
