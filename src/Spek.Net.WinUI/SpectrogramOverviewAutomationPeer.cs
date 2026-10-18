using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace Spek.Net.WinUI;

internal sealed partial class SpectrogramOverviewAutomationPeer(SpectrogramOverview owner)
    : FrameworkElementAutomationPeer(owner), IRangeValueProvider, IScrollProvider
{
    private double[]? previousScroll;
    protected override string GetClassNameCore() => nameof(SpectrogramOverview);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
    protected override object GetPatternCore(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.RangeValue or PatternInterface.Scroll => this,
        _ => base.GetPatternCore(patternInterface),
    };

    public bool IsReadOnly => !owner.IsEnabled;
    public double Minimum => 0;
    public double Maximum => owner.RangeMaximum;
    public double SmallChange => owner.SmallStep;
    public double LargeChange => owner.LargeStep;
    public double Value => owner.RangeValue;
    public bool HorizontallyScrollable => owner.Source is { TimeZoomFactor: > 1.000001 };
    public bool VerticallyScrollable => owner.Source is { FrequencyZoomFactor: > 1.000001 };
    public double HorizontalViewSize => 100 / (owner.Source?.TimeZoomFactor ?? 1);
    public double VerticalViewSize => 100 / (owner.Source?.FrequencyZoomFactor ?? 1);
    public double HorizontalScrollPercent => HorizontallyScrollable ? Value / Maximum * 100 : -1;
    public double VerticalScrollPercent => VerticallyScrollable && owner.Source is { } source
        ? (source.NyquistFrequency - source.VisibleFrequencyRange.Maximum) / (source.NyquistFrequency - source.VisibleFrequencyRange.Span) * 100 : -1;

    public void SetScrollPercent(double horizontalPercent, double verticalPercent)
    {
        if (!owner.IsEnabled) throw new ElementNotEnabledException();
        Validate(horizontalPercent, HorizontallyScrollable);
        Validate(verticalPercent, VerticallyScrollable);
        if (owner.Source is not { } source) return;
        double start = horizontalPercent == -1 ? Value : horizontalPercent / 100 * Maximum;
        double maximum = verticalPercent == -1 ? source.VisibleFrequencyRange.Maximum
            : source.NyquistFrequency - verticalPercent / 100 * (source.NyquistFrequency - source.VisibleFrequencyRange.Span);
        owner.MoveTo(start, maximum);

        static void Validate(double percent, bool scrollable)
        {
            if (percent == -1) return;
            if (!double.IsFinite(percent) || percent < 0 || percent > 100) throw new ArgumentOutOfRangeException(nameof(percent));
            if (!scrollable) throw new InvalidOperationException("This axis is not zoomed.");
        }
    }

    public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
    {
        if (!owner.IsEnabled) throw new ElementNotEnabledException();
        if (owner.Source is not { } source) return;
        double x = Step(horizontalAmount, source.VisibleTimeRange.Span, HorizontallyScrollable);
        double y = Step(verticalAmount, source.VisibleFrequencyRange.Span, VerticallyScrollable);
        owner.MoveTo(Value + x, source.VisibleFrequencyRange.Maximum - y);

        static double Step(ScrollAmount amount, double span, bool scrollable)
        {
            if (amount != ScrollAmount.NoAmount && !scrollable) throw new InvalidOperationException("This axis is not zoomed.");
            return amount switch
            {
                ScrollAmount.NoAmount => 0,
                ScrollAmount.SmallDecrement => -span / 10,
                ScrollAmount.SmallIncrement => span / 10,
                ScrollAmount.LargeDecrement => -span * 0.9,
                ScrollAmount.LargeIncrement => span * 0.9,
                _ => throw new ArgumentOutOfRangeException(nameof(amount)),
            };
        }
    }

    public void SetValue(double value)
    {
        if (!owner.IsEnabled) throw new ElementNotEnabledException();
        if (!double.IsFinite(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
        owner.MoveToStart(value);
    }

    internal void RangeChanged(double oldValue, double oldMaximum)
    {
        if (oldValue != Value) RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, oldValue, Value);
        if (oldMaximum != Maximum) RaisePropertyChangedEvent(RangeValuePatternIdentifiers.MaximumProperty, oldMaximum, Maximum);
        double[] current = [HorizontalScrollPercent, VerticalScrollPercent, HorizontalViewSize, VerticalViewSize];
        if (previousScroll is { } old)
        {
            AutomationProperty[] properties = [ScrollPatternIdentifiers.HorizontalScrollPercentProperty,
                ScrollPatternIdentifiers.VerticalScrollPercentProperty, ScrollPatternIdentifiers.HorizontalViewSizeProperty,
                ScrollPatternIdentifiers.VerticalViewSizeProperty];
            for (int i = 0; i < current.Length; i++)
                if (old[i] != current[i]) RaisePropertyChangedEvent(properties[i], old[i], current[i]);
            if ((old[2] < 100) != HorizontallyScrollable)
                RaisePropertyChangedEvent(ScrollPatternIdentifiers.HorizontallyScrollableProperty, old[2] < 100, HorizontallyScrollable);
            if ((old[3] < 100) != VerticallyScrollable)
                RaisePropertyChangedEvent(ScrollPatternIdentifiers.VerticallyScrollableProperty, old[3] < 100, VerticallyScrollable);
        }
        previousScroll = current;
    }
}
