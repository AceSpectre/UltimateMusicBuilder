# Runtime setup for Windows; -Dev also prepares a source checkout.
[CmdletBinding()]
param([switch]$Dev, [switch]$DryRun)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Use bash setup.sh on macOS or Linux.'
}

function Test-Command([string]$Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Invoke-SetupCommand([string]$Command, [string[]]$Arguments) {
    Write-Host "  $Command $($Arguments -join ' ')"
    if (-not $DryRun) {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE" }
    }
}

function Update-SetupPath {
    # Installers update the registry, not the already-running PowerShell process.
    $env:Path = @(
        $env:Path
        [Environment]::GetEnvironmentVariable('Path', 'Machine')
        [Environment]::GetEnvironmentVariable('Path', 'User')
    ) -join ';'
}

function Install-WinGetPackage([string]$Id) {
    if (-not (Test-Command winget)) {
        throw 'Install Windows App Installer (winget) from Microsoft Store, then rerun setup.'
    }
    Invoke-SetupCommand winget @('install', '--id', $Id, '--exact', '--source', 'winget')
    if (-not $DryRun) { Update-SetupPath }
}

if ($Dev) {
    if (-not (Test-Path (Join-Path $PSScriptRoot 'UMB.Desktop/package.json'))) {
        throw '-Dev requires a source checkout.'
    }
    if (-not (Test-Command dotnet) -or -not ((& dotnet --list-sdks) -match '^8\.')) {
        throw 'Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0'
    }
    if (-not (Test-Command node) -or -not (Test-Command npm)) {
        throw 'Install Node.js 20+ and npm: https://nodejs.org/en/download'
    }
    if ((& node -p "Number(process.versions.node.split('.')[0]) >= 20") -ne 'true') {
        throw 'Node.js 20 or newer is required.'
    }
}

Write-Host 'UMB setup (Windows)'
if (-not (Test-Command ffmpeg) -or -not (Test-Command ffprobe) -or -not (Test-Command ffplay)) {
    Install-WinGetPackage 'Gyan.FFmpeg'
} else {
    Write-Host 'FFmpeg is already available.'
}

if (-not (Test-Command pymusiclooper)) {
    # py selects a real Python installation rather than the Windows Store alias.
    if (-not (Test-Command py)) { Install-WinGetPackage 'Python.Python.3.12' }
    if (-not $DryRun -and -not (Test-Command py)) {
        throw 'Python was installed but py is not on PATH. Open a new terminal and rerun setup.'
    }
    $HasPipx = $false
    if (-not $DryRun) {
        $HasPipx = (& py -3 -c "import importlib.util; print(importlib.util.find_spec('pipx') is not None)") -eq 'True'
        if ($LASTEXITCODE -ne 0) { throw 'Cannot run Python. Open a new terminal and rerun setup.' }
    }
    if (-not $HasPipx) {
        Invoke-SetupCommand py @('-3', '-m', 'pip', 'install', '--user', 'pipx')
    }
    if (-not $DryRun) {
        $PipxBin = & py -3 -m pipx environment --value PIPX_BIN_DIR
        if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the pipx application directory.' }
        $env:Path = "$PipxBin;$env:Path"
    }
    if (-not (Test-Command pymusiclooper)) {
        Invoke-SetupCommand py @('-3', '-m', 'pipx', 'install', 'pymusiclooper')
    }
    Invoke-SetupCommand py @('-3', '-m', 'pipx', 'ensurepath')
} else {
    Write-Host 'pymusiclooper is already available.'
}

if ($Dev) {
    $FetchScript = Join-Path $PSScriptRoot 'scripts/fetch-tools.ps1'
    Write-Host "  $FetchScript"
    if (-not $DryRun) {
        & $FetchScript
        foreach ($Tool in 'Nus3Audio/nus3audio.exe', 'UltimateTexCli/ultimate_tex_cli.exe', 'BgmProperty/bgm-property.exe', 'vgmstream-cli/vgmstream-cli.exe') {
            if (-not (Test-Path (Join-Path $PSScriptRoot "Tools/$Tool"))) {
                throw "Native tool is missing: Tools/$Tool"
            }
        }
    }
    Invoke-SetupCommand npm @('--prefix', (Join-Path $PSScriptRoot 'UMB.Desktop'), 'ci')
}

if ($DryRun) {
    Write-Host 'Dry run complete; nothing was installed.'
} else {
    foreach ($Tool in 'ffmpeg', 'ffprobe', 'ffplay', 'pymusiclooper') {
        if (-not (Test-Command $Tool)) { throw "$Tool is still missing from PATH." }
    }
    Write-Host 'Setup complete. Open a new terminal before launching UMB.'
    Write-Host 'Game data and Resources/template.nus3bank must be supplied separately.'
}
