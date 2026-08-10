using System.Diagnostics;
using Vmic.Core;

namespace Vmic.App.Services;

/// <summary>
/// Manages the Windows Firewall inbound rules Vmic needs on the Host:
/// UDP discovery + audio and TCP control. Adding rules requires elevation, so
/// <see cref="AddRulesElevated"/> triggers a UAC prompt. All methods are
/// best-effort no-ops off Windows.
/// </summary>
public static class FirewallHelper
{
    private const string UdpRuleName = "Vmic";
    private const string TcpRuleName = "Vmic Control";

    /// <summary>True if the Vmic inbound rules already exist.</summary>
    public static bool IsRulePresent()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            var psi = new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{UdpRuleName}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000)) return false;
            return process.ExitCode == 0 && output.Contains(UdpRuleName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Adds the inbound rules (UDP discovery/audio + TCP control) with elevation.
    /// Returns false if the user declined the UAC prompt.
    /// </summary>
    public static bool AddRulesElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            string command =
                $"netsh advfirewall firewall add rule name=\"{UdpRuleName}\" dir=in action=allow " +
                $"protocol=UDP localport={Constants.DiscoveryPort},{Constants.AudioPort} enable=yes & " +
                $"netsh advfirewall firewall add rule name=\"{TcpRuleName}\" dir=in action=allow " +
                $"protocol=TCP localport={Constants.ControlPort} enable=yes";

            var psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                UseShellExecute = true,
                Verb = "runas", // triggers UAC
            };
            var process = Process.Start(psi);
            process?.WaitForExit(10000);
            return process is { ExitCode: 0 };
        }
        catch
        {
            return false; // user declined UAC or elevation failed
        }
    }
}
