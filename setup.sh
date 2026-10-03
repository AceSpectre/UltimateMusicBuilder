#!/usr/bin/env bash
# Install runtime dependencies; --dev also prepares a source checkout.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEV=0
DRY_RUN=0
for arg in "$@"; do
    case "$arg" in
        --dev) DEV=1 ;;
        --dry-run) DRY_RUN=1 ;;
        --help|-h)
            cat <<'EOF'
Usage: bash setup.sh [--dev] [--dry-run]

Installs missing FFmpeg (ffmpeg/ffprobe/ffplay) and pymusiclooper.
Supports macOS with Homebrew, Debian/Ubuntu with apt, Fedora with dnf,
and Arch Linux with pacman. Install the package manager first.

--dev      Also fetch pinned native tools and run npm ci. Requires .NET 8
           SDK, Node.js 20+ and npm; Rust/cargo is needed to build a missing
           bgm-property tool on Unix.
--dry-run  Print installation commands without changing anything.

Windows: powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1
EOF
            exit 0 ;;
        *) printf 'Unknown option: %s\n' "$arg" >&2; exit 2 ;;
    esac
done

have() { command -v "$1" >/dev/null 2>&1; }
# pipx < 1.1 (e.g. Ubuntu 22.04) has no `environment --value`.
pipx_bin_dir() { pipx environment --value PIPX_BIN_DIR 2>/dev/null || printf '%s\n' "${PIPX_BIN_DIR:-$HOME/.local/bin}"; }
fail() { printf 'Setup failed: %s\n' "$*" >&2; exit 1; }
run() {
    printf '  '
    printf '%q ' "$@"
    printf '\n'
    if (( ! DRY_RUN )); then "$@"; fi
}

# Recognize common per-user installations in this process, without editing profiles.
export PATH="$PATH:${PIPX_BIN_DIR:-$HOME/.local/bin}:$HOME/.dotnet:$HOME/.cargo/bin"
case "$(uname -s)" in
    Darwin)
        OS=macos
        if ! have brew; then
            for prefix in /opt/homebrew /usr/local; do
                if [[ -x "$prefix/bin/brew" ]]; then
                    export PATH="$prefix/bin:$PATH"
                    break
                fi
            done
        fi ;;
    Linux) OS=linux ;;
    *) fail 'Use setup.ps1 on Windows. This shell script supports macOS and Linux.' ;;
esac

if (( DEV )); then
    [[ -f "$ROOT/UMB.Desktop/package.json" ]] || fail '--dev requires a source checkout.'
    have dotnet || fail 'Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0'
    SDK_LIST="$(dotnet --list-sdks)"
    [[ "$SDK_LIST" =~ (^|$'\n')8\. ]] || fail 'The .NET 8 SDK is required (a runtime alone is insufficient).'
    have node && have npm || fail 'Install Node.js 20+ and npm: https://nodejs.org/en/download'
    [[ "$(node -p 'Number(process.versions.node.split(".")[0]) >= 20')" == true ]] \
        || fail 'Node.js 20 or newer is required.'
    if [[ ! -f "$ROOT/Tools/BgmProperty/bgm-property" ]]; then
        have cargo || fail 'Install Rust/cargo to build bgm-property: https://rustup.rs'
    fi
fi

MANAGER=''
APT_UPDATED=0
install_package() {
    local package=$1
    if [[ -z "$MANAGER" ]]; then
        if [[ "$OS" == macos ]]; then
            have brew || fail 'Install Homebrew first: https://brew.sh'
            MANAGER=brew
        elif have apt-get; then MANAGER=apt
        elif have dnf; then MANAGER=dnf
        elif have pacman; then MANAGER=pacman
        else fail 'No supported package manager found. Install FFmpeg and pipx manually.'
        fi
    fi
    case "$MANAGER" in
        brew) run brew install "$package" ;;
        apt)
            if (( ! APT_UPDATED )); then
                as_admin apt-get update
                APT_UPDATED=1
            fi
            as_admin apt-get install -y "$package" ;;
        dnf)
            # Fedora ships ffmpeg-free in its official repositories.
            if [[ "$package" == ffmpeg ]]; then package=ffmpeg-free; fi
            as_admin dnf install -y "$package" ;;
        pacman)
            if [[ "$package" == pipx ]]; then package=python-pipx; fi
            as_admin pacman -S --needed "$package" ;;
    esac
}
as_admin() {
    if (( EUID == 0 )); then run "$@"
    else
        have sudo || fail 'sudo is required to install system packages.'
        run sudo "$@"
    fi
}

printf 'UMB setup (%s)\n' "$OS"
if ! have ffmpeg || ! have ffprobe || ! have ffplay; then
    install_package ffmpeg
else
    printf 'FFmpeg is already available.\n'
fi

if have pipx; then
    PATH="$(pipx_bin_dir):$PATH"
fi
if ! have pymusiclooper; then
    if ! have pipx; then install_package pipx; fi
    if (( ! DRY_RUN )); then
        have pipx || fail 'pipx was installed but is not on PATH. Open a new terminal and rerun setup.'
        PATH="$(pipx_bin_dir):$PATH"
    fi
    if ! have pymusiclooper; then run pipx install pymusiclooper; fi
    run pipx ensurepath
else
    printf 'pymusiclooper is already available.\n'
fi

if (( DEV )); then
    run bash "$ROOT/scripts/fetch-tools.sh"
    if (( ! DRY_RUN )); then
        for tool in Nus3Audio/nus3audio UltimateTexCli/ultimate_tex_cli BgmProperty/bgm-property vgmstream-cli/vgmstream-cli; do
            [[ -x "$ROOT/Tools/$tool" ]] || fail "Native tool is missing or not executable: Tools/$tool"
        done
    fi
    run npm --prefix "$ROOT/UMB.Desktop" ci
fi

if (( DRY_RUN )); then
    printf 'Dry run complete; nothing was installed.\n'
else
    for tool in ffmpeg ffprobe ffplay pymusiclooper; do
        have "$tool" || fail "$tool is still missing from PATH."
    done
    printf 'Setup complete. Open a new terminal before launching UMB.\n'
    printf 'Game data and Resources/template.nus3bank must be supplied separately.\n'
fi
