using System.Diagnostics;

namespace Vmic.App.Services;

/// <summary>
/// Manages the Windows Firewall inbound rule Vmic needs on the Host. The rule
/// is bound to the current Vmic.exe and limited to the local subnet. Adding it
/// requires elevation, so <see cref="AddRulesElevated"/> triggers a UAC prompt.
/// All methods are best-effort no-ops off Windows.
/// </summary>
public static class FirewallHelper
{
    private const string RuleName = "Vmic";

    /// <summary>True if the Vmic inbound rules already exist.</summary>
    public static bool IsRulePresent()
    {
        if (!OperatingSystem.IsWindows()) return true;
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        try
        {
            var psi = new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{RuleName}\" verbose")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000)) return false;
            return process.ExitCode == 0 &&
                   output.Contains(RuleName, StringComparison.OrdinalIgnoreCase) &&
                   output.Contains(executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Adds an inbound application rule for the current executable with elevation.
    /// Returns false if the user declined the UAC prompt.
    /// </summary>
    public static bool AddRulesElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"')) return false;
        try
        {
            string arguments =
                $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow " +
                $"program=\"{executablePath}\" enable=yes profile=any remoteip=localsubnet";

            var psi = new ProcessStartInfo("netsh.exe", arguments)
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
