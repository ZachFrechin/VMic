using Vmic.Core.Audio;
using Xunit;

namespace Vmic.Core.Tests.Audio;

public class PcmConvTests
{
    [Fact]
    public void Pcm16ToFloat_DecodesKnownValues()
    {
        // short 16384 (0x4000) => 0.5 ; short -16384 => -0.5
        byte[] pcm = { 0x00, 0x40, 0x00, 0xC0 };
        var dest = new float[2];
        PcmConv.Pcm16ToFloat(pcm, dest);
        Assert.Equal(0.5f, dest[0], 4);
        Assert.Equal(-0.5f, dest[1], 4);
    }

    [Fact]
    public void FloatToPcm16_RoundTrips()
    {
        var samples = new float[] { 0f, 0.5f, -0.5f, 1f, -1f };
        var pcm = new byte[samples.Length * 2];
        PcmConv.FloatToPcm16(samples, pcm);

        var back = new float[samples.Length];
        PcmConv.Pcm16ToFloat(pcm, back);

        for (int i = 0; i < samples.Length; i++)
            Assert.Equal(samples[i], back[i], 3);
    }

    [Fact]
    public void FloatToPcm16_ClampsOutOfRange()
    {
        var pcm = new byte[4];
        PcmConv.FloatToPcm16(new float[] { 1.5f, -1.5f }, pcm);

        var back = new float[2];
        PcmConv.Pcm16ToFloat(pcm, back);
        Assert.True(back[0] <= 1.0f && back[0] > 0.99f);
        Assert.True(back[1] >= -1.0f && back[1] < -0.99f);
    }

    [Fact]
    public void StereoToMono_AveragesChannels()
    {
        var stereo = new float[] { 1.0f, -1.0f, 0.5f, 0.5f, 0.2f, 0.6f };
        var mono = new float[3];
        PcmConv.StereoToMono(stereo, mono);
        Assert.Equal(0.0f, mono[0], 4);
        Assert.Equal(0.5f, mono[1], 4);
        Assert.Equal(0.4f, mono[2], 4);
    }
}
