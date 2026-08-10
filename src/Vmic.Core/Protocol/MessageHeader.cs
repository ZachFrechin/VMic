using System.Buffers.Binary;

namespace Vmic.Core.Protocol;

/// <summary>
/// The 16-byte header that prefixes every Vmic message (TCP and UDP alike).
/// All multi-byte fields are little-endian.
///
/// Layout:
///   off 0  magic       u16  (== <see cref="Constants.Magic"/>)
///   off 2  version     u8
///   off 3  kind        u8   (<see cref="MessageKind"/>)
///   off 4  sessionId   u32
///   off 8  sequence    u32
///   off 12 payloadLen  u32
/// </summary>
public readonly record struct MessageHeader(
    MessageKind Kind,
    uint SessionId,
    uint Sequence,
    uint PayloadLength)
{
    /// <summary>Writes the header into <paramref name="dest"/> (must be ≥ 16 bytes).</summary>
    public void WriteTo(Span<byte> dest)
    {
        if (dest.Length < Constants.HeaderSize)
            throw new ArgumentException($"Header needs {Constants.HeaderSize} bytes.", nameof(dest));

        BinaryPrimitives.WriteUInt16LittleEndian(dest[0..], Constants.Magic);
        dest[2] = Constants.Version;
        dest[3] = (byte)Kind;
        BinaryPrimitives.WriteUInt32LittleEndian(dest[4..], SessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[8..], Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[12..], PayloadLength);
    }

    /// <summary>
    /// Parses a header from <paramref name="src"/>. Returns false on a short buffer,
    /// a bad magic, or an unknown version.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> src, out MessageHeader header)
    {
        header = default;
        if (src.Length < Constants.HeaderSize)
            return false;

        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(src[0..]);
        byte version = src[2];
        if (magic != Constants.Magic || version != Constants.Version)
            return false;

        var kind = (MessageKind)src[3];
        uint sessionId = BinaryPrimitives.ReadUInt32LittleEndian(src[4..]);
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(src[8..]);
        uint payloadLen = BinaryPrimitives.ReadUInt32LittleEndian(src[12..]);

        header = new MessageHeader(kind, sessionId, sequence, payloadLen);
        return true;
    }
}
