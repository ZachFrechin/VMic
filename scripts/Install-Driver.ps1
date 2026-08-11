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

$driverPackages = @(
    [PSCustomObject]@{
        Name = "base driver"
        Path = Join-Path $PackagePath "ComponentizedAudioSample.inf"
    },
    [PSCustomObject]@{
        Name = "device extension"
        Path = Join-Path $PackagePath "ComponentizedAudioSampleExtension.inf"
    },
    [PSCustomObject]@{
        Name = "APO software component"
        Path = Join-Path $PackagePath "ComponentizedApoSample.inf"
    }
)

foreach ($driverPackage in $driverPackages) {
    if (-not (Test-Path $driverPackage.Path)) {
        throw "$($driverPackage.Path) was not found. Rebuild the complete SYSVAD package before installing it."
    }
}

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

function Install-DriverPackage([string]$Name, [string]$Path) {
    Write-Host "Installing Vmic Bridge $Name from $Path"
    pnputil /add-driver $Path /install
    $pnputilExitCode = $LASTEXITCODE

    if ($pnputilExitCode -eq 3010) {
        $script:restartRequired = $true
        Write-Warning "The Vmic Bridge $Name was installed and Windows must be restarted."
    }
    elseif ($pnputilExitCode -eq 1641) {
        Write-Host "The Vmic Bridge $Name was installed and Windows is restarting."
        exit 1641
    }
    elseif ($pnputilExitCode -eq 259) {
        Write-Host "PnPUtil reported no change for the Vmic Bridge $Name. Final device checks will confirm whether it is active."
    }
    elseif ($pnputilExitCode -ne 0) {
        throw "PnPUtil could not install the Vmic Bridge $Name (exit code $pnputilExitCode). Check %windir%\inf\setupapi.dev.log."
    }
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

$script:restartRequired = $false
foreach ($driverPackage in $driverPackages) {
    Install-DriverPackage -Name $driverPackage.Name -Path $driverPackage.Path
}

pnputil /scan-devices
if ($LASTEXITCODE -ne 0) {
    Write-Warning "PnPUtil could not rescan devices (exit code $LASTEXITCODE). Windows may need to be restarted before validation."
}

$vmicDevices = Get-VmicDevices
$problemDevices = @($vmicDevices | Where-Object { $_.ConfigManagerErrorCode -ne 0 })
if ($problemDevices.Count -gt 0) {
    $problemDetails = @($problemDevices | ForEach-Object {
        $problemStatus = "unavailable"
        try {
            $statusProperty = Get-PnpDeviceProperty -InstanceId $_.PNPDeviceID `
                -KeyName "DEVPKEY_Device_ProblemStatus" -ErrorAction Stop
            $problemStatus = $statusProperty.Data
        }
        catch {
            # The PnpDevice module is present on supported Windows 10 builds,
            # but retain the standard PnP code if the detailed property cannot
            # be queried for any reason.
        }

        "$($_.PNPDeviceID): code $($_.ConfigManagerErrorCode), status $problemStatus"
    }) -join "; "
    $osVersion = [Environment]::OSVersion.Version
    throw "Vmic Bridge failed to start on Windows $osVersion ($problemDetails). Check %windir%\inf\setupapi.dev.log."
}

$driverService = Get-CimInstance -ClassName Win32_SystemDriver `
    -Filter "Name='sysvad_componentizedaudiosample'" -ErrorAction SilentlyContinue
if (-not $driverService) {
    throw "The Vmic Bridge device exists, but the sysvad_componentizedaudiosample driver service was not registered. Check %windir%\inf\setupapi.dev.log."
}

Write-Host "Vmic Bridge driver service: $($driverService.State)"
if ($script:restartRequired) {
    Write-Warning "Restart Windows before running the bridge diagnostic."
}

Write-Host "All Vmic Bridge driver packages are installed. Run artifacts/diagnostics/Vmic.Diagnostics.exe bridge to verify render-to-capture."
