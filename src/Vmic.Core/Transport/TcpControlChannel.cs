using System.Net.Sockets;
using System.Threading.Channels;
using Vmic.Core.Protocol;

namespace Vmic.Core.Transport;

/// <summary>
/// A framed TCP control channel carrying <see cref="Message"/>s. The 16-byte
/// header already encodes the payload length, so framing is simply
/// "read header, then read exactly that many payload bytes".
///
/// Sending is thread-safe (guarded by a lock). Receiving runs on a dedicated
/// thread and pushes decoded messages into a bounded channel.
/// </summary>
public sealed class TcpControlChannel : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Channel<Message> _inbound;
    private readonly object _sendLock = new();
    private readonly Thread _receiveThread;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    /// <summary>Decoded inbound messages. Completes when the channel closes.</summary>
    public ChannelReader<Message> Inbound => _inbound.Reader;

    /// <summary>Raised (once) when the underlying connection closes or faults.</summary>
    public event Action? Disconnected;

    public bool IsConnected => !_disposed && _client.Connected;

    /// <summary>Wraps an already-connected <see cref="TcpClient"/>.</summary>
    public TcpControlChannel(TcpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _client.NoDelay = true;
        _stream = client.GetStream();

        _inbound = Channel.CreateBounded<Message>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true,
        });

        _receiveThread = new Thread(ReceiveLoop)
        {
            IsBackground = true,
            Name = "vmic-tcp-recv",
        };
    }

    public void Start() => _receiveThread.Start();

    /// <summary>Serializes and sends a message. Returns false if the send failed.</summary>
    public bool Send(Message message)
    {
        if (_disposed) return false;
        var wire = MessageCodec.Encode(message);
        lock (_sendLock)
        {
            try
            {
                _stream.Write(wire, 0, wire.Length);
                _stream.Flush();
                return true;
            }
            catch (IOException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }
    }

    private void ReceiveLoop()
    {
        var headerBuffer = new byte[Constants.HeaderSize];

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (!ReadExact(headerBuffer, 0, Constants.HeaderSize))
                    break;
                if (!MessageHeader.TryRead(headerBuffer, out var header))
                    break; // corrupt stream — drop the connection.

                var payload = new byte[header.PayloadLength];
                if (payload.Length > 0 && !ReadExact(payload, 0, payload.Length))
                    break;

                _inbound.Writer.TryWrite(new Message(header, payload));
            }
        }
        catch (IOException) { /* connection reset */ }
        catch (ObjectDisposedException) { /* closed */ }
        finally
        {
            _inbound.Writer.TryComplete();
            Disconnected?.Invoke();
        }
    }

    private bool ReadExact(byte[] buffer, int offset, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = _stream.Read(buffer, offset + read, count - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    public void Close()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _stream.Close(); } catch { /* best-effort */ }
        try { _client.Close(); } catch { /* best-effort */ }
    }

    public void Dispose()
    {
        Close();
        _cts.Dispose();
    }
}
