using System.Buffers.Binary;
using NAudio.Wave;
using Vmic.Core;
using Vmic.Core.Audio;

namespace Vmic.App.Audio;

/// <summary>
/// Pulls mono float samples from an <see cref="IAudioSource"/> and presents them
/// as a 48 kHz, 16-bit, mono <see cref="IWaveProvider"/>.
/// </summary>
public sealed class SourceMonoProvider : IWaveProvider
{
    private readonly IAudioSource _source;
    private float[] _floatBuffer = Array.Empty<float>();

    public WaveFormat WaveFormat { get; } =
        new(Constants.SampleRate, 16, 1);

    public SourceMonoProvider(IAudioSource source) => _source = source;

    public int Read(byte[] buffer, int offset, int count)
    {
        int samples = count / 2;
        if (samples <= 0) return 0;
        if (_floatBuffer.Length < samples) _floatBuffer = new float[samples];

        _source.Read(_floatBuffer.AsSpan(0, samples));
        PcmConv.FloatToPcm16(_floatBuffer.AsSpan(0, samples), buffer.AsSpan(offset, samples * 2));
        return samples * 2;
    }
}

/// <summary>
/// Converts a mono 16-bit stream (at the device's sample rate) into the device's
/// shared-mode mix format: duplicates the mono channel across all output channels
/// and writes the mix format's bit depth (float32 / pcm16 / pcm24 / pcm32).
/// </summary>
public sealed class MonoToMixFormatProvider : IWaveProvider
{
    private readonly IWaveProvider _mono;
    private readonly int _bytesPerSample;
    private byte[] _monoBuffer = Array.Empty<byte>();

    public WaveFormat WaveFormat { get; }

    public MonoToMixFormatProvider(IWaveProvider mono, WaveFormat mixFormat)
    {
        _mono = mono;
        WaveFormat = mixFormat;
        _bytesPerSample = mixFormat.BitsPerSample / 8;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int outFrames = count / WaveFormat.BlockAlign;
        if (outFrames <= 0) return 0;

        int monoBytes = outFrames * 2;
        if (_monoBuffer.Length < monoBytes) _monoBuffer = new byte[monoBytes];

        int read = ReadFully(_mono, _monoBuffer, monoBytes);
        int frames = read / 2;
        if (frames <= 0) return 0;

        int channels = WaveFormat.Channels;
        int index = offset;
        for (int i = 0; i < frames; i++)
        {
            short s = (short)(_monoBuffer[i * 2] | (_monoBuffer[i * 2 + 1] << 8));
            float f = s / 32768f;
            for (int c = 0; c < channels; c++)
            {
                WriteSample(buffer, index, f);
                index += _bytesPerSample;
            }
        }

        return frames * WaveFormat.BlockAlign;
    }

    private void WriteSample(byte[] buffer, int index, float f)
    {
        if (f > 1f) f = 1f;
        else if (f < -1f) f = -1f;

        if (WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat && _bytesPerSample == 4)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                buffer.AsSpan(index, 4), BitConverter.SingleToInt32Bits(f));
            return;
        }

        switch (_bytesPerSample)
        {
            case 2:
            {
                short v = (short)(f * short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(index, 2), v);
                break;
            }
            case 3:
            {
                int v = (int)(f * 8388607f); // 24-bit range
                buffer[index] = (byte)(v & 0xFF);
                buffer[index + 1] = (byte)((v >> 8) & 0xFF);
                buffer[index + 2] = (byte)((v >> 16) & 0xFF);
                break;
            }
            case 4:
            {
                int v = (int)(f * int.MaxValue);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(index, 4), v);
                break;
            }
        }
    }

    private static int ReadFully(IWaveProvider provider, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = provider.Read(buffer, read, count - read);
            if (n <= 0) break;
            read += n;
        }
        return read;
    }
}
