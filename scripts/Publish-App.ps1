[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot "artifacts"
$appOut = Join-Path $artifacts "app"
$diagOut = Join-Path $artifacts "diagnostics"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw ".NET SDK 10 is required. Install it with: winget install Microsoft.DotNet.SDK.10 --source winget"
}
$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or [int]($sdkVersion.Split('.')[0]) -lt 10) {
    throw ".NET SDK 10 or newer is required (current SDK: $sdkVersion). Install it with: winget install Microsoft.DotNet.SDK.10 --source winget"
}

dotnet test (Join-Path $repoRoot "Vmic.slnx") --configuration $Configuration --disable-build-servers -m:1
if ($LASTEXITCODE -ne 0) { throw "Tests failed; publication aborted." }

dotnet publish (Join-Path $repoRoot "src/Vmic.App/Vmic.App.csproj") --configuration $Configuration --runtime win-x64 --self-contained true --output $appOut -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "Application publish failed." }
dotnet publish (Join-Path $repoRoot "tools/Vmic.Diagnostics/Vmic.Diagnostics.csproj") --configuration $Configuration --runtime win-x64 --self-contained true --output $diagOut -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "Diagnostic publish failed." }

$zip = Join-Path $artifacts "Vmic-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $appOut "*"), (Join-Path $diagOut "*") -DestinationPath $zip
Write-Host "Published $zip"
