using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;

namespace Spek.Net.WinUI;

public sealed partial class SpekView
{
    private const double DetailedTimeZoomThreshold = 4;
    public static readonly DependencyProperty IsRefiningProperty =
        Register(nameof(IsRefining), typeof(bool), false, null);
    public static readonly DependencyProperty RefinementStatusProperty =
        Register(nameof(RefinementStatus), typeof(string), "", null);

    private CancellationTokenSource? refinementRun;
    private int refinementGeneration;
    private Spectrogram? refinement;
    private TimeRange? refinementRange;
    private AnalysisQuality? refinementQuality;
    private CanvasBitmap? refinementImage;

    public bool IsRefining { get => (bool)GetValue(IsRefiningProperty); private set => SetValue(IsRefiningProperty, value); }
    public string RefinementStatus { get => (string)GetValue(RefinementStatusProperty); private set => SetValue(RefinementStatusProperty, value); }

    private bool HasTimeZoom => info is { Duration: > 0 } && VisibleTimeRange.Span < info.Duration - 0.000001;

    private void CancelRefinement(bool clear = false)
    {
        refinementGeneration++;
        refinementRun?.Cancel();
        refinementRun?.Dispose();
        refinementRun = null;
        IsRefining = false;
        RefinementStatus = "";
        if (!clear) return;
        refinement = null;
        refinementRange = null;
        refinementQuality = null;
        refinementImage?.Dispose();
        refinementImage = null;
    }

    private void ScheduleRefinement()
    {
        CancelRefinement();
        if (!loaded || IsAnalyzing || !HasTimeZoom || spectrogram is null || settings is null || info is null
            || runError != AudioError.None || info.ErrorFor(Stream) != AudioError.None) return;
        var time = VisibleTimeRange;
        var quality = time.Span <= info.Duration / DetailedTimeZoomThreshold ? AnalysisQuality.Detailed : AnalysisQuality.Fast;
        // Bound the additional value/pixel buffers to 32 MiB, even with a large FFT or high-DPI window.
        int columns = Math.Min(PlotColumns(), Math.Max(1, 32 * 1024 * 1024 / (spectrogram.Bins * 8)));
        if (columns <= 0) return;
        if (refinement is not null && refinementRange is { } cached && cached.Start <= time.Start && cached.End >= time.End
            && (quality == AnalysisQuality.Fast || refinementQuality == AnalysisQuality.Detailed)
            && refinement.Columns * time.Span / cached.Span >= columns * 0.95)
        {
            RefinementStatus = "Zoom refined.";
            return;
        }
        // Plenty of fast overview pixels are not a substitute for the detailed algorithm.
        if (quality == AnalysisQuality.Fast && spectrogram.Columns * time.Span / info.Duration >= columns * 0.95) return;
        var ffmpegPath = string.IsNullOrEmpty(FfmpegPath) ? Ffmpeg.Find([]) : FfmpegPath;
        if (ffmpegPath is null) return;
        int version = refinementGeneration;
        var token = (refinementRun = new CancellationTokenSource()).Token;
        IsRefining = true;
        RefinementStatus = "Refining zoom...";
        _ = RefineAsync(new Ffmpeg(ffmpegPath), info, settings with { Quality = quality }, time, columns, version, token);
    }

    private async Task RefineAsync(Ffmpeg ffmpeg, AudioFileInfo file, AnalysisSettings analysis, TimeRange time,
        int columns, int version, CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token);
            var palette = Palette;
            int lower = LowerDb, upper = UpperDb;
            var result = await Task.Run(() => new Spectrogram(columns, SpekDefaults.BinsFor(analysis.FftBits), palette, lower, upper), token);
            token.ThrowIfCancellationRequested();
            await SpekAnalyzer.AnalyzeRangeAsync(ffmpeg, file, analysis, result, time, token);
            if (result.ColumnsDone == 0) throw new InvalidOperationException("No audio columns were decoded for the zoom interval.");
            // Palette/range changes do not restart decoding. Recolour the replacement off the UI thread before swapping.
            while (version == refinementGeneration && loaded
                && (result.Palette != Palette || result.LowerDb != LowerDb || result.UpperDb != UpperDb))
            {
                palette = Palette;
                lower = LowerDb;
                upper = UpperDb;
                await Task.Run(() => result.SetRangeAndPalette(palette, lower, upper), token);
            }
            if (version != refinementGeneration || !loaded || token.IsCancellationRequested) return;
            refinementImage?.Dispose();
            refinementImage = null;
            refinement = result;
            refinementRange = time;
            refinementQuality = analysis.Quality;
            UpdateReadout();
            canvas.Invalidate();
            RefinementStatus = "Zoom refined.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is FfmpegException or System.ComponentModel.Win32Exception or IOException or InvalidOperationException or ArgumentException)
        {
            if (version == refinementGeneration)
                RefinementStatus = "Zoom refinement failed. Showing the previous analysis.";
        }
        finally
        {
            if (version == refinementGeneration)
            {
                IsRefining = false;
                refinementRun?.Dispose();
                refinementRun = null;
            }
        }
    }

    private Spectrogram? ReadoutData(double seconds, out TimeRange time)
    {
        if (HasTimeZoom && refinement is not null && refinementRange is { } detail && seconds >= detail.Start && seconds <= detail.End)
        {
            int column = Math.Clamp((int)(detail.FractionAt(seconds) * refinement.Columns), 0, refinement.Columns - 1);
            if (refinement.HasColumn(column))
            {
                time = detail;
                return refinement;
            }
        }
        time = new TimeRange(0, Math.Max(0.001, info?.Duration ?? 0));
        return spectrogram;
    }
}
