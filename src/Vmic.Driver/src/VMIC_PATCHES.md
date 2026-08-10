# Vmic patches on top of the SYSVAD sample

This document tells you **exactly what to change** in Microsoft's SYSVAD sample
(`Windows-driver-samples/audio/sysvad`) to turn it into the Vmic render→capture
bridge. The new module `vmic_bridge.[ch]` is already written; you add it to the
project and make the small edits below.

> **Heads-up:** SYSVAD's internal symbol names can shift slightly between WDK
> releases. The snippets below name the symbols as they appear in recent WDK
> releases. If a name doesn't match, use your editor to find the construct
> described (each section says what to look for) — the edit is the same either
> way. Everything compiles only with the WDK on Windows.

---

## 0. Get the sample and drop in the bridge

1. Clone or download the sample: `https://github.com/microsoft/Windows-driver-samples`
   → `audio/sysvad`.
2. Copy `vmic_bridge.h` and `vmic_bridge.cpp` into the `sysvad` source directory
   (next to `adapter.cpp`).
3. In the SYSVAD project (`sysvad.vcxproj` or the package solution), add both files
   to the compiled sources, or edit the `SOURCES`/`.vcxproj` to include them.

---

## 1. Allocate / free the bridge — `adapter.cpp`

SYSVAD's adapter common object (`CAdapterCommon`) has an `Init`/constructor path
and a destructor/free path. That is where the shared ring is created and torn
down.

Find `CAdapterCommon::Init(...)` (or the place where SYSVAD finishes setting up
the adapter). At the end of successful init, add:

```cpp
#include "vmic_bridge.h"
...
NTSTATUS status = VmicBridgeInit(&g_VmicBridge, VMIC_BRIDGE_DEFAULT_SIZE);
if (!NT_SUCCESS(status)) {
    return status;
}
```

Find the adapter's cleanup/destructor (e.g. `CAdapterCommon::~CAdapterCommon` or
its `Free` method) and add:

```cpp
VmicBridgeDestroy(&g_VmicBridge);
```

> The ring is a single global (`g_VmicBridge`) because there is exactly one render
> endpoint feeding exactly one capture endpoint on this device.

---

## 2. Render side: write into the bridge instead of saving to file

SYSVAD's render stream doesn't play anywhere — it writes the rendered DMA buffer
to a WAV file through a `SaveData`/`m_SaveData` helper. Find where the render
stream hands its buffer to that helper. In the WaveRT stream class
(`CMiniportWaveRTStream`, in `minwavertstream.cpp`) look for the routine that
processes completed render DMA — it calls something like `m_SaveData.WriteData()`
/ `SaveData()` / `m_pSaveData->...`.

Replace (or immediately follow) that call with a write into the bridge:

```cpp
#include "vmic_bridge.h"

// pData = pointer to the rendered DMA bytes, ulNumBytes = their length.
VmicBridgeWrite(&g_VmicBridge, pData, ulNumBytes);
```

Concretely, wherever you see the render stream copying its DMA buffer to the
file sink, add the `VmicBridgeWrite` line with the same pointer and length. You
can leave the file-save in place (it's harmless) or `#if 0` it out.

---

## 3. Capture side: read from the bridge instead of generating a tone

SYSVAD's capture stream fills its DMA buffer with a sine wave from a
`ToneGenerator` (member like `m_ToneGenerator`). Find the capture stream's
buffer-fill routine in `minwavertstream.cpp` — it calls something like
`m_ToneGenerator.GenerateSamples(...)` to populate the capture DMA buffer.

Replace that call with a read from the bridge:

```cpp
#include "vmic_bridge.h"

// pData = pointer to the capture DMA buffer to fill, ulNumBytes = its length.
VmicBridgeRead(&g_VmicBridge, pData, ulNumBytes);
```

`VmicBridgeRead` pads with silence when the render side hasn't produced enough,
so the capture endpoint always has data (no glitching on underrun).

---

## 4. Endpoint topology

For v1 you do **not** need to touch the topology miniport. The OS already sees
one render endpoint and one capture endpoint from SYSVAD; the bridge is invisible
to it. The Host app plays into the render endpoint, and consumer apps capture
from the capture endpoint.

Optionally, to reduce confusion, you can disable SYSVAD's extra endpoints (it
exposes several) so only one render + one capture remain — edit the topology
tables in `basetopo.cpp`/`mintopo.cpp` if you want that. Not required.

---

## 5. INF / naming

Use the provided `VmicBridge.inf` (rename the strings to "Vmic Bridge", hardware
id `ROOT\VMIC_BRIDGE`). If you prefer to keep SYSVAD's `.inx` template, just
change the device friendly name to "Vmic Bridge" so the app can find it.

---

## 6. Build, sign, install

See `docs/DRIVER-BUILD.md` at the repo root for the full WDK build, test-signing,
and `pnputil` install procedure.

---

## Verification (on Windows)

1. Build + install the driver; confirm **"Vmic Bridge"** appears under both
   **Playback** and **Recording** in the Sound control panel.
2. Set any player's output to "Vmic Bridge" and open Voice Recorder / Audacity
   with "Vmic Bridge" as the microphone — you should hear/see the played audio.
   This validates the bridge without the app.
3. Then run the Vmic app in Host mode, select "Vmic Bridge" as the render target,
   and confirm the mixed mic appears at the capture endpoint.
