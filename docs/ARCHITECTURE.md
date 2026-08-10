# Architecture

~~~
Client microphone -- UDP frames --+
                                  |
Host microphone ------------------+--> float mixer + limiter
                                  |             |
                                  +-------------+
                                                | WASAPI render
                                       Vmic Bridge Input
                                                | kernel ring
                                   Vmic Bridge Microphone
                                                |
                                    Zoom / OBS / Discord
~~~

Vmic.Core is platform-neutral: wire protocol, transports, jitter buffer,
mixer and sessions. Vmic.App is the Windows WPF interface and contains the
NAudio WASAPI adapters. Vmic.Diagnostics validates the loopback networking
path and, on Windows, sends a known tone through the installed virtual cable.

The current protocol uses UDP 5800 for discovery, TCP 5801 for control, and UDP
5802 for audio. Audio is 48 kHz mono PCM frames at 10 ms. This v1 assumes a
trusted LAN and deliberately has no encryption or device pairing.
