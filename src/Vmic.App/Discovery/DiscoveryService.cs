using System.Collections.ObjectModel;
using System.Windows.Threading;
using Vmic.Core.Session;
using Vmic.Core.Transport;

namespace Vmic.App.Discovery;

/// <summary>
/// Wraps <see cref="DiscoveryClient"/> and maintains a de-duplicated, UI-thread-safe
/// list of discovered hosts for the client view.
/// </summary>
public sealed class DiscoveryService : IDisposable
{
    private readonly DiscoveryClient _client;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, PeerInfo> _seen = new();

    /// <summary>Hosts currently visible on the LAN (UI thread collection).</summary>
    public ObservableCollection<PeerInfo> Hosts { get; } = new();

    public DiscoveryService(string clientName, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _client = new DiscoveryClient(clientName);
        _client.HostDiscovered += OnHostDiscovered;
    }

    public void Start() => _client.Start();

    public void Stop() => _client.Stop();

    private void OnHostDiscovered(PeerInfo peer)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _seen[peer.Key] = peer;

            var existing = FindIndex(peer.Key);
            if (existing >= 0)
                Hosts[existing] = peer; // refresh
            else
                Hosts.Add(peer);
        });
    }

    private int FindIndex(string key)
    {
        for (int i = 0; i < Hosts.Count; i++)
            if (Hosts[i].Key == key) return i;
        return -1;
    }

    public void Dispose()
    {
        _client.HostDiscovered -= OnHostDiscovered;
        _client.Dispose();
    }
}
