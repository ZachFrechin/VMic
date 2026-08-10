namespace Vmic.Core.Audio;

/// <summary>
/// One 10 ms audio frame as carried through the jitter buffer: a monotonically
/// increasing sequence number plus 16-bit PCM mono samples
/// (<see cref="Constants.BytesPerFrame"/> bytes for a full frame).
/// </summary>
public readonly record struct Frame(uint Sequence, byte[] Pcm16)
{
    /// <summary>A silent frame with the given sequence (used for concealment at startup).</summary>
    public static Frame Silence(uint sequence) => new(sequence, new byte[Constants.BytesPerFrame]);
}
