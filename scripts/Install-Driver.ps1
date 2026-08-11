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

$devcon = Find-WdkTool "devcon.exe"
$devgen = Find-WdkTool "devgen.exe"
if ($devcon) {
    # Removing a non-existent device returns a non-zero DevCon status, which is
    # harmless here: the next step creates a fresh root-enumerated device.
    & $devcon.FullName remove "@ROOT\VmicBridge\*"
}

if ($devgen) {
    # Microsoft recommends DevGen + PnPUtil over DevCon install. DevCon's
    # combined create/update operation can leave a created device behind when
    # its update phase fails, as it did for the original installer flow.
    & $devgen.FullName /add /bus ROOT /hardwareid "Root\VmicBridge"
    if ($LASTEXITCODE -ne 0) { throw "DevGen could not create the root-enumerated Vmic Bridge device." }

    pnputil /add-driver $inf /install
    $pnputilExitCode = $LASTEXITCODE
    if ($pnputilExitCode -eq 3010) {
        Write-Warning "Vmic Bridge was installed successfully and Windows must be restarted to finish installation."
    }
    elseif ($pnputilExitCode -eq 1641) {
        Write-Host "Vmic Bridge was installed successfully and Windows is restarting."
        exit 1641
    }
    elseif ($pnputilExitCode -ne 0) {
        throw "PnPUtil could not install the Vmic Bridge driver package (exit code $pnputilExitCode). Check %windir%\inf\setupapi.dev.log."
    }
}
else {
    if (-not $devcon) { throw "DevGen or DevCon x64 was not found. Install the Windows Driver Kit." }

    Write-Warning "DevGen x64 was not found; falling back to DevCon install."
    & $devcon.FullName install $inf "Root\VmicBridge"
    if ($LASTEXITCODE -ne 0) { throw "DevCon could not create and install the root-enumerated Vmic Bridge device." }
}

Write-Host "Vmic Bridge installed. Run artifacts/diagnostics/Vmic.Diagnostics.exe bridge to verify render-to-capture."
