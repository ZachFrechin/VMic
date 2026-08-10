using Vmic.Core;
using Vmic.Core.Protocol;
using Xunit;

namespace Vmic.Core.Tests.Protocol;

public class MessageHeaderTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var header = new MessageHeader(MessageKind.AudioData, SessionId: 0xDEADBEEF, Sequence: 42, PayloadLength: 968);

        Span<byte> buffer = stackalloc byte[Constants.HeaderSize];
        header.WriteTo(buffer);

        Assert.True(MessageHeader.TryRead(buffer, out var parsed));
        Assert.Equal(header.Kind, parsed.Kind);
        Assert.Equal(header.SessionId, parsed.SessionId);
        Assert.Equal(header.Sequence, parsed.Sequence);
        Assert.Equal(header.PayloadLength, parsed.PayloadLength);
    }

    [Fact]
    public void TryRead_RejectsShortBuffer()
    {
        Assert.False(MessageHeader.TryRead(new byte[Constants.HeaderSize - 1], out _));
    }

    [Fact]
    public void TryRead_RejectsBadMagic()
    {
        var header = new MessageHeader(MessageKind.Keepalive, 0, 0, 0);
        Span<byte> buffer = stackalloc byte[Constants.HeaderSize];
        header.WriteTo(buffer);
        buffer[0] = 0x00; // corrupt the magic

        Assert.False(MessageHeader.TryRead(buffer, out _));
    }

    [Fact]
    public void TryRead_RejectsUnknownVersion()
    {
        var header = new MessageHeader(MessageKind.Keepalive, 0, 0, 0);
        Span<byte> buffer = stackalloc byte[Constants.HeaderSize];
        header.WriteTo(buffer);
        buffer[2] = 0x7F; // corrupt the version

        Assert.False(MessageHeader.TryRead(buffer, out _));
    }
}

public class MessageCodecTests
{
    private static Message RoundTrip(Message original)
    {
        var wire = MessageCodec.Encode(original);
        Assert.True(MessageCodec.TryDecode(wire, out var decoded));
        Assert.NotNull(decoded);
        return decoded!;
    }

    [Fact]
    public void DiscoverReq_RoundTrips()
    {
        var decoded = RoundTrip(MessageCodec.DiscoverReq("alice-laptop"));
        Assert.Equal(MessageKind.DiscoverReq, decoded.Kind);
        Assert.True(MessageCodec.TryParseDiscoverReq(decoded.Payload.Span, out var payload));
        Assert.Equal("alice-laptop", payload.ClientName);
    }

    [Fact]
    public void DiscoverResp_RoundTrips()
    {
        var decoded = RoundTrip(MessageCodec.DiscoverResp("host-pc", Constants.ControlPort));
        Assert.Equal(MessageKind.DiscoverResp, decoded.Kind);
        Assert.True(MessageCodec.TryParseDiscoverResp(decoded.Payload.Span, out var payload));
        Assert.Equal("host-pc", payload.HostName);
        Assert.Equal(Constants.ControlPort, payload.ControlPort);
    }

    [Fact]
    public void ConnectReq_RoundTrips()
    {
        var decoded = RoundTrip(MessageCodec.ConnectReq("bob-desktop"));
        Assert.Equal(MessageKind.ConnectReq, decoded.Kind);
        Assert.True(MessageCodec.TryParseConnectReq(decoded.Payload.Span, out var payload));
        Assert.Equal("bob-desktop", payload.ClientName);
    }

    [Fact]
    public void ConnectAck_CarriesSessionIdInHeader()
    {
        var decoded = RoundTrip(MessageCodec.ConnectAck(sessionId: 777));
        Assert.Equal(MessageKind.ConnectAck, decoded.Kind);
        Assert.Equal(777u, decoded.SessionId);
        Assert.Empty(decoded.Payload.ToArray());
    }

    [Fact]
    public void ConnectRej_RoundTrips()
    {
        var decoded = RoundTrip(MessageCodec.ConnectRej("host is full"));
        Assert.Equal(MessageKind.ConnectRej, decoded.Kind);
        Assert.True(MessageCodec.TryParseConnectRej(decoded.Payload.Span, out var payload));
        Assert.Equal("host is full", payload.Reason);
    }

    [Fact]
    public void Keepalive_HasEmptyPayload()
    {
        var decoded = RoundTrip(MessageCodec.Keepalive(sessionId: 5));
        Assert.Equal(MessageKind.Keepalive, decoded.Kind);
        Assert.Equal(5u, decoded.SessionId);
        Assert.Empty(decoded.Payload.ToArray());
    }

    [Fact]
    public void Disconnect_RoundTrips()
    {
        var decoded = RoundTrip(MessageCodec.Disconnect(sessionId: 9));
        Assert.Equal(MessageKind.Disconnect, decoded.Kind);
        Assert.Equal(9u, decoded.SessionId);
    }

    [Fact]
    public void AudioData_PreservesTimestampAndSamples()
    {
        var pcm = new byte[Constants.BytesPerFrame];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = (byte)(i & 0xFF);
        long ts = 1_234_567_890L;

        var decoded = RoundTrip(MessageCodec.AudioData(sessionId: 1, sequence: 12345, sendTimestampUs: ts, pcm));
        Assert.Equal(MessageKind.AudioData, decoded.Kind);
        Assert.Equal(12345u, decoded.Sequence);
        Assert.True(MessageCodec.TryParseAudio(decoded.Payload.Span, out var audio));
        Assert.Equal(ts, audio.SendTimestampUs);
        Assert.Equal(pcm, audio.Pcm16.ToArray());
    }

    [Fact]
    public void TryDecode_RejectsTruncatedPayload()
    {
        var wire = MessageCodec.Encode(MessageCodec.DiscoverResp("host", 5801));
        // Chop the last byte off so the declared payload overruns the buffer.
        var truncated = wire.AsSpan(0, wire.Length - 1).ToArray();
        Assert.False(MessageCodec.TryDecode(truncated, out _));
    }

    [Fact]
    public void TryDecode_RejectsGarbage()
    {
        Assert.False(MessageCodec.TryDecode(new byte[] { 1, 2, 3 }, out _));
    }

    [Fact]
    public void TryDecode_IgnoresTrailingBytes()
    {
        var wire = MessageCodec.Encode(MessageCodec.Keepalive(1));
        var padded = wire.Concat(new byte[] { 0xAA, 0xBB }).ToArray();
        Assert.True(MessageCodec.TryDecode(padded, out var decoded));
        Assert.Equal(MessageKind.Keepalive, decoded!.Kind);
    }
}
