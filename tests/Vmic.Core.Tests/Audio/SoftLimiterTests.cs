using Vmic.Core.Audio;
using Xunit;

namespace Vmic.Core.Tests.Audio;

public class SoftLimiterTests
{
    [Fact]
    public void BelowThreshold_PassesThroughUnchanged()
    {
        var samples = new float[] { 0.1f, -0.5f, 0.89f };
        var original = (float[])samples.Clone();
        SoftLimiter.Process(samples, threshold: 0.9f);
        Assert.Equal(original, samples);
    }

    [Fact]
    public void AboveThreshold_IsCompressed_BelowOne()
    {
        var samples = new float[] { 2.0f, -3.0f, 1.2f };
        SoftLimiter.Process(samples, threshold: 0.9f);
        foreach (var s in samples)
        {
            // Approaches 1.0 asymptotically; may equal 1.0 at extreme overdrive,
            // but must never exceed full scale.
            Assert.True(Math.Abs(s) <= 1.0f, $"expected |{s}| <= 1");
            Assert.True(Math.Abs(s) > 0.9f, $"expected |{s}| > threshold");
        }
    }

    [Fact]
    public void Output_NeverExceedsUnity_ForHugeInput()
    {
        var samples = new float[] { 1000f, -1000f, 50f };
        SoftLimiter.Process(samples, threshold: 0.9f);
        foreach (var s in samples)
            Assert.True(Math.Abs(s) <= 1.0f);
    }

    [Fact]
    public void TransferFunction_IsMonotonic()
    {
        // Larger inputs must produce larger (or equal) magnitudes.
        float prev = 0f;
        for (float x = 0f; x <= 10f; x += 0.1f)
        {
            var buf = new float[] { x };
            SoftLimiter.Process(buf, threshold: 0.9f);
            Assert.True(buf[0] >= prev - 1e-6f, $"not monotonic at {x}: {buf[0]} < {prev}");
            prev = buf[0];
        }
    }

    [Fact]
    public void NegativeInput_KeepsSign()
    {
        var samples = new float[] { -1.5f };
        SoftLimiter.Process(samples, threshold: 0.9f);
        Assert.True(samples[0] < 0f);
    }
}
