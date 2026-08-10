[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$Worktree = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Worktree)) {
    $Worktree = Join-Path $repoRoot ".work/windows-driver-samples"
}
& (Join-Path $PSScriptRoot "Prepare-Sysvad.ps1") -Worktree $Worktree

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio/Installer/vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "Visual Studio Build Tools + Windows SDK + WDK are required. See docs/WINDOWS-BUILD.md."
}
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) {
    throw "MSBuild was not found. Install the C++ desktop workload and the WDK."
}

$solution = Join-Path $Worktree "audio/sysvad/sysvad.sln"
& $msbuild $solution /m /t:Rebuild "/p:Configuration=$Configuration" "/p:Platform=x64"
if ($LASTEXITCODE -ne 0) { throw "SYSVAD build failed." }

$package = Get-ChildItem $Worktree -Filter "ComponentizedAudioSample.inf" -File -Recurse |
    Where-Object { $_.FullName -match "\\package\\" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $package) { throw "The SYSVAD package was not found after the build." }

$destination = Join-Path $repoRoot "artifacts/driver/$Configuration-x64"
New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item (Join-Path $package.Directory "*") $destination -Recurse -Force
Write-Host "Driver package copied to $destination"
Write-Host "Next: run scripts/Install-Driver.ps1 -PackagePath '$destination' as Administrator."
