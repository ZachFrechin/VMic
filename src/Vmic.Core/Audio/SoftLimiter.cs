namespace Vmic.Core.Audio;

/// <summary>
/// A stateless soft limiter. Samples at or below the threshold pass through
/// unchanged; samples above it are compressed along a smooth exponential curve
/// that asymptotically approaches 1.0, so the output never clips and the
/// transfer function is C1-continuous at the threshold (no clicks).
/// </summary>
public static class SoftLimiter
{
    /// <summary>Default limiting threshold (~ -0.9 dBFS).</summary>
    public const float DefaultThreshold = 0.9f;

    /// <summary>Limits <paramref name="samples"/> in place.</summary>
    public static void Process(Span<float> samples, float threshold = DefaultThreshold)
    {
        if (threshold <= 0f) threshold = 0.0001f;
        if (threshold >= 1f)
        {
            // No soft region — just hard-clamp to [-1, 1].
            for (int i = 0; i < samples.Length; i++)
            {
                if (samples[i] > 1f) samples[i] = 1f;
                else if (samples[i] < -1f) samples[i] = -1f;
            }
            return;
        }

        float headroom = 1f - threshold;
        for (int i = 0; i < samples.Length; i++)
        {
            float x = samples[i];
            float a = Math.Abs(x);
            if (a <= threshold)
                continue;

            float over = a - threshold;
            // threshold + headroom * (1 - e^(-over/headroom)) maps
            // threshold→threshold and ∞→1, smoothly and monotonically.
            float compressed = threshold + headroom * (1f - MathF.Exp(-over / headroom));
            samples[i] = Math.Sign(x) * compressed;
        }
    }
}
