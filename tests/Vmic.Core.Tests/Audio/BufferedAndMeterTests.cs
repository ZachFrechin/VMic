using Vmic.Core;
using Vmic.Core.Audio;
using Vmic.Core.Diagnostics;
using Xunit;

namespace Vmic.Core.Tests.Audio;

public class BufferedFloatSourceTests
{
    [Fact]
    public void PushThenRead_ReturnsSameSamples()
    {
        var src = new BufferedFloatSource("mic");
        src.Push(new float[] { 0.1f, 0.2f, 0.3f });

        var buffer = new float[3];
        src.Read(buffer);
        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f }, buffer);
    }

    [Fact]
    public void Read_PadsWithSilence_WhenUnderflow()
    {
        var src = new BufferedFloatSource("mic");
        src.Push(new float[] { 0.5f });

        var buffer = new float[4];
        src.Read(buffer);
        Assert.Equal(0.5f, buffer[0]);
        Assert.Equal(0f, buffer[1]);
        Assert.Equal(0f, buffer[2]);
        Assert.Equal(0f, buffer[3]);
    }

    [Fact]
    public void Overflow_DropsOldest()
    {
        var src = new BufferedFloatSource("mic", capacitySamples: 4);
        src.Push(new float[] { 1, 2, 3, 4, 5, 6 }); // only last 4 kept

        var buffer = new float[4];
        src.Read(buffer);
        Assert.Equal(new float[] { 3, 4, 5, 6 }, buffer);
    }
}

public class JitterBufferSourceTests
{
    [Fact]
    public void ReadsFramesAsFloat()
    {
        var jb = new JitterBuffer();
        // PCM16 value 16384 == 0.5f.
        var pcm = new byte[Constants.BytesPerFrame];
        for (int i = 0; i < Constants.SamplesPerFrame; i++)
        {
            pcm[i * 2] = 0x00;
            pcm[i * 2 + 1] = 0x40;
        }
        // Enqueue enough frames to pass warm-up.
        for (uint s = 0; s < 5; s++) jb.Enqueue(new Frame(s, pcm));

        var source = new JitterBufferSource(jb);
        var buffer = new float[Constants.SamplesPerFrame];
        source.Read(buffer);
        Assert.All(buffer, s => Assert.Equal(0.5f, s, 3));
    }

    [Fact]
    public void EmptyJitter_ProducesSilence()
    {
        var source = new JitterBufferSource(new JitterBuffer());
        var buffer = new float[Constants.SamplesPerFrame];
        buffer.AsSpan().Fill(9f);
        source.Read(buffer);
        Assert.All(buffer, s => Assert.Equal(0f, s));
    }
}

public class LevelMeterTests
{
    [Fact]
    public void ConstantSignal_ReportsPeakAndRms()
    {
        var meter = new LevelMeter();
        var block = new float[480];
        Array.Fill(block, 0.5f);
        meter.Process(block);

        var (peak, rms) = meter.Snapshot();
        Assert.True(peak >= 0.5f - 1e-4f);
        Assert.Equal(0.5f, rms, 3);
    }

    [Fact]
    public void Reset_ClearsLevels()
    {
        var meter = new LevelMeter();
        meter.Process(new float[] { 0.9f });
        meter.Reset();
        var (peak, rms) = meter.Snapshot();
        Assert.Equal(0f, peak);
        Assert.Equal(0f, rms);
    }
}
