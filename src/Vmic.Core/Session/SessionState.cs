namespace Vmic.Core.Session;

/// <summary>Lifecycle of the client role.</summary>
public enum ClientState
{
    Idle,
    Connecting,
    Connected,
    Error,
}

/// <summary>Lifecycle of the host role.</summary>
public enum HostState
{
    Idle,
    Running,
    Error,
}
