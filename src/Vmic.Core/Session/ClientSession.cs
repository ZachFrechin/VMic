using System.Net;
using System.Net.Sockets;
using Vmic.Core.Audio;
using Vmic.Core.Diagnostics;
using Vmic.Core.Protocol;
using Vmic.Core.Transport;

namespace Vmic.Core.Session;

/// <summary>
/// The client role: capture the local microphone, packetize it as 10 ms PCM16
/// frames, and stream them to the host over UDP, coordinated by a TCP control
/// channel (handshake, keepalive, graceful disconnect).
/// </summary>
public sealed class ClientSession : IDisposable
{
    private readonly IAudioCapture _capture;
    private readonly string _clientName;
    private readonly IPEndPoint _controlEndPoint;
    private readonly IPEndPoint _audioEndPoint;

    private TcpControlChannel? _control;
    private UdpTransport? _udp;
    private CancellationTokenSource? _runCts;
    private Task? _keepaliveTask;
    private Task? _controlReadTask;

    private uint _sessionId;
    private uint _sequence;

    // Accumulator so we can emit exact 480-sample frames regardless of the
    // capture callback's chunk size.
    private readonly object _accLock = new();
    private readonly float[] _acc = new float[Constants.SamplesPerFrame];
    private int _accCount;
    private readonly byte[] _pcm = new byte[Constants.BytesPerFrame];

    /// <summary>Level of the local microphone input (for the UI meter).</summary>
    public LevelMeter InputLevel { get; } = new();

    /// <summary>Send/receive counters (for the UI).</summary>
    public SessionStats Stats { get; } = new();

    public ClientState State { get; private set; } = ClientState.Idle;
    public string StatusMessage { get; private set; } = "Not connected";

    /// <summary>Raised whenever <see cref="State"/> or <see cref="StatusMessage"/> changes.</summary>
    public event Action? StateChanged;

    /// <param name="host">The host to connect to (address + control port).</param>
    /// <param name="capture">Local microphone source.</param>
    /// <param name="clientName">Human-readable name shown on the host.</param>
    public ClientSession(PeerInfo host, IAudioCapture capture, string clientName)
    {
        _capture = capture;
        _clientName = clientName;
        _controlEndPoint = host.ControlEndPoint;
        _audioEndPoint = new IPEndPoint(host.Address, Constants.AudioPort);
    }

    /// <summary>Connects to the host and starts streaming audio.</summary>
    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (State == ClientState.Connected || State == ClientState.Connecting)
            return true;

        SetState(ClientState.Connecting, $"Connecting to {_controlEndPoint.Address}…");
        try
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(_controlEndPoint.Address, _controlEndPoint.Port, ct).ConfigureAwait(false);
            _control = new TcpControlChannel(tcp);
            _control.Start();
            _control.Disconnected += OnControlLost;

            _control.Send(MessageCodec.ConnectReq(_clientName));

            // Wait for the handshake reply (bounded).
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(TimeSpan.FromSeconds(5));
            var reply = await _control.Inbound.ReadAsync(handshakeCts.Token).ConfigureAwait(false);

            if (reply.Kind == MessageKind.ConnectRej)
            {
                MessageCodec.TryParseConnectRej(reply.Payload.Span, out var rej);
                SetState(ClientState.Error, $"Host refused: {rej.Reason}");
                CleanupTransport();
                return false;
            }
            if (reply.Kind != MessageKind.ConnectAck)
            {
                SetState(ClientState.Error, "Unexpected handshake reply.");
                CleanupTransport();
                return false;
            }

            _sessionId = reply.SessionId;

            // Audio send socket (ephemeral local port).
            _udp = new UdpTransport(bindPort: 0, allowBroadcast: false);
            _udp.Start();

            _capture.FrameReady += OnCaptureFrame;
            _capture.Start();

            _runCts = new CancellationTokenSource();
            _keepaliveTask = Task.Run(() => KeepaliveLoopAsync(_runCts.Token));
            _controlReadTask = Task.Run(() => ControlReadLoopAsync(_runCts.Token));

            SetState(ClientState.Connected, $"Connected · session {_sessionId}");
            return true;
        }
        catch (OperationCanceledException)
        {
            SetState(ClientState.Error, "Connection timed out.");
            CleanupTransport();
            return false;
        }
        catch (SocketException e)
        {
            SetState(ClientState.Error, $"Could not reach host: {e.SocketErrorCode}");
            CleanupTransport();
            return false;
        }
    }

    /// <summary>Stops streaming and disconnects.</summary>
    public void Disconnect()
    {
        _capture.FrameReady -= OnCaptureFrame;
        _capture.Stop();
        _runCts?.Cancel();
        try { _control?.Send(MessageCodec.Disconnect(_sessionId)); } catch { /* best-effort */ }
        CleanupTransport();
        SetState(ClientState.Idle, "Disconnected");
    }

    private void OnCaptureFrame(float[] frame)
    {
        InputLevel.Process(frame);
        lock (_accLock)
        {
            int idx = 0;
            while (idx < frame.Length)
            {
                int take = Math.Min(Constants.SamplesPerFrame - _accCount, frame.Length - idx);
                Array.Copy(frame, idx, _acc, _accCount, take);
                _accCount += take;
                idx += take;

                if (_accCount == Constants.SamplesPerFrame)
                {
                    SendFrame(_acc);
                    _accCount = 0;
                }
            }
        }
    }

    private void SendFrame(float[] samples)
    {
        if (_udp is null || _control is null || State != ClientState.Connected) return;

        PcmConv.FloatToPcm16(samples, _pcm);
        long tsUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
        var message = MessageCodec.AudioData(_sessionId, _sequence++, tsUs, _pcm);
        _udp.Send(MessageCodec.Encode(message), _audioEndPoint);
        Stats.RecordSent();
    }

    private async Task KeepaliveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(Constants.KeepaliveInterval, ct).ConfigureAwait(false);
                if (_control is { IsConnected: true })
                    _control.Send(MessageCodec.Keepalive(_sessionId));
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    private async Task ControlReadLoopAsync(CancellationToken ct)
    {
        if (_control is null) return;
        try
        {
            await foreach (var msg in _control.Inbound.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (msg.Kind == MessageKind.Disconnect)
                {
                    SetState(ClientState.Idle, "Host ended the session.");
                    Disconnect();
                    break;
                }
                // Keepalive and other messages are ignored on the client.
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    private void OnControlLost()
    {
        if (State == ClientState.Connected || State == ClientState.Connecting)
            SetState(ClientState.Error, "Lost connection to host.");
    }

    private void CleanupTransport()
    {
        _control?.Dispose();
        _control = null;
        _udp?.Dispose();
        _udp = null;
    }

    private void SetState(ClientState state, string message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        Disconnect();
        _runCts?.Dispose();
    }
}
