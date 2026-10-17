using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Spek.Net.WinUI;

// A two-axis minimap over the existing whole-file bitmap. It never probes or decodes audio.
public sealed partial class SpectrogramOverview : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(SpekView), typeof(SpectrogramOverview),
        new PropertyMetadata(null, (owner, _) => ((SpectrogramOverview)owner).AttachSource()));

    private readonly CanvasControl canvas;
    private readonly ProgressRing progressRing;
    private readonly Border progressOverlay;
    private long refinementCallback;
    private SpekView? attachedSource;
    private Point? panAnchor;
    private double panStart;
    private double panSpan;
    private double panMaximum;
    private double panFrequencySpan;
    private uint panPointer;
    private double previousValue;
    private double previousMaximum;
    private bool hovered;
    private readonly UISettings uiSettings = new();

    public SpectrogramOverview()
    {
        Width = 200;
        Height = 100;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Visibility = Visibility.Collapsed;
        IsEnabled = false;
        canvas = new CanvasControl { ClearColor = Colors.Transparent, UseSharedDevice = true };
        canvas.Draw += Draw;
        progressRing = new ProgressRing { Width = 20, Height = 20, IsActive = false, IsHitTestVisible = false };
        AutomationProperties.SetAutomationId(progressRing, "ZoomRefinementProgress");
        AutomationProperties.SetName(progressRing, "Refining zoom");
        progressOverlay = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            Child = progressRing,
        };
        var content = new Grid();
        content.Children.Add(canvas);
        content.Children.Add(progressOverlay);
        Content = content;
        AutomationProperties.SetAutomationId(this, "SpectrogramOverview");
        AutomationProperties.SetName(this, "Spectrogram minimap");
        AutomationProperties.SetHelpText(this, "The rectangle shows visible time and frequency. Click to recenter or drag to pan both axes. Left and right pan time; up and down pan frequency. Page Up and Page Down move by a time passage. Home and End go to track boundaries. R resets zoom.");
        Loaded += (_, _) => { AttachSource(); uiSettings.ColorValuesChanged += ThemeColorsChanged; };
        Unloaded += (_, _) => { CancelPan(); DetachSource(); uiSettings.ColorValuesChanged -= ThemeColorsChanged; };
        ActualThemeChanged += (_, _) => canvas.Invalidate();
        SizeChanged += (_, _) => canvas.Invalidate();
        PointerPressed += PanPressed;
        PointerMoved += PanMoved;
        PointerReleased += PanReleased;
        PointerCanceled += (_, _) => CancelPan();
        PointerCaptureLost += (_, _) => CancelPan();
        PointerEntered += (_, _) => { hovered = true; canvas.Invalidate(); };
        PointerExited += (_, _) => { hovered = false; canvas.Invalidate(); };
    }

    public SpekView? Source { get => (SpekView?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    internal double RangeValue => Source?.VisibleTimeRange.Start ?? 0;
    internal double RangeMaximum => Source is { } source ? Math.Max(0, source.DurationSeconds - source.VisibleTimeRange.Span) : 0;
    internal double SmallStep => (Source?.VisibleTimeRange.Span ?? 0) / 10;
    internal double LargeStep => (Source?.VisibleTimeRange.Span ?? 0) * 0.9;

    protected override AutomationPeer OnCreateAutomationPeer() => new SpectrogramOverviewAutomationPeer(this);

    private void AttachSource()
    {
        CancelPan();
        DetachSource();
        attachedSource = Source;
        if (attachedSource is not null)
        {
            attachedSource.InspectionChanged += SourceChanged;
            attachedSource.OverviewInvalidated += SourceChanged;
            attachedSource.Loaded += SourceLoaded;
            attachedSource.Unloaded += SourceLoaded;
            refinementCallback = attachedSource.RegisterPropertyChangedCallback(SpekView.IsRefiningProperty, (_, _) => RefreshProgress());
        }
        Refresh();
    }

    private void DetachSource()
    {
        progressRing.IsActive = false;
        progressOverlay.Visibility = Visibility.Collapsed;
        if (attachedSource is null) return;
        attachedSource.InspectionChanged -= SourceChanged;
        attachedSource.OverviewInvalidated -= SourceChanged;
        attachedSource.Loaded -= SourceLoaded;
        attachedSource.Unloaded -= SourceLoaded;
        attachedSource.UnregisterPropertyChangedCallback(SpekView.IsRefiningProperty, refinementCallback);
        attachedSource = null;
    }

    private void SourceChanged(object? sender, EventArgs args) => Refresh();
    private void SourceLoaded(object sender, RoutedEventArgs args) => Refresh();
    private void ThemeColorsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => canvas.Invalidate());

    private void Refresh()
    {
        double zoom = Source is { } source ? Math.Max(source.TimeZoomFactor, source.FrequencyZoomFactor) : 1;
        bool visible = Source is { HasOverview: true } && Math.Round(zoom * 100, 2) > 100;
        IsEnabled = visible;
        Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RefreshProgress();
        if (!visible || (panAnchor is not null && (Math.Abs(Source!.VisibleTimeRange.Span - panSpan) > 0.000001
            || Math.Abs(Source.VisibleFrequencyRange.Span - panFrequencySpan) > 0.000001))) CancelPan();
        if (FrameworkElementAutomationPeer.FromElement(this) is SpectrogramOverviewAutomationPeer peer)
            peer.RangeChanged(previousValue, previousMaximum);
        previousValue = RangeValue;
        previousMaximum = RangeMaximum;
        if (visible) canvas.Invalidate();
    }

    private void RefreshProgress()
    {
        bool active = Visibility == Visibility.Visible && Source is { HasOverview: true, IsRefining: true };
        progressRing.IsActive = active;
        progressOverlay.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    private Rect TrackRect => new(4, 4, Math.Max(0, ActualWidth - 8), Math.Max(0, ActualHeight - 8));

    private Rect ViewRect(Rect track)
    {
        if (Source is not { DurationSeconds: > 0, NyquistFrequency: > 0 } source || track.Width <= 0) return new Rect();
        var time = source.VisibleTimeRange;
        var frequency = source.VisibleFrequencyRange;
        return new Rect(track.Left + time.Start / source.DurationSeconds * track.Width,
            track.Top + (source.NyquistFrequency - frequency.Maximum) / source.NyquistFrequency * track.Height,
            Math.Min(track.Width, time.Span / source.DurationSeconds * track.Width),
            Math.Min(track.Height, frequency.Span / source.NyquistFrequency * track.Height));
    }

    private static Rect HitRect(Rect view) => new(view.X - Math.Max(0, 8 - view.Width) / 2,
        view.Y - Math.Max(0, 8 - view.Height) / 2, Math.Max(8, view.Width), Math.Max(8, view.Height));

    private void Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var drawing = args.DrawingSession;
        drawing.Clear(Colors.Transparent);
        var track = TrackRect;
        if (track.Width <= 0 || track.Height <= 0 || Source is not { HasOverview: true } source) return;
        drawing.FillRectangle(track, Colors.Black);
        source.DrawOverview(drawing, sender, track);
        var view = ViewRect(track);
        var shade = Color.FromArgb(150, 0, 0, 0);
        drawing.FillRectangle(new Rect(track.Left, track.Top, Math.Max(0, view.Left - track.Left), track.Height), shade);
        drawing.FillRectangle(new Rect(view.Right, track.Top, Math.Max(0, track.Right - view.Right), track.Height), shade);
        drawing.FillRectangle(new Rect(view.Left, track.Top, view.Width, Math.Max(0, view.Top - track.Top)), shade);
        drawing.FillRectangle(new Rect(view.Left, view.Bottom, view.Width, Math.Max(0, track.Bottom - view.Bottom)), shade);
        drawing.DrawRectangle(view, Colors.Black, 4);
        drawing.DrawRectangle(view, Colors.White, panAnchor is not null ? 2.5f : hovered ? 2 : 1.5f);
    }

    private void PanPressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(this);
        var track = TrackRect;
        if (!IsEnabled || Source is null || track.Width <= 0 || track.Height <= 0
            || !point.Properties.IsLeftButtonPressed || !track.Contains(point.Position)) return;
        Focus(FocusState.Pointer);
        panSpan = Source.VisibleTimeRange.Span;
        panFrequencySpan = Source.VisibleFrequencyRange.Span;
        if (!HitRect(ViewRect(track)).Contains(point.Position))
            Source.PanTo((point.Position.X - track.Left) / track.Width * Source.DurationSeconds,
                (1 - (point.Position.Y - track.Top) / track.Height) * Source.NyquistFrequency);
        panStart = Source.VisibleTimeRange.Start;
        panMaximum = Source.VisibleFrequencyRange.Maximum;
        panAnchor = point.Position;
        panPointer = point.PointerId;
        CapturePointer(args.Pointer);
        canvas.Invalidate();
        args.Handled = true;
    }

    private void PanMoved(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(this);
        if (panAnchor is not Point anchor || point.PointerId != panPointer || Source is null || TrackRect.Width <= 0 || TrackRect.Height <= 0) return;
        MoveTo(panStart + (point.Position.X - anchor.X) / TrackRect.Width * Source.DurationSeconds,
            panMaximum - (point.Position.Y - anchor.Y) / TrackRect.Height * Source.NyquistFrequency);
        args.Handled = true;
    }

    private void PanReleased(object sender, PointerRoutedEventArgs args)
    {
        if (panAnchor is null || args.GetCurrentPoint(this).PointerId != panPointer) return;
        PanMoved(sender, args);
        CancelPan();
        args.Handled = true;
    }

    private void CancelPan()
    {
        panAnchor = null;
        ReleasePointerCaptures();
        canvas.Invalidate();
    }

    internal void MoveToStart(double start)
    {
        MoveTo(start, Source?.VisibleFrequencyRange.Maximum ?? 0);
    }

    internal void MoveTo(double start, double maximum)
    {
        if (!IsEnabled || Source is not { DurationSeconds: > 0, NyquistFrequency: > 0 } source) return;
        source.PanTo(start + source.VisibleTimeRange.Span / 2, maximum - source.VisibleFrequencyRange.Span / 2);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs args)
    {
        double start = RangeValue;
        double maximum = Source?.VisibleFrequencyRange.Maximum ?? 0;
        switch (args.Key)
        {
            case VirtualKey.Escape: Source?.ClearSelection(); args.Handled = true; return;
            case VirtualKey.R: Source?.ResetZoom(); Source?.Focus(FocusState.Keyboard); args.Handled = true; return;
            case VirtualKey.Left: start -= SmallStep; break;
            case VirtualKey.Right: start += SmallStep; break;
            case VirtualKey.Up: maximum += (Source?.VisibleFrequencyRange.Span ?? 0) / 10; break;
            case VirtualKey.Down: maximum -= (Source?.VisibleFrequencyRange.Span ?? 0) / 10; break;
            case VirtualKey.PageUp: start -= LargeStep; break;
            case VirtualKey.PageDown: start += LargeStep; break;
            case VirtualKey.Home: start = 0; break;
            case VirtualKey.End: start = RangeMaximum; break;
            default: base.OnKeyDown(args); return;
        }
        MoveTo(start, maximum);
        args.Handled = true;
    }
}
