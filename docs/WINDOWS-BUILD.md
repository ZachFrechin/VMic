# Build Windows

Vmic targets Windows 10 version 1903 (build 18362) and later, plus Windows 11,
on x64 machines. The graphical app and
the diagnostic utility are self-contained after publication; only driver
development needs Visual Studio, the Windows SDK, and the Windows Driver Kit.
The preparation script uses a pinned pre-Windows-11 SYSVAD revision and emits
model decorations for Windows 10 build 18362+. This avoids importing newer
kernel APIs from a Windows 11-oriented SYSVAD binary. When that pinned revision
changes, the generated `.work` checkout is refreshed automatically.
The preparation also suppresses only WDK warning C4996 for the pinned
down-level pool API and applies Microsoft's removal of the obsolete APO
`EmbedManifest=false` setting, allowing the older source to build with current
WDK toolchains without changing its Windows runtime requirements. The build
also fixes `_NT_TARGET_VERSION` to `NTDDI_WIN10_19H1` (Windows 10 version 1903),
so the current WDK cannot select imports from its newest OS contract.

## One-time prerequisites

Install Git for Windows, .NET SDK 10, a supported Visual Studio C++ toolchain,
the Windows 10/11 SDK, and the matching Windows Driver Kit. In **Visual Studio
Installer → Modify → Individual components**, make sure these driver-build
components are installed for the active v145 toolset:

- **MSVC v145 C++ x64/x86 Spectre-mitigated libs**
- **C++ ATL for latest v145 build tools (x86 & x64)**
- **C++ ATL for latest v145 build tools with Spectre Mitigations (x86 & x64)**

The SYSVAD solution also builds APO projects. ATL supplies `atlbase.h`; the
Spectre libraries are required by the WDK build configuration. Missing either
causes errors such as `atlbase.h` not found or `MSB8040`; they cannot be fixed
from the Vmic source tree. The official SYSVAD instructions also require the
WIL submodule; `Prepare-Sysvad.ps1` retrieves it automatically.

Open an elevated PowerShell in the repository and activate Windows test-signing:

~~~powershell
./scripts/Install-Driver.ps1 -PackagePath . -EnableTestSigning
~~~

Restart Windows when requested. Test-signing is solely for this development
driver; disable it afterwards with bcdedit /set TESTSIGNING OFF and reboot.

## Build and run

~~~powershell
./scripts/Publish-App.ps1
./scripts/Build-Driver.ps1 -Configuration Debug
./scripts/Install-Driver.ps1 -PackagePath ./artifacts/driver/Debug-x64
./artifacts/diagnostics/Vmic.Diagnostics.exe all
~~~

`Install-Driver.ps1` installs the generated base, extension, and APO INF files;
do not install only `ComponentizedAudioSample.inf` manually. The script also
checks that Windows registered the SYSVAD driver service before reporting
success.

The final command must report a passing network test and a passing bridge test.
Then launch artifacts/app/Vmic.exe, select your real microphone as input and
Vmic Bridge Input as Host output. Conferencing software must select Vmic Bridge
Microphone as its microphone.

## If a build fails

- Missing WDK or C++ components: rerun the Visual Studio Installer and add the
  Desktop C++ workload, Windows SDK, and Windows Driver Kit.
- A rejected driver signature: confirm that Windows restarted in test-signing
  mode, then inspect the build output for the generated catalog/signature.
- Failed installation: inspect %windir%\\inf\\setupapi.dev.log, then run
  ./scripts/Uninstall-Driver.ps1 before retrying.
- `InfVerif.dll` missing under `bin\\...\\x86` or `aitstatic` exit code 193:
  update the repository, then rerun `Build-Driver.ps1`. The script prioritizes
  64-bit MSBuild, which makes the WDK use its x64 validation tools for the x64
  driver. It intentionally does not skip INF or API validation.
- Failed bridge diagnostic: open Sound settings and confirm both Vmic Bridge
  endpoints are active; send the result and the generated logs before changing
  the driver sources.
- Device Manager code 39 with status `0xC0000263`: Windows could not resolve a
  driver import. Confirm that the build prints `_NT_TARGET_VERSION=0x0A000007`,
  then uninstall, rebuild, and reinstall; the installer includes the detailed
  PnP problem status in its error message.
