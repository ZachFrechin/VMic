using NAudio.CoreAudioApi;

namespace Vmic.App.Audio;

/// <summary>A device entry for the UI dropdowns.</summary>
public sealed record DeviceInfo(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Enumerates WASAPI audio endpoints (microphones and render outputs) and
/// resolves a stored device id back to an <see cref="MMDevice"/>.
/// </summary>
public static class DeviceEnumerator
{
    /// <summary>Active capture (microphone) endpoints.</summary>
    public static IReadOnlyList<DeviceInfo> GetCaptureDevices()
        => Enumerate(DataFlow.Capture);

    /// <summary>Active render (speaker / virtual-cable) endpoints.</summary>
    public static IReadOnlyList<DeviceInfo> GetRenderDevices()
        => Enumerate(DataFlow.Render);

    private static IReadOnlyList<DeviceInfo> Enumerate(DataFlow flow)
    {
        var result = new List<DeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                result.Add(new DeviceInfo(device.ID, device.FriendlyName));
        }
        catch
        {
            // No audio subsystem (shouldn't happen on Windows) — return empty.
        }
        return result;
    }

    /// <summary>Resolves a device id to an <see cref="MMDevice"/>, or null if gone.</summary>
    public static MMDevice? GetDevice(string id)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDevice(id);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The default capture endpoint, or null.</summary>
    public static MMDevice? GetDefaultCapture() => GetDefault(DataFlow.Capture);

    /// <summary>The default render endpoint, or null.</summary>
    public static MMDevice? GetDefaultRender() => GetDefault(DataFlow.Render);

    private static MMDevice? GetDefault(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDefaultAudioEndpoint(flow, Role.Communications);
        }
        catch
        {
            return null;
        }
    }
}
