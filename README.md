# Vmic 🎙

**Two computers, one combined virtual microphone.**

Vmic lets two computers on the same network act as a **Host** and a **Client**.
The Host mixes its own microphone with the Client's microphone (streamed over the
LAN) and publishes the result as a **single virtual microphone** that any app on
the Host — Zoom, OBS, Discord, Voice Recorder — can capture.

Built with **C# / WPF / NAudio** for the Windows app and a **SYSVAD-based**
test driver for the virtual device.

> **Development status:** the Core, WPF app, packaging scripts, loopback
> diagnostic, and reproducible SYSVAD patch are in this repository. The driver
> still has to be built and test-installed on a Windows x64 PC with the WDK;
> it is not production-signed.

---

## How it works

```
 Client PC                                Host PC
 ┌───────────────┐                        ┌──────────────────────────────────┐
 │ mic ─► capture │   UDP audio (5802)    │  ┌───────────┐   ┌────────────┐   │
 │  + discovery   │ ════════════════════► │  │ jitter    │   │ host mic   │   │
 │  + control TCP │   TCP control (5801)  │  │ buffer    │   │ capture    │   │
 └───────────────┘   UDP discover (5800)  │  └─────┬─────┘   └─────┬──────┘   │
                                          │        └────► MIX ◄────┘          │
                                          │               │                    │
                                          │               ▼                    │
                                          │      play into render endpoint     │
                                          │               │                    │
                                          │      ┌────────▼────────┐           │
                                          │      │  Vmic Bridge    │ SYSVAD    │
                                          │      │ render→capture  │ driver    │
                                          │      └────────┬────────┘           │
                                          │               ▼                    │
                                          │     "Vmic Bridge" microphone       │
                                          │        (Zoom / OBS / …)            │
                                          └──────────────────────────────────┘
```

- The **Client** captures its mic, resamples to 48 kHz mono, and sends 10 ms
  PCM16 frames over UDP.
- The **Host** reassembles the stream in a jitter buffer, mixes it with its own
  mic (per-source gain + mute, soft limiter to prevent clipping), and plays the
  mix into the render endpoint of the virtual device.
- The **virtual device** (SYSVAD-derived) bridges render→capture, so the mix
  re-appears as a microphone.

## Features

- Single executable with a **Host** / **Client** role picker — minimal, dark, comfortable UI.
- **Auto-discovery** of hosts on the LAN (broadcast) with manual-IP fallback.
- **UDP audio + TCP control**: low-latency streaming with reliable connect/disconnect.
- **Jitter buffer** with reordering, duplicate/late handling, and loss concealment.
- Per-source **gain & mute**, level meters, connected-client list, speaker-feedback warning.
- Windows Firewall first-run helper.
- Core audio + protocol logic is **platform-neutral and unit-tested** (runs on macOS).

## Repository layout

```
Vmic/
├─ src/
│  ├─ Vmic.Core/       # protocol, jitter buffer, mixer, transports, sessions (testable)
│  ├─ Vmic.App/        # WPF app (Host + Client), NAudio adapters, UI
│  └─ Vmic.Driver/     # pinned SYSVAD patch + virtual-cable ring buffer
├─ tests/Vmic.Core.Tests/
├─ tools/Vmic.Diagnostics/ # local network + virtual-cable verification
├─ scripts/            # Windows build, install, uninstall, publish automation
└─ docs/               # architecture, protocol and Windows runbooks
```

## Status & supported platforms

| Component | Build here (macOS) | Run |
|-----------|--------------------|-----|
| `Vmic.Core` + tests | ✅ `dotnet build` / `dotnet test` | ✅ any OS |
| `Vmic.App` (WPF) | ✅ compiles via `EnableWindowsTargeting` | 🪟 Windows only |
| `Vmic.Driver` (SYSVAD) | ❌ needs WDK | 🪟 Windows only |

## Quickstart (development, on this Mac)

```bash
dotnet build Vmic.slnx            # builds Core + WPF app (cross-targeting)
dotnet test tests/Vmic.Core.Tests # runs all core tests (protocol, DSP, sessions)
```

## Running for real (Windows)

1. On the Windows PC, follow [the build runbook](docs/WINDOWS-BUILD.md) to
   publish the app, build/install the test driver, and run the diagnostics.
2. On the **Host PC**: run the app → **Host** → pick your mic and
   the render endpoint ending in "(Vmic Bridge)" as the output → **Start
   hosting**. Allow the firewall prompt.
3. On the **Client PC**: run the app → **Client** → pick your mic → select the
   discovered host (or type its IP) → **Connect**.
4. In Zoom/OBS on the Host, choose the capture endpoint ending in
   **"(Vmic Bridge)"** as the microphone.

> On a single Windows PC, run Vmic.Diagnostics.exe all to validate the loopback
> Host/Client transport and virtual-cable bridge before an actual LAN test.

## Design notes & limits

- Audio is canonical **48 kHz mono**, mixed in 32-bit float, limited to never clip.
- Target one-way latency is ~**65 ms** on a wired LAN (see `docs/ARCHITECTURE.md`).
- **v1 trusts your LAN**: there is no encryption or pairing. Anyone on the network
  can discover and connect. Don't use it on untrusted networks.
- One host, N clients (the mixer is N-way; the UI is tuned for one remote client).

## License

MIT. SYSVAD remains subject to Microsoft's upstream license; NAudio is MIT.
