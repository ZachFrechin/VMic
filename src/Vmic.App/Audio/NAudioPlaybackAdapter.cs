using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vmic.Core;
using Vmic.Core.Audio;

namespace Vmic.App.Audio;

/// <summary>
/// <see cref="IAudioPlayback"/> backed by WASAPI shared-mode render. Pulls the mono
/// mix from the supplied <see cref="IAudioSource"/>, adapts it to the device's
/// shared-mode mix format (sample rate, channel count and bit depth), and plays it.
/// </summary>
public sealed class NAudioPlaybackAdapter : IAudioPlayback
{
    private readonly MMDevice _device;
    private WasapiOut? _output;
    private volatile bool _running;

    public bool IsRunning => _running;
    public string DeviceName => _device.FriendlyName;

    public NAudioPlaybackAdapter(MMDevice device)
    {
        _device = device;
    }

    public void Start(IAudioSource source)
    {
        if (_running) return;

        // The device's shared-mode mix format (what the render endpoint expects).
        WaveFormat mixFormat;
        using (var audioClient = _device.AudioClient)
            mixFormat = audioClient.MixFormat;

        // mono float 48k -> mono 16-bit 48k -> (resample) -> mix format.
        IWaveProvider chain = new SourceMonoProvider(source);
        if (mixFormat.SampleRate != Constants.SampleRate)
        {
            chain = new MediaFoundationResampler(
                chain,
                new WaveFormat(mixFormat.SampleRate, 16, 1));
        }
        var provider = new MonoToMixFormatProvider(chain, mixFormat);

        _output = new WasapiOut(_device, AudioClientShareMode.Shared, false, 100);
        _output.Init(provider);
        _output.Play();
        _running = true;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _output?.Stop(); } catch { /* best-effort */ }
        _output?.Dispose();
        _output = null;
    }

    public void Dispose() => Stop();
}
