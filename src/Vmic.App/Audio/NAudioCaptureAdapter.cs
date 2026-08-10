using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vmic.Core;
using Vmic.Core.Audio;

namespace Vmic.App.Audio;

/// <summary>
/// <see cref="IAudioCapture"/> backed by WASAPI shared-mode capture. The device's
/// native mix format is resampled to <see cref="Constants.SampleRate"/> (if needed)
/// and downmixed to mono, then delivered as float frames on <see cref="IAudioCapture.FrameReady"/>.
/// </summary>
public sealed class NAudioCaptureAdapter : IAudioCapture
{
    private readonly MMDevice _device;
    private WasapiCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _samples;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private volatile bool _running;

    public event Action<float[]>? FrameReady;

    public bool IsRunning => _running;
    public string DeviceName => _device.FriendlyName;

    public NAudioCaptureAdapter(MMDevice device)
    {
        _device = device;
    }

    public void Start()
    {
        if (_running) return;

        _capture = new WasapiCapture(_device);
        var deviceFormat = _capture.WaveFormat;

        _buffer = new BufferedWaveProvider(deviceFormat)
        {
            DiscardOnBufferOverflow = true,
        };

        // Resample to the canonical rate if the device runs at something else.
        IWaveProvider source = _buffer;
        if (deviceFormat.SampleRate != Constants.SampleRate)
        {
            source = new MediaFoundationResampler(
                source,
                new WaveFormat(Constants.SampleRate, 16, deviceFormat.Channels));
        }

        _samples = source.ToSampleProvider();

        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();

        _running = true;
        _cts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoop(_cts.Token));
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _cts?.Cancel();

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            try { _capture.StopRecording(); } catch { /* best-effort */ }
        }

        try { _readTask?.Wait(TimeSpan.FromMilliseconds(250)); } catch { /* best-effort */ }

        _capture?.Dispose();
        _capture = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
        => _buffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);

    private void ReadLoop(CancellationToken ct)
    {
        int channels = _samples!.WaveFormat.Channels;
        var raw = new float[Constants.SamplesPerFrame * channels];
        var mono = new float[Constants.SamplesPerFrame];

        while (!ct.IsCancellationRequested && _running)
        {
            int read = ReadAvailable(_samples, raw, raw.Length);
            if (read <= 0)
            {
                Thread.Sleep(2);
                continue;
            }

            int frames = read / channels;
            if (frames <= 0) continue;

            float[] frame;
            if (channels == 1)
            {
                frame = new float[frames];
                Array.Copy(raw, frame, frames);
            }
            else
            {
                PcmConv.StereoToMono(raw.AsSpan(0, frames * channels), mono.AsSpan(0, frames));
                frame = new float[frames];
                Array.Copy(mono, frame, frames);
            }

            FrameReady?.Invoke(frame);
        }
    }

    private static int ReadAvailable(ISampleProvider provider, float[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = provider.Read(buffer, total, count - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    public void Dispose() => Stop();
}
