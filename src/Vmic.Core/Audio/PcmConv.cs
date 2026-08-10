namespace Vmic.Core.Audio;

/// <summary>
/// Conversions between 16-bit little-endian PCM and 32-bit float samples, and a
/// stereo→mono downmix. All methods are allocation-free where the caller supplies
/// the destination.
/// </summary>
public static class PcmConv
{
    /// <summary>Converts 16-bit LE PCM samples to floats in [-1, 1].</summary>
    public static void Pcm16ToFloat(ReadOnlySpan<byte> pcm16, Span<float> dest)
    {
        int samples = Math.Min(pcm16.Length / 2, dest.Length);
        for (int i = 0; i < samples; i++)
        {
            short s = (short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8));
            dest[i] = s / 32768f;
        }
    }

    /// <summary>Converts floats in [-1, 1] to 16-bit LE PCM, clamping out-of-range values.</summary>
    public static void FloatToPcm16(ReadOnlySpan<float> samples, Span<byte> destPcm16)
    {
        int n = Math.Min(samples.Length, destPcm16.Length / 2);
        for (int i = 0; i < n; i++)
        {
            float f = samples[i];
            if (f > 1f) f = 1f;
            else if (f < -1f) f = -1f;
            short s = (short)(f * 32767f);
            destPcm16[i * 2] = (byte)(s & 0xFF);
            destPcm16[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
    }

    /// <summary>
    /// Downmixes interleaved stereo float samples to mono by averaging the channels.
    /// <paramref name="dest"/> receives <c>stereo.Length / 2</c> samples.
    /// </summary>
    public static void StereoToMono(ReadOnlySpan<float> stereo, Span<float> dest)
    {
        int frames = Math.Min(stereo.Length / 2, dest.Length);
        for (int i = 0; i < frames; i++)
            dest[i] = (stereo[i * 2] + stereo[i * 2 + 1]) * 0.5f;
    }
}
