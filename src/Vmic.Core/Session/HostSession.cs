using System.Net;
using System.Net.Sockets;
using Vmic.Core.Audio;
using Vmic.Core.Diagnostics;
using Vmic.Core.Protocol;
using Vmic.Core.Transport;

namespace Vmic.Core.Session;

/// <summary>
/// The host role: advertise over discovery, accept client control connections,
/// receive each client's UDP audio into a jitter buffer, mix them with the local
/// microphone, and hand the mix to the playback sink (the virtual device's render
/// endpoint). Runs fine with zero clients (local-mic passthrough).
/// </summary>
public sealed class HostSession : IDisposable
{
    private sealed class ClientSlot
    {
        public uint SessionId { get; }
        public string Name { get; set; } = string.Empty;
        public JitterBuffer Jitter { get; } = new();
        public JitterBufferSource Source { get; }
        public TcpControlChannel? Channel { get; set; }
        public DateTime LastControlSeen { get; set; } = DateTime.UtcNow;
        public DateTime LastAudioSeen { get; set; } = DateTime.UtcNow;

        public ClientSlot(uint sessionId)
        {
            SessionId = sessionId;
            Source = new JitterBufferSource(Jitter, $"client-{sessionId}");
        }
    }

    /// <summary>Snapshot of a connected client for the UI.</summary>
    public readonly record struct ConnectedClient(uint SessionId, string Name, int BufferDepth);

    private readonly IAudioCapture _capture;
    private readonly IAudioPlayback _playback;
    private readonly string _hostName;

    private readonly MonoMixer _mixer = new();
    private readonly BufferedFloatSource _localSource;
    private readonly object _clientsLock = new();
    private readonly Dictionary<uint, ClientSlot> _clients = new();

    private DiscoveryServer? _discovery;
    private UdpTransport? _audioReceiver;
    private TcpListener? _controlListener;
    private CancellationTokenSource? _runCts;
    private Task? _audioTask;
    private Task? _acceptTask;
    private Task? _monitorTask;

    private uint _nextSessionId = 1;
    private readonly float[] _levelScratch = new float[Constants.SamplesPerFrame];

    /// <summary>The combined mix; the app plugs this into the render endpoint.</summary>
    public MonoMixer Mixer => _mixer;

    /// <summary>The host's own microphone source (for per-source gain/mute).</summary>
    public IAudioSource LocalSource => _localSource;

    /// <summary>The remote client sources currently in the mix (for gain/mute).</summary>
    public IReadOnlyList<IAudioSource> ClientSources
    {
        get { lock (_clientsLock) return _clients.Values.Select(c => (IAudioSource)c.Source).ToList(); }
    }

    /// <summary>Level of the host's own microphone.</summary>
    public LevelMeter LocalLevel { get; } = new();

    /// <summary>Level of the (mixed) remote client audio.</summary>
    public LevelMeter RemoteLevel { get; } = new();

    public SessionStats Stats { get; } = new();

    public HostState State { get; private set; } = HostState.Idle;
    public string StatusMessage { get; private set; } = "Stopped";

    public event Action? StateChanged;
    public event Action? ClientsChanged;

    public HostSession(IAudioCapture capture, IAudioPlayback playback, string hostName)
    {
        _capture = capture;
        _playback = playback;
        _hostName = hostName;
        _localSource = new BufferedFloatSource("host-mic");
    }

    /// <summary>Starts advertising, listening, capturing, mixing and playing.</summary>
    public void Start()
    {
        if (State == HostState.Running) return;
        try
        {
            _runCts = new CancellationTokenSource();

            // Local mic → mixer.
            _mixer.AddSource(_localSource);
            _capture.FrameReady += OnLocalFrame;
            _capture.Start();

            // Mix → playback (render endpoint of the virtual device).
            _playback.Start(_mixer);

            // Discovery advertising.
            _discovery = new DiscoveryServer(_hostName, Constants.ControlPort);
            _discovery.Start();

            // UDP audio receiver.
            _audioReceiver = new UdpTransport(Constants.AudioPort, allowBroadcast: true);
            _audioReceiver.Start();
            _audioTask = Task.Run(() => AudioReceiveLoopAsync(_runCts.Token));

            // TCP control listener.
            _controlListener = new TcpListener(IPAddress.Any, Constants.ControlPort);
            _controlListener.Start();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_runCts.Token));

            // Keepalive / stale-client monitor.
            _monitorTask = Task.Run(() => MonitorLoopAsync(_runCts.Token));

