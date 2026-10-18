namespace Spek.Net;

public static class SpekDefaults
{
    public const int FftBits = 11;
    public const int MinFftBits = 8;
    public const int MaxFftBits = 14;

    public const int UpperDb = 0;
    public const int LowerDb = -120;
    public const int MaxDb = 0;
    public const int MinDb = -140;

    public static int BinsFor(int fftBits) => (1 << (fftBits - 1)) + 1;

    // About 1 ms or less, and at least 16 overlapping windows per FFT length. Never depends on zoom.
    public static int HopSizeFor(int fftBits, int sampleRate)
    {
        if (fftBits < MinFftBits || fftBits > MaxFftBits) throw new ArgumentOutOfRangeException(nameof(fftBits));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        return Math.Min((1 << fftBits) / 16, Math.Max(1, (int)Math.Round(sampleRate / 1000.0)));
    }
}
