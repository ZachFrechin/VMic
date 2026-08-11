[CmdletBinding()]
param(
    [string]$Worktree = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$revision = (Get-Content (Join-Path $repoRoot "src/Vmic.Driver/SYSVAD_VERSION") -Raw).Trim()
$patch = Join-Path $repoRoot "src/Vmic.Driver/patches/sysvad-vmic.patch"
$bridgeDir = Join-Path $repoRoot "src/Vmic.Driver/src"
if ([string]::IsNullOrWhiteSpace($Worktree)) {
    $Worktree = Join-Path $repoRoot ".work/windows-driver-samples"
}

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "'$Name' is required. Install Git for Windows, then run this command again."
    }
}

Require-Command git
$sysvad = Join-Path $Worktree "audio/sysvad"

function Sync-VmicBridgeSources {
    param([string]$SysvadRoot)

    # These two files are maintained in this repository. The SYSVAD checkout is
    # generated under .work, so refresh them on every run instead of treating a
    # preparation marker as proof that their contents are still current.
    $endpointsCommon = Join-Path $SysvadRoot "EndpointsCommon"
    if (-not (Test-Path $endpointsCommon)) {
        throw "Prepared SYSVAD directory is missing: $endpointsCommon"
    }

    Copy-Item -Force (Join-Path $bridgeDir "vmic_bridge.h") (Join-Path $endpointsCommon "vmic_bridge.h")
    Copy-Item -Force (Join-Path $bridgeDir "vmic_bridge.cpp") (Join-Path $endpointsCommon "vmic_bridge.cpp")
}

$preparedMarker = Join-Path $Worktree ".vmic-prepared-$revision"
if (Test-Path $Worktree) {
    $current = (git -C $Worktree rev-parse HEAD 2>$null).Trim()
    if ($current -eq $revision -and (Test-Path $preparedMarker) -and -not $Force) {
        Sync-VmicBridgeSources $sysvad
        Write-Host "SYSVAD worktree already prepared; refreshed Vmic bridge sources at $Worktree"
        exit 0
    }
    if ($current -eq $revision -and -not $Force) {
        # A prior run can have stopped between cloning and INF rewriting. Reset
        # this private .work checkout so the patch is applied exactly once.
        Write-Host "Resetting incomplete SYSVAD preparation at $Worktree"
        git -C $Worktree reset --hard $revision
        git -C $Worktree clean -fd
    }
    elseif (-not $Force) {
        throw "$Worktree already exists but is not the expected revision. Re-run with -Force after checking it."
    }
    else {
        Remove-Item -Recurse -Force $Worktree
    }
}

if (-not (Test-Path $Worktree)) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $Worktree) | Out-Null
    git clone --filter=blob:none https://github.com/microsoft/Windows-driver-samples.git $Worktree
    git -C $Worktree checkout $revision
    git -C $Worktree submodule update --init wil
}

Sync-VmicBridgeSources $sysvad
git -C $Worktree apply --check $patch
git -C $Worktree apply $patch

# ComponentizedAudioSample.inx is UTF-16LE. .NET APIs work in both Windows
# PowerShell 5.1 and PowerShell 7, unlike the Encoding parameter on old hosts.
$inf = Join-Path $sysvad "TabletAudioSample/ComponentizedAudioSample.inx"
$utf16 = [System.Text.Encoding]::Unicode
$text = [System.IO.File]::ReadAllText($inf, $utf16)
$text = $text.Replace('Root\sysvad_ComponentizedAudioSample', 'Root\VmicBridge')
$text = $text.Replace('Virtual Audio Device (WDM) - Tablet Sample', 'Vmic Bridge Virtual Audio Device')
$text = $text.Replace('SYSVAD Wave Speaker', 'Vmic Bridge Input')
$text = $text.Replace('SYSVAD Wave Microphone Headphone', 'Vmic Bridge Microphone')
[System.IO.File]::WriteAllText($inf, $text, $utf16)
[System.IO.File]::WriteAllText($preparedMarker, $revision + [Environment]::NewLine, [System.Text.Encoding]::ASCII)

Write-Host "Prepared SYSVAD revision $revision"
Write-Host "Source directory: $sysvad"
