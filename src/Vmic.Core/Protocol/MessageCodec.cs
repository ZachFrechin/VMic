using System.Buffers.Binary;

namespace Vmic.Core.Protocol;

/// <summary>
/// Encodes and decodes Vmic wire messages. Non-audio payloads use length-prefixed
/// UTF-8 strings; the audio payload is handled with Span-based code for speed.
/// </summary>
public static class MessageCodec
{
    // ---------------------------------------------------------------- serial

    /// <summary>Serializes a message to its wire bytes (header + payload).</summary>
    public static byte[] Encode(Message message)
    {
        var buffer = new byte[message.WireSize];
        message.Header.WriteTo(buffer);
        message.Payload.Span.CopyTo(buffer.AsSpan(Constants.HeaderSize));
        return buffer;
    }

    /// <summary>
    /// Parses one message from <paramref name="wire"/>. Returns false when the
    /// buffer is too short, the magic/version is wrong, or the declared payload
    /// extends past the end of the buffer.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> wire, out Message? message)
    {
        message = null;
        if (!MessageHeader.TryRead(wire, out var header))
            return false;

        int total = Constants.HeaderSize + checked((int)header.PayloadLength);
        if (wire.Length < total)
            return false;

        var payload = wire.Slice(Constants.HeaderSize, (int)header.PayloadLength).ToArray();
        message = new Message(header, payload);
        return true;
    }

    // -------------------------------------------------------------- factories

    public static Message DiscoverReq(string clientName)
    {
        var body = new List<byte>();
        PayloadString.Write(body, clientName);
        return Make(MessageKind.DiscoverReq, 0, 0, body);
    }

    public static Message DiscoverResp(string hostName, ushort controlPort)
    {
        var body = new List<byte>();
        PayloadString.Write(body, hostName);
        Span<byte> port = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(port, controlPort);
        body.AddRange(port);
        return Make(MessageKind.DiscoverResp, 0, 0, body);
    }

    public static Message ConnectReq(string clientName)
    {
        var body = new List<byte>();
        PayloadString.Write(body, clientName);
        return Make(MessageKind.ConnectReq, 0, 0, body);
    }

    public static Message ConnectAck(uint sessionId)
        => Make(MessageKind.ConnectAck, sessionId, 0, Array.Empty<byte>());

    public static Message ConnectRej(string reason)
    {
        var body = new List<byte>();
        PayloadString.Write(body, reason);
        return Make(MessageKind.ConnectRej, 0, 0, body);
    }

    public static Message Keepalive(uint sessionId)
        => Make(MessageKind.Keepalive, sessionId, 0, Array.Empty<byte>());

    public static Message Disconnect(uint sessionId)
        => Make(MessageKind.Disconnect, sessionId, 0, Array.Empty<byte>());

    /// <summary>Builds one UDP audio message from a 16-bit PCM mono frame.</summary>
    public static Message AudioData(uint sessionId, uint sequence, long sendTimestampUs, ReadOnlySpan<byte> pcm16)
    {
        var payload = new byte[sizeof(long) + pcm16.Length];
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(0, sizeof(long)), sendTimestampUs);
        pcm16.CopyTo(payload.AsSpan(sizeof(long)));
        return Make(MessageKind.AudioData, sessionId, sequence, payload);
    }

    // ---------------------------------------------------------------- parsers

    public static bool TryParseDiscoverReq(ReadOnlySpan<byte> payload, out DiscoverReqPayload value)
    {
        value = default;
        int offset = 0;
        if (!PayloadString.TryRead(payload, ref offset, out var name)) return false;
        value = new DiscoverReqPayload(name);
        return true;
    }

    public static bool TryParseDiscoverResp(ReadOnlySpan<byte> payload, out DiscoverRespPayload value)
    {
        value = default;
        int offset = 0;
        if (!PayloadString.TryRead(payload, ref offset, out var host)) return false;
        if (offset + 2 > payload.Length) return false;
        ushort port = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
        value = new DiscoverRespPayload(host, port);
        return true;
    }

    public static bool TryParseConnectReq(ReadOnlySpan<byte> payload, out ConnectReqPayload value)
    {
        value = default;
        int offset = 0;
        if (!PayloadString.TryRead(payload, ref offset, out var name)) return false;
        value = new ConnectReqPayload(name);
        return true;
    }

    public static bool TryParseConnectRej(ReadOnlySpan<byte> payload, out ConnectRejPayload value)
    {
        value = default;
        int offset = 0;
        if (!PayloadString.TryRead(payload, ref offset, out var reason)) return false;
        value = new ConnectRejPayload(reason);
        return true;
    }

    /// <summary>Parses an audio payload into timestamp + PCM16 slice.</summary>
    public static bool TryParseAudio(ReadOnlySpan<byte> payload, out AudioPayload value)
    {
        value = default;
        if (payload.Length < sizeof(long)) return false;
        long ts = BinaryPrimitives.ReadInt64LittleEndian(payload);
        var pcm = payload.Slice(sizeof(long)).ToArray();
        value = new AudioPayload(ts, pcm);
        return true;
    }

    // ------------------------------------------------------------------ helpers

    private static Message Make(MessageKind kind, uint sessionId, uint sequence, List<byte> body)
        => Make(kind, sessionId, sequence, body.ToArray());

    private static Message Make(MessageKind kind, uint sessionId, uint sequence, byte[] payload)
    {
        var header = new MessageHeader(kind, sessionId, sequence, (uint)payload.Length);
        return new Message(header, payload);
    }
}
