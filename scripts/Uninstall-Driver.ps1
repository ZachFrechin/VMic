[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
}

$devcon = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\Tools" -Filter devcon.exe -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\x64\\" } | Select-Object -First 1
if ($devcon) { & $devcon.FullName remove "@ROOT\VmicBridge\*" }
pnputil /enum-drivers | Select-String -Pattern "Vmic" -Context 0,6
Write-Host "The device was removed. If PnPUtil lists an old Vmic package, delete the published oem*.inf from an elevated prompt."
