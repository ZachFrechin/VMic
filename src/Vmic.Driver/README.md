# Vmic.Driver — SYSVAD-based virtual audio bridge

A kernel-mode virtual audio device that exposes **one render endpoint and one
capture endpoint**, bridged so that anything played into the render endpoint
appears as microphone input on the capture endpoint (a "virtual cable", like
VB-Cable). The Vmic Host app plays the mixed mic audio into the render endpoint;
conferencing apps then select the capture endpoint as their microphone.

This cannot be built on macOS — it requires the Windows Driver Kit. The sources
here are an **overlay** on Microsoft's SYSVAD sample.

## What's here

```
Vmic.Driver/
├─ README.md                 ← you are here
├─ VmicBridge.inf            ← device INF (renames SYSVAD → "Vmic Bridge")
├─ VmicBridge.vcxproj        ← WDK project (representative; see note below)
└─ src/
   ├─ vmic_bridge.h          ← render→capture ring buffer (complete)
   ├─ vmic_bridge.cpp        ← ring buffer implementation (complete)
   └─ VMIC_PATCHES.md        ← step-by-step edits to SYSVAD's existing files
```

## How to assemble

1. Get the SYSVAD sample: clone `microsoft/Windows-driver-samples`, use
   `audio/sysvad`.
2. Copy `src/vmic_bridge.h` and `src/vmic_bridge.cpp` into the sample's source
   directory and add them to the build (see `VMIC_PATCHES.md` §0).
3. Apply the small edits in `src/VMIC_PATCHES.md` (allocate the ring in the
   adapter; write render DMA into the ring; read capture DMA out of the ring).
4. Rename the built driver / INF strings to "Vmic Bridge" (`VmicBridge.inf`).
5. Build, test-sign, and install — full steps in `docs/DRIVER-BUILD.md`.

## Why an overlay instead of a full driver?

SYSVAD is a large AVStream/port-class sample. Rewriting it from scratch here
would be error-prone and unverifiable without a Windows+WDK machine. Reusing the
official sample and changing only the audio source/sink (file-save → ring,
tone → ring) is the standard, minimal-risk way to build a virtual cable, and it
keeps the diff small and reviewable.

## Notes

- The bridge is a single non-paged ring protected by a spin lock; render writes
  drop oldest data on overflow, capture reads pad with silence on underrun.
- No custom IOCTLs are used: the user-mode app talks to the device purely through
  WASAPI (render endpoint in, capture endpoint out).
- First WDK build may need small fixups for your specific WDK release — the patch
  guide is written to be robust to minor symbol-name differences.
