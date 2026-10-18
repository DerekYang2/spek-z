namespace Spek.Net.Tests;

[TestClass]
public sealed class FastAnalysisTests
{
    [TestMethod]
    public void DefaultAnalysisStaysFast()
    {
        Assert.AreEqual(AnalysisQuality.Fast, new AnalysisSettings().Quality);
        Assert.AreEqual(AnalysisQuality.Detailed, (new AnalysisSettings() with { Quality = AnalysisQuality.Detailed }).Quality);
    }

    [TestMethod]
    public void FastPipelineMatchesPublished020ReferenceValues()
    {
        var random = new Random(42);
        var samples = Enumerable.Range(0, 12345).Select(_ => (float)((random.NextDouble() - 0.5) * 0.2)).ToArray();
        var data = new Spectrogram(37, 1025, Palette.Spectrum, -120, 0);
        var pipeline = new FastSpectrogramPipeline(11, WindowFunction.Hann, 37, samples.Length, data.SetColumn);
        pipeline.Push(samples);
        pipeline.Complete();
        // Captured from the published Spek.NET 0.2.0 package, not from the new implementation.
        (int X, int Bin, double Db)[] expected =
        [
            (0, 0, -75.83732), (0, 1, -75.8163), (0, 64, -87.96546), (0, 256, -84.58406), (0, 1024, -90.72297),
            (17, 0, -57.59523), (17, 1, -59.24732), (17, 64, -58.751923), (17, 256, -64.253914), (17, 1024, -59.848286),
            (36, 0, -86.90373), (36, 1, -68.51792), (36, 64, -63.537136), (36, 256, -57.16164), (36, 1024, -80.0673),
        ];
        foreach (var value in expected) Assert.AreEqual(value.Db, data.GetValue(value.X, value.Bin), 0.0001);
        Assert.AreEqual(37, data.ColumnsDone);
    }

    [TestMethod]
    public void FastRetainsLegacyAveragingWhileDetailedPreservesBurstEnergy()
    {
        var samples = new float[1024];
        for (int i = 512; i < samples.Length; i++) samples[i] = (float)Math.Sin(2 * Math.PI * 16 * i / 256);
        float fast = 0, detailed = 0;
        var legacy = new FastSpectrogramPipeline(8, WindowFunction.Hann, 1, samples.Length, (_, values) => fast = values[16]);
        legacy.Push(samples);
        legacy.Complete();
        var dense = new SpectrogramPipeline(8, WindowFunction.Hann, 1, samples.Length, (_, values) => detailed = values[16]);
        dense.Push(samples);
        dense.Complete();
        Assert.IsTrue(float.IsNegativeInfinity(fast));
        Assert.IsTrue(float.IsFinite(detailed) && detailed > -25);
    }
}
