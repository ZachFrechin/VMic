[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
}

$vmicDevices = @(Get-CimInstance -ClassName Win32_PnPEntity | Where-Object {
    $_.HardwareID -contains "Root\VmicBridge"
})
foreach ($device in $vmicDevices) {
    pnputil /remove-device $device.PNPDeviceID
    if ($LASTEXITCODE -ne 0) { throw "Could not remove Vmic Bridge device $($device.PNPDeviceID)." }
}
pnputil /enum-drivers | Select-String -Pattern "Vmic" -Context 0,6
Write-Host "The device was removed. If PnPUtil lists an old Vmic package, delete the published oem*.inf from an elevated prompt."
