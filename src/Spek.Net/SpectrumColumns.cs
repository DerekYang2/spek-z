namespace Spek.Net;

// Area-average fixed-grid power measurements into pixels. Integer time coordinates are scaled by the
// column count, so fractional sample widths remain exact even when there are more pixels than samples.
internal sealed class SpectrumColumns(int columns, int bins, long totalFrames, long startFrame, ColumnHandler onColumn)
{
    private readonly double[] sums = new double[bins];
    private readonly float[] output = new float[bins];
    private readonly Int128 start = (Int128)startFrame * columns;
    private readonly Int128 end = (Int128)(startFrame + totalFrames) * columns;
    private double weight;

    public int ColumnsDone { get; private set; }
    public bool IsDone => ColumnsDone >= columns;

    public void Add(long firstFrame, long endFrame, ReadOnlySpan<float> power)
    {
        if (IsDone || totalFrames == 0 || endFrame <= firstFrame) return;
        Int128 first = Int128.Max(start, (Int128)firstFrame * columns);
        Int128 last = Int128.Min(end, (Int128)endFrame * columns);
        if (last <= first) return;
        while (!IsDone)
        {
            Int128 pixelStart = start + (Int128)ColumnsDone * totalFrames;
            Int128 pixelEnd = pixelStart + totalFrames;
            if (pixelStart >= last) break;
            Int128 overlap = Int128.Min(pixelEnd, last) - Int128.Max(pixelStart, first);
            if (overlap > 0)
            {
                double contribution = (double)overlap;
                for (int bin = 0; bin < sums.Length; bin++) sums[bin] += power[bin] * contribution;
                weight += contribution;
            }
            if (pixelEnd > last) break;
            Emit();
        }
    }

    public void Complete()
    {
        if (totalFrames == 0)
        {
            while (!IsDone) Emit();
        }
        else if (weight > 0 && !IsDone) Emit();
    }

    private void Emit()
    {
        for (int bin = 0; bin < sums.Length; bin++)
            output[bin] = weight > 0 && sums[bin] > 0
                ? (float)(10 * Math.Log10(sums[bin] / weight)) : float.NegativeInfinity;
        onColumn(ColumnsDone, output);
        ColumnsDone++;
        Array.Clear(sums);
        weight = 0;
    }
}
