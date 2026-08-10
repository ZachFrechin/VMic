using Vmic.Core.Diagnostics;

namespace Vmic.Core.Audio;

/// <summary>
/// Absorbs network jitter for the incoming audio stream. Frames arrive out of
/// order, duplicated, or not at all; the buffer emits a continuous, in-order
/// frame stream at the caller's pull rate (one frame per 10 ms).
///
/// Behaviour:
///   • reorder window keyed by wrap-safe sequence number,
///   • duplicate and late frames are dropped,
///   • a missing frame is concealed with zero-order hold (repeat of the previous
///     frame) for a short burst, then silence,
///   • a short warm-up buffers a few frames before the first output to absorb
///     initial jitter.
///
/// Thread-safety: <see cref="Enqueue"/> is called from the network thread and
/// <see cref="TryDequeue"/> from the audio thread; both serialize on one lock.
/// </summary>
public sealed class JitterBuffer
{
    // Warm-up: start emitting once we have this many frames queued, or after this
    // many empty pulls (whichever comes first) — bounds startup latency.
    private const int MinStartFrames = 4;
    private const int MaxWarmupPulls = 8;
    // After this many consecutive concealed frames we output silence instead of
    // repeating stale audio.
    private const int MaxConcealFrames = 10;
    // Hard cap on the reorder window to bound memory under pathological input.
    private const int MaxPending = 64;

    private readonly object _gate = new();
    private readonly Dictionary<uint, byte[]> _pending = new();

    private bool _started;
    private int _warmupPulls;
    private uint _nextSeq;
    private byte[]? _lastFrame;
    private int _consecutiveLoss;

    private long _received;
    private long _loss;
    private long _duplicate;
    private long _late;
    private long _outOfOrder;

    /// <summary>Stores a frame arriving from the network.</summary>
    public void Enqueue(in Frame frame)
    {
        lock (_gate)
        {
            _received++;
            uint seq = frame.Sequence;

            if (_started)
            {
                int diff = SeqDiff(seq, _nextSeq);
                if (diff < 0)
                {
                    _late++; // already emitted past this point
                    return;
                }
            }

            if (_pending.ContainsKey(seq))
            {
                _duplicate++;
                return;
            }

            // Copy so the caller can reuse its buffer.
            var copy = new byte[frame.Pcm16.Length];
            Array.Copy(frame.Pcm16, copy, frame.Pcm16.Length);
            _pending[seq] = copy;

            if (_started && SeqDiff(seq, _nextSeq) > 0)
                _outOfOrder++;

            // Bound the window: drop the oldest frame if we overflow.
            if (_pending.Count > MaxPending)
            {
                uint oldest = MinSeq(_pending.Keys);
                _pending.Remove(oldest);
            }
        }
    }

    /// <summary>
    /// Produces the next in-order frame (real or concealed). Returns false only
    /// before the first frame has arrived (caller should emit silence).
    /// </summary>
    public bool TryDequeue(out Frame frame)
    {
        lock (_gate)
        {
            frame = default;
            if (_pending.Count == 0 && !_started)
                return false;

            if (!_started)
            {
                bool enough = _pending.Count >= MinStartFrames;
                bool timedOut = _warmupPulls >= MaxWarmupPulls;
                if (!enough && !timedOut)
                {
                    _warmupPulls++;
                    return false; // still warming up — caller emits silence
                }
                _started = true;
                _nextSeq = MinSeq(_pending.Keys);
            }

            if (_pending.TryGetValue(_nextSeq, out var pcm))
            {
                _pending.Remove(_nextSeq);
                _lastFrame = pcm;
                _consecutiveLoss = 0;
                frame = new Frame(_nextSeq, pcm);
                _nextSeq++;
                return true;
            }

            // Missing frame — conceal.
            _loss++;
            _consecutiveLoss++;
            byte[] concealed;
            if (_consecutiveLoss <= MaxConcealFrames && _lastFrame is not null)
            {
                concealed = new byte[_lastFrame.Length];
                Array.Copy(_lastFrame, concealed, _lastFrame.Length); // zero-order hold
            }
            else
            {
                concealed = new byte[Constants.BytesPerFrame]; // silence
            }
            frame = new Frame(_nextSeq, concealed);
            _nextSeq++;
            return true;
        }
    }

    /// <summary>Resets the buffer to its initial (empty, un-started) state.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _pending.Clear();
            _started = false;
            _warmupPulls = 0;
            _nextSeq = 0;
            _lastFrame = null;
            _consecutiveLoss = 0;
        }
    }

    /// <summary>Current buffer occupancy, in frames.</summary>
    public int Depth
    {
        get { lock (_gate) return _pending.Count; }
    }

    /// <summary>A snapshot of the buffer's diagnostic counters.</summary>
    public BufferStats GetStats()
    {
        lock (_gate)
        {
            return new BufferStats(_received, _loss, _duplicate, _late, _outOfOrder, _pending.Count);
        }
    }

    /// <summary>
    /// Wrap-safe signed distance <c>a - b</c>: positive when <paramref name="a"/>
    /// is after <paramref name="b"/> in sequence space.
    /// </summary>
    internal static int SeqDiff(uint a, uint b) => (int)(a - b);

    private static uint MinSeq(IEnumerable<uint> seqs)
    {
        using var it = seqs.GetEnumerator();
        it.MoveNext();
        uint min = it.Current;
        while (it.MoveNext())
            if (SeqDiff(it.Current, min) < 0)
                min = it.Current;
        return min;
    }
}
