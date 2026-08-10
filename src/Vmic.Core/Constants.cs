namespace Vmic.Core;

/// <summary>
/// Central protocol + audio constants shared by Host and Client.
/// </summary>
public static class Constants
{
    // ---- Audio format ----------------------------------------------------
    /// <summary>Canonical sample rate for all processing and transport.</summary>
    public const int SampleRate = 48_000;

    /// <summary>One audio frame is 10 ms of audio.</summary>
    public const int FrameMs = 10;

    /// <summary>Samples per 10 ms frame at 48 kHz (mono).</summary>
    public const int SamplesPerFrame = SampleRate * FrameMs / 1000; // 480

    /// <summary>Bytes per frame when encoded as 16-bit PCM (480 samples * 2 bytes).</summary>
    public const int BytesPerFrame = SamplesPerFrame * sizeof(short); // 960

    // ---- Network ports ---------------------------------------------------
    /// <summary>UDP broadcast discovery (client → LAN, host → client).</summary>
    public const int DiscoveryPort = 5800;

    /// <summary>TCP control channel (session setup, keepalive, disconnect).</summary>
    public const int ControlPort = 5801;

    /// <summary>UDP audio stream (client → host).</summary>
    public const int AudioPort = 5802;

    // ---- Timing ----------------------------------------------------------
    /// <summary>Interval between keepalive messages.</summary>
    public static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(5);

    /// <summary>No traffic for this long ⇒ declare the peer disconnected.</summary>
    public static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(15);

    /// <summary>No UDP audio for this long ⇒ mark "audio lost".</summary>
    public static readonly TimeSpan AudioLossTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Client retries discovery broadcast at this cadence while scanning.</summary>
    public static readonly TimeSpan DiscoveryInterval = TimeSpan.FromSeconds(2);

    // ---- Protocol --------------------------------------------------------
    /// <summary>Two-byte magic marking every Vmic packet ("VM").</summary>
    public const ushort Magic = 0x564D;

    /// <summary>Current protocol version.</summary>
    public const byte Version = 0x01;

    /// <summary>Size of the common message header, in bytes.</summary>
    public const int HeaderSize = 16;

    /// <summary>Maximum UDP datagram we will emit (kept under the Ethernet MTU).</summary>
    public const int MaxUdpPacket = 1400;

    /// <summary>Jitter-buffer nominal depth in frames (8 × 10 ms = 80 ms).</summary>
    public const int JitterNominalFrames = 8;

    /// <summary>Jitter-buffer ceiling in frames (20 × 10 ms = 200 ms).</summary>
    public const int JitterMaxFrames = 20;
}
