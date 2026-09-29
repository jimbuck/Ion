namespace Ion.Extensions.Audio;

/// <summary>
/// Offline sample-rate conversion with a windowed-sinc (Blackman) filter, used once per sound at load time. When the
/// rate goes down, the filter's cutoff follows the new Nyquist frequency so that high frequencies do not alias.
/// </summary>
public static class Resampler
{
	/// <summary>Half the filter length in taps at the input rate (for up-sampling; wider when down-sampling).</summary>
	private const int HalfTaps = 16;

	/// <summary>Largest number of distinct filter phases precomputed in a table; rarer ratios compute the kernel per frame.</summary>
	private const int MaxTablePhases = 8192;

	/// <summary>
	/// Resamples interleaved <paramref name="input"/> from <paramref name="inputRate"/> to <paramref name="outputRate"/>.
	/// Returns <paramref name="input"/> copied when the rates are equal.
	/// </summary>
	public static float[] Resample(ReadOnlySpan<float> input, int channels, int inputRate, int outputRate)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(inputRate, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(outputRate, 1);

		if (inputRate == outputRate) return input.ToArray();

		var inFrames = input.Length / channels;
		if (inFrames == 0) return [];

		var outFrames = (int)(((long)inFrames * outputRate + inputRate - 1) / inputRate);
		var output = new float[outFrames * channels];

		// Output frame j sits at input position j * inputRate / outputRate = base + phase / outputRate', where the
		// fraction takes one of (outputRate / gcd) values.
		var gcd = _gcd(inputRate, outputRate);
		var num = inputRate / gcd;   // input step per output frame, in units of 1/den
		var den = outputRate / gcd;  // number of distinct phases

		var cutoff = Math.Min(1.0, (double)outputRate / inputRate);
		var half = (int)Math.Ceiling(HalfTaps / cutoff);
		var taps = 2 * half;

		float[]? table = null;
		if (den <= MaxTablePhases)
		{
			table = new float[den * taps];
			for (var p = 0; p < den; p++) _kernel(table.AsSpan(p * taps, taps), (double)p / den, half, cutoff);
		}

		Span<float> kernel = table is null ? new float[taps] : default;

		long position = 0; // in units of 1/den input frames
		for (var j = 0; j < outFrames; j++, position += num)
		{
			var center = (int)(position / den);
			var phase = (int)(position % den);

			ReadOnlySpan<float> k;
			if (table is not null)
			{
				k = table.AsSpan(phase * taps, taps);
			}
			else
			{
				_kernel(kernel, (double)phase / den, half, cutoff);
				k = kernel;
			}

			// Taps cover input frames center - half + 1 .. center + half.
			var first = center - half + 1;
			for (var c = 0; c < channels; c++)
			{
				var sum = 0.0f;
				for (var t = 0; t < taps; t++)
				{
					var i = first + t;
					if ((uint)i >= (uint)inFrames) continue; // zero outside the sound
					sum += input[i * channels + c] * k[t];
				}
				output[j * channels + c] = sum;
			}
		}

		return output;
	}

	/// <summary>
	/// Fills <paramref name="kernel"/> (2 * <paramref name="half"/> taps) for an output frame at fractional offset
	/// <paramref name="frac"/> past the center tap, normalized so that a constant signal keeps its level.
	/// </summary>
	private static void _kernel(Span<float> kernel, double frac, int half, double cutoff)
	{
		var sum = 0.0;
		for (var t = 0; t < kernel.Length; t++)
		{
			// Distance from the output position to input frame (center - half + 1 + t).
			var x = (t - half + 1) - frac;
			var v = cutoff * _sinc(cutoff * x) * _blackman(x / half);
			kernel[t] = (float)v;
			sum += v;
		}

		if (sum != 0)
		{
			var scale = (float)(1.0 / sum);
			for (var t = 0; t < kernel.Length; t++) kernel[t] *= scale;
		}
	}

	private static double _sinc(double x)
	{
		if (Math.Abs(x) < 1e-12) return 1.0;
		var px = Math.PI * x;
		return Math.Sin(px) / px;
	}

	/// <summary>Blackman window over [-1, 1], 0 outside.</summary>
	private static double _blackman(double x)
	{
		if (x <= -1 || x >= 1) return 0;
		return 0.42 + 0.5 * Math.Cos(Math.PI * x) + 0.08 * Math.Cos(2 * Math.PI * x);
	}

	private static int _gcd(int a, int b)
	{
		while (b != 0) (a, b) = (b, a % b);
		return a;
	}
}
