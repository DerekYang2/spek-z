using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace Spek.Net.WinUI;

public sealed partial class SpekView
{
    public static readonly DependencyProperty ReferenceLinesProperty =
        Register(nameof(ReferenceLines), typeof(ReferenceLines), ReferenceLines.None, Redraw);
    public static readonly DependencyProperty ShowCutoffMarkerProperty =
        Register(nameof(ShowCutoffMarker), typeof(bool), false, InspectionOptionChanged);
    public static readonly DependencyProperty ShowSpectrumProperty =
        Register(nameof(ShowSpectrum), typeof(bool), false, InspectionOptionChanged);
    public static readonly DependencyProperty MinimumFrequencyProperty =
        Register(nameof(MinimumFrequency), typeof(double), 0.0, ViewportChanged);
    public static readonly DependencyProperty MaximumFrequencyProperty =
        Register(nameof(MaximumFrequency), typeof(double), 0.0, ViewportChanged);
    public static readonly DependencyProperty MinimumTimeProperty =
        Register(nameof(MinimumTime), typeof(double), 0.0, ViewportChanged);
    public static readonly DependencyProperty MaximumTimeProperty =
        Register(nameof(MaximumTime), typeof(double), 0.0, ViewportChanged);
    public static readonly DependencyProperty ReadoutTextProperty =
        Register(nameof(ReadoutText), typeof(string), "", null);
    public static readonly DependencyProperty ReadoutTimeTextProperty =
        Register(nameof(ReadoutTimeText), typeof(string), "", null);
    public static readonly DependencyProperty ReadoutFrequencyTextProperty =
        Register(nameof(ReadoutFrequencyText), typeof(string), "", null);
    public static readonly DependencyProperty ReadoutLevelTextProperty =
        Register(nameof(ReadoutLevelText), typeof(string), "", null);

    private CancellationTokenSource? profileRun;
    private int profileGeneration;
    private double selectionStart;
    private double selectionEnd = 1;
    private double? dragAnchor;
    private double dragEnd;
    private double? keyboardSelectionAnchor;
    private uint dragPointer;
    private Point? cursor;
    private bool settingViewport;
    private (Point Root, double Time, double Frequency)? pendingWheelAnchor;

    public ReferenceLines ReferenceLines { get => (ReferenceLines)GetValue(ReferenceLinesProperty); set => SetValue(ReferenceLinesProperty, value); }
    public bool ShowCutoffMarker { get => (bool)GetValue(ShowCutoffMarkerProperty); set => SetValue(ShowCutoffMarkerProperty, value); }
    public bool ShowSpectrum { get => (bool)GetValue(ShowSpectrumProperty); set => SetValue(ShowSpectrumProperty, value); }
    public double MinimumFrequency { get => (double)GetValue(MinimumFrequencyProperty); set => SetValue(MinimumFrequencyProperty, value); }

    // Zero selects the stream's Nyquist frequency automatically.
    public double MaximumFrequency { get => (double)GetValue(MaximumFrequencyProperty); set => SetValue(MaximumFrequencyProperty, value); }
    public double MinimumTime { get => (double)GetValue(MinimumTimeProperty); set => SetValue(MinimumTimeProperty, value); }
    // Zero selects the end of the file automatically. Time is measured in seconds.
    public double MaximumTime { get => (double)GetValue(MaximumTimeProperty); set => SetValue(MaximumTimeProperty, value); }
    public string ReadoutText
    {
        get => (string)GetValue(ReadoutTextProperty);
        private set
        {
            if (string.IsNullOrEmpty(value))
            {
                ReadoutTimeText = "";
                ReadoutFrequencyText = "";
                ReadoutLevelText = "";
            }
            SetValue(ReadoutTextProperty, value);
        }
    }
    public string ReadoutTimeText { get => (string)GetValue(ReadoutTimeTextProperty); private set => SetValue(ReadoutTimeTextProperty, value); }
    public string ReadoutFrequencyText { get => (string)GetValue(ReadoutFrequencyTextProperty); private set => SetValue(ReadoutFrequencyTextProperty, value); }
    public string ReadoutLevelText { get => (string)GetValue(ReadoutLevelTextProperty); private set => SetValue(ReadoutLevelTextProperty, value); }

