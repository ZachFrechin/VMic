namespace Vmic.Core.Protocol;

/// <summary>
/// On-the-wire message type. The byte value is part of the protocol and must
/// remain stable; new kinds are appended, never renumbered.
/// </summary>
public enum MessageKind : byte
{
    /// <summary>Client → LAN broadcast: "any hosts out there?".</summary>
    DiscoverReq = 0x01,

    /// <summary>Host → Client (unicast): advertises hostname + control port.</summary>
    DiscoverResp = 0x02,

    /// <summary>Client → Host over TCP: request a session.</summary>
    ConnectReq = 0x10,

    /// <summary>Host → Client over TCP: session accepted, session id assigned.</summary>
    ConnectAck = 0x11,

    /// <summary>Host → Client over TCP: session refused.</summary>
    ConnectRej = 0x12,

    /// <summary>Either direction over TCP: liveness ping (empty payload).</summary>
    Keepalive = 0x20,

    /// <summary>Client → Host over UDP: one 10 ms PCM16 audio frame.</summary>
    AudioData = 0x30,

    /// <summary>Either direction: graceful close.</summary>
    Disconnect = 0xFF,
}
