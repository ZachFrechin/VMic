using System.Buffers.Binary;
using System.Text;

namespace Vmic.Core.Protocol;

/// <summary>Client name broadcast while scanning for hosts.</summary>
public readonly record struct DiscoverReqPayload(string ClientName);

/// <summary>Host advertising itself in response to a discovery broadcast.</summary>
public readonly record struct DiscoverRespPayload(string HostName, ushort ControlPort);

/// <summary>Client asking the host for a session.</summary>
public readonly record struct ConnectReqPayload(string ClientName);

/// <summary>Host refusing a session.</summary>
public readonly record struct ConnectRejPayload(string Reason);

/// <summary>
/// One UDP audio frame: a send timestamp (for diagnostics) plus 16-bit PCM mono.
/// </summary>
public readonly record struct AudioPayload(long SendTimestampUs, ReadOnlyMemory<byte> Pcm16);

/// <summary>
/// Small helpers for reading/writing the length-prefixed UTF-8 strings used by
/// the non-audio payloads. Audio is handled separately with Span-based code.
/// </summary>
internal static class PayloadString
{
    public static void Write(List<byte> dest, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
        if (bytes.Length > ushort.MaxValue)
            throw new ArgumentException("String too long for payload.");
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(len, (ushort)bytes.Length);
        dest.AddRange(len);
        dest.AddRange(bytes);
    }

    public static bool TryRead(ReadOnlySpan<byte> src, ref int offset, out string s)
    {
        s = string.Empty;
        if (offset + 2 > src.Length) return false;
        ushort len = BinaryPrimitives.ReadUInt16LittleEndian(src[offset..]);
        offset += 2;
        if (offset + len > src.Length) return false;
        s = Encoding.UTF8.GetString(src.Slice(offset, len));
        offset += len;
        return true;
    }
}
