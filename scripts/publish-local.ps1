# Builds a local copy of the GitHub *release* build, so you can test the exact
# self-contained, single-file artifact without waiting on / downloading CI output.
#
# Run from anywhere:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-local.ps1
#   pwsh       -NoProfile -ExecutionPolicy Bypass -File scripts/publish-local.ps1
#
# Output lands in <repo>\publish\ (same layout the release archive has):
#   publish\UMB.CLI.exe        <- the CLI (double-click or run from a terminal)
#   publish\Resources\         <- ParamLabels etc. (Resources\Game is NOT shipped)
#   publish\Tools\             <- native tool binaries
#   publish\appsettings.json
#   publish\desktop\           <- only when -Desktop is passed
#
# This mirrors the "Publish (self-contained, single-file)" step in
# .github\workflows\release.yml. The release builds win-x64 (Smash modding tools
# are 64-bit only), so that is the default here too.
#
# Switches:
#   -Rid <rid>   Target runtime identifier (default win-x64).
#   -Desktop     Also build + bundle the Electron desktop app into publish\desktop\
#                (slow: runs npm ci + electron-builder). Requires Node 20+.
#   -Force       Passed through to fetch-tools (re-download tool binaries).

[CmdletBinding()]
param(
    [string]$Rid = 'win-x64',
    [switch]$Desktop,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $PSCommandPath
$RepoRoot  = Split-Path -Parent $ScriptDir
$PublishDir = Join-Path $RepoRoot 'publish'

Write-Host "Repo root:   $RepoRoot"
Write-Host "Publish dir: $PublishDir"
Write-Host "Runtime:     $Rid"
Write-Host ""

# 1. Ensure the native tools exist (VGAudioCli.exe is referenced at build time; the
#    rest are copied into publish\Tools\). fetch-tools skips anything already present.
$vgAudio = Join-Path $RepoRoot 'Tools\VGAudioCli.exe'
if ($Force -or -not (Test-Path $vgAudio)) {
    Write-Host "== Fetching tools ==" -ForegroundColor Cyan
    & (Join-Path $ScriptDir 'fetch-tools.ps1') @(if ($Force) { '-Force' })
} else {
    Write-Host "Tools already present (pass -Force to re-fetch)." -ForegroundColor DarkGray
}

# 2. Clean the publish dir. `dotnet publish` doesn't remove stale files, and an
#    incremental re-publish can leave the folder in an inconsistent state (missing
#    appsettings.json / native DLLs / Resources). A clean dir guarantees the output
#    matches the release archive exactly.
if (Test-Path $PublishDir) {
    Write-Host "Cleaning $PublishDir ..." -ForegroundColor DarkGray
    Get-ChildItem -Path $PublishDir -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

# 3. Publish the CLI exactly like the release workflow does.
Write-Host ""
Write-Host "== Publishing CLI (self-contained, single-file) ==" -ForegroundColor Cyan
& dotnet publish (Join-Path $RepoRoot 'UMB.CLI') `
    -c Release `
    -r $Rid `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:DebugType=embedded `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

# 3. Strip leftover .pdb files (release archives don't ship them).
Get-ChildItem -Path $PublishDir -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force

# 4. Optionally build + bundle the Electron desktop app (matches the release archive's
#    desktop\ folder). extraResources in electron-builder.yml copies publish\ into the
#    app's resources\cli\, so publish the CLI first (above) before packaging.
if ($Desktop) {
    Write-Host ""
    Write-Host "== Building desktop app ==" -ForegroundColor Cyan
    $DesktopDir = Join-Path $RepoRoot 'UMB.Desktop'
    $unpacked = Join-Path $DesktopDir 'out\win-unpacked'
    Push-Location $DesktopDir
    try {
        # `npm ci` wipes node_modules and can fail on Windows if a binary (esbuild.exe) is
        # locked. Locally we only need deps present, so skip install when they already are.
        if (Test-Path (Join-Path $DesktopDir 'node_modules')) {
            Write-Host "node_modules present, skipping install (delete it to force a clean npm ci)." -ForegroundColor DarkGray
        } else {
            & npm ci;          if ($LASTEXITCODE -ne 0) { throw "npm ci failed." }
        }
        & npm run build;       if ($LASTEXITCODE -ne 0) { throw "npm run build failed." }
        # No code signing for a local test build.
        $env:CSC_IDENTITY_AUTO_DISCOVERY = 'false'
        & npx electron-builder --dir
        # electron-builder finishes packaging out\win-unpacked BEFORE its code-signing prep.
        # That prep downloads winCodeSign, whose archive holds macOS symlinks that Windows
        # refuses to create without Developer Mode / admin, so it exits non-zero even though
        # the unpacked app is already complete. Signing is skipped anyway (no cert), so treat
        # a non-zero exit as OK as long as the runnable app exists.
        if ($LASTEXITCODE -ne 0) {
            if (Test-Path (Join-Path $unpacked 'UltimateMusicBuilder.exe')) {
                Write-Host ""
                Write-Host "electron-builder exited non-zero during code-signing prep (winCodeSign" -ForegroundColor Yellow
                Write-Host "symlink privilege). The unpacked app built fine; continuing." -ForegroundColor Yellow
                Write-Host "To silence it, enable Developer Mode or run from an elevated terminal." -ForegroundColor DarkGray
            } else {
                throw "electron-builder failed before producing out\win-unpacked."
            }
        }
    } finally {
        Pop-Location
    }

    if (-not (Test-Path $unpacked)) { throw "electron-builder output not found: $unpacked" }
    $stageDesktop = Join-Path $PublishDir 'desktop'
    New-Item -ItemType Directory -Force $stageDesktop | Out-Null
    Copy-Item (Join-Path $unpacked '*') $stageDesktop -Recurse -Force
    Write-Host "Desktop staged into $stageDesktop" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "== Done ==" -ForegroundColor Green
Write-Host "Release build is in: $PublishDir"
Write-Host ""
Write-Host "To run the CLI release build, from that folder you still need to add:" -ForegroundColor Yellow
Write-Host "  publish\Mods\MusicMods\<your mods>      (your mods)"
Write-Host "  publish\Resources\Game\<extracted data> (vanilla game data - see Resources\Game\README.txt)"
Write-Host "Then run:  publish\UMB.CLI.exe"
