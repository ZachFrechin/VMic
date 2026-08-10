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
if (Test-Path $Worktree) {
    $current = (git -C $Worktree rev-parse HEAD 2>$null).Trim()
    if ($current -eq $revision -and -not $Force) {
        Write-Host "SYSVAD worktree already prepared at $Worktree"
        exit 0
    }
    if (-not $Force) {
        throw "$Worktree already exists but is not the expected revision. Re-run with -Force after checking it."
    }
    Remove-Item -Recurse -Force $Worktree
}

New-Item -ItemType Directory -Force (Split-Path -Parent $Worktree) | Out-Null
git clone --filter=blob:none https://github.com/microsoft/Windows-driver-samples.git $Worktree
git -C $Worktree checkout $revision
git -C $Worktree submodule update --init wil

$sysvad = Join-Path $Worktree "audio/sysvad"
Copy-Item (Join-Path $bridgeDir "vmic_bridge.h") (Join-Path $sysvad "EndpointsCommon/vmic_bridge.h")
Copy-Item (Join-Path $bridgeDir "vmic_bridge.cpp") (Join-Path $sysvad "EndpointsCommon/vmic_bridge.cpp")
git -C $Worktree apply --check $patch
git -C $Worktree apply $patch

# ComponentizedAudioSample.inx is UTF-16LE. Keep the upstream encoding intact.
$inf = Join-Path $sysvad "TabletAudioSample/ComponentizedAudioSample.inx"
$text = Get-Content -Raw -Encoding Unicode $inf
$text = $text.Replace('Root\sysvad_ComponentizedAudioSample', 'Root\VmicBridge')
$text = $text.Replace('Virtual Audio Device (WDM) - Tablet Sample', 'Vmic Bridge Virtual Audio Device')
$text = $text.Replace('SYSVAD Wave Speaker', 'Vmic Bridge Input')
$text = $text.Replace('SYSVAD Wave Microphone Headphone', 'Vmic Bridge Microphone')
Set-Content -Encoding Unicode -NoNewline $inf $text

Write-Host "Prepared SYSVAD revision $revision"
Write-Host "Source directory: $sysvad"
