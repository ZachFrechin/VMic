[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,
    [switch]$EnableTestSigning
)

$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
}

if ($EnableTestSigning) {
    bcdedit /set TESTSIGNING ON
    Write-Warning "TESTSIGNING was enabled. Restart Windows, then rerun this command without -EnableTestSigning."
    exit 3010
}

$inf = Join-Path $PackagePath "ComponentizedAudioSample.inf"
if (-not (Test-Path $inf)) { throw "ComponentizedAudioSample.inf not found in $PackagePath" }
$devcon = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\Tools" -Filter devcon.exe -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\x64\\" } | Select-Object -First 1
if (-not $devcon) { throw "DevCon x64 was not found. Install the Windows Driver Kit." }

& $devcon.FullName remove "@ROOT\VmicBridge\*"
pnputil /add-driver $inf
if ($LASTEXITCODE -ne 0) { throw "PnPUtil could not add the driver package." }
& $devcon.FullName install $inf "Root\VmicBridge"
if ($LASTEXITCODE -ne 0) { throw "DevCon could not create the root-enumerated Vmic Bridge device." }

Write-Host "Vmic Bridge installed. Run artifacts/diagnostics/Vmic.Diagnostics.exe bridge to verify render-to-capture."
