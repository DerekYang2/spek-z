namespace Spek.Net.Tests;

[TestClass]
public sealed class SpectrogramPipelineTests
{
    private const int Bits = 11;
    private const int N = 1 << Bits;

    [TestMethod]
    public void EmitsEveryColumnOnceInOrder()
    {
        const int columns = 37;
        long total = 123_457;
        var seen = new List<int>();
        var pipeline = new SpectrogramPipeline(Bits, WindowFunction.Hann, columns, total, (x, _) => seen.Add(x));

        PushInChunks(pipeline, Noise((int)total, 1), 1000);
        pipeline.Complete();

        CollectionAssert.AreEqual(Enumerable.Range(0, columns).ToList(), seen);
    }

    [TestMethod]
    public void ToneLandsInItsBinInEveryColumn()
    {
        const int bin = 64, columns = 20;
        var samples = Tone(bin, columns * 3 * N);
        var peaks = new int[columns];
        var pipeline = new SpectrogramPipeline(Bits, WindowFunction.Hann, columns, samples.Length, (x, v) => peaks[x] = ArgMax(v));

        pipeline.Push(samples);
        pipeline.Complete();

        CollectionAssert.AreEqual(Enumerable.Repeat(bin, columns).ToArray(), peaks);
    }

    [TestMethod]
    public void CentredWindowsKeepTheToneChangeNearItsActualTime()
    {
        // A centred window can see the onset just before the boundary, but must not delay it by half a window.
        const int columns = 40, bin = 200;
        int total = columns * 2 * N;
        var samples = new float[total];
        Tone(bin, total).AsSpan(total / 2).CopyTo(samples.AsSpan(total / 2));

        var peakLevel = new float[columns];
        var pipeline = new SpectrogramPipeline(Bits, WindowFunction.Hann, columns, total, (x, v) => peakLevel[x] = v[bin]);
        PushInChunks(pipeline, samples, 4096);
        pipeline.Complete();

        for (int x = 0; x < columns; x++)
        {
            if (x < columns / 2 - 1) Assert.IsTrue(peakLevel[x] < -200, $"column {x} should be silent but was {peakLevel[x]} dB");
            else if (x >= columns / 2) Assert.IsTrue(peakLevel[x] > -20, $"column {x} should hold the tone but was {peakLevel[x]} dB");
        }
    }

    [TestMethod]
    public void ShortColumnsStillGetAnFft()
    {
        // 50 frames per column, far below the FFT size.
        const int columns = 300;
        var samples = Tone(32, columns * 50);
        var values = new float[columns];
        var pipeline = new SpectrogramPipeline(Bits, WindowFunction.Hann, columns, samples.Length, (x, v) => values[x] = v[32]);

        pipeline.Push(samples);
        pipeline.Complete();

        Assert.IsTrue(values.Skip(columns / 2).All(v => v > -20));
    }

    [TestMethod]
    public void AudioShorterThanTheEstimateFinishesTheLastColumn()
    {
        const int columns = 10;
        var seen = new List<int>();
        var pipeline = new SpectrogramPipeline(Bits, WindowFunction.Hann, columns, 100_000, (x, _) => seen.Add(x));

        pipeline.Push(Noise(99_800, 2));
        pipeline.Complete();

        Assert.AreEqual(columns, seen.Count);
    }

    [TestMethod]
    public void AudioLongerThanTheEstimateIsIgnoredPastTheEnd()
    {
        const int columns = 10;
        var seen = new List<int>();
        var pipeline = new SpectrogramPipeline(Bits, WindowFunction.Hann, columns, 100_000, (x, _) => seen.Add(x));

        pipeline.Push(Noise(150_000, 3));
        pipeline.Complete();

        Assert.AreEqual(columns, seen.Count);
        Assert.IsTrue(pipeline.IsDone);
    }

    [TestMethod]
    public void MoreColumnsThanFramesStillFillsEveryColumn()
    {
        const int columns = 200;
        var seen = new List<int>();
        var pipeline = new SpectrogramPipeline(Bits, WindowFunction.Hann, columns, 50, (x, _) => seen.Add(x));

        pipeline.Push(Noise(50, 4));
        pipeline.Complete();

        CollectionAssert.AreEqual(Enumerable.Range(0, columns).ToList(), seen);
    }

    [TestMethod]
    public void SpectrogramColoursColumnsBottomUp()
    {
        var spectrogram = new Spectrogram(2, 3, Palette.Mono, -100, 0);
        spectrogram.SetColumn(1, [0f, -50f, -200f]);

        // Bin 0 is the bottom row, and values clamp to the range.
        Assert.AreEqual(0, spectrogram.Pixels[(0 * 2 + 1) * 4]);
        Assert.AreEqual(128, spectrogram.Pixels[(1 * 2 + 1) * 4]);
        Assert.AreEqual(255, spectrogram.Pixels[(2 * 2 + 1) * 4]);
        Assert.AreEqual(1, spectrogram.ColumnsDone);

        spectrogram.SetRangeAndPalette(Palette.Mono, -50, 0);
        Assert.AreEqual(0, spectrogram.Pixels[(1 * 2 + 1) * 4]);
    }

    [TestMethod]
    public void PrimingSuppliesFftContextWithoutAdvancingColumns()
    {
        float[]? result = null;
        var pipeline = new SpectrogramPipeline(8, WindowFunction.Hann, 8, 256, (column, values) =>
        {
            if (column == 0) result = values.ToArray();
        });
        var context = Enumerable.Repeat(1f, 256).ToArray();
        pipeline.Prime(context.AsSpan(0, 100));
        pipeline.Prime(context.AsSpan(100));
        Assert.AreEqual(0, pipeline.ColumnsDone);
        pipeline.Push(context.AsSpan(0, 192));
        Assert.IsTrue(pipeline.ColumnsDone > 0);
        Assert.IsNotNull(result);
        Assert.AreEqual(-6.0206, result[0], 0.1);
    }

    [TestMethod]
    public void CannotPrimeAfterAnalysisHasStarted()
    {
        var pipeline = new SpectrogramPipeline(8, WindowFunction.Hann, 1, 64, (_, _) => { });
        pipeline.Push([1f]);
        Assert.Throws<InvalidOperationException>(() => pipeline.Prime([1f]));
    }

    private static void PushInChunks(SpectrogramPipeline pipeline, float[] samples, int chunk)
    {
        for (int i = 0; i < samples.Length; i += chunk)
        {
            pipeline.Push(samples.AsSpan(i, Math.Min(chunk, samples.Length - i)));
        }
    }

    private static float[] Tone(int bin, int length)
    {
        var samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = (float)Math.Sin(2 * Math.PI * bin * i / N);
        }
        return samples;
    }

    private static float[] Noise(int length, int seed)
    {
        var random = new Random(seed);
        var samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = (float)(random.NextDouble() * 2 - 1);
        }
        return samples;
    }

    private static int ArgMax(ReadOnlySpan<float> values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > values[best]) best = i;
        }
        return best;
    }
}