    public SpectrumProfile? SpectrumProfile { get; private set; }
    public SpectralDrop? MeasuredDrop { get; private set; }
    public double SelectionStartSeconds => selectionStart * (info?.Duration ?? 0);
    public double SelectionEndSeconds => selectionEnd * (info?.Duration ?? 0);
    public bool HasSelection => selectionStart > 0 || selectionEnd < 1;
    public double DurationSeconds => Math.Max(0, info?.Duration ?? 0);
    public double NyquistFrequency => Math.Max(0, (info?.GetStream(Stream)?.SampleRate ?? 0) / 2.0);
    public double TimeZoomFactor => DurationSeconds > 0 ? DurationSeconds / VisibleTimeRange.Span : 1;
    public double FrequencyZoomFactor => info?.GetStream(Stream) is { SampleRate: > 0 } stream
        ? stream.SampleRate / 2.0 / VisibleFrequencyRange.Span : 1;

    public FrequencyRange VisibleFrequencyRange
    {
        get
        {
            double ceiling = Math.Max(1, (info?.GetStream(Stream)?.SampleRate ?? 0) / 2.0);
            double max = MaximumFrequency;
            if (!double.IsFinite(max) || max <= 0) max = ceiling;
            max = Math.Clamp(max, Math.Min(100, ceiling), ceiling);
            double min = MinimumFrequency;
            if (!double.IsFinite(min)) min = 0;
            min = Math.Clamp(min, 0, max - Math.Min(100, ceiling));
            return new FrequencyRange(min, max);
        }
    }

    public TimeRange VisibleTimeRange
    {
        get
        {
            double duration = Math.Max(0.001, info?.Duration ?? 0);
            double end = MaximumTime;
            if (!double.IsFinite(end) || end <= 0) end = duration;
            double minimumSpan = Math.Min(0.01, duration);
            end = Math.Clamp(end, minimumSpan, duration);
            double start = double.IsFinite(MinimumTime) ? MinimumTime : 0;
            return new TimeRange(Math.Clamp(start, 0, end - minimumSpan), end);
        }
    }

    // Fires for a new profile, time selection or zoom. Hover text uses a dependency property separately.
    public event EventHandler? InspectionChanged;

    public void ResetFrequencyZoom()
    {
        SetFrequencyRange(0, 0);
    }

    public void ResetZoom()
    {
        pendingWheelAnchor = null;
        SetViewport(0, 0, 0, 0);
    }

    public void ZoomToTimeRange(double start, double end)
    {
        pendingWheelAnchor = null;
        var range = new TimeRange(start, end);
        double duration = info?.Duration ?? 0;
        if (duration <= 0 || range.End > duration || range.Span < Math.Min(0.01, duration))
            throw new ArgumentOutOfRangeException(nameof(end), "The interval must fit the file and span at least 0.01 seconds.");
        SetViewport(MinimumFrequency, MaximumFrequency, range.Start, range.End);
    }

    public void ZoomToFrequencyRange(double minimum, double maximum)
    {
        pendingWheelAnchor = null;
        var range = new FrequencyRange(minimum, maximum);
        double ceiling = (info?.GetStream(Stream)?.SampleRate ?? 0) / 2.0;
        if (ceiling <= 0 || range.Maximum > ceiling || range.Span < Math.Min(100, ceiling))
            throw new ArgumentOutOfRangeException(nameof(maximum), "The interval must fit the stream and span at least 100 Hz.");
        SetFrequencyRange(range.Minimum, range.Maximum);
    }

    public void PanTo(double timeSeconds, double frequencyHz)
    {
        pendingWheelAnchor = null;
        if (!double.IsFinite(timeSeconds)) throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        if (!double.IsFinite(frequencyHz)) throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        if (DurationSeconds <= 0 || NyquistFrequency <= 0) return;
        var time = VisibleTimeRange.PanTo(timeSeconds, DurationSeconds);
        var frequency = VisibleFrequencyRange.PanTo(frequencyHz, NyquistFrequency);
        if (time == VisibleTimeRange && frequency == VisibleFrequencyRange) return;
        SetViewport(frequency.Minimum, frequency.Maximum, time.Start, time.End);
    }

