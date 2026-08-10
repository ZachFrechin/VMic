using Vmic.Core.Audio;
using Xunit;

namespace Vmic.Core.Tests.Audio;

public class MonoMixerTests
{
    /// <summary>Test source that emits a constant sample value.</summary>
    private sealed class ConstantSource : IAudioSource
    {
        private readonly float _value;
        public string Name { get; }
        public int ReadCount;
        public ConstantSource(string name, float value) { Name = name; _value = value; }
        public int Read(Span<float> buffer) { buffer.Fill(_value); ReadCount++; return buffer.Length; }
    }

    private static float[] Mix(MonoMixer mixer, int n = 8)
    {
        var output = new float[n];
        mixer.Read(output);
        return output;
    }

    [Fact]
    public void TwoSources_AreSummed()
    {
        var mixer = new MonoMixer { Limit = false, MasterGain = 1f };
        mixer.AddSource(new ConstantSource("a", 0.3f));
        mixer.AddSource(new ConstantSource("b", 0.4f));

        var result = Mix(mixer);
        Assert.All(result, s => Assert.Equal(0.7f, s, 4));
    }

    [Fact]
    public void PerSourceGain_IsApplied()
    {
        var mixer = new MonoMixer { Limit = false };
        var a = new ConstantSource("a", 0.5f);
        var b = new ConstantSource("b", 0.5f);
        mixer.AddSource(a, gain: 0.5f); // 0.25
        mixer.AddSource(b, gain: 1.0f); // 0.50

        var result = Mix(mixer);
        Assert.All(result, s => Assert.Equal(0.75f, s, 4));

        mixer.SetGain(a, 1.0f); // now 0.5 + 0.5 = 1.0
        result = Mix(mixer);
        Assert.All(result, s => Assert.Equal(1.0f, s, 4));
    }

    [Fact]
    public void MasterGain_ScalesTheMix()
    {
        var mixer = new MonoMixer { Limit = false, MasterGain = 0.5f };
        mixer.AddSource(new ConstantSource("a", 0.6f));

        var result = Mix(mixer);
        Assert.All(result, s => Assert.Equal(0.3f, s, 4));
    }

    [Fact]
    public void MutedSource_IsDrainedButNotMixed()
    {
        var mixer = new MonoMixer { Limit = false };
        var a = new ConstantSource("a", 0.9f);
        var b = new ConstantSource("b", 0.9f);
        mixer.AddSource(a, gain: 0f); // muted
        mixer.AddSource(b, gain: 1f);

        var result = Mix(mixer);
        Assert.All(result, s => Assert.Equal(0.9f, s, 4)); // only b contributes
        Assert.True(a.ReadCount > 0); // but a was still drained
    }

    [Fact]
    public void Limiter_PreventsClipping()
    {
        var mixer = new MonoMixer { Limit = true };
        mixer.AddSource(new ConstantSource("a", 0.9f));
        mixer.AddSource(new ConstantSource("b", 0.9f)); // sums to 1.8 without limiting

        var result = Mix(mixer);
        Assert.All(result, s => Assert.True(s < 1.0f && s > 0.8f, $"got {s}"));
    }

    [Fact]
    public void NoSources_ProducesSilence()
    {
        var mixer = new MonoMixer();
        var result = Mix(mixer);
        Assert.All(result, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void RemoveSource_StopsMixing()
    {
        var mixer = new MonoMixer { Limit = false };
        var a = new ConstantSource("a", 0.5f);
        mixer.AddSource(a);
        Assert.All(Mix(mixer), s => Assert.Equal(0.5f, s, 4));

        mixer.RemoveSource(a);
        Assert.All(Mix(mixer), s => Assert.Equal(0f, s));
    }
}
