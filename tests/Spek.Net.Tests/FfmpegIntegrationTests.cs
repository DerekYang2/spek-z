using System.Diagnostics;

namespace Spek.Net.Tests;

// These run the real ffmpeg. They use SPEK_Z_FFMPEG, the old FLUENTSPEK_FFMPEG variable, or PATH.
[TestClass]
public sealed class FfmpegIntegrationTests
{
    private static string? ffmpegPath;
    private static string folder = "";

    [ClassInitialize]
    public static void Setup(TestContext _)
    {
        ffmpegPath = Ffmpeg.Find([Environment.GetEnvironmentVariable("SPEK_Z_FFMPEG"), Environment.GetEnvironmentVariable("FLUENTSPEK_FFMPEG")]);
        folder = Path.Combine(Path.GetTempPath(), "Spek.Net.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [TestMethod]
    public async Task ProbesAndAnalysesEachChannel()
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate("tones.flac",
            "-f", "lavfi", "-i", "aevalsrc=0.5*sin(2*PI*1000*t)|0.5*sin(2*PI*5000*t):s=44100:d=3",
            "-c:a", "flac", "-sample_fmt", "s32", "-bits_per_raw_sample", "24");

        var info = await ffmpeg.ProbeAsync(file);

        Assert.AreEqual(AudioError.None, info.Error);
        Assert.AreEqual(3.0, info.Duration, 0.011);
        var stream = info.Streams.Single();
        Assert.AreEqual("flac", stream.CodecName);
        Assert.AreEqual(44100, stream.SampleRate);
        Assert.AreEqual(2, stream.Channels);
        Assert.AreEqual(24, stream.BitsPerSample);
        StringAssert.StartsWith(stream.CodecDescription, "FLAC");

        // 1 kHz and 5 kHz fall in bins 46 and 232 of a 2048-point FFT at 44.1 kHz.
        Assert.AreEqual(46, await PeakBin(ffmpeg, info, channel: 0));
        Assert.AreEqual(232, await PeakBin(ffmpeg, info, channel: 1));
    }

    [TestMethod]
    public async Task FindsEveryAudioStream()
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate("two.mka",
            "-f", "lavfi", "-i", "sine=f=440:d=2",
            "-f", "lavfi", "-i", "sine=f=3000:d=2",
            "-map", "0", "-map", "1", "-c:a", "flac");

        var info = await ffmpeg.ProbeAsync(file);

        Assert.AreEqual(2, info.Streams.Count);
        Assert.AreEqual(139, await PeakBin(ffmpeg, info, channel: 0, stream: 1));
    }

    [TestMethod]
    public async Task ReportsFilesItCannotOpen()
    {
        var ffmpeg = RequireFfmpeg();

        var missing = await ffmpeg.ProbeAsync(Path.Combine(folder, "missing.flac"));
        Assert.AreEqual(AudioError.CannotOpenFile, missing.Error);

        var text = Path.Combine(folder, "text.mp3");
        await File.WriteAllTextAsync(text, "not audio");
        Assert.AreNotEqual(AudioError.None, (await ffmpeg.ProbeAsync(text)).Error);
    }

