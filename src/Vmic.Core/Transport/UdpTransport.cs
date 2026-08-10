using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Vmic.Core.Transport;

/// <summary>A received datagram plus the endpoint that sent it.</summary>
public readonly record struct UdpDatagram(byte[] Data, IPEndPoint Sender);

/// <summary>
/// Minimal UDP transport used for both discovery and audio. Inbound datagrams
/// are surfaced on a bounded <see cref="Channel{T}"/>.
///
/// The receive loop is asynchronous (<see cref="Socket.ReceiveFromAsync(Memory{byte}, EndPoint, CancellationToken)"/>)
/// rather than a blocking <c>ReceiveFrom</c> on a dedicated thread. On Unix a
/// synchronous blocking receive cannot be reliably interrupted by closing the
/// socket, which would hang <see cref="Dispose"/>; async receive with a
/// cancellation token cancels cleanly on every platform.
/// </summary>
public sealed class UdpTransport : IDisposable
{
    private readonly Socket _socket;
    private readonly Channel<UdpDatagram> _inbound;
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveTask;
    private bool _disposed;

    /// <summary>The local endpoint the socket is bound to.</summary>
    public IPEndPoint LocalEndPoint { get; }

    /// <summary>Inbound datagrams. Completes when the transport is disposed.</summary>
    public ChannelReader<UdpDatagram> Inbound => _inbound.Reader;

    /// <param name="bindPort">
    /// Local port to bind. 0 picks an ephemeral port (typical for the client's
    /// audio send socket).
    /// </param>
    /// <param name="allowBroadcast">Enable sending/receiving broadcast datagrams.</param>
    /// <param name="reuseAddress">Allow rebinding a port in TIME_WAIT (host listeners).</param>
    public UdpTransport(int bindPort, bool allowBroadcast = true, bool reuseAddress = true)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (reuseAddress)
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        if (allowBroadcast)
            _socket.EnableBroadcast = true;

        _socket.Bind(new IPEndPoint(IPAddress.Any, bindPort));
        LocalEndPoint = (IPEndPoint)_socket.LocalEndPoint!;

        // Bounded so a stalled consumer cannot cause unbounded memory growth;
        // dropping the oldest datagram is the right behaviour for live audio.
        _inbound = Channel.CreateBounded<UdpDatagram>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true,
        });
    }

    /// <summary>Starts the receive loop. Safe to call once.</summary>
    public void Start()
    {
        if (_receiveTask is not null) return;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    /// <summary>Sends a datagram to <paramref name="destination"/>. Fire-and-forget.</summary>
    public void Send(ReadOnlySpan<byte> data, IPEndPoint destination)
    {
        if (_disposed) return;
        try
        {
            _socket.SendTo(data, destination);
        }
        catch (SocketException)
        {
            // Dropping an outbound datagram is acceptable for live audio/discovery.
        }
        catch (ObjectDisposedException)
        {
            // Shutting down concurrently — ignore.
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        // 1500 covers the Ethernet MTU; our packets are ~1 KB.
        var buffer = new byte[1500];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                SocketReceiveFromResult result;
                try
                {
                    result = await _socket.ReceiveFromAsync(buffer.AsMemory(), remote, ct).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    // Transient ICMP / reset; keep receiving.
                    continue;
                }

                if (result.ReceivedBytes <= 0) continue;

                var copy = buffer.AsSpan(0, result.ReceivedBytes).ToArray();
                if (result.RemoteEndPoint is IPEndPoint ipSender)
                    _inbound.Writer.TryWrite(new UdpDatagram(copy, ipSender));
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (ObjectDisposedException) { /* socket closed during shutdown */ }
        catch (SocketException) { /* socket closed during shutdown */ }
        finally
        {
            _inbound.Writer.TryComplete();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _socket.Close(); } catch { /* best-effort */ }
        try { _socket.Dispose(); } catch { /* best-effort */ }
        // Do not block waiting on the receive task; it observes cancellation and
        // completes on its own. (Callers that need a hard drain can await Inbound.)
        _cts.Dispose();
    }
}
