using System.Diagnostics;
using System.Net;
using Vmic.Core;
using Vmic.Core.Audio;
using Vmic.Core.Session;
using Xunit;

namespace Vmic.Core.Tests.Session;

/// <summary>Test double: emits frames on demand instead of a real microphone.</summary>
internal sealed class FakeCapture : IAudioCapture
{
    public event Action<float[]>? FrameReady;
    public bool IsRunning { get; private set; }
    public string DeviceName => "fake-mic";
    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;
    public void Emit(float[] frame) => FrameReady?.Invoke(frame);
    public void Dispose() => Stop();
}

/// <summary>Test double: lets the test pull what would go to the speakers.</summary>
internal sealed class FakePlayback : IAudioPlayback
{
    private IAudioSource? _source;
    public bool IsRunning { get; private set; }
    public string DeviceName => "fake-out";

    public void Start(IAudioSource source) { _source = source; IsRunning = true; }
    public void Stop() { IsRunning = false; }

    public float[] Pull(int count)
    {
        var buffer = new float[count];
        if (_source is not null && IsRunning)
            _source.Read(buffer);
        return buffer;
    }
    public void Dispose() => Stop();
}

public class SessionIntegrationTests
{
    private static float[] Constant(float value)
        => Enumerable.Repeat(value, Constants.SamplesPerFrame).ToArray();

    [Fact]
    public async Task HostAndClient_LocalAndRemoteAudio_BothReachTheMix()
    {
        var hostCapture = new FakeCapture();
        var hostPlayback = new FakePlayback();
        using var host = new HostSession(hostCapture, hostPlayback, "test-host");
        host.Start();
        Assert.Equal(HostState.Running, host.State);

        var clientCapture = new FakeCapture();
        var hostPeer = new PeerInfo("test-host", IPAddress.Loopback, Constants.ControlPort);
        using var client = new ClientSession(hostPeer, clientCapture, "test-client");

        var connected = await client.ConnectAsync();
        Assert.True(connected, client.StatusMessage);
        Assert.Equal(ClientState.Connected, client.State);

        // Give the host a moment to register the client.
        await Task.Delay(100);
        Assert.Single(host.ConnectedClients);
        Assert.Equal("test-client", host.ConnectedClients[0].Name);

        // Pump local (0.2) and remote (0.3) frames while pulling the mix. When
        // both reach the mixer the combined level exceeds either alone.
        var localFrame = Constant(0.2f);
        var remoteFrame = Constant(0.3f);
        var stopwatch = Stopwatch.StartNew();
        float maxSample = 0f;

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(8) && maxSample < 0.35f)
        {
            hostCapture.Emit(localFrame);
            clientCapture.Emit(remoteFrame);
            await Task.Delay(8);

            var mix = hostPlayback.Pull(Constants.SamplesPerFrame);
            foreach (var s in mix)
                if (Math.Abs(s) > maxSample) maxSample = Math.Abs(s);
        }

        Assert.True(maxSample > 0.35f,
            $"expected mixed level > 0.35 (local 0.2 + remote 0.3), got {maxSample}");

        client.Disconnect();
        await Task.Delay(150);
        Assert.Empty(host.ConnectedClients);
    }

    [Fact]
    public async Task HostWithNoClients_LocalMicPassesThrough()
    {
        var hostCapture = new FakeCapture();
        var hostPlayback = new FakePlayback();
        using var host = new HostSession(hostCapture, hostPlayback, "solo-host");
        host.Start();

        var localFrame = Constant(0.4f);
        float maxSample = 0f;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(3) && maxSample < 0.3f)
        {
            hostCapture.Emit(localFrame);
            await Task.Delay(8);
            var mix = hostPlayback.Pull(Constants.SamplesPerFrame);
            foreach (var s in mix)
                if (Math.Abs(s) > maxSample) maxSample = Math.Abs(s);
        }

        Assert.True(maxSample > 0.3f, $"expected local passthrough, got {maxSample}");
    }
}
