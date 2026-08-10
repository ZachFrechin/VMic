namespace Vmic.Core.Audio;

/// <summary>
/// A microphone capture source. Implementations (NAudio in the app layer) deliver
/// mono float frames at <see cref="Constants.SampleRate"/> via
/// <see cref="FrameReady"/> on a capture thread. Handlers must consume or copy the
/// array synchronously.
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>Raised for each captured mono float frame.</summary>
    event Action<float[]>? FrameReady;

    /// <summary>Starts capturing.</summary>
    void Start();

    /// <summary>Stops capturing.</summary>
    void Stop();

    /// <summary>True while capturing.</summary>
    bool IsRunning { get; }

    /// <summary>Friendly name of the underlying device (for the UI).</summary>
    string DeviceName { get; }
}
