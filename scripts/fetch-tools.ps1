# Fetches external binaries that UltimateMusicBuilder needs at runtime.
# Run from the repo root:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\fetch-tools.ps1
# Or on PowerShell 7+:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/fetch-tools.ps1
#
# (The -ExecutionPolicy Bypass is needed because the script isn't digitally
# signed. If you'd rather not pass that flag every time, run once:
#   Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
# then Unblock-File scripts\fetch-tools.ps1.)
#
# Tools installed into .\Tools\<subfolder>\:
#   - nus3audio.exe          (Rust, jam1garner/nus3audio-rs)
#   - ultimate_tex_cli.exe   (Rust, ScanMountGoat/ultimate_tex)
#   - bgm-property.exe       (Rust, jam1garner/smash-bgm-property)
#   - vgmstream-cli.exe      (vgmstream/vgmstream, r2083 prebuilt)
#
# Not bundled (script prints a hint — install via your system package manager):
#   - ffmpeg / ffprobe / ffplay   (`choco install ffmpeg`)
#   - pymusiclooper                (`pipx install pymusiclooper`)
#
# Re-running is safe: existing binaries are skipped unless -Force is passed.

[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Pinned versions
$NUS3AUDIO_TAG       = 'v1.1.7'
$ULTIMATE_TEX_TAG    = '0.3.1'
$VGMSTREAM_TAG       = 'r2083'
$BGM_PROPERTY_TAG    = 'v1.2.0'

# Locate repo root
$ScriptDir = Split-Path -Parent $PSCommandPath
$RepoRoot  = Split-Path -Parent $ScriptDir
$ToolsDir  = Join-Path $RepoRoot 'Tools'
New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null

# Detect arch (we only support x64 on Windows for now)
$Arch = (Get-CimInstance Win32_Processor).Architecture
# 9 = x64, 12 = ARM64
if ($Arch -ne 9) {
    Write-Warning "Detected non-x64 Windows ($Arch). Prebuilts target x64; some tools may not run via emulation."
}

Write-Host ">> Tools dir: $ToolsDir"
Write-Host ""

function Test-NeedsInstall {
    param([string]$Target)
    if ((Test-Path $Target) -and -not $Force) {
        Write-Host "   * already present: $Target"
        return $false
    }
    return $true
}

function Get-File {
    param([string]$Url, [string]$Dest)
    Write-Host "   v $Url"
    # IWR is much faster than BITS for single small files when ProgressPreference is silenced.
    $oldPref = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -Uri $Url -OutFile $Dest -UseBasicParsing
    } finally {
        $ProgressPreference = $oldPref
    }
}

function Expand-Single {
    param([string]$ZipPath, [string]$DestDir)
    Expand-Archive -Path $ZipPath -DestinationPath $DestDir -Force
}

