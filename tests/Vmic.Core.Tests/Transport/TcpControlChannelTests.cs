using System.Net;
using System.Net.Sockets;
using Vmic.Core;
using Vmic.Core.Protocol;
using Vmic.Core.Transport;
using Xunit;

namespace Vmic.Core.Tests.Transport;

public class TcpControlChannelTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Handshake_RequestAndAck_OverLoopback()
    {
        using var cts = new CancellationTokenSource(Wait);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.Server.LocalEndPoint!).Port;

        // Server side: accept one connection, verify the request, send an ack.
        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
            using var serverChannel = new TcpControlChannel(serverClient);
            serverChannel.Start();

            var request = await serverChannel.Inbound.ReadAsync(cts.Token);
            Assert.Equal(MessageKind.ConnectReq, request.Kind);
            Assert.True(MessageCodec.TryParseConnectReq(request.Payload.Span, out var req));
            Assert.Equal("unit-client", req.ClientName);

            serverChannel.Send(MessageCodec.ConnectAck(sessionId: 42));
            await Task.Delay(100, CancellationToken.None);
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var clientChannel = new TcpControlChannel(client);
        clientChannel.Start();

        Assert.True(clientChannel.Send(MessageCodec.ConnectReq("unit-client")));

        var ack = await clientChannel.Inbound.ReadAsync(cts.Token);
        Assert.Equal(MessageKind.ConnectAck, ack.Kind);
        Assert.Equal(42u, ack.SessionId);

        await serverTask;
        listener.Stop();
    }

    [Fact]
    public async Task ManyMessages_AreAllDelivered()
    {
        using var cts = new CancellationTokenSource(Wait);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.Server.LocalEndPoint!).Port;

        const int count = 50;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync(cts.Token);
            using var serverChannel = new TcpControlChannel(serverClient);
            serverChannel.Start();

            for (uint i = 0; i < count; i++)
            {
                var msg = await serverChannel.Inbound.ReadAsync(cts.Token);
                Assert.Equal(MessageKind.Keepalive, msg.Kind);
                Assert.Equal(i, msg.Sequence);
            }
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var clientChannel = new TcpControlChannel(client);
        clientChannel.Start();

        for (uint i = 0; i < count; i++)
            Assert.True(clientChannel.Send(new Message(
                new MessageHeader(MessageKind.Keepalive, 1, i, 0), Array.Empty<byte>())));

        await serverTask;
        listener.Stop();
    }
}
