namespace Vmic.Core.Audio;

/// <summary>
/// A push/pull bridge: the capture side pushes mono float samples, the mixer
/// pulls them. Implemented as a fixed circular buffer; overflow drops the oldest
/// samples (a stalled mixer must not grow memory without bound).
/// </summary>
public sealed class BufferedFloatSource : IAudioSource
{
    private readonly object _gate = new();
    private readonly float[] _ring;
    private int _readIndex;
    private int _count;

    public string Name { get; }

    /// <param name="capacitySamples">Ring size; defaults to ~1 second of audio.</param>
    public BufferedFloatSource(string name, int capacitySamples = Constants.SampleRate)
    {
        Name = name;
        _ring = new float[Math.Max(1, capacitySamples)];
    }

    /// <summary>Pushes samples (called from the capture thread).</summary>
    public void Push(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;
        lock (_gate)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                int writeIndex = (_readIndex + _count) % _ring.Length;
                _ring[writeIndex] = samples[i];
                if (_count == _ring.Length)
                {
                    // Full — overwrite oldest: advance the read pointer.
                    _readIndex = (_readIndex + 1) % _ring.Length;
                }
                else
                {
                    _count++;
                }
            }
        }
    }

    /// <summary>Pulls samples, padding with silence if not enough are buffered.</summary>
    public int Read(Span<float> buffer)
    {
        lock (_gate)
        {
            int n = Math.Min(buffer.Length, _count);
            for (int i = 0; i < n; i++)
            {
                buffer[i] = _ring[_readIndex];
                _readIndex = (_readIndex + 1) % _ring.Length;
            }
            _count -= n;
            buffer[n..].Clear(); // pad with silence
            return buffer.Length;
        }
    }

    /// <summary>Number of samples currently buffered.</summary>
    public int Buffered
    {
        get { lock (_gate) return _count; }
    }

    /// <summary>Empties the buffer.</summary>
    public void Clear()
    {
        lock (_gate) { _count = 0; _readIndex = 0; }
    }
}
