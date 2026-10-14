using System.Diagnostics;

namespace XinputG84HE.Services;

public readonly record struct OutputDiagnosticSample(
    long TotalUpdates, double UpdatesPerSecond, double JitterMs, double LongestIntervalMs);

/// <summary>Measures completed output-loop updates without changing their scheduling.</summary>
public sealed class OutputDiagnostics
{
    private readonly object _lock = new();
    private long _windowStart = Stopwatch.GetTimestamp();
    private long _lastUpdate, _total, _windowUpdates, _intervals;
    private double _sumMs, _sumSquaresMs, _longestMs;

    public void BeginRun()
    {
        lock (_lock) { _lastUpdate = 0; }
    }

    public void RecordUpdate()
    {
        lock (_lock)
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastUpdate != 0)
            {
                double ms = (now - _lastUpdate) * 1000.0 / Stopwatch.Frequency;
                _sumMs += ms;
                _sumSquaresMs += ms * ms;
                _longestMs = Math.Max(_longestMs, ms);
                _intervals++;
            }
            _lastUpdate = now;
            _total++;
            _windowUpdates++;
        }
    }

    public OutputDiagnosticSample Sample()
    {
        lock (_lock)
        {
            long now = Stopwatch.GetTimestamp();
            double seconds = Math.Max(0.001, (now - _windowStart) / (double)Stopwatch.Frequency);
            double mean = _intervals > 0 ? _sumMs / _intervals : 0;
            double variance = _intervals > 0 ? Math.Max(0, _sumSquaresMs / _intervals - mean * mean) : 0;
            var result = new OutputDiagnosticSample(_total, _windowUpdates / seconds, Math.Sqrt(variance), _longestMs);
            _windowStart = now;
            _windowUpdates = _intervals = 0;
            _sumMs = _sumSquaresMs = _longestMs = 0;
            return result;
        }
    }
}
