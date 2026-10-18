namespace Spek.Net;

// A linear frequency interval, independent of the height or DPI of the view.
public readonly record struct FrequencyRange
{
    public FrequencyRange(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum < 0 || maximum <= minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum), "The frequency interval must be finite and increasing.");
        Minimum = minimum;
        Maximum = maximum;
    }

    public double Minimum { get; }
    public double Maximum { get; }
    public double Span => Maximum - Minimum;

    public double FrequencyAt(double fractionFromTop) => Maximum - Math.Clamp(fractionFromTop, 0, 1) * Span;

    public double FractionFromTop(double frequency) => (Maximum - frequency) / Span;

    public FrequencyRange PanTo(double centre, double ceiling)
    {
        if (!double.IsFinite(centre)) throw new ArgumentOutOfRangeException(nameof(centre));
        if (!double.IsFinite(ceiling) || ceiling <= 0) throw new ArgumentOutOfRangeException(nameof(ceiling));
        double span = Math.Min(Span, ceiling);
        double minimum = Math.Clamp(centre - span / 2, 0, ceiling - span);
        return new FrequencyRange(minimum, minimum + span);
    }

    public FrequencyRange Zoom(double factor, double anchor, double ceiling, double minimumSpan = 100)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        if (!double.IsFinite(ceiling) || ceiling <= 0) throw new ArgumentOutOfRangeException(nameof(ceiling));
        if (!double.IsFinite(anchor)) throw new ArgumentOutOfRangeException(nameof(anchor));
        if (!double.IsFinite(minimumSpan) || minimumSpan <= 0) throw new ArgumentOutOfRangeException(nameof(minimumSpan));

        double span = Math.Clamp(Span / factor, Math.Min(minimumSpan, ceiling), ceiling);
        double position = Math.Clamp((anchor - Minimum) / Span, 0, 1);
        double minimum = Math.Clamp(anchor - position * span, 0, ceiling - span);
        return new FrequencyRange(minimum, minimum + span);
    }
}
