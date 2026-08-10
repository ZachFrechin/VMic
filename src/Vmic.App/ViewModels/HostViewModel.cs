using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Vmic.App.Audio;
using Vmic.App.Services;
using Vmic.Core.Audio;
using Vmic.Core.Session;

namespace Vmic.App.ViewModels;

/// <summary>Host mode: capture the local mic, mix in remote clients, feed the mix to
/// a render endpoint (the virtual device), and expose controls for all of it.</summary>
public sealed class HostViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private HostSession? _session;
    private IAudioCapture? _capture;
    private IAudioPlayback? _playback;
    private readonly DispatcherTimer _meterTimer;

    public ObservableCollection<DeviceInfo> CaptureDevices { get; } = new();
    public ObservableCollection<DeviceInfo> RenderDevices { get; } = new();
    public ObservableCollection<HostSession.ConnectedClient> ConnectedClients { get; } = new();

    private DeviceInfo? _selectedCaptureDevice;
    public DeviceInfo? SelectedCaptureDevice
    {
        get => _selectedCaptureDevice;
        set { if (Set(ref _selectedCaptureDevice, value)) _settings.CaptureDeviceId = value?.Id; }
    }

    private DeviceInfo? _selectedRenderDevice;
    public DeviceInfo? SelectedRenderDevice
    {
        get => _selectedRenderDevice;
        set
        {
            if (Set(ref _selectedRenderDevice, value))
            {
                _settings.RenderDeviceId = value?.Id;
                OnPropertyChanged(nameof(ShowSpeakerWarning));
            }
        }
    }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    private string _statusText = "Stopped";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private float _localLevel;
    public float LocalLevel { get => _localLevel; private set => Set(ref _localLevel, value); }

    private float _remoteLevel;
    public float RemoteLevel { get => _remoteLevel; private set => Set(ref _remoteLevel, value); }

    private float _hostMicGain = 1f;
    public float HostMicGain
    {
        get => _hostMicGain;
        set { if (Set(ref _hostMicGain, value)) ApplyGains(); }
    }

    private bool _hostMicMuted;
    public bool HostMicMuted
    {
        get => _hostMicMuted;
        set { if (Set(ref _hostMicMuted, value)) ApplyGains(); }
    }

    private float _remoteGain = 1f;
    public float RemoteGain
    {
        get => _remoteGain;
        set { if (Set(ref _remoteGain, value)) ApplyGains(); }
    }

    private bool _remoteMuted;
    public bool RemoteMuted
    {
        get => _remoteMuted;
        set { if (Set(ref _remoteMuted, value)) ApplyGains(); }
    }

    private bool _showFirewallHint;
    public bool ShowFirewallHint { get => _showFirewallHint; private set => Set(ref _showFirewallHint, value); }

    /// <summary>Warn when the selected output looks like real speakers (feedback risk).</summary>
    public bool ShowSpeakerWarning =>
        SelectedRenderDevice is not null &&
        !SelectedRenderDevice.Name.Contains("vmic", StringComparison.OrdinalIgnoreCase) &&
        !SelectedRenderDevice.Name.Contains("cable", StringComparison.OrdinalIgnoreCase) &&
        !SelectedRenderDevice.Name.Contains("virtual", StringComparison.OrdinalIgnoreCase);

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand DisconnectClientCommand { get; }
    public RelayCommand AddFirewallRuleCommand { get; }

    public HostViewModel(AppSettings settings)
    {
        _settings = settings;
        StartCommand = new RelayCommand(Start, () => !IsRunning);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        DisconnectClientCommand = new RelayCommand(p => { if (p is uint id) _session?.DisconnectClient(id); });
        AddFirewallRuleCommand = new RelayCommand(() =>
        {
            if (FirewallHelper.AddRulesElevated()) ShowFirewallHint = false;
        });

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _meterTimer.Tick += (_, _) => UpdateMeters();

        RefreshDevices();
        ShowFirewallHint = !FirewallHelper.IsRulePresent();
    }

    public void RefreshDevices()
    {
        CaptureDevices.Clear();
        foreach (var d in DeviceEnumerator.GetCaptureDevices()) CaptureDevices.Add(d);
        RenderDevices.Clear();
        foreach (var d in DeviceEnumerator.GetRenderDevices()) RenderDevices.Add(d);

        SelectedCaptureDevice =
            CaptureDevices.FirstOrDefault(d => d.Id == _settings.CaptureDeviceId) ??
            CaptureDevices.FirstOrDefault();
        SelectedRenderDevice =
            RenderDevices.FirstOrDefault(d => d.Id == _settings.RenderDeviceId) ??
            RenderDevices.FirstOrDefault(d => d.Name.Contains("Vmic", StringComparison.OrdinalIgnoreCase)) ??
            RenderDevices.FirstOrDefault();
    }

    private void Start()
    {
        if (SelectedCaptureDevice is null || SelectedRenderDevice is null)
        {
            StatusText = "Select a microphone and an output device first.";
            return;
        }

        var captureDevice = DeviceEnumerator.GetDevice(SelectedCaptureDevice.Id);
        var renderDevice = DeviceEnumerator.GetDevice(SelectedRenderDevice.Id);
        if (captureDevice is null || renderDevice is null)
        {
            StatusText = "Selected device is no longer available.";
            RefreshDevices();
            return;
        }

        try
        {
            _capture = new NAudioCaptureAdapter(captureDevice);
            _playback = new NAudioPlaybackAdapter(renderDevice);
            _session = new HostSession(_capture, _playback, Environment.MachineName);

            _session.StateChanged += OnSessionStateChanged;
            _session.ClientsChanged += OnClientsChanged;

            _session.Start();
            ApplyGains();
            _meterTimer.Start();
            _settings.Save();
        }
        catch (Exception e)
        {
            StatusText = $"Could not start: {e.Message}";
            CleanupSession();
        }
    }

    private void Stop()
    {
        _meterTimer.Stop();
        CleanupSession();
        LocalLevel = 0;
        RemoteLevel = 0;
    }

    private void CleanupSession()
    {
        _session?.Dispose();
        _session = null;
        _capture?.Dispose();
        _capture = null;
        _playback?.Dispose();
        _playback = null;
        ConnectedClients.Clear();
        IsRunning = false;
        StatusText = "Stopped";
    }

    private void OnSessionStateChanged() => OnUi(() =>
    {
        if (_session is null) return;
        IsRunning = _session.State == HostState.Running;
        StatusText = _session.StatusMessage;
    });

    private void OnClientsChanged() => OnUi(() =>
    {
        ConnectedClients.Clear();
        if (_session is null) return;
        foreach (var c in _session.ConnectedClients) ConnectedClients.Add(c);
        StatusText = ConnectedClients.Count == 0
            ? "Listening for clients"
            : $"{ConnectedClients.Count} client(s) connected";
        ApplyGains(); // ensure newly added sources inherit the remote gain
    });

    private void ApplyGains()
    {
        if (_session is null) return;
        _session.Mixer.SetGain(_session.LocalSource, HostMicMuted ? 0f : HostMicGain);
        float remote = RemoteMuted ? 0f : RemoteGain;
        foreach (var src in _session.ClientSources)
            _session.Mixer.SetGain(src, remote);
    }

    private void UpdateMeters()
    {
        if (_session is null) return;
        LocalLevel = _session.LocalLevel.Snapshot().Peak;
        RemoteLevel = _session.RemoteLevel.Snapshot().Peak;
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        CleanupSession();
    }
}
