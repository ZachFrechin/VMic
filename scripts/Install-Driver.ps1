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

function Find-WdkTool([string]$Name) {
    Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\Tools" -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
}

function Get-VmicDevices {
    @(Get-CimInstance -ClassName Win32_PnPEntity | Where-Object {
        $_.HardwareID -contains "Root\VmicBridge"
    })
}

$devgen = Find-WdkTool "devgen.exe"
$vmicDevices = Get-VmicDevices

if ($vmicDevices.Count -eq 0) {
    if (-not $devgen) { throw "DevGen x64 was not found. Install the Windows Driver Kit." }

    # Microsoft recommends DevGen + PnPUtil over DevCon install. Create the
    # root device once, then let PnPUtil install or update its package.
    & $devgen.FullName /add /bus ROOT /hardwareid "Root\VmicBridge"
    if ($LASTEXITCODE -ne 0) { throw "DevGen could not create the root-enumerated Vmic Bridge device." }
    $vmicDevices = Get-VmicDevices
}

if ($vmicDevices.Count -eq 0) { throw "Vmic Bridge device was not found after DevGen completed." }
if ($vmicDevices.Count -gt 1) {
    Write-Warning "Found $($vmicDevices.Count) Vmic Bridge devices from prior installs. Run Uninstall-Driver.ps1 once to remove duplicates, then install again."
}

pnputil /add-driver $inf /install
$pnputilExitCode = $LASTEXITCODE
if ($pnputilExitCode -eq 3010) {
    Write-Warning "Vmic Bridge was installed successfully and Windows must be restarted to finish installation."
}
elseif ($pnputilExitCode -eq 1641) {
    Write-Host "Vmic Bridge was installed successfully and Windows is restarting."
    exit 1641
}
elseif ($pnputilExitCode -eq 259) {
    Write-Host "Vmic Bridge is already using the current driver package; no update was needed."
}
elseif ($pnputilExitCode -ne 0) {
    throw "PnPUtil could not install the Vmic Bridge driver package (exit code $pnputilExitCode). Check %windir%\inf\setupapi.dev.log."
}

Write-Host "Vmic Bridge installed. Run artifacts/diagnostics/Vmic.Diagnostics.exe bridge to verify render-to-capture."
