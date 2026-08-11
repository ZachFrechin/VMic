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

function Update-VmicInfTemplates {
    param([string]$SysvadRoot)

    # Keep the generated package explicitly limited to Windows 10 1809 and
    # newer. The pinned pre-Windows-11 SYSVAD revision uses undecorated model
    # sections; previously prepared worktrees may contain the newer 22621 floor.
    $templateDir = Join-Path $SysvadRoot "TabletAudioSample"
    $utf16 = [System.Text.Encoding]::Unicode
    foreach ($name in @("ComponentizedAudioSample.inx", "ComponentizedAudioSampleExtension.inx", "ComponentizedApoSample.inx")) {
        $inf = Join-Path $templateDir $name
        $text = [System.IO.File]::ReadAllText($inf, $utf16)
        if ($text.Contains('NT$ARCH$.10.0...17763')) {
            # Already prepared. Keep this branch first because the target
            # decoration itself also contains the generic NT$ARCH$ substring.
        }
        elseif ($text.Contains('NT$ARCH$.10.0...22621')) {
            $text = $text.Replace('NT$ARCH$.10.0...22621', 'NT$ARCH$.10.0...17763')
        }
        elseif ($text.Contains('NT$ARCH$')) {
            # Upgrade worktrees prepared by the earlier generic-decoration
            # version of this script without requiring a manual reset.
            $text = $text.Replace('NT$ARCH$', 'NT$ARCH$.10.0...17763')
        }

        if ($name -eq "ComponentizedAudioSample.inx") {
            $text = $text.Replace('Root\sysvad_ComponentizedAudioSample', 'Root\VmicBridge')
            $text = $text.Replace('Virtual Audio Device (WDM) - Tablet Sample', 'Vmic Bridge Virtual Audio Device')
            $text = $text.Replace('SYSVAD Wave Speaker', 'Vmic Bridge Input')
            $text = $text.Replace('SYSVAD Wave Microphone Headphone', 'Vmic Bridge Microphone')
        }
        elseif ($name -eq "ComponentizedAudioSampleExtension.inx") {
            $text = $text.Replace('Root\sysvad_ComponentizedAudioSample', 'Root\VmicBridge')
        }

        [System.IO.File]::WriteAllText($inf, $text, $utf16)
    }
}

$preparedMarker = Join-Path $Worktree ".vmic-prepared-$revision"
if (Test-Path $Worktree) {
    $current = (git -C $Worktree rev-parse HEAD 2>$null).Trim()
    $ownedMarkers = @(Get-ChildItem $Worktree -Filter ".vmic-prepared-*" -File -ErrorAction SilentlyContinue)
    if ($current -eq $revision -and (Test-Path $preparedMarker) -and -not $Force) {
        Sync-VmicBridgeSources $sysvad
        Update-VmicInfTemplates $sysvad
        Write-Host "SYSVAD worktree already prepared; refreshed Vmic bridge sources and INF templates at $Worktree"
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
        if ($ownedMarkers.Count -gt 0) {
            Write-Host "Replacing the generated SYSVAD worktree because the pinned revision changed."
            Remove-Item -Recurse -Force $Worktree
        }
        else {
            throw "$Worktree already exists but is not the expected revision. Re-run with -Force after checking it."
        }
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
Update-VmicInfTemplates $sysvad
[System.IO.File]::WriteAllText($preparedMarker, $revision + [Environment]::NewLine, [System.Text.Encoding]::ASCII)

Write-Host "Prepared SYSVAD revision $revision"
Write-Host "Source directory: $sysvad"
