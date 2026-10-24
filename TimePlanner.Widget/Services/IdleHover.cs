namespace TimePlanner.Widget.Services;

/// <summary>Monotonic hover time, independent of the check-in clock and wall-clock changes.</summary>
public sealed class IdleHover(TimeProvider clock)
{
    private long? _started;
    public void Start() => _started = clock.GetTimestamp();
    public void Cancel() => _started = null;
    public double Fraction => _started is { } start
        ? Math.Clamp(clock.GetElapsedTime(start).TotalSeconds / 3, 0, 1) : 0;
}
