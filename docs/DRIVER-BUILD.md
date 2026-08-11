# Vmic Bridge driver

The driver is a focused patch over Microsoft's SYSVAD sample, not a hand-written
kernel driver. The pinned source revision is stored in
src/Vmic.Driver/SYSVAD_VERSION; the reproducible change is
src/Vmic.Driver/patches/sysvad-vmic.patch.

The preparation script clones that exact revision, initializes WIL, copies the
Vmic ring-buffer sources into EndpointsCommon, applies the patch, and changes
the componentized INF to expose:

- Vmic Bridge Input — the render endpoint selected by VMic Host.
- Vmic Bridge Microphone — the capture endpoint selected in Zoom, OBS, or
  Discord.

The patch limits SYSVAD to one render and one capture endpoint. Render DMA is
written to the shared non-paged ring; capture DMA reads the same ring and emits
silence on underrun. This is the only data path required for a virtual cable.

Before building, install the matching Windows SDK/WDK plus the active v145
toolset's x64/x86 Spectre libraries, ATL, and ATL with Spectre mitigations.
SYSVAD builds APO projects, so omitting those Visual Studio components produces
`atlbase.h` or `MSB8040` failures. See [the Windows build runbook](WINDOWS-BUILD.md)
for the exact component names.

Run the complete build and installation flow from an elevated PowerShell:

~~~powershell
./scripts/Build-Driver.ps1 -Configuration Debug
./scripts/Install-Driver.ps1 -PackagePath ./artifacts/driver/Debug-x64
./artifacts/diagnostics/Vmic.Diagnostics.exe bridge
~~~

This is a test-signed development driver. It is not suitable for public
distribution until it has a production driver-signing and compatibility process.
The SYSVAD code remains subject to Microsoft's license in its upstream
repository; Vmic does not redistribute the upstream source tree.