    public void SelectTimeRange(double startSeconds, double endSeconds)
    {
        double duration = info?.Duration ?? 0;
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || duration <= 0
            || startSeconds < 0 || endSeconds <= startSeconds || endSeconds > duration)
            throw new ArgumentOutOfRangeException(nameof(endSeconds), "The time range must fit within the file.");
        SetSelection(startSeconds / duration, endSeconds / duration);
    }

    public void ClearSelection()
    {
        keyboardSelectionAnchor = null;
        SetSelection(0, 1);
    }

    private void InitializeInspection()
    {
        AutomationProperties.SetAutomationId(this, "Spectrogram");
        AutomationProperties.SetName(this, "Spectrogram. Use arrow keys to inspect, Shift and left or right to select time, plus and minus to zoom, R to reset zoom, Escape to clear selection.");
        PointerMoved += OnInspectionPointerMoved;
        PointerExited += (_, _) => { if (dragAnchor is null) { cursor = null; ReadoutText = ""; canvas.Invalidate(); } };
        PointerReleased += OnSelectionReleased;
        PointerCanceled += (_, _) => CancelDrag();
        PointerCaptureLost += (_, _) => CancelDrag();
        PointerWheelChanged += OnZoomWheel;
        LayoutUpdated += RestoreWheelAnchorAfterLayout;
        DoubleTapped += (_, _) => ResetZoom();
    }

    private static void InspectionOptionChanged(SpekView view)
    {
        _ = view.RefreshProfileAsync();
        view.canvas.Invalidate();
    }

    private static void ViewportChanged(SpekView view)
    {
        if (view.settingViewport) return;
        view.UpdateReadout();
        view.canvas.Invalidate();
        view.InspectionChanged?.Invoke(view, EventArgs.Empty);
        view.ScheduleRefinement();
    }

    private void SetFrequencyRange(double minimum, double maximum)
    {
        SetViewport(minimum, maximum, MinimumTime, MaximumTime);
    }

    private void SetViewport(double minimum, double maximum, double start, double end)
    {
        CancelDrag();
        ReleasePointerCaptures();
        keyboardSelectionAnchor = null;
        settingViewport = true;
        MinimumFrequency = minimum;
        MaximumFrequency = maximum;
        MinimumTime = start;
        MaximumTime = end;
        settingViewport = false;
        UpdateReadout();
        canvas.Invalidate();
        InspectionChanged?.Invoke(this, EventArgs.Empty);
        ScheduleRefinement();
    }

    private void SetSelection(double start, double end)
    {
        selectionStart = Math.Clamp(Math.Min(start, end), 0, 1);
        selectionEnd = Math.Clamp(Math.Max(start, end), 0, 1);
        canvas.Invalidate();
        _ = RefreshProfileAsync();
    }

    private void BeginSelection(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var plot = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight));
        if (!point.Properties.IsLeftButtonPressed || !plot.Contains(point.Position) || spectrogram is null) return;
        dragAnchor = FileFractionAt(point.Position.X, plot);
        keyboardSelectionAnchor = null;
        dragEnd = dragAnchor.Value;
        dragPointer = point.PointerId;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnInspectionPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var plot = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight));
        cursor = plot.Contains(point.Position) ? point.Position : null;
        if (dragAnchor is not null && point.PointerId == dragPointer && plot.Width > 0)
            dragEnd = FileFractionAt(point.Position.X, plot);
        UpdateReadout();
        canvas.Invalidate();
    }

    private void OnSelectionReleased(object sender, PointerRoutedEventArgs e)
    {
        if (dragAnchor is not double start || e.GetCurrentPoint(this).PointerId != dragPointer) return;
        double end = dragEnd;
        dragAnchor = null;
        ReleasePointerCapture(e.Pointer);
        double pixelWidth = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight)).Width
            * (info?.Duration ?? 0) / VisibleTimeRange.Span;
        if (Math.Abs(end - start) * pixelWidth >= 3) SetSelection(start, end);
        e.Handled = true;
    }

    private void CancelDrag()
    {
        dragAnchor = null;
        canvas.Invalidate();
    }

    private void OnZoomWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDown(VirtualKey.Control) || spectrogram is null) return;
        var point = e.GetCurrentPoint(this);
        var plot = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight));
        if (!plot.Contains(point.Position)) return;
        double frequency = VisibleFrequencyRange.FrequencyAt((point.Position.Y - plot.Top) / plot.Height);
        double time = VisibleTimeRange.TimeAt((point.Position.X - plot.Left) / plot.Width);
        double factor = Math.Pow(1.25, point.Properties.MouseWheelDelta / 120.0);
        if (XamlRoot?.Content is { } root)
            pendingWheelAnchor = (TransformToVisual(root).TransformPoint(point.Position), time, frequency);
        cursor = point.Position;
        ZoomView(factor, time, frequency);
        e.Handled = true;
    }

    private void RestoreWheelAnchorAfterLayout(object? sender, object args)
    {
        // Navigation chrome can move/shrink the plot after a wheel event. Preserve the data under the
        // root-space pointer rather than letting that layout change move the zoom anchor.
        var anchor = pendingWheelAnchor;
        pendingWheelAnchor = null;
        if (anchor is null || XamlRoot?.Content is not { } root || spectrogram is null) return;
        var point = root.TransformToVisual(this).TransformPoint(anchor.Value.Root);
        var plot = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight));
        if (!plot.Contains(point) || plot.Width <= 0 || plot.Height <= 0) return;
        var time = VisibleTimeRange;
        var frequency = VisibleFrequencyRange;
        double start = Math.Clamp(anchor.Value.Time - (point.X - plot.Left) / plot.Width * time.Span, 0, DurationSeconds - time.Span);
        double maximum = Math.Clamp(anchor.Value.Frequency + (point.Y - plot.Top) / plot.Height * frequency.Span,
            frequency.Span, info!.GetStream(Stream)!.SampleRate / 2.0);
        cursor = point;
        SetViewport(maximum - frequency.Span, maximum, start, start + time.Span);
    }

    private void ZoomView(double factor, double? timeAnchor = null, double? frequencyAnchor = null)
    {
        double ceiling = (info?.GetStream(Stream)?.SampleRate ?? 0) / 2.0;
        double duration = info?.Duration ?? 0;
        if (ceiling <= 0 || duration <= 0 || spectrogram is null) return;
        var current = VisibleFrequencyRange;
        var next = current.Zoom(factor, frequencyAnchor ?? (current.Minimum + current.Maximum) / 2, ceiling);
        var time = VisibleTimeRange;
        // Keep the current image during zoom; refine after it settles. Do not imply detail shorter than one FFT window.
        var nextTime = time.Zoom(factor, timeAnchor ?? (time.Start + time.End) / 2, duration,
            Math.Max(0.01, (1 << FftBits) / (double)info!.GetStream(Stream)!.SampleRate));
        SetViewport(next.Minimum, next.Maximum, nextTime.Start, nextTime.End);
    }

    private bool HandleInspectionKey(KeyRoutedEventArgs e)
    {
        pendingWheelAnchor = null;
        if (IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu) || IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) return false;
        switch (e.Key)
        {
            case VirtualKey.R: ResetZoom(); break;
            case VirtualKey.Escape:
                CancelDrag();
                ReleasePointerCaptures();
                ClearSelection();
                break;
            // Windows.System.VirtualKey leaves the OEM plus and minus keys unnamed.
            case VirtualKey.Add or (VirtualKey)0xBB: ZoomView(1.5); break;
            case VirtualKey.Subtract or (VirtualKey)0xBD: ZoomView(1 / 1.5); break;
            case VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down:
                MoveInspectionCursor(e.Key, IsDown(VirtualKey.Shift));
                break;
            default: return false;
        }
        e.Handled = true;
        return true;
    }

    private void MoveInspectionCursor(VirtualKey key, bool extend)
    {
        if (spectrogram is null) return;
        var plot = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight));
        if (plot.Width <= 0 || plot.Height <= 0) return;
        var old = cursor ?? new Point(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2);
        double x = old.X, y = old.Y;
        var cursorData = ReadoutData(VisibleTimeRange.TimeAt((old.X - plot.Left) / plot.Width), out var dataTime)!;
        double visibleColumns = cursorData.Columns * VisibleTimeRange.Span / dataTime.Span;
        if (key == VirtualKey.Left) x -= Math.Max(1, plot.Width / visibleColumns);
        if (key == VirtualKey.Right) x += Math.Max(1, plot.Width / visibleColumns);
        if (key == VirtualKey.Up) y -= 1;
        if (key == VirtualKey.Down) y += 1;
        cursor = new Point(Math.Clamp(x, plot.Left, plot.Right), Math.Clamp(y, plot.Top, plot.Bottom));
        if (extend && key is VirtualKey.Left or VirtualKey.Right)
        {
            keyboardSelectionAnchor ??= FileFractionAt(old.X, plot);
            SetSelection(keyboardSelectionAnchor.Value, FileFractionAt(cursor.Value.X, plot));
        }
        else keyboardSelectionAnchor = null;
        UpdateReadout();
        canvas.Invalidate();
    }

    private void UpdateReadout()
    {
        if (cursor is not Point point || spectrogram is null) { ReadoutText = ""; return; }
        var plot = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight));
        if (plot.Width <= 0 || plot.Height <= 0) return;
        double fraction = FileFractionAt(point.X, plot);
        double time = fraction * (info?.Duration ?? 0);
        var data = ReadoutData(time, out var dataTime)!;
        double hz = VisibleFrequencyRange.FrequencyAt((point.Y - plot.Top) / plot.Height);
        int x = Math.Clamp((int)(dataTime.FractionAt(time) * data.Columns), 0, data.Columns - 1);
        double nyquist = (info?.GetStream(Stream)?.SampleRate ?? 0) / 2.0;
        int bin = nyquist > 0 ? Math.Clamp((int)Math.Round(hz / nyquist * (data.Bins - 1)), 0, data.Bins - 1) : 0;
        ReadoutTimeText = $"{(int)(time / 60)}:{time % 60:00.000}";
        ReadoutFrequencyText = $"{hz:0} Hz";
        ReadoutLevelText = data.HasColumn(x) ? $"{data.GetValue(x, bin):0.0} dB" : "not analysed yet";
        ReadoutText = $"{ReadoutTimeText}   {ReadoutFrequencyText}   {ReadoutLevelText}";
    }

    private double FileFractionAt(double x, Rect plot) =>
        VisibleTimeRange.TimeAt((x - plot.Left) / plot.Width) / Math.Max(0.001, info?.Duration ?? 0);

    private void CancelProfile()
    {
        profileRun?.Cancel();
        profileRun?.Dispose();
        profileRun = null;
        profileGeneration++;
    }

    private void ResetProfile()
    {
        pendingWheelAnchor = null;
        CancelProfile();
        SpectrumProfile = null;
        MeasuredDrop = null;
        cursor = null;
        ReadoutText = "";
        InspectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshProfileAsync()
    {
        CancelProfile();
        SpectrumProfile = null;
        MeasuredDrop = null;
        InspectionChanged?.Invoke(this, EventArgs.Empty);
        var data = spectrogram;
        int rate = info?.GetStream(Stream)?.SampleRate ?? 0;
        if (!loaded || IsAnalyzing || data is null || rate <= 0 || (!ShowSpectrum && !ShowCutoffMarker)) return;

        int version = profileGeneration;
        var token = (profileRun = new CancellationTokenSource()).Token;
        int first = Math.Clamp((int)(selectionStart * data.Columns), 0, data.Columns - 1);
        int end = Math.Clamp((int)Math.Ceiling(selectionEnd * data.Columns), first + 1, data.Columns);
        try
        {
            var result = await Task.Run(() =>
            {
                var profile = data.GetProfile(first, end, rate, token);
                return (Profile: profile, Drop: profile.FindDrop());
            }, token);
            if (version != profileGeneration || !loaded) return;
            SpectrumProfile = result.Profile;
            MeasuredDrop = result.Drop;
            canvas.Invalidate();
            InspectionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
    }

    private void DrawInspection(CanvasDrawingSession ds, Size size)
    {
        var plot = SpekRenderer.PlotRect(size);
        if (plot.Width <= 0 || plot.Height <= 0) return;
        using var clip = ds.CreateLayer(1, plot);
        double first = dragAnchor ?? selectionStart, end = dragAnchor is null ? selectionEnd : dragEnd;
        if (dragAnchor is not null || HasSelection)
        {
            double duration = info?.Duration ?? 0;
            var time = VisibleTimeRange;
            var selected = new Rect(plot.Left + time.FractionAt(Math.Min(first, end) * duration) * plot.Width, plot.Top,
                Math.Abs(end - first) * duration / time.Span * plot.Width, plot.Height);
            ds.FillRectangle(selected, Color.FromArgb(30, 255, 255, 255));
            ds.DrawRectangle(selected, Colors.White);
        }
        if (ShowSpectrum && cursor is Point point)
        {
            ds.DrawLine((float)plot.Left, (float)point.Y, (float)plot.Right, (float)point.Y, Colors.Black, 3);
            ds.DrawLine((float)plot.Left, (float)point.Y, (float)plot.Right, (float)point.Y, Colors.White);
            ds.DrawLine((float)point.X, (float)plot.Top, (float)point.X, (float)plot.Bottom, Colors.Black, 3);
            ds.DrawLine((float)point.X, (float)plot.Top, (float)point.X, (float)plot.Bottom, Colors.White);
        }
    }
}
