namespace Vmic.Core.Protocol;

/// <summary>
/// A decoded message: header + payload. Constructed by
/// <see cref="MessageCodec.TryDecode"/> or via the typed factory helpers on
/// <see cref="MessageCodec"/>.
/// </summary>
public sealed class Message
{
    public MessageHeader Header { get; }
    public ReadOnlyMemory<byte> Payload { get; }

    public MessageKind Kind => Header.Kind;
    public uint SessionId => Header.SessionId;
    public uint Sequence => Header.Sequence;

    public Message(MessageHeader header, ReadOnlyMemory<byte> payload)
    {
        Header = header;
        Payload = payload;
    }

    /// <summary>Total serialized size (header + payload).</summary>
    public int WireSize => Constants.HeaderSize + Payload.Length;
}
