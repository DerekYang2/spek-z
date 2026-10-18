namespace Spek.Net;

// Preserve the 0.2.0 overview/light-zoom algorithm. Detailed zoom uses SpectrogramPipeline instead.
internal sealed class FastSpectrogramPipeline
{
    private readonly int columns;
    private readonly long totalFrames;
    private readonly ColumnHandler onColumn;
    private readonly RealFft fft;
    private readonly float[] window;
    private readonly float[] ring;
    private readonly float[] input;
    private readonly float[] power;
    private readonly float[] sum;
    private readonly int mask;
    private int ringPos;
    private int column;
    private long columnEnd;
    private long frames;
    private long framesInColumn;
    private int ffts;

    public FastSpectrogramPipeline(int fftBits, WindowFunction windowFunction, int columns, long totalFrames, ColumnHandler onColumn)
    {
        if (fftBits < SpekDefaults.MinFftBits || fftBits > SpekDefaults.MaxFftBits)
            throw new ArgumentOutOfRangeException(nameof(fftBits));
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (totalFrames < 0) throw new ArgumentOutOfRangeException(nameof(totalFrames));
        this.columns = columns;
        this.totalFrames = totalFrames;
        this.onColumn = onColumn;
        fft = new RealFft(fftBits);
        window = WindowFunctions.Create(windowFunction, fft.Size);
        ring = new float[fft.Size];
        input = new float[fft.Size];
        power = new float[fft.Bins];
        sum = new float[fft.Bins];
        mask = fft.Size - 1;
        columnEnd = Boundary(1);
    }

    public void Prime(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples)
        {
            ring[ringPos] = sample;
            ringPos = (ringPos + 1) & mask;
        }
    }

    public void Push(ReadOnlySpan<float> samples)
    {
        EndEmptyColumns();
        foreach (float sample in samples)
        {
            if (column >= columns) return;
            ring[ringPos] = sample;
            ringPos = (ringPos + 1) & mask;
            frames++;
            framesInColumn++;
            if (framesInColumn % fft.Size == 0) RunFft();
            if (frames == columnEnd)
            {
                EndColumn();
                EndEmptyColumns();
            }
        }
    }

    public void Complete()
    {
        EndEmptyColumns();
        if (column < columns && framesInColumn > 0) EndColumn();
    }

    private long Boundary(int index) => index >= columns ? totalFrames : (long)((Int128)index * totalFrames / columns);
    private void EndEmptyColumns()
    {
        while (column < columns && frames == columnEnd) EndColumn();
    }
    private void RunFft()
    {
        for (int i = 0; i < input.Length; i++) input[i] = ring[(ringPos + i) & mask] * window[i];
        fft.PowerDb(input, power);
        for (int i = 0; i < sum.Length; i++) sum[i] += power[i];
        ffts++;
    }
    private void EndColumn()
    {
        if (ffts == 0) RunFft();
        for (int i = 0; i < sum.Length; i++) sum[i] /= ffts;
        onColumn(column, sum);
        Array.Clear(sum);
        ffts = 0;
        framesInColumn = 0;
        column++;
        columnEnd = Boundary(column + 1);
    }
}
