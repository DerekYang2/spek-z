namespace Spek.Net.Tests;

[TestClass]
public sealed class FixedGridTests
{
    [TestMethod]
    [DataRow(8, 48000, 16)]
    [DataRow(11, 48000, 48)]
    [DataRow(11, 44100, 44)]
    [DataRow(14, 192000, 192)]
    [DataRow(8, 8000, 8)]
    [DataRow(8, 1, 1)]
    public void DenseHopDoesNotDependOnDisplayWidth(int bits, int rate, int expected)
    {
        Assert.AreEqual(expected, SpekDefaults.HopSizeFor(bits, rate));
    }

    [TestMethod]
    public void MeasurementCountIsIndependentOfPixelCount()
    {
        var samples = Noise(12345);
        var narrow = Analyse(samples, 13);
        var wide = Analyse(samples, 1300);
        Assert.AreEqual(narrow.Pipeline.MeasurementsDone, wide.Pipeline.MeasurementsDone);
        Assert.AreEqual(narrow.Pipeline.InputStartFrame, wide.Pipeline.InputStartFrame);
        Assert.AreEqual(narrow.Pipeline.InputEndFrame, wide.Pipeline.InputEndFrame);
    }

    [TestMethod]
    public void CoarsePixelsMatchTheEnergyAverageOfAlignedFinePixels()
    {
        var samples = Noise(12345);
        Array.Clear(samples, 4000, 3000);
        var coarse = Analyse(samples, 13).Values;
        var fine = Analyse(samples, 130).Values;
        for (int x = 0; x < coarse.Length; x++)
            for (int bin = 0; bin < coarse[x].Length; bin++)
            {
                double mean = Enumerable.Range(x * 10, 10).Average(i => Math.Pow(10, fine[i][bin] / 10.0));
                double expected = 10 * Math.Log10(mean);
                if (double.IsNegativeInfinity(expected)) Assert.IsTrue(float.IsNegativeInfinity(coarse[x][bin]));
                else Assert.AreEqual(expected, coarse[x][bin], 0.0001, $"column {x}, bin {bin}");
            }
    }

    [TestMethod]
    [DataRow(3700, 2600)]
    [DataRow(0, 2600)]
    [DataRow(7300, 2700)]
    public void CropsKeepTheSameFileAnchoredMeasurements(int start, int length)
    {
        var samples = Noise(10000);
        var full = Analyse(samples, 100).Values;
        var crop = Analyse(samples, length / 100, start, length).Values;
        for (int x = 0; x < crop.Length; x++)
            for (int bin = 0; bin < crop[x].Length; bin++)
                Assert.AreEqual(full[start / 100 + x][bin], crop[x][bin], 0.0001, $"column {x}, bin {bin}");
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(11)]
    [DataRow(48)]
    public void OddAndEvenHopSizesKeepCropAlignment(int hop)
    {
        var samples = Noise(10000);
        var full = Analyse(samples, 100, hop: hop).Values;
        var crop = Analyse(samples, 26, 3700, 2600, hop: hop).Values;
        for (int x = 0; x < crop.Length; x++)
            for (int bin = 0; bin < crop[x].Length; bin++)
                Assert.AreEqual(full[37 + x][bin], crop[x][bin], 0.0001);
    }

    [TestMethod]
    public void DecoderChunkBoundariesDoNotAffectTheImage()
    {
        var samples = Noise(12345);
        var first = Analyse(samples, 37, chunk: 7).Values;
        var second = Analyse(samples, 37, chunk: 4096).Values;
        for (int x = 0; x < first.Length; x++) CollectionAssert.AreEqual(first[x], second[x]);
    }

    [TestMethod]
    [DataRow(8)]
    [DataRow(11)]
    [DataRow(14)]
    public void CentredWindowsDoNotShiftAnImpulseByHalfAnFft(int bits)
    {
        int size = 1 << bits;
        var samples = new float[size * 4];
        samples[size * 2] = 1;
        var levels = new float[256];
        var pipeline = new SpectrogramPipeline(bits, WindowFunction.Hann, 256, samples.Length, (x, values) => levels[x] = values[1]);
        pipeline.Push(samples);
        pipeline.Complete();
        int peak = Array.IndexOf(levels, levels.Max());
        Assert.IsTrue(Math.Abs(peak - 128) <= 2, $"Impulse moved to column {peak} for FFT size {size}.");
    }

    [TestMethod]
    public void ShortDecodeDoesNotFillAnOverestimatedDurationWithSilence()
    {
        var pipeline = new SpectrogramPipeline(8, WindowFunction.Hann, 1000, 1_000_000, (_, _) => { });
        pipeline.Push(Noise(100));
        pipeline.Complete();
        Assert.AreEqual(1, pipeline.ColumnsDone);
        Assert.IsTrue(pipeline.MeasurementsDone < 10, "EOF padding analysed the missing duration.");
    }

    [TestMethod]
    public void PushCanCancelBetweenDenseFftMeasurements()
    {
        using var cancellation = new CancellationTokenSource();
        var pipeline = new SpectrogramPipeline(11, WindowFunction.Hann, 1000, 100000,
            (_, _) => cancellation.Cancel(), 0, SpekDefaults.HopSizeFor(11, 48000));
        Assert.Throws<OperationCanceledException>(() => pipeline.Push(Noise(100000), cancellation.Token));
        Assert.IsTrue(pipeline.ColumnsDone < 10);
    }

    [TestMethod]
    public void InvalidSamplingAndIntervalsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpekDefaults.HopSizeFor(7, 48000));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpekDefaults.HopSizeFor(11, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpectrogramPipeline(11, WindowFunction.Hann, 10, 100, (_, _) => { }, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpectrogramPipeline(11, WindowFunction.Hann, 10, 100, (_, _) => { }, 0, 3000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpectrogramPipeline(11, WindowFunction.Hann, 10, long.MaxValue, (_, _) => { }));
    }

    private static (SpectrogramPipeline Pipeline, float[][] Values) Analyse(float[] source, int columns,
        int start = 0, int? length = null, int chunk = 1024, int hop = 16)
    {
        var values = new float[columns][];
        var pipeline = new SpectrogramPipeline(8, WindowFunction.Hann, columns, length ?? source.Length,
            (x, power) => values[x] = power.ToArray(), start, hop);
        int end = (int)Math.Min(source.Length, pipeline.InputEndFrame);
        for (int i = (int)pipeline.InputStartFrame; i < end; i += chunk)
            pipeline.Push(source.AsSpan(i, Math.Min(chunk, end - i)));
        pipeline.Complete();
        Assert.AreEqual(columns, pipeline.ColumnsDone);
        return (pipeline, values);
    }

    private static float[] Noise(int length)
    {
        var random = new Random(42);
        return Enumerable.Range(0, length).Select(_ => (float)(random.NextDouble() - 0.5)).ToArray();
    }
}
