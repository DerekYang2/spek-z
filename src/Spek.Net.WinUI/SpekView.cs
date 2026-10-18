using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Spek.Net.WinUI;

// Shows Spek's spectrogram of FilePath, drawing columns as they're analysed. Changing the file, stream, channel, FFT
// size, window function or width analyses again. Changing the palette or dB range recolours what's there. When
// focused, the view takes Spek's keys: c, f, l, p, s, u and w step forward, and with Shift they step back.
public sealed partial class SpekView : UserControl
{
    public static readonly DependencyProperty FilePathProperty = Register(nameof(FilePath), typeof(string), null, Reload);
    public static readonly DependencyProperty FfmpegPathProperty = Register(nameof(FfmpegPath), typeof(string), null, Reload);
    public static readonly DependencyProperty StreamProperty = Register(nameof(Stream), typeof(int), 0, Reanalyse);
    public static readonly DependencyProperty ChannelProperty = Register(nameof(Channel), typeof(int), 0, Reanalyse);
    public static readonly DependencyProperty FftBitsProperty = Register(nameof(FftBits), typeof(int), SpekDefaults.FftBits, Reanalyse);
    public static readonly DependencyProperty WindowFunctionProperty = Register(nameof(WindowFunction), typeof(WindowFunction), WindowFunction.Hann, Reanalyse);
    public static readonly DependencyProperty PaletteProperty = Register(nameof(Palette), typeof(Palette), Palettes.Default, Recolour);
    public static readonly DependencyProperty LowerDbProperty = Register(nameof(LowerDb), typeof(int), SpekDefaults.LowerDb, Recolour);
    public static readonly DependencyProperty UpperDbProperty = Register(nameof(UpperDb), typeof(int), SpekDefaults.UpperDb, Recolour);
    public static readonly DependencyProperty ShowFullPathProperty = Register(nameof(ShowFullPath), typeof(bool), true, Redraw);
    public static readonly DependencyProperty ShowDetailedDescriptionProperty = Register(nameof(ShowDetailedDescription), typeof(bool), false, Redescribe);
    public static readonly DependencyProperty BrandNameProperty = Register(nameof(BrandName), typeof(string), null, Redraw);
    public static readonly DependencyProperty BrandVersionProperty = Register(nameof(BrandVersion), typeof(string), null, Redraw);
    public static readonly DependencyProperty InterpolationProperty =
        Register(nameof(Interpolation), typeof(CanvasImageInterpolation), CanvasImageInterpolation.HighQualityCubic, Redraw);
    public static readonly DependencyProperty StreamCountProperty = Register(nameof(StreamCount), typeof(int), 0, null);
    public static readonly DependencyProperty ChannelCountProperty = Register(nameof(ChannelCount), typeof(int), 0, null);
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description), typeof(string), "", null);
    public static readonly DependencyProperty IsAnalyzingProperty = Register(nameof(IsAnalyzing), typeof(bool), false, null);

    private readonly CanvasControl canvas;
    private readonly DispatcherQueueTimer frameTimer;
    private readonly DispatcherQueueTimer resizeTimer;

    private CancellationTokenSource? run;
    private int generation;
    private AudioFileInfo? info;
    private AnalysisSettings? settings;
    private AudioError runError;
    private Spectrogram? spectrogram;
    private CanvasBitmap? image;
    private CanvasBitmap? paletteImage;
    private Palette paletteImageFor;
    private int analysedColumns;
    private bool loaded;

    public SpekView()
    {
        IsTabStop = true;
        canvas = new CanvasControl { ClearColor = Microsoft.UI.Colors.Black, UseSharedDevice = true };
        canvas.Draw += OnDraw;
        canvas.CreateResources += OnCreateResources;
        Content = canvas;

        var queue = DispatcherQueue.GetForCurrentThread();
        frameTimer = queue.CreateTimer();
        frameTimer.Interval = TimeSpan.FromMilliseconds(16);
        frameTimer.Tick += OnFrame;
        resizeTimer = queue.CreateTimer();
        resizeTimer.Interval = TimeSpan.FromMilliseconds(200);
        resizeTimer.IsRepeating = false;
        resizeTimer.Tick += (_, _) => StartIfWidthChanged();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        InitializeInspection();
    }

    public event EventHandler? AnalysisCompleted;
    internal event EventHandler? OverviewInvalidated;

    public string? FilePath { get => (string?)GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }

    // ffmpeg.exe to use. When empty, the view looks for ffmpeg on PATH.
    public string? FfmpegPath { get => (string?)GetValue(FfmpegPathProperty); set => SetValue(FfmpegPathProperty, value); }

    public int Stream { get => (int)GetValue(StreamProperty); set => SetValue(StreamProperty, value); }

    public int Channel { get => (int)GetValue(ChannelProperty); set => SetValue(ChannelProperty, value); }

    public int FftBits { get => (int)GetValue(FftBitsProperty); set => SetValue(FftBitsProperty, value); }

    public WindowFunction WindowFunction { get => (WindowFunction)GetValue(WindowFunctionProperty); set => SetValue(WindowFunctionProperty, value); }

    public Palette Palette { get => (Palette)GetValue(PaletteProperty); set => SetValue(PaletteProperty, value); }

    public int LowerDb { get => (int)GetValue(LowerDbProperty); set => SetValue(LowerDbProperty, value); }

    public int UpperDb { get => (int)GetValue(UpperDbProperty); set => SetValue(UpperDbProperty, value); }

    public bool ShowFullPath { get => (bool)GetValue(ShowFullPathProperty); set => SetValue(ShowFullPathProperty, value); }

    // Adds the FFT size and window function to the description, as Spek's "detailed description" preference does.
    public bool ShowDetailedDescription { get => (bool)GetValue(ShowDetailedDescriptionProperty); set => SetValue(ShowDetailedDescriptionProperty, value); }

    // Drawn at the top right, where Spek shows its name and version. Nothing is drawn when empty.
    public string? BrandName { get => (string?)GetValue(BrandNameProperty); set => SetValue(BrandNameProperty, value); }

    public string? BrandVersion { get => (string?)GetValue(BrandVersionProperty); set => SetValue(BrandVersionProperty, value); }

    // How the FFT bins scale to the plot's height. Spek uses nearest neighbour.
    public CanvasImageInterpolation Interpolation { get => (CanvasImageInterpolation)GetValue(InterpolationProperty); set => SetValue(InterpolationProperty, value); }

    public int StreamCount { get => (int)GetValue(StreamCountProperty); private set => SetValue(StreamCountProperty, value); }

    public int ChannelCount { get => (int)GetValue(ChannelCountProperty); private set => SetValue(ChannelCountProperty, value); }

    public string Description { get => (string)GetValue(DescriptionProperty); private set => SetValue(DescriptionProperty, value); }

    public bool IsAnalyzing { get => (bool)GetValue(IsAnalyzingProperty); private set => SetValue(IsAnalyzingProperty, value); }

    // Saves what the view shows as a PNG, at the view's size in physical pixels.
    public async Task SaveAsync(string path)
    {
        var size = new Size(ActualWidth, ActualHeight);
        float dpi = canvas.Dpi;
        var device = canvas.Device ?? CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, (float)size.Width, (float)size.Height, dpi);
        using (var ds = target.CreateDrawingSession())
        {
            using var palette = SpekRenderer.CreatePaletteBitmap(device, Palette);
            using var bitmap = CopyBitmap(device, spectrogram);
            using var detail = HasTimeZoom ? CopyBitmap(device, refinement) : null;
            SpekRenderer.Draw(ds, size, CreateScene(), bitmap, VisibleColumns(), palette, Interpolation, detail, refinementRange, refinement?.ColumnsDone ?? 0);
        }
        await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
    }

    private static CanvasBitmap? CopyBitmap(ICanvasResourceCreator creator, Spectrogram? data)
    {
        if (data is null) return null;
        var bitmap = SpekRenderer.CreateSpectrogramBitmap(creator, data);
        bitmap.SetPixelBytes(data.CopyPixels());
        return bitmap;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (HandleInspectionKey(e)) return;
        if (IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu) || IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            base.OnKeyDown(e);
            return;
        }

        bool back = IsDown(VirtualKey.Shift);
        int step = back ? -1 : 1;
        switch (e.Key)
        {
            case VirtualKey.C when ChannelCount > 0:
                Channel = Wrap(Channel + step, ChannelCount);
                break;
            case VirtualKey.F:
                WindowFunction = (WindowFunction)Wrap((int)WindowFunction + step, Enum.GetValues<WindowFunction>().Length);
                break;
            case VirtualKey.L:
                LowerDb = back ? Math.Max(LowerDb - 1, SpekDefaults.MinDb) : Math.Min(LowerDb + 1, UpperDb - 1);
                break;
            case VirtualKey.P:
                Palette = (Palette)Wrap((int)Palette + step, Enum.GetValues<Palette>().Length);
                break;
            case VirtualKey.S when StreamCount > 1:
                Stream = Wrap(Stream + step, StreamCount);
                break;
            case VirtualKey.U:
                UpperDb = back ? Math.Max(UpperDb - 1, LowerDb + 1) : Math.Min(UpperDb + 1, SpekDefaults.MaxDb);
                break;
            case VirtualKey.W:
                FftBits = back ? Math.Max(FftBits - 1, SpekDefaults.MinFftBits) : Math.Min(FftBits + 1, SpekDefaults.MaxFftBits);
                break;
            default:
                base.OnKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        BeginSelection(e);
        base.OnPointerPressed(e);
    }

    private static DependencyProperty Register(string name, Type type, object? defaultValue, Action<SpekView>? changed) =>
        DependencyProperty.Register(name, type, typeof(SpekView), new PropertyMetadata(defaultValue,
            changed is null ? null : (d, _) => changed((SpekView)d)));

    private static void Reload(SpekView view)
    {
        view.ClearSelection();
        view.ResetZoom();
        view.info = null;
        view.Stream = 0;
        view.Channel = 0;
        view.Start();
    }

    private static void Reanalyse(SpekView view) => view.Start();

    private static void Recolour(SpekView view)
    {
        if (view.LowerDb >= view.UpperDb) return;
        view.spectrogram?.SetRangeAndPalette(view.Palette, view.LowerDb, view.UpperDb);
        view.refinement?.SetRangeAndPalette(view.Palette, view.LowerDb, view.UpperDb);
        view.canvas.Invalidate();
        view.OverviewInvalidated?.Invoke(view, EventArgs.Empty);
    }

    private static void Redescribe(SpekView view)
    {
        view.UpdateDescription();
        view.canvas.Invalidate();
    }

    private static void Redraw(SpekView view) => view.canvas.Invalidate();

    private static int Wrap(int value, int count) => ((value % count) + count) % count;

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        loaded = true;
        if (XamlRoot is not null) XamlRoot.Changed += OnXamlRootChanged;
        Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        loaded = false;
        if (XamlRoot is not null) XamlRoot.Changed -= OnXamlRootChanged;
        Cancel();
        frameTimer.Stop();
        resizeTimer.Stop();
        CancelProfile();
        CancelRefinement(clear: true);
        OverviewInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => resizeTimer.Start();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        canvas.Invalidate();
        if (spectrogram is null) StartIfWidthChanged();
        else resizeTimer.Start();
    }

    // The analysis makes one column per physical pixel of the plot's width.
    private int PlotColumns()
    {
        double scale = XamlRoot?.RasterizationScale ?? 1;
        return (int)Math.Floor(SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight)).Width * scale);
    }

    private void StartIfWidthChanged()
    {
        if (PlotColumns() != analysedColumns) Start();
    }

    private void Cancel()
    {
        run?.Cancel();
        run?.Dispose();
        run = null;
        generation++;
    }

    private async void Start()
    {
        if (!loaded) return;
        Cancel();
        CancelRefinement(clear: true);
        int current = generation;
        var token = (run = new CancellationTokenSource()).Token;

        spectrogram = null;
        OverviewInvalidated?.Invoke(this, EventArgs.Empty);
        image?.Dispose();
        image = null;
        ResetProfile();
        runError = AudioError.None;
        analysedColumns = PlotColumns();
        IsAnalyzing = false;

        var path = FilePath;
        if (string.IsNullOrEmpty(path))
        {
            info = null;
            UpdateDescription();
            canvas.Invalidate();
            return;
        }

        var ffmpegPath = string.IsNullOrEmpty(FfmpegPath) ? Ffmpeg.Find([]) : FfmpegPath;
        if (ffmpegPath is null)
        {
            info = AudioFileInfo.Failed(path, AudioError.FfmpegNotFound);
            UpdateDescription();
            canvas.Invalidate();
            return;
        }
        var ffmpeg = new Ffmpeg(ffmpegPath);

        try
        {
            if (info is null || info.Path != path)
            {
                var probed = await ffmpeg.ProbeAsync(path, token);
                if (current != generation) return;
                info = probed;
            }

            StreamCount = info.Streams.Count;
            if (Stream >= StreamCount && StreamCount > 0)
            {
                Stream = 0;
                return;
            }
            ChannelCount = info.GetStream(Stream)?.Channels ?? 0;
            if (Channel >= ChannelCount && ChannelCount > 0)
            {
                Channel = 0;
                return;
            }

            settings = new AnalysisSettings(Stream, Channel, FftBits, WindowFunction);
            UpdateDescription();
            if (info.ErrorFor(Stream) != AudioError.None || analysedColumns <= 0)
            {
                canvas.Invalidate();
                return;
            }

            spectrogram = new Spectrogram(analysedColumns, SpekDefaults.BinsFor(FftBits), Palette, LowerDb, UpperDb);
            IsAnalyzing = true;
            frameTimer.Start();
            await SpekAnalyzer.AnalyzeAsync(ffmpeg, info, settings, spectrogram, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is FfmpegException or System.ComponentModel.Win32Exception or IOException or InvalidOperationException or ArgumentException)
        {
            if (current != generation) return;
            runError = AudioError.DecodingFailed;
            UpdateDescription();
        }

        if (current != generation) return;
        IsAnalyzing = false;
        canvas.Invalidate();
        OverviewInvalidated?.Invoke(this, EventArgs.Empty);
        _ = RefreshProfileAsync();
        ScheduleRefinement();
        AnalysisCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDescription()
    {
        if (info is null)
        {
            Description = "";
            return;
        }
        Description = SpekDescription.Describe(info, settings ?? new AnalysisSettings(Stream, Channel, FftBits, WindowFunction),
            ShowDetailedDescription, runError);
    }

    private int VisibleColumns()
    {
        return spectrogram?.ColumnsDone ?? 0;
    }

    private void OnFrame(DispatcherQueueTimer sender, object args)
    {
        canvas.Invalidate();
        OverviewInvalidated?.Invoke(this, EventArgs.Empty);
        if (!IsAnalyzing) frameTimer.Stop();
    }

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        image?.Dispose();
        paletteImage?.Dispose();
        refinementImage?.Dispose();
        image = null;
        paletteImage = null;
        refinementImage = null;
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (paletteImage is null || paletteImageFor != Palette)
        {
            paletteImage?.Dispose();
            paletteImage = SpekRenderer.CreatePaletteBitmap(sender, Palette);
            paletteImageFor = Palette;
        }

        var current = spectrogram;
        UpdateOverviewBitmap(sender);

        // While a resize is pending, the previous image stretches to the new plot.
        if (HasTimeZoom && refinement is { } detail)
        {
            if (refinementImage is null)
            {
                refinementImage = SpekRenderer.CreateSpectrogramBitmap(sender, detail);
                detail.MarkAllDirty();
            }
            SpekRenderer.Upload(detail, refinementImage);
        }
        SpekRenderer.Draw(args.DrawingSession, sender.Size, CreateScene(), current is null ? null : image, VisibleColumns(), paletteImage,
            Interpolation, HasTimeZoom ? refinementImage : null, refinementRange, refinement?.ColumnsDone ?? 0);
        DrawInspection(args.DrawingSession, sender.Size);
    }

    private void UpdateOverviewBitmap(ICanvasResourceCreator creator)
    {
        if (spectrogram is not { } data) return;
        if (image is null || image.SizeInPixels.Width != data.Columns || image.SizeInPixels.Height != data.Bins)
        {
            image?.Dispose();
            image = SpekRenderer.CreateSpectrogramBitmap(creator, data);
            data.MarkAllDirty();
        }
        SpekRenderer.Upload(data, image);
    }

    internal bool HasOverview => loaded && spectrogram is not null && DurationSeconds > 0;

    internal void DrawOverview(CanvasDrawingSession drawing, ICanvasResourceCreator creator, Rect bounds)
    {
        if (!HasOverview || bounds.Width <= 0 || bounds.Height <= 0) return;
        UpdateOverviewBitmap(creator);
        if (image is null || spectrogram is null || spectrogram.ColumnsDone == 0) return;
        int columns = Math.Min(spectrogram.ColumnsDone, spectrogram.Columns);
        drawing.DrawImage(image, new Rect(bounds.X, bounds.Y, bounds.Width * columns / spectrogram.Columns, bounds.Height),
            new Rect(0, 0, columns, spectrogram.Bins), 1, CanvasImageInterpolation.Linear);
    }

    private SpekScene CreateScene()
    {
        var path = FilePath;
        string? title = string.IsNullOrEmpty(path) ? null : ShowFullPath ? path : System.IO.Path.GetFileName(path);
        var stream = info?.GetStream(Stream);
        return new SpekScene(title, Description, info?.Duration ?? 0, stream?.SampleRate ?? 0, LowerDb, UpperDb, BrandName, BrandVersion,
            VisibleFrequencyRange, ReferenceLines, ShowCutoffMarker ? MeasuredDrop : null, VisibleTimeRange);
    }
}
