namespace Vmic.Core.Audio;

/// <summary>
/// An audio output sink. Implementations (NAudio in the app layer) pull mono float
/// samples from the provided <see cref="IAudioSource"/> to fill the device buffer.
/// The host session hands it the <see cref="MonoMixer"/>.
/// </summary>
public interface IAudioPlayback : IDisposable
{
    /// <summary>Begins playback, pulling from <paramref name="source"/>.</summary>
    void Start(IAudioSource source);

    /// <summary>Stops playback.</summary>
    void Stop();

    /// <summary>True while playing.</summary>
    bool IsRunning { get; }

    /// <summary>Friendly name of the underlying device (for the UI).</summary>
    string DeviceName { get; }
}
