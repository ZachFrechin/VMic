using System.Diagnostics;
using System.Net;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Vmic.Core;
using Vmic.Core.Audio;
using Vmic.Core.Session;

namespace Vmic.Diagnostics;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "all";
        if (command is not ("all" or "network" or "bridge"))
        {
            Console.Error.WriteLine("Usage: Vmic.Diagnostics [all|network|bridge]");
            return 64;
        }

        var ok = true;
        if (command is "all" or "network") ok &= await TestNetworkAsync();
        if (command is "all" or "bridge") ok &= await TestBridgeAsync();
        return ok ? 0 : 1;
    }

    private static async Task<bool> TestNetworkAsync()
    {
        Console.WriteLine("[network] Starting Host + Client over 127.0.0.1...");
        using var hostCapture = new SyntheticCapture();
        using var hostPlayback = new MeteredPlayback();
        using var host = new HostSession(hostCapture, hostPlayback, "diagnostic-host");
        host.Start();
        if (host.State != HostState.Running)
        {
            Console.Error.WriteLine($"[network] Host failed: {host.StatusMessage}");
            return false;
        }

        using var clientCapture = new SyntheticCapture();
        using var client = new ClientSession(
            new PeerInfo("diagnostic-host", IPAddress.Loopback, Constants.ControlPort),
            clientCapture,
            "diagnostic-client");
        if (!await client.ConnectAsync())
        {
            Console.Error.WriteLine($"[network] Client failed: {client.StatusMessage}");
            return false;
        }

        var local = Constant(0.20f);
        var remote = Constant(0.30f);
        var until = Stopwatch.StartNew();
        while (until.Elapsed < TimeSpan.FromSeconds(3) && hostPlayback.Peak < 0.35f)
        {
            hostCapture.Emit(local);
            clientCapture.Emit(remote);
            hostPlayback.Pull(Constants.SamplesPerFrame);
            await Task.Delay(10);
        }

        client.Disconnect();
        var passed = hostPlayback.Peak > 0.35f;
        Console.WriteLine(passed
            ? $"[network] PASS (mixed peak {hostPlayback.Peak:F3})"
            : $"[network] FAIL (mixed peak {hostPlayback.Peak:F3}; expected > 0.350)");
        return passed;
    }

    private static async Task<bool> TestBridgeAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("[bridge] Windows is required.");
            return false;
        }

        using var enumerator = new MMDeviceEnumerator();
        var render = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(x => x.FriendlyName.Contains("Vmic Bridge", StringComparison.OrdinalIgnoreCase));
        var capture = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .FirstOrDefault(x => x.FriendlyName.Contains("Vmic Bridge", StringComparison.OrdinalIgnoreCase));
        if (render is null || capture is null)
        {
            Console.Error.WriteLine("[bridge] FAIL: Vmic Bridge render and capture endpoints must both be installed.");
            PrintActiveEndpoints(enumerator, DataFlow.Render, "render");
            PrintActiveEndpoints(enumerator, DataFlow.Capture, "capture");
            return false;
        }

        Console.WriteLine($"[bridge] Render:  {render.FriendlyName}");
        Console.WriteLine($"[bridge] Capture: {capture.FriendlyName}");
        float peak = 0;
        using var recorder = new WasapiCapture(capture);
        recorder.DataAvailable += (_, e) =>
        {
            var bytesPerSample = recorder.WaveFormat.BitsPerSample / 8;
            for (var offset = 0; offset + bytesPerSample <= e.BytesRecorded; offset += bytesPerSample)
            {
                var sample = recorder.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat &&
                             recorder.WaveFormat.BitsPerSample == 32
                    ? BitConverter.ToSingle(e.Buffer, offset)
                    : recorder.WaveFormat.BitsPerSample == 16
                        ? BitConverter.ToInt16(e.Buffer, offset) / 32768f
                        : 0f;
                peak = Math.Max(peak, Math.Abs(sample));
            }
        };
        using var output = new WasapiOut(render, AudioClientShareMode.Shared, false, 80);
        var tone = new SignalGenerator(48_000, 2) { Frequency = 997, Gain = 0.10, Type = SignalGeneratorType.Sin };
        output.Init(tone.ToWaveProvider());

        recorder.StartRecording();
        output.Play();
        await Task.Delay(TimeSpan.FromSeconds(3));
        output.Stop();
        recorder.StopRecording();

        var passed = peak >= 0.02f;
        Console.WriteLine(passed
            ? $"[bridge] PASS (captured peak {peak:F3})"
            : $"[bridge] FAIL (captured peak {peak:F3}; expected >= 0.020)");
        return passed;
    }

    private static void PrintActiveEndpoints(MMDeviceEnumerator enumerator, DataFlow flow, string label)
    {
        var endpoints = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        Console.Error.WriteLine($"[bridge] Active {label} endpoints ({endpoints.Count}):");
        foreach (var endpoint in endpoints)
        {
            Console.Error.WriteLine($"[bridge]   {endpoint.FriendlyName}");
        }
    }

    private static float[] Constant(float value) => Enumerable.Repeat(value, Constants.SamplesPerFrame).ToArray();

    private sealed class SyntheticCapture : IAudioCapture
    {
        public event Action<float[]>? FrameReady;
        public string DeviceName => "Synthetic diagnostic capture";
        public bool IsRunning { get; private set; }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Emit(float[] frame) => FrameReady?.Invoke(frame);
        public void Dispose() { }
    }

    private sealed class MeteredPlayback : IAudioPlayback
    {
        private IAudioSource? _source;
        public float Peak { get; private set; }
        public string DeviceName => "Diagnostic playback sink";
        public bool IsRunning { get; private set; }
        public void Start(IAudioSource source)
        {
            _source = source;
            IsRunning = true;
        }
        public void Stop()
        {
            _source = null;
            IsRunning = false;
        }
        public void Pull(int count)
        {
            if (_source is null) return;
            var buffer = new float[count];
            _source.Read(buffer);
            foreach (var sample in buffer) Peak = Math.Max(Peak, Math.Abs(sample));
        }
        public void Dispose() { }
    }
}
