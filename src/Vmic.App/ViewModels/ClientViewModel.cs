using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using Vmic.App.Audio;
using Vmic.App.Discovery;
using Vmic.App.Services;
using Vmic.Core;
using Vmic.Core.Audio;
using Vmic.Core.Session;

namespace Vmic.App.ViewModels;

/// <summary>Client mode: capture the local mic and stream it to a host.</summary>
public sealed class ClientViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private ClientSession? _session;
    private IAudioCapture? _capture;
    private DiscoveryService? _discovery;
    private readonly DispatcherTimer _meterTimer;

    public ObservableCollection<DeviceInfo> CaptureDevices { get; } = new();
    public ObservableCollection<PeerInfo> DiscoveredHosts { get; } = new();

    private DeviceInfo? _selectedCaptureDevice;
    public DeviceInfo? SelectedCaptureDevice
    {
        get => _selectedCaptureDevice;
        set { if (Set(ref _selectedCaptureDevice, value)) _settings.CaptureDeviceId = value?.Id; }
    }

    private PeerInfo? _selectedHost;
    public PeerInfo? SelectedHost { get => _selectedHost; set => Set(ref _selectedHost, value); }

    private string _manualHostIp = string.Empty;
    public string ManualHostIp
    {
        get => _manualHostIp;
        set { if (Set(ref _manualHostIp, value)) _settings.LastHostIp = value; }
    }

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value); }

    private bool _isConnecting;
    public bool IsConnecting { get => _isConnecting; private set => Set(ref _isConnecting, value); }

    private string _statusText = "Not connected";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private float _inputLevel;
    public float InputLevel { get => _inputLevel; private set => Set(ref _inputLevel, value); }

    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }

    public ClientViewModel(AppSettings settings)
    {
        _settings = settings;
        _manualHostIp = settings.LastHostIp ?? string.Empty;
        ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !IsConnected && !IsConnecting);
        DisconnectCommand = new RelayCommand(Disconnect, () => IsConnected);

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _meterTimer.Tick += (_, _) =>
        {
            if (_session is not null) InputLevel = _session.InputLevel.Snapshot().Peak;
        };

        _mirrorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _mirrorTimer.Tick += (_, _) => SyncDiscoveredHosts();

        RefreshDevices();
    }

    public void RefreshDevices()
    {
        CaptureDevices.Clear();
        foreach (var d in DeviceEnumerator.GetCaptureDevices()) CaptureDevices.Add(d);
        SelectedCaptureDevice =
            CaptureDevices.FirstOrDefault(d => d.Id == _settings.CaptureDeviceId) ??
            CaptureDevices.FirstOrDefault();
    }

    /// <summary>Called when the client view becomes active — start discovering hosts.</summary>
    public void Activate()
    {
        if (_discovery is null && !IsConnected)
        {
            _discovery = new DiscoveryService(Environment.MachineName, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
            // Mirror discovered hosts into our observable list.
            _discovery.Start();
            _mirrorTimer.Start();
        }
    }

    /// <summary>Called when leaving the client view.</summary>
    public void Deactivate()
    {
        _mirrorTimer.Stop();
        _discovery?.Stop();
    }

    private readonly DispatcherTimer _mirrorTimer;
    private void SyncDiscoveredHosts()
    {
        if (_discovery is null) return;
        DiscoveredHosts.Clear();
        foreach (var h in _discovery.Hosts) DiscoveredHosts.Add(h);
    }

    private PeerInfo? ResolveTargetHost()
    {
        if (!string.IsNullOrWhiteSpace(ManualHostIp))
        {
            if (IPAddress.TryParse(ManualHostIp.Trim(), out var ip))
                return new PeerInfo("manual host", ip, Constants.ControlPort);
            StatusText = $"“{ManualHostIp.Trim()}” is not a valid IP address.";
            return null;
        }
        if (SelectedHost is not null) return SelectedHost;
        StatusText = "Pick a discovered host or type an IP address.";
        return null;
    }

    private async Task ConnectAsync()
    {
        var host = ResolveTargetHost();
        if (host is null) return;

        if (SelectedCaptureDevice is null)
        {
            StatusText = "Select a microphone first.";
            return;
        }
        var captureDevice = DeviceEnumerator.GetDevice(SelectedCaptureDevice.Id);
        if (captureDevice is null)
        {
            StatusText = "Selected microphone is no longer available.";
            RefreshDevices();
            return;
        }

        IsConnecting = true;
        StatusText = $"Connecting to {host.Address}…";
        _discovery?.Stop();

        try
        {
            _capture = new NAudioCaptureAdapter(captureDevice);
            _session = new ClientSession(host, _capture, Environment.MachineName);
            _session.StateChanged += OnSessionStateChanged;

            var ok = await _session.ConnectAsync();
            if (ok)
            {
                _meterTimer.Start();
                _settings.Save();
            }
            else
            {
                CleanupSession();
            }
        }
        catch (Exception e)
        {
            StatusText = $"Could not connect: {e.Message}";
            CleanupSession();
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void Disconnect()
    {
        _meterTimer.Stop();
        _session?.Disconnect();
        CleanupSession();
        InputLevel = 0;
        _discovery?.Start(); // resume discovery so the user can pick another host
    }

    private void CleanupSession()
    {
        _session?.Dispose();
        _session = null;
        _capture?.Dispose();
        _capture = null;
        IsConnected = false;
    }

    private void OnSessionStateChanged() => OnUi(() =>
    {
        if (_session is null) return;
        IsConnected = _session.State == ClientState.Connected;
        StatusText = _session.StatusMessage;
        if (_session.State == ClientState.Error || _session.State == ClientState.Idle)
            _meterTimer.Stop();
    });

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        _mirrorTimer.Stop();
        _discovery?.Dispose();
        CleanupSession();
    }
}
