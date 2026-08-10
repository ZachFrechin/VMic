using System.Net;
using Vmic.Core.Protocol;
using Vmic.Core.Session;

namespace Vmic.Core.Transport;

/// <summary>
/// Client-side discovery. Periodically broadcasts a
/// <see cref="MessageKind.DiscoverReq"/> to the LAN and raises
/// <see cref="HostDiscovered"/> for every <see cref="MessageKind.DiscoverResp"/>.
/// Repeated responses from the same host are surfaced each time; callers
/// de-duplicate using <see cref="PeerInfo.Key"/>.
/// </summary>
public sealed class DiscoveryClient : IDisposable
{
    private readonly UdpTransport _udp;
    private readonly string _clientName;
    private readonly IPEndPoint _broadcast = new(IPAddress.Broadcast, Constants.DiscoveryPort);
    private CancellationTokenSource? _cts;
    private Task? _sendTask;
    private Task? _recvTask;
    private bool _disposed;

    /// <summary>Raised on the thread-pool whenever a host advertises itself.</summary>
    public event Action<PeerInfo>? HostDiscovered;

    public DiscoveryClient(string clientName)
    {
        _clientName = clientName;
        // Ephemeral local port; we only need to send broadcasts and hear replies.
        _udp = new UdpTransport(bindPort: 0, allowBroadcast: true, reuseAddress: false);
    }

    public void Start()
    {
        if (_cts is not null) return; // already running
        _udp.Start();
        _cts = new CancellationTokenSource();
        _sendTask = Task.Run(() => SendLoopAsync(_cts.Token));
        _recvTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        var request = MessageCodec.Encode(MessageCodec.DiscoverReq(_clientName));
        try
        {
            while (!ct.IsCancellationRequested)
            {
                _udp.Send(request, _broadcast);
                await Task.Delay(Constants.DiscoveryInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var datagram in _udp.Inbound.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (!MessageCodec.TryDecode(datagram.Data, out var message) || message is null)
                    continue;
                if (message.Kind != MessageKind.DiscoverResp)
                    continue;
                if (!MessageCodec.TryParseDiscoverResp(message.Payload.Span, out var payload))
                    continue;

                var peer = new PeerInfo(payload.HostName, datagram.Sender.Address, payload.ControlPort);
                HostDiscovered?.Invoke(peer);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        var tasks = new[] { _sendTask, _recvTask }.OfType<Task>().ToArray();
        if (tasks.Length > 0)
        {
            try { Task.WaitAll(tasks, TimeSpan.FromSeconds(1)); } catch { /* best-effort */ }
        }
        _udp.Dispose();
        _cts?.Dispose();
    }
}
