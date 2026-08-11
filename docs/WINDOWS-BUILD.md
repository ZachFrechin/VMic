# Build Windows

Vmic targets Windows 10 version 1809 (build 17763) and later, plus Windows 11,
on x64 machines. The graphical app and
the diagnostic utility are self-contained after publication; only driver
development needs Visual Studio, the Windows SDK, and the Windows Driver Kit.
The preparation script converts the upstream SYSVAD model decorations from
Windows 11 build 22621+ to Windows 10 build 17763+ before building, so the
generated test driver can be selected on supported Windows 10 systems too.

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
