using System.Net;
using Vmic.Core.Transport;
using Xunit;

namespace Vmic.Core.Tests.Transport;

public class UdpTransportTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task Send_IsReceivedByPeer_OnLoopback()
    {
        using var cts = new CancellationTokenSource(Wait);
        using var receiver = new UdpTransport(bindPort: 0, allowBroadcast: false);
        using var sender = new UdpTransport(bindPort: 0, allowBroadcast: false);
        receiver.Start();
        sender.Start();

        var payload = new byte[] { 1, 2, 3, 4, 5 };
        sender.Send(payload, new IPEndPoint(IPAddress.Loopback, receiver.LocalEndPoint.Port));

        var datagram = await receiver.Inbound.ReadAsync(cts.Token);
        Assert.Equal(payload, datagram.Data);
        Assert.Equal(sender.LocalEndPoint.Port, datagram.Sender.Port);
    }

    [Fact]
    public async Task MultipleDatagrams_AreAllDelivered()
    {
        using var cts = new CancellationTokenSource(Wait);
        using var receiver = new UdpTransport(bindPort: 0, allowBroadcast: false);
        using var sender = new UdpTransport(bindPort: 0, allowBroadcast: false);
        receiver.Start();
        sender.Start();

        const int count = 20;
        var dest = new IPEndPoint(IPAddress.Loopback, receiver.LocalEndPoint.Port);
        for (int i = 0; i < count; i++)
            sender.Send(new byte[] { (byte)i }, dest);

        var seen = new HashSet<byte>();
        for (int i = 0; i < count; i++)
        {
            var d = await receiver.Inbound.ReadAsync(cts.Token);
            seen.Add(d.Data[0]);
        }

        Assert.Equal(count, seen.Count);
    }
}
