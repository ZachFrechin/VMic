namespace Vmic.Core.Diagnostics;

/// <summary>Diagnostic counters for a <see cref="Audio.JitterBuffer"/>.</summary>
public readonly record struct BufferStats(
    long Received,
    long Loss,
    long Duplicate,
    long Late,
    long OutOfOrder,
    int Depth)
{
    /// <summary>Packet loss ratio in [0, 1]; 0 when nothing has been received.</summary>
    public double LossRatio => Received == 0 ? 0 : (double)Loss / Received;

    public override string ToString() =>
        $"recv={Received} loss={Loss} dup={Duplicate} late={Late} ooo={OutOfOrder} depth={Depth}";
}

/// <summary>Rolling network/audio health snapshot shown in the UI.</summary>
public sealed class SessionStats
{
    private long _framesSent;
    private long _framesReceived;
    private long _bytesReceived;

    public long FramesSent => Interlocked.Read(ref _framesSent);
    public long FramesReceived => Interlocked.Read(ref _framesReceived);
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public void RecordSent(int frames = 1) => Interlocked.Add(ref _framesSent, frames);
    public void RecordReceived(int frames, int bytes)
    {
        Interlocked.Add(ref _framesReceived, frames);
        Interlocked.Add(ref _bytesReceived, bytes);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _framesSent, 0);
        Interlocked.Exchange(ref _framesReceived, 0);
        Interlocked.Exchange(ref _bytesReceived, 0);
    }
}