function Test-Binary {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path $Path)) {
        Write-Host "   ! $Label not at expected path: $Path"
        return
    }
    # We only check the file is executable and produces *some* output when probed.
    # Don't rely on $LASTEXITCODE — different tools use different conventions for
    # --help vs -h vs no-args, and many native tools emit help to stderr.
    # In Windows PowerShell 5.1, `2>&1` on a native exe wraps stderr lines in
    # ErrorRecords and trips $ErrorActionPreference='Stop' — so we redirect to
    # files instead and read them back.
    $tmpOut = [System.IO.Path]::GetTempFileName()
    $tmpErr = [System.IO.Path]::GetTempFileName()
    $probeOk = $false
    foreach ($flag in '--help','-h','-V','--version') {
        try {
            Start-Process -FilePath $Path -ArgumentList $flag -NoNewWindow -Wait `
                -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr -ErrorAction Stop | Out-Null
            $outLen = (Get-Item $tmpOut).Length
            $errLen = (Get-Item $tmpErr).Length
            if ($outLen -gt 0 -or $errLen -gt 0) { $probeOk = $true; break }
        } catch {
            # try next flag
        }
    }
    Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
    if ($probeOk) {
        Write-Host "   + $Label OK"
    } else {
        Write-Host "   ! $Label installed but help/version probe produced no output (may still work)"
    }
}

# ----- nus3audio -----
function Install-Nus3Audio {
    Write-Host ""
    Write-Host "-- nus3audio ($NUS3AUDIO_TAG) --"
    $dir    = Join-Path $ToolsDir 'Nus3Audio'
    $target = Join-Path $dir 'nus3audio.exe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    if (-not (Test-NeedsInstall $target)) { return }

    $url = "https://github.com/jam1garner/nus3audio-rs/releases/download/$NUS3AUDIO_TAG/nus3audio.exe"
    Get-File $url $target
    Test-Binary $target 'nus3audio'
}

# ----- ultimate_tex_cli -----
function Install-UltimateTexCli {
    Write-Host ""
    Write-Host "-- ultimate_tex_cli ($ULTIMATE_TEX_TAG) --"
    $dir    = Join-Path $ToolsDir 'UltimateTexCli'
    $target = Join-Path $dir 'ultimate_tex_cli.exe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    if (-not (Test-NeedsInstall $target)) { return }

    $url = "https://github.com/ScanMountGoat/ultimate_tex/releases/download/$ULTIMATE_TEX_TAG/ultimate_tex_cli_win_x64.zip"
    $tmp = [System.IO.Path]::GetTempFileName() + '.zip'
    try {
        Get-File $url $tmp
        Expand-Single $tmp $dir
    } finally {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
    Test-Binary $target 'ultimate_tex_cli'
}

# ----- bgm-property -----
function Install-BgmProperty {
    Write-Host ""
    Write-Host "-- bgm-property ($BGM_PROPERTY_TAG) --"
    $dir    = Join-Path $ToolsDir 'BgmProperty'
    $target = Join-Path $dir 'bgm-property.exe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    if (-not (Test-NeedsInstall $target)) { return }

    # Upstream binary is `bgm_property_yaml.exe`; we rename to keep paths consistent.
    $url = "https://github.com/jam1garner/smash-bgm-property/releases/download/$BGM_PROPERTY_TAG/bgm_property_yaml.exe"
    Get-File $url $target
    Test-Binary $target 'bgm-property'
}

# ----- vgmstream-cli -----
function Install-VgmStreamCli {
    Write-Host ""
    Write-Host "-- vgmstream-cli ($VGMSTREAM_TAG) --"
    $dir    = Join-Path $ToolsDir 'vgmstream-cli'
    $target = Join-Path $dir 'vgmstream-cli.exe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    if (-not (Test-NeedsInstall $target)) { return }

    $url = "https://github.com/vgmstream/vgmstream/releases/download/$VGMSTREAM_TAG/vgmstream-win64.zip"
    $tmp = [System.IO.Path]::GetTempFileName() + '.zip'
    try {
        Get-File $url $tmp
        Expand-Single $tmp $dir
    } finally {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
    Test-Binary $target 'vgmstream-cli'
}

# ----- PATH-resolved tools (not bundled) -----
function Show-PathToolHints {
    Write-Host ""
    Write-Host "-- PATH-resolved tools (not bundled) --"

    if ((Get-Command ffmpeg -ErrorAction SilentlyContinue) `
        -and (Get-Command ffprobe -ErrorAction SilentlyContinue) `
        -and (Get-Command ffplay -ErrorAction SilentlyContinue)) {
        Write-Host "   + ffmpeg, ffprobe, ffplay found on PATH"
    } else {
        Write-Host "   Install ffmpeg via Chocolatey (recommended):"
        Write-Host "       choco install ffmpeg"
        Write-Host "   Or via winget:"
        Write-Host "       winget install --id=Gyan.FFmpeg -e"
        Write-Host "   Or download a static build from https://www.gyan.dev/ffmpeg/builds/"
        Write-Host "   and add its bin\ directory to PATH."
    }

    if (Get-Command pymusiclooper -ErrorAction SilentlyContinue) {
        Write-Host "   + pymusiclooper found on PATH"
    } else {
        Write-Host "   Install pymusiclooper via pipx (recommended):"
        Write-Host "       pipx install pymusiclooper"
        Write-Host "   If pipx isn't installed:"
        Write-Host "       python -m pip install --user pipx; python -m pipx ensurepath"
    }
}

Install-Nus3Audio
Install-UltimateTexCli
Install-BgmProperty
Install-VgmStreamCli
Show-PathToolHints

Write-Host ""
Write-Host "Done. Build UMB with: cd UMB.CLI; dotnet build; dotnet run"