    [TestMethod]
    public async Task CancellingStopsTheDecode()
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate("long.flac", "-f", "lavfi", "-i", "anoisesrc=d=600:r=44100", "-c:a", "flac", "-compression_level", "0");
        var info = await ffmpeg.ProbeAsync(file);
        var spectrogram = new Spectrogram(500, SpekDefaults.BinsFor(SpekDefaults.FftBits), Palette.Spectrum, -120, 0);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            SpekAnalyzer.AnalyzeAsync(ffmpeg, info, new AnalysisSettings(), spectrogram, cancellation.Token));

        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(5), $"cancelling took {watch.Elapsed}");
        Assert.IsTrue(spectrogram.ColumnsDone < 500);
    }

    private static async Task<int> PeakBin(Ffmpeg ffmpeg, AudioFileInfo info, int channel, int stream = 0)
    {
        var settings = new AnalysisSettings(Stream: stream, Channel: channel);
        var spectrogram = new Spectrogram(50, SpekDefaults.BinsFor(settings.FftBits), Palette.Spectrum, -120, 0);
        await SpekAnalyzer.AnalyzeAsync(ffmpeg, info, settings, spectrogram);

        Assert.AreEqual(50, spectrogram.ColumnsDone);
        int x = 25, best = 0;
        for (int bin = 1; bin < spectrogram.Bins; bin++)
        {
            if (spectrogram.GetValue(x, bin) > spectrogram.GetValue(x, best)) best = bin;
        }
        return best;
    }

    [TestMethod]
    public async Task MeasuresTheSpectralDropAfterMp3IsReencodedAsFlac()
    {
        var ffmpeg = RequireFfmpeg();
        var mp3 = Generate("cutoff.mp3", "-f", "lavfi", "-i", "anoisesrc=d=8:r=48000:a=0.1:seed=42",
            "-ac", "2", "-c:a", "libmp3lame", "-b:a", "128k");
        var file = Generate("cutoff.flac", "-i", mp3, "-c:a", "flac");
        var info = await ffmpeg.ProbeAsync(file);
        var data = new Spectrogram(100, 1025, Palette.Spectrum, -120, 0);
        await SpekAnalyzer.AnalyzeAsync(ffmpeg, info, new AnalysisSettings(), data);
        var drop = data.GetProfile(20, 80, 48000).FindDrop();
        Assert.IsNotNull(drop);
        Assert.AreEqual(17000, drop.FrequencyHz, 800);
        Assert.IsTrue(drop.DropDb >= 20);
        Assert.AreEqual("flac", info.Streams[0].CodecName);
    }

    [TestMethod]
    public async Task FullBandwidthNoiseDoesNotGetACutoffMarker()
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate("broadband.flac", "-f", "lavfi", "-i", "anoisesrc=d=8:r=48000:a=0.1:seed=42", "-c:a", "flac");
        var info = await ffmpeg.ProbeAsync(file);
        var data = new Spectrogram(100, 1025, Palette.Spectrum, -120, 0);
        await SpekAnalyzer.AnalyzeAsync(ffmpeg, info, new AnalysisSettings(), data);
        Assert.IsNull(data.GetProfile(0, 100, 48000).FindDrop());
    }

    [TestMethod]
    public async Task RangeDecodeMatchesTheCorrespondingLosslessSamples()
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate("range-noise.flac", "-f", "lavfi", "-i", "anoisesrc=d=3:r=48000:a=0.2:seed=42", "-c:a", "flac");
        var full = new List<float>();
        await ffmpeg.DecodeAsync(file, 0, 0, samples => full.AddRange(samples.ToArray()));
        var cropped = new List<float>();
        var range = new TimeRange(1.125, 1.375);
        await ffmpeg.DecodeRangeAsync(file, 0, 0, range, samples => cropped.AddRange(samples.ToArray()));
        Assert.AreEqual(12000, cropped.Count);
        CollectionAssert.AreEqual(full.Skip(54000).Take(12000).ToArray(), cropped.ToArray());
    }

    [TestMethod]
    public async Task RefinementUsesPrecedingAudioForTheFirstFft()
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate("range-context.flac", "-f", "lavfi", "-i", "aevalsrc=0.5*sin(2*PI*1000*t):s=48000:d=3", "-c:a", "flac");
        var info = await ffmpeg.ProbeAsync(file);
        var target = new Spectrogram(200, 1025, Palette.Spectrum, -120, 0);
        await SpekAnalyzer.AnalyzeRangeAsync(ffmpeg, info, new AnalysisSettings(), target, new TimeRange(1, 1.1));
        Assert.AreEqual(200, target.ColumnsDone);
        Assert.IsTrue(target.GetValue(0, 43) > -25, "The first column must not start with a zero-padded FFT.");
        Assert.AreEqual(target.GetValue(100, 43), target.GetValue(0, 43), 1);
    }

    [TestMethod]
    [DataRow("flac", "flac")]
    [DataRow("mp3", "libmp3lame")]
    [DataRow("opus", "libopus")]
    public async Task RefinedIntervalHasFullWidthAndCorrectTimeAlignment(string extension, string codec)
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate($"range-change.{extension}",
            "-f", "lavfi", "-i", "aevalsrc=0.25*sin(2*PI*(1000+4000*gte(t\\,2))*t):s=48000:d=4", "-c:a", codec);
        var info = await ffmpeg.ProbeAsync(file);
        var target = new Spectrogram(400, 1025, Palette.Spectrum, -120, 0);
        await SpekAnalyzer.AnalyzeRangeAsync(ffmpeg, info, new AnalysisSettings(), target, new TimeRange(1.5, 2.5));
        Assert.AreEqual(400, target.ColumnsDone);
        Assert.IsTrue(target.GetValue(50, 43) > target.GetValue(50, 213) + 20, "The interval should begin in the 1 kHz section.");
        Assert.IsTrue(target.GetValue(350, 213) > target.GetValue(350, 43) + 20, "The interval should end in the 5 kHz section.");
        Assert.IsTrue(info.Duration / target.Columns > 1.0 / target.Columns, "The refined interval must have finer time sampling than the whole-file view.");
    }

    [TestMethod]
    public async Task CancellingARangeDecodeStopsItsProcess()
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate("range-cancel.flac", "-f", "lavfi", "-i", "anoisesrc=d=10:r=48000", "-c:a", "flac");
        using var cancellation = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() => ffmpeg.DecodeRangeAsync(file, 0, 0,
            new TimeRange(1, 9), _ => cancellation.Cancel(), cancellation.Token));
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task InvalidAndCancelledRefinementRequestsAreRejected()
    {
        var ffmpeg = RequireFfmpeg();
        var info = new AudioFileInfo("unused", AudioError.None, 10, [new("flac", "FLAC", 48000, 1, 16, 0)]);
        var target = new Spectrogram(10, 1025, Palette.Spectrum, -120, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => SpekAnalyzer.AnalyzeRangeAsync(ffmpeg, info, new(), target, new TimeRange(9, 11)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpekAnalyzer.AnalyzeRangeAsync(ffmpeg, info, new(Channel: 1), target, new TimeRange(1, 2)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => SpekAnalyzer.AnalyzeRangeAsync(ffmpeg, info, new(), target,
            new TimeRange(1, 2), cancellation.Token));
        Assert.AreEqual(0, target.ColumnsDone);
    }

    [TestMethod]
    [DataRow(44100)]
    [DataRow(48000)]
    [DataRow(96000)]
    public async Task LosslessCropMatchesTheSamePixelsInTheOverview(int rate)
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate($"grid-{rate}.flac", "-f", "lavfi", "-i", $"anoisesrc=d=3:r={rate}:a=0.2:seed=42", "-c:a", "flac");
        var info = await ffmpeg.ProbeAsync(file);
        int first = rate + 700;
        var whole = new Spectrogram(rate * 3 / 100, 1025, Palette.Spectrum, -120, 0);
        var crop = new Spectrogram(100, 1025, Palette.Spectrum, -120, 0);
        var settings = new AnalysisSettings { Quality = AnalysisQuality.Detailed };
        await SpekAnalyzer.AnalyzeAsync(ffmpeg, info, settings, whole);
        await SpekAnalyzer.AnalyzeRangeAsync(ffmpeg, info, settings, crop,
            new TimeRange(first / (double)rate, (first + 10000) / (double)rate));
        Assert.AreEqual(100, crop.ColumnsDone);
        for (int x = 0; x < crop.Columns; x++)
            for (int bin = 0; bin < crop.Bins; bin++)
                Assert.AreEqual(whole.GetValue(first / 100 + x, bin), crop.GetValue(x, bin), 0.0001,
                    $"{rate} Hz, column {x}, bin {bin}");
    }

    private static Ffmpeg RequireFfmpeg()
    {
        if (ffmpegPath is null) Assert.Inconclusive("ffmpeg not found. Set SPEK_Z_FFMPEG or add ffmpeg to PATH.");
        return new Ffmpeg(ffmpegPath);
    }

    private static string Generate(string name, params string[] args)
    {
        var output = Path.Combine(folder, name);
        var info = new ProcessStartInfo(ffmpegPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-y" }.Concat(args).Append(output))
        {
            info.ArgumentList.Add(arg);
        }
        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, stderr);
        return output;
    }
}
