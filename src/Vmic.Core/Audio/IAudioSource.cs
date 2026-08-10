namespace Vmic.Core.Audio;

/// <summary>
/// A pull-based producer of mono float samples at <see cref="Constants.SampleRate"/>.
/// The mixer calls <see cref="Read"/> on the audio thread. Implementations must
/// not block; if they run out of data they pad with silence and still return
/// <paramref name="buffer"/>.Length.
/// </summary>
public interface IAudioSource
{
    /// <summary>Display name (used for per-source gain controls in the UI).</summary>
    string Name { get; }

    /// <summary>
    /// Fills <paramref name="buffer"/> with mono float samples and returns the
    /// number written (equal to <c>buffer.Length</c>, silence-padded).
    /// </summary>
    int Read(Span<float> buffer);
}
