namespace Vmic.Core.Audio;

/// <summary>
/// An <see cref="IAudioSource"/> backed by a <see cref="JitterBuffer"/>: pulls
/// 10 ms PCM16 frames from the jitter buffer and converts them to float on the
/// fly, presenting a continuous sample stream to the mixer.
/// </summary>
public sealed class JitterBufferSource : IAudioSource
{
    private readonly JitterBuffer _jitter;
    private readonly float[] _frameSamples = new float[Constants.SamplesPerFrame];
    private int _frameOffset;
    private bool _haveFrame;

    public string Name { get; }

    public JitterBufferSource(JitterBuffer jitter, string name = "remote")
    {
        _jitter = jitter;
        Name = name;
    }

    public int Read(Span<float> buffer)
    {
        int written = 0;
        while (written < buffer.Length)
        {
            if (!_haveFrame || _frameOffset >= Constants.SamplesPerFrame)
            {
                if (_jitter.TryDequeue(out var frame))
                {
                    PcmConv.Pcm16ToFloat(frame.Pcm16, _frameSamples);
                    _frameOffset = 0;
                    _haveFrame = true;
                }
                else
                {
                    // Nothing has arrived yet — pad the rest with silence.
                    buffer[written..].Clear();
                    return buffer.Length;
                }
            }

            int take = Math.Min(buffer.Length - written, Constants.SamplesPerFrame - _frameOffset);
            _frameSamples.AsSpan(_frameOffset, take).CopyTo(buffer.Slice(written, take));
            _frameOffset += take;
            written += take;
        }
        return buffer.Length;
    }

    /// <summary>Discards any partially consumed frame (e.g., on reconnect).</summary>
    public void Reset()
    {
        _haveFrame = false;
        _frameOffset = 0;
    }
}
