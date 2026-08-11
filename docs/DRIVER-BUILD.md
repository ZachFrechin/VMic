# Vmic Bridge driver

The driver is a focused patch over Microsoft's SYSVAD sample, not a hand-written
kernel driver. The pinned source revision is stored in
src/Vmic.Driver/SYSVAD_VERSION; the reproducible change is
src/Vmic.Driver/patches/sysvad-vmic.patch.

The pinned revision is the final SYSVAD update before Microsoft's Windows
10 build 22000 work. Newer SYSVAD sources import kernel APIs such as
`ExAllocatePool2` and ship with Windows 11 model decorations, so merely lowering
their INF version floor can produce Device Manager code 39 on Windows 10. The
Vmic bridge follows the pinned source by using `ExAllocatePoolWithTag` with a
non-executable pool, rather than adding that newer import back into the binary.

The preparation script clones that exact revision, initializes WIL, copies the
Vmic ring-buffer sources into EndpointsCommon, applies the patch, and changes
the componentized INF to expose:

- Vmic Bridge Input — the render endpoint selected by VMic Host.
- Vmic Bridge Microphone — the capture endpoint selected in Zoom, OBS, or
  Discord.

When the pinned revision changes, the script replaces only its own generated
`.work/windows-driver-samples` checkout. No manual worktree deletion is needed.

The patch limits SYSVAD to one render and one capture endpoint. Render DMA is
written to the shared non-paged ring; capture DMA reads the same ring and emits
silence on underrun. This is the only data path required for a virtual cable.

Before building, install the matching Windows SDK/WDK plus the active v145
toolset's x64/x86 Spectre libraries, ATL, and ATL with Spectre mitigations.
SYSVAD builds APO projects, so omitting those Visual Studio components produces
`atlbase.h` or `MSB8040` failures. See [the Windows build runbook](WINDOWS-BUILD.md)
for the exact component names.

`Build-Driver.ps1` explicitly sets both `TargetVersion=Windows10` and
`_NT_TARGET_VERSION=0x0A000006` (`NTDDI_WIN10_RS5`). This is the compile-time
counterpart to the INF's Windows 10 build 17763 floor and prevents a current WDK
from silently compiling the driver against its newest NTDDI contract.

Run the complete build and installation flow from an elevated PowerShell:

~~~powershell
./scripts/Build-Driver.ps1 -Configuration Debug
./scripts/Install-Driver.ps1 -PackagePath ./artifacts/driver/Debug-x64
./artifacts/diagnostics/Vmic.Diagnostics.exe bridge
~~~

The installer applies all three componentized SYSVAD packages in order: the
base audio driver, its device extension, and the APO software component. It
then verifies both the root device's PnP status and registration of the
`sysvad_componentizedaudiosample` driver service. A successful script exit
therefore means more than merely staging an INF in the Windows driver store.

This is a test-signed development driver. It is not suitable for public
distribution until it has a production driver-signing and compatibility process.
The SYSVAD code remains subject to Microsoft's license in its upstream
repository; Vmic does not redistribute the upstream source tree.
