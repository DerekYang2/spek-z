namespace Spek.Net;

public delegate void ColumnHandler(int column, ReadOnlySpan<float> values);

// Centred FFT windows on a file-anchored sample grid, independent of the display width. Each measurement
// represents its nearest-centre time cell; pixels average overlapping cells in linear power, not dB.
public sealed class SpectrogramPipeline
{
    private readonly long startFrame;
    private readonly long endFrame;
    private readonly RealFft fft;
    private readonly SpectrumColumns columns;
    private readonly float[] window;
    private readonly float[] ring;
    private readonly float[] input;
    private readonly float[] power;
    private readonly int mask;
    private readonly int leftCell;
    private readonly int rightCell;
    private long inputFrame;
    private long centre;
    private long outputEnd;
    private int ringPos;
    private bool completed;

    public SpectrogramPipeline(int fftBits, WindowFunction windowFunction, int columns, long totalFrames, ColumnHandler onColumn)
        : this(fftBits, windowFunction, columns, totalFrames, onColumn, 0, 0) { }

    public SpectrogramPipeline(int fftBits, WindowFunction windowFunction, int columns, long totalFrames,
        ColumnHandler onColumn, long startFrame, int hopSize)
    {
        if (fftBits < SpekDefaults.MinFftBits || fftBits > SpekDefaults.MaxFftBits)
            throw new ArgumentOutOfRangeException(nameof(fftBits));
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (totalFrames < 0) throw new ArgumentOutOfRangeException(nameof(totalFrames));
        if (startFrame < 0 || startFrame > long.MaxValue - totalFrames - (1 << fftBits))
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        ArgumentNullException.ThrowIfNull(onColumn);
        int size = 1 << fftBits;
        HopSize = hopSize == 0 ? size / 16 : hopSize;
        if (HopSize <= 0 || HopSize > size) throw new ArgumentOutOfRangeException(nameof(hopSize));

        this.startFrame = startFrame;
        endFrame = startFrame + totalFrames;
        outputEnd = endFrame;
        leftCell = HopSize / 2;
        rightCell = HopSize - leftCell;
        centre = CentreFor(startFrame);
        InputStartFrame = totalFrames == 0 ? startFrame : Math.Max(0, centre - size / 2);
        InputEndFrame = totalFrames == 0 ? startFrame : CentreFor(endFrame - 1) + size / 2;
        inputFrame = InputStartFrame;
        fft = new RealFft(fftBits);
        this.columns = new SpectrumColumns(columns, fft.Bins, totalFrames, startFrame, onColumn);
        window = WindowFunctions.Create(windowFunction, size);
        ring = new float[size];
        input = new float[size];
        power = new float[fft.Bins];
        mask = size - 1;
    }

    public int Bins => fft.Bins;
    public int ColumnsDone => columns.ColumnsDone;
    public bool IsDone => columns.IsDone;
    public int HopSize { get; }
    public long InputStartFrame { get; }
    public long InputEndFrame { get; }
    public long MeasurementsDone { get; private set; }

    // Optional prehistory before InputStartFrame. Normal file analysis supplies context through Push instead.
    public void Prime(ReadOnlySpan<float> samples)
    {
        if (inputFrame != InputStartFrame || MeasurementsDone != 0 || completed)
            throw new InvalidOperationException("Prime the pipeline before pushing analysis samples.");
        foreach (float sample in samples) WriteRing(sample);
    }

    public void Push(ReadOnlySpan<float> samples) => Push(samples, CancellationToken.None);

    public void Push(ReadOnlySpan<float> samples, CancellationToken cancellationToken)
    {
        if (completed || endFrame == startFrame) return;
        foreach (float sample in samples)
        {
            if (IsDone || inputFrame >= InputEndFrame) return;
            Consume(sample, cancellationToken);
        }
    }

    public void Complete() => Complete(CancellationToken.None);

    public void Complete(CancellationToken cancellationToken)
    {
        if (completed) return;
        cancellationToken.ThrowIfCancellationRequested();
        completed = true;
        // Pad only the final FFT context, not missing audio implied by an overestimated duration.
        outputEnd = Math.Min(endFrame, inputFrame);
        if (!IsDone && outputEnd > startFrame)
        {
            long lastCentre = CentreFor(outputEnd - 1);
            while (centre <= lastCentre) Consume(0, cancellationToken);
        }
        columns.Complete();
    }

    private long CentreFor(long frame) => (frame + leftCell) / HopSize * HopSize;

    private void WriteRing(float sample)
    {
        ring[ringPos] = sample;
        ringPos = (ringPos + 1) & mask;
    }

    private void Consume(float sample, CancellationToken cancellationToken)
    {
        WriteRing(sample);
        inputFrame++;
        if (inputFrame != centre + fft.Size / 2) return;
        cancellationToken.ThrowIfCancellationRequested();
        for (int i = 0; i < input.Length; i++) input[i] = ring[(ringPos + i) & mask] * window[i];
        fft.PowerLinear(input, power);
        columns.Add(Math.Max(0, centre - leftCell), Math.Min(outputEnd, centre + rightCell), power);
        MeasurementsDone++;
        centre += HopSize;
    }
}