            SetState(HostState.Running, "Listening for clients");
        }
        catch (Exception e)
        {
            SetState(HostState.Error, $"Failed to start: {e.Message}");
            Stop();
        }
    }

    /// <summary>Stops everything and disconnects all clients.</summary>
    public void Stop()
    {
        _runCts?.Cancel();

        _capture.FrameReady -= OnLocalFrame;
        _capture.Stop();
        _playback.Stop();

        _discovery?.Dispose();
        _discovery = null;
        _audioReceiver?.Dispose();
        _audioReceiver = null;
        try { _controlListener?.Stop(); } catch { /* best-effort */ }
        _controlListener = null;

        lock (_clientsLock)
        {
            foreach (var slot in _clients.Values)
                RemoveClientNoLock(slot, notify: false);
            _clients.Clear();
        }

        if (State != HostState.Error)
            SetState(HostState.Idle, "Stopped");
        ClientsChanged?.Invoke();
    }

    /// <summary>Force-disconnects a client (the "kick" button in the UI).</summary>
    public void DisconnectClient(uint sessionId)
    {
        lock (_clientsLock)
        {
            if (_clients.TryGetValue(sessionId, out var slot))
            {
                try { slot.Channel?.Send(MessageCodec.Disconnect(sessionId)); } catch { /* best-effort */ }
                RemoveClientNoLock(slot, notify: true);
            }
        }
    }

    /// <summary>Connected clients (for the UI list).</summary>
    public IReadOnlyList<ConnectedClient> ConnectedClients
    {
        get
        {
            lock (_clientsLock)
                return _clients.Values
                    .Select(c => new ConnectedClient(c.SessionId, c.Name, c.Jitter.Depth))
                    .ToList();
        }
    }

    private void OnLocalFrame(float[] frame)
    {
        LocalLevel.Process(frame);
        _localSource.Push(frame);
    }

    private async Task AudioReceiveLoopAsync(CancellationToken ct)
    {
        if (_audioReceiver is null) return;
        try
        {
            await foreach (var dgram in _audioReceiver.Inbound.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (!MessageCodec.TryDecode(dgram.Data, out var msg) || msg is null)
                    continue;
                if (msg.Kind != MessageKind.AudioData)
                    continue;
                if (!MessageCodec.TryParseAudio(msg.Payload.Span, out var audio))
                    continue;

                ClientSlot? slot;
                lock (_clientsLock)
                    _clients.TryGetValue(msg.SessionId, out slot);
                if (slot is null) continue;

                slot.Jitter.Enqueue(new Frame(msg.Sequence, audio.Pcm16.ToArray()));
                slot.LastAudioSeen = DateTime.UtcNow;
                Stats.RecordReceived(1, audio.Pcm16.Length);

                // Meter the remote audio (convert this frame to float).
                PcmConv.Pcm16ToFloat(audio.Pcm16.Span, _levelScratch);
                RemoteLevel.Process(_levelScratch);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        if (_controlListener is null) return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var tcp = await _controlListener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleControlClientAsync(tcp, ct), ct);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (ObjectDisposedException) { /* listener stopped */ }
        catch (SocketException) { /* listener stopped */ }
    }

    private async Task HandleControlClientAsync(TcpClient tcp, CancellationToken ct)
    {
        var channel = new TcpControlChannel(tcp);
        channel.Start();

        try
        {
            // Handshake: expect a ConnectReq promptly.
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(TimeSpan.FromSeconds(5));
            var first = await channel.Inbound.ReadAsync(handshakeCts.Token).ConfigureAwait(false);

            if (first.Kind != MessageKind.ConnectReq ||
                !MessageCodec.TryParseConnectReq(first.Payload.Span, out var req))
            {
                channel.Close();
                return;
            }

            uint sessionId;
            ClientSlot slot;
            lock (_clientsLock)
            {
                sessionId = _nextSessionId++;
                slot = new ClientSlot(sessionId) { Name = req.ClientName, Channel = channel };
                _clients[sessionId] = slot;
                _mixer.AddSource(slot.Source);
            }

            channel.Send(MessageCodec.ConnectAck(sessionId));
            ClientsChanged?.Invoke();

            // Keep the session alive while the client talks to us.
            await foreach (var msg in channel.Inbound.ReadAllAsync(ct).ConfigureAwait(false))
            {
                slot.LastControlSeen = DateTime.UtcNow;
                if (msg.Kind == MessageKind.Disconnect)
                    break;
                // Keepalive and others just refresh LastControlSeen.
            }
        }
        catch (OperationCanceledException) { /* shutdown or handshake timeout */ }
        finally
        {
            // Find and remove the slot owned by this channel.
            lock (_clientsLock)
            {
                var match = _clients.Values.FirstOrDefault(c => c.Channel == channel);
                if (match is not null)
                    RemoveClientNoLock(match, notify: true);
            }
            channel.Dispose();
        }
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(Constants.KeepaliveInterval, ct).ConfigureAwait(false);

                var now = DateTime.UtcNow;
                List<ClientSlot>? stale = null;
                lock (_clientsLock)
                {
                    foreach (var slot in _clients.Values)
                        if (now - slot.LastControlSeen > Constants.ControlTimeout)
                            (stale ??= new()).Add(slot);
                    if (stale is not null)
                        foreach (var s in stale) RemoveClientNoLock(s, notify: false);
                }
                if (stale is not null) ClientsChanged?.Invoke();
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    private void RemoveClientNoLock(ClientSlot slot, bool notify)
    {
        _clients.Remove(slot.SessionId);
        _mixer.RemoveSource(slot.Source);
        slot.Channel?.Close();
        if (notify) ClientsChanged?.Invoke();
    }

    private void SetState(HostState state, string message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke();
    }

    public void Dispose() => Stop();
}
