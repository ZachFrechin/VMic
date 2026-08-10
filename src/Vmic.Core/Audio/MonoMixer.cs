namespace Vmic.Core.Audio;

/// <summary>
/// Mixes N mono float <see cref="IAudioSource"/>s into one mono float stream.
/// Per-source gain and a master gain are applied, then an optional soft limiter
/// prevents clipping. <see cref="Read"/> is called on the audio thread and never
/// blocks; muted sources are still drained so their buffers can't grow.
///
/// The mixer is itself an <see cref="IAudioSource"/>, so the playback sink can
/// pull the final mix directly.
/// </summary>
public sealed class MonoMixer : IAudioSource
{
    public string Name => "mix";
    private sealed record Entry(IAudioSource Source, float Gain);

    private readonly object _gate = new();
    private readonly List<Entry> _sources = new();
    private float[] _scratch = Array.Empty<float>();

    /// <summary>Applied to the summed signal before limiting.</summary>
    public float MasterGain { get; set; } = 1f;

    /// <summary>Whether to run the soft limiter on the output.</summary>
    public bool Limit { get; set; } = true;

    /// <summary>Limiting threshold passed to <see cref="SoftLimiter"/>.</summary>
    public float LimitThreshold { get; set; } = SoftLimiter.DefaultThreshold;

    public void AddSource(IAudioSource source, float gain = 1f)
    {
        lock (_gate)
        {
            if (_sources.Any(e => e.Source == source)) return;
            _sources.Add(new Entry(source, gain));
        }
    }

    public bool RemoveSource(IAudioSource source)
    {
        lock (_gate)
        {
            int idx = _sources.FindIndex(e => e.Source == source);
            if (idx < 0) return false;
            _sources.RemoveAt(idx);
            return true;
        }
    }

    public void SetGain(IAudioSource source, float gain)
    {
        lock (_gate)
        {
            for (int i = 0; i < _sources.Count; i++)
                if (_sources[i].Source == source)
                    _sources[i] = _sources[i] with { Gain = gain };
        }
    }

    public float GetGain(IAudioSource source)
    {
        lock (_gate)
        {
            return _sources.FirstOrDefault(e => e.Source == source)?.Gain ?? 1f;
        }
    }

    /// <summary>Snapshot of the current sources (for the UI).</summary>
    public IReadOnlyList<(IAudioSource Source, float Gain)> Sources
    {
        get { lock (_gate) return _sources.Select(e => (e.Source, e.Gain)).ToList(); }
    }

    /// <summary>
    /// Mixes all sources into <paramref name="output"/> (silence if none). Called
    /// on the audio thread; non-blocking. Returns <c>output.Length</c>.
    /// </summary>
    public int Read(Span<float> output)
    {
        output.Clear();

        Entry[] snapshot;
        lock (_gate)
        {
            if (_sources.Count == 0) return output.Length;
            if (_scratch.Length < output.Length)
                _scratch = new float[output.Length];
            snapshot = _sources.ToArray();
        }

        foreach (var entry in snapshot)
        {
            int n = entry.Source.Read(_scratch.AsSpan(0, output.Length));
            if (entry.Gain == 0f) continue; // muted — drained but not mixed

            float g = entry.Gain;
            for (int i = 0; i < n && i < output.Length; i++)
                output[i] += _scratch[i] * g;
        }

        if (MasterGain != 1f)
        {
            float mg = MasterGain;
            for (int i = 0; i < output.Length; i++)
                output[i] *= mg;
        }

        if (Limit)
            SoftLimiter.Process(output, LimitThreshold);

        return output.Length;
    }
}
