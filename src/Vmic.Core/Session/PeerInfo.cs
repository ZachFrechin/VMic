using System.Net;

namespace Vmic.Core.Session;

/// <summary>
/// A discovered (or manually entered) host that a client can connect to.
/// </summary>
public sealed record PeerInfo(string Name, IPAddress Address, int ControlPort)
{
    /// <summary>TCP endpoint used to open the control channel.</summary>
    public IPEndPoint ControlEndPoint => new(Address, ControlPort);

    /// <summary>Stable key for de-duplicating repeated discovery responses.</summary>
    public string Key => $"{Address}:{ControlPort}";

    public override string ToString() => string.IsNullOrWhiteSpace(Name)
        ? Address.ToString()
        : $"{Name} ({Address})";
}
