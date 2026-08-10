using Vmic.Core.Protocol;

namespace Vmic.Core.Transport;

/// <summary>
/// Host-side discovery. Listens on the discovery UDP port and answers every
/// <see cref="MessageKind.DiscoverReq"/> with a unicast
/// <see cref="MessageKind.DiscoverResp"/> carrying the host name and the TCP
/// control port.
/// </summary>
public sealed class DiscoveryServer : IDisposable
{
    private readonly UdpTransport _udp;
    private readonly string _hostName;
    private readonly ushort _controlPort;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private bool _disposed;

    public DiscoveryServer(string hostName, int controlPort = Constants.ControlPort)
    {
        _hostName = hostName;
        _controlPort = (ushort)controlPort;
        _udp = new UdpTransport(Constants.DiscoveryPort, allowBroadcast: true, reuseAddress: true);
    }

    public void Start()
    {
        if (_cts is not null) return; // already running
        _udp.Start();
        _cts = new CancellationTokenSource();
        _runTask = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var datagram in _udp.Inbound.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (!MessageCodec.TryDecode(datagram.Data, out var message) || message is null)
                    continue;
                if (message.Kind != MessageKind.DiscoverReq)
                    continue;

                var response = MessageCodec.DiscoverResp(_hostName, _controlPort);
                _udp.Send(MessageCodec.Encode(response), datagram.Sender);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        try { _runTask?.Wait(TimeSpan.FromSeconds(1)); } catch { /* best-effort */ }
        _udp.Dispose();
        _cts?.Dispose();
    }
}
