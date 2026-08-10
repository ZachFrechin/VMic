namespace Vmic.Core.Diagnostics;

/// <summary>
/// Thread-safe peak/RMS level meter. The audio thread calls
/// <see cref="Process"/> per buffer; the UI thread reads
/// <see cref="Snapshot"/> at its own cadence. Peak decays a little on each
/// processed block so the meter doesn't stick.
/// </summary>
public sealed class LevelMeter
{
    private readonly object _gate = new();
    private float _peak;
    private float _rms;

    /// <summary>Peak decay applied per processed block.</summary>
    private const float PeakDecay = 0.85f;

    /// <summary>Updates the meter from a block of mono float samples.</summary>
    public void Process(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;

        float peak = 0f;
        double sumSq = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float a = Math.Abs(samples[i]);
            if (a > peak) peak = a;
            sumSq += (double)samples[i] * samples[i];
        }
        float rms = (float)Math.Sqrt(sumSq / samples.Length);

        lock (_gate)
        {
            _peak = Math.Max(peak, _peak * PeakDecay);
            _rms = rms;
        }
    }

    /// <summary>Current (peak, rms) pair, each in [0, ~1+].</summary>
    public (float Peak, float Rms) Snapshot()
    {
        lock (_gate) return (_peak, _rms);
    }

    /// <summary>Clears the meter back to silence.</summary>
    public void Reset()
    {
        lock (_gate) { _peak = 0f; _rms = 0f; }
    }
}
