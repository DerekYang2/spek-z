namespace Spek.Net;

// A real-input FFT of size n = 2^bits, computed as a complex FFT of size n/2 followed by the usual split step.
internal sealed class RealFft
{
    private readonly int size;
    private readonly int half;
    private readonly int[] bitReverse;
    private readonly float[] twiddleCos;
    private readonly float[] twiddleSin;
    private readonly float[] splitCos;
    private readonly float[] splitSin;
    private readonly float[] re;
    private readonly float[] im;

    public RealFft(int bits)
    {
        if (bits < 2 || bits > 24) throw new ArgumentOutOfRangeException(nameof(bits));

        size = 1 << bits;
        half = size / 2;
        re = new float[half];
        im = new float[half];

        int halfBits = bits - 1;
        bitReverse = new int[half];
        for (int i = 0; i < half; i++)
        {
            int reversed = 0;
            for (int b = 0; b < halfBits; b++)
            {
                if ((i & (1 << b)) != 0) reversed |= 1 << (halfBits - 1 - b);
            }
            bitReverse[i] = reversed;
        }

        twiddleCos = new float[Math.Max(1, half / 2)];
        twiddleSin = new float[Math.Max(1, half / 2)];
        for (int i = 0; i < half / 2; i++)
        {
            double angle = 2 * Math.PI * i / half;
            twiddleCos[i] = (float)Math.Cos(angle);
            twiddleSin[i] = (float)Math.Sin(angle);
        }

        splitCos = new float[half + 1];
        splitSin = new float[half + 1];
        for (int k = 0; k <= half; k++)
        {
            double angle = 2 * Math.PI * k / size;
            splitCos[k] = (float)Math.Cos(angle);
            splitSin[k] = (float)Math.Sin(angle);
        }
    }

    public int Size => size;

    public int Bins => half + 1;

    // Writes 10 * log10(|X[k]|^2 / n^2) for k = 0 .. n/2, the same scale Spek uses.
    public void PowerDb(ReadOnlySpan<float> input, Span<float> output)
    {
        PowerLinear(input, output);
        for (int bin = 0; bin < Bins; bin++) output[bin] = 10f * MathF.Log10(output[bin]);
    }

    // Keep energy linear until the display column has combined all contributing measurements.
    public void PowerLinear(ReadOnlySpan<float> input, Span<float> output)
    {
        if (input.Length != size) throw new ArgumentException("Input length must equal the FFT size.", nameof(input));
        if (output.Length < Bins) throw new ArgumentException("Output is shorter than the number of bins.", nameof(output));

        for (int k = 0; k < half; k++)
        {
            int j = bitReverse[k];
            re[j] = input[2 * k];
            im[j] = input[2 * k + 1];
        }

        Transform();

        float n2 = (float)size * size;
        float dc = re[0] + im[0];
        float nyquist = re[0] - im[0];
        output[0] = dc * dc / n2;
        output[half] = nyquist * nyquist / n2;

        for (int k = 1; k < half; k++)
        {
            float ar = re[k], ai = im[k];
            float br = re[half - k], bi = -im[half - k];

            // Even part E = (Z[k] + conj Z[n/2-k]) / 2 and odd part O = (Z[k] - conj Z[n/2-k]) / 2i.
            float er = 0.5f * (ar + br), ei = 0.5f * (ai + bi);
            float or = 0.5f * (ai - bi), oi = -0.5f * (ar - br);

            // X[k] = E + e^(-2 pi i k / n) O
            float c = splitCos[k], s = splitSin[k];
            float xr = er + c * or + s * oi;
            float xi = ei + c * oi - s * or;
            output[k] = (xr * xr + xi * xi) / n2;
        }
    }

    private void Transform()
    {
        for (int length = 2; length <= half; length <<= 1)
        {
            int halfLength = length >> 1;
            int step = half / length;
            for (int start = 0; start < half; start += length)
            {
                for (int j = 0; j < halfLength; j++)
                {
                    float wr = twiddleCos[j * step];
                    float wi = -twiddleSin[j * step];
                    int a = start + j;
                    int b = a + halfLength;
                    float tr = re[b] * wr - im[b] * wi;
                    float ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }
}
