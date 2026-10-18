using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Spek.Net.WinUI;

// A companion graph for SpekView.SpectrumProfile. It uses the same frequency interval as the spectrogram.
public sealed partial class SpectrumProfileView : UserControl
{
    private readonly CanvasControl canvas;
    private SpectrumProfile? profile;
    private FrequencyRange? frequencyRange;
    private SpectralDrop? measuredDrop;
    private string caption = "Average spectral level of the whole track";

    public SpectrumProfileView()
    {
        canvas = new CanvasControl { ClearColor = Colors.Black };
        canvas.Draw += Draw;
        Content = canvas;
        AutomationProperties.SetAutomationId(this, "SpectrumProfile");
        AutomationProperties.SetName(this, caption);
    }

    public SpectrumProfile? Profile { get => profile; set { profile = value; canvas.Invalidate(); } }
    public FrequencyRange? FrequencyRange { get => frequencyRange; set { frequencyRange = value; canvas.Invalidate(); } }
    public SpectralDrop? MeasuredDrop { get => measuredDrop; set { measuredDrop = value; canvas.Invalidate(); } }
    public string Caption
    {
        get => caption;
        set
        {
            if (caption == value) return;
            caption = value ?? "";
            AutomationProperties.SetName(this, caption);
            canvas.Invalidate();
        }
    }

    private void Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(Colors.Black);
        using var font = new CanvasTextFormat { FontFamily = "Segoe UI", FontSize = 11, WordWrapping = CanvasWordWrapping.NoWrap };
        ds.DrawText(caption, 60, 4, Colors.White, font);
        if (profile is null || profile.IncludedColumns == 0)
        {
            ds.DrawText("Select a non-silent passage. Its spectrum appears when analysis finishes.", 60, 36, Colors.White, font);
            return;
        }

        var plot = new Rect(60, 28, Math.Max(0, sender.ActualWidth - 84), Math.Max(0, sender.ActualHeight - 60));
        if (plot.Width <= 0 || plot.Height <= 0) return;
        var range = frequencyRange ?? new FrequencyRange(0, profile.Nyquist);
        var levels = profile.LevelsDb.Span;
        float peak = float.NegativeInfinity;
        foreach (float level in levels) if (level > peak) peak = level;
        double upper = Math.Min(0, Math.Ceiling((peak + 5) / 10) * 10);
        double lower = upper - 100;

        ds.DrawRectangle(plot, Colors.White);
        for (int step = 0; step <= 4; step++)
        {
            float y = (float)(plot.Top + step / 4.0 * plot.Height);
            ds.DrawText($"{upper - step * 25:0}", 16, y - 7, Colors.White, font);
            ds.DrawLine(56, y, 60, y, Colors.White);
            float x = (float)(plot.Left + step / 4.0 * plot.Width);
            double hz = range.Minimum + step / 4.0 * range.Span;
            ds.DrawText($"{hz / 1000:0.##} kHz", Math.Clamp(x - 16, (float)plot.Left, (float)plot.Right - 44), (float)plot.Bottom + 8, Colors.White, font);
        }
        ds.DrawText("dB", 16, 4, Colors.White, font);

        using var layer = ds.CreateLayer(1, plot);
        using var path = new CanvasPathBuilder(sender);
        int first = Math.Clamp((int)(range.Minimum / profile.BinWidthHz), 0, levels.Length - 1);
        int end = Math.Clamp((int)Math.Ceiling(range.Maximum / profile.BinWidthHz) + 1, first + 1, levels.Length);
        for (int i = first; i < end; i++)
        {
            double hz = i * profile.BinWidthHz;
            double level = float.IsFinite(levels[i]) ? levels[i] : lower;
            var point = new Vector2((float)(plot.Left + (hz - range.Minimum) / range.Span * plot.Width),
                (float)(plot.Bottom - (Math.Clamp(level, lower, upper) - lower) / (upper - lower) * plot.Height));
            if (i == first) path.BeginFigure(point);
            else path.AddLine(point);
        }
        path.EndFigure(CanvasFigureLoop.Open);
        using var geometry = CanvasGeometry.CreatePath(path);
        ds.DrawGeometry(geometry, Colors.White, 1.5f);
        if (measuredDrop is not null && measuredDrop.FrequencyHz >= range.Minimum && measuredDrop.FrequencyHz <= range.Maximum)
        {
            float x = (float)(plot.Left + (measuredDrop.FrequencyHz - range.Minimum) / range.Span * plot.Width);
            using var dots = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dot };
            ds.DrawLine(x, (float)plot.Top, x, (float)plot.Bottom, Colors.White, 1, dots);
        }
    }
}
