using Vmic.Core;
using Vmic.Core.Audio;
using Xunit;

namespace Vmic.Core.Tests.Audio;

public class JitterBufferTests
{
    private static Frame MakeFrame(uint seq, byte fill)
    {
        var pcm = new byte[Constants.BytesPerFrame];
        Array.Fill(pcm, fill);
        return new Frame(seq, pcm);
    }

    private static void Enqueue(JitterBuffer jb, params uint[] seqs)
    {
        foreach (var s in seqs) jb.Enqueue(MakeFrame(s, (byte)(s & 0x7F)));
    }

    [Fact]
    public void Empty_TryDequeue_ReturnsFalse()
    {
        var jb = new JitterBuffer();
        Assert.False(jb.TryDequeue(out _));
    }

    [Fact]
    public void InOrder_FramesComeOutInOrder()
    {
        var jb = new JitterBuffer();
        Enqueue(jb, 0, 1, 2, 3, 4);

        for (uint expected = 0; expected <= 4; expected++)
        {
            Assert.True(jb.TryDequeue(out var frame));
            Assert.Equal(expected, frame.Sequence);
        }
    }

    [Fact]
    public void OutOfOrder_AreReordered()
    {
        var jb = new JitterBuffer();
        Enqueue(jb, 0, 1, 3, 2, 4);

        for (uint expected = 0; expected <= 4; expected++)
        {
            Assert.True(jb.TryDequeue(out var frame));
            Assert.Equal(expected, frame.Sequence);
        }
    }

    [Fact]
    public void MissingFrame_IsConcealedWithLastFrame()
    {
        var jb = new JitterBuffer();
        // seq 2 is missing; frame 1 has fill 0x01.
        Enqueue(jb, 0, 1, 3, 4, 5);

        Assert.True(jb.TryDequeue(out var f0)); Assert.Equal(0u, f0.Sequence);
        Assert.True(jb.TryDequeue(out var f1)); Assert.Equal(1u, f1.Sequence);

        // Next should be seq 2 — concealed as a copy of frame 1's samples.
        Assert.True(jb.TryDequeue(out var concealed));
        Assert.Equal(2u, concealed.Sequence);
        Assert.All(concealed.Pcm16, b => Assert.Equal(0x01, b));

        Assert.True(jb.TryDequeue(out var f3)); Assert.Equal(3u, f3.Sequence);

        var stats = jb.GetStats();
        Assert.Equal(1, stats.Loss);
    }

    [Fact]
    public void Duplicate_IsIgnored()
    {
        var jb = new JitterBuffer();
        Enqueue(jb, 0, 1, 2, 3);
        jb.Enqueue(MakeFrame(1, 0x55)); // duplicate of seq 1

        for (uint expected = 0; expected <= 3; expected++)
        {
            Assert.True(jb.TryDequeue(out var frame));
            Assert.Equal(expected, frame.Sequence);
        }
        Assert.Equal(1, jb.GetStats().Duplicate);
    }

    [Fact]
    public void LateFrame_AfterDequeue_IsDropped()
    {
        var jb = new JitterBuffer();
        Enqueue(jb, 0, 1, 2, 3, 4);
        Assert.True(jb.TryDequeue(out _)); // emits 0, nextSeq=1
        Assert.True(jb.TryDequeue(out _)); // emits 1, nextSeq=2

        jb.Enqueue(MakeFrame(0, 0x77)); // late — already past seq 0
        Assert.Equal(1, jb.GetStats().Late);
    }

    [Fact]
    public void ProlongedLoss_FallsBackToSilence()
    {
        var jb = new JitterBuffer();
        Enqueue(jb, 0, 1, 2, 3);

        // Drain the 4 real frames.
        for (int i = 0; i < 4; i++) Assert.True(jb.TryDequeue(out _));

        // Next 10 concealed frames repeat the last frame (fill 0x03).
        for (int i = 0; i < 10; i++)
        {
            Assert.True(jb.TryDequeue(out var c));
            Assert.All(c.Pcm16, b => Assert.Equal(0x03, b));
        }

        // After that, concealment becomes silence.
        Assert.True(jb.TryDequeue(out var silent));
        Assert.All(silent.Pcm16, b => Assert.Equal(0, b));
    }

    [Fact]
    public void SequenceWraparound_IsHandled()
    {
        var jb = new JitterBuffer();
        uint start = uint.MaxValue - 1; // MaxValue-1, MaxValue, 0, 1, 2, 3
        Enqueue(jb, start, start + 1, start + 2, start + 3, start + 4, start + 5);

        for (uint i = 0; i < 6; i++)
        {
            Assert.True(jb.TryDequeue(out var frame));
            Assert.Equal(start + i, frame.Sequence); // wraps naturally in uint space
        }
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var jb = new JitterBuffer();
        Enqueue(jb, 0, 1, 2, 3);
        jb.Reset();
        Assert.False(jb.TryDequeue(out _));
        Assert.Equal(0, jb.Depth);
    }
}
