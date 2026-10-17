namespace Spek.Net;

// Times are absolute seconds in the file, independent of the plot's width and selection.
public readonly record struct TimeRange
{
    public TimeRange(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start)
            throw new ArgumentOutOfRangeException(nameof(end), "The time interval must be finite and increasing.");
        Start = start;
        End = end;
    }

    public double Start { get; }
    public double End { get; }
    public double Span => End - Start;
    public double TimeAt(double fraction) => Start + Math.Clamp(fraction, 0, 1) * Span;
    public double FractionAt(double seconds) => (seconds - Start) / Span;

    public TimeRange PanTo(double centre, double duration)
    {
        if (!double.IsFinite(centre)) throw new ArgumentOutOfRangeException(nameof(centre));
        if (!double.IsFinite(duration) || duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
        double span = Math.Min(Span, duration);
        double start = Math.Clamp(centre - span / 2, 0, duration - span);
        return new TimeRange(start, start + span);
    }

    public TimeRange Zoom(double factor, double anchor, double duration, double minimumSpan = 0.01)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        if (!double.IsFinite(anchor)) throw new ArgumentOutOfRangeException(nameof(anchor));
        if (!double.IsFinite(duration) || duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
        if (!double.IsFinite(minimumSpan) || minimumSpan <= 0) throw new ArgumentOutOfRangeException(nameof(minimumSpan));
        double span = Math.Clamp(Span / factor, Math.Min(minimumSpan, duration), duration);
        double position = Math.Clamp(FractionAt(anchor), 0, 1);
        double start = Math.Clamp(anchor - position * span, 0, duration - span);
        return new TimeRange(start, start + span);
    }
}
