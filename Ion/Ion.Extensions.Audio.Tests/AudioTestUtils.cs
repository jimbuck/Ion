namespace Ion.Extensions.Audio.Tests;

/// <summary>
/// Builds WAV files and sounds with known content, and measures signals.
/// </summary>
internal static class AudioTestUtils
{
	public const int Rate = 48000;

	/// <summary>
	/// Writes a WAV file. <paramref name="frames"/> holds one value in [-1, 1] per channel per frame, interleaved.
	/// </summary>
	/// <param name="format">1 = PCM, 3 = IEEE float.</param>
	public static byte[] Wav(float[] frames, int channels, int sampleRate, int bits, int format = 1, bool extensible = false, bool listChunk = false)
	{
		var bytesPerSample = bits / 8;
		var blockAlign = channels * bytesPerSample;
		using var stream = new MemoryStream();
		var w = new BinaryWriter(stream);

		w.Write("RIFF"u8);
		w.Write(0);
		w.Write("WAVE"u8);

		if (listChunk)
		{
			w.Write("LIST"u8);
			w.Write(3);
			w.Write(new byte[] { 1, 2, 3, 0 }); // odd size plus pad byte
		}

		w.Write("fmt "u8);
		w.Write(extensible ? 40 : 16);
		w.Write((short)(extensible ? unchecked((short)0xFFFE) : format));
		w.Write((short)channels);
		w.Write(sampleRate);
		w.Write(sampleRate * blockAlign);
		w.Write((short)blockAlign);
		w.Write((short)bits);
		if (extensible)
		{
			w.Write((short)22);          // cbSize
			w.Write((short)bits);        // valid bits
			w.Write(0);                  // channel mask
			w.Write((short)format);      // sub format GUID, first two bytes
			w.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 });
		}

		w.Write("data"u8);
		w.Write(frames.Length * bytesPerSample);
		foreach (var v in frames)
		{
			if (format == 3)
			{
				if (bits == 32) w.Write(v);
				else w.Write((double)v);
				continue;
			}

			switch (bits)
			{
				case 8:
					w.Write((byte)Math.Clamp((int)Math.Round(v * 128) + 128, 0, 255));
					break;
				case 16:
					w.Write((short)Math.Clamp((int)Math.Round(v * 32768), short.MinValue, short.MaxValue));
					break;
				case 24:
					var s24 = Math.Clamp((int)Math.Round(v * 8388608), -8388608, 8388607);
					w.Write((byte)s24);
					w.Write((byte)(s24 >> 8));
					w.Write((byte)(s24 >> 16));
					break;
				case 32:
					w.Write((int)Math.Clamp(Math.Round(v * 2147483648.0), int.MinValue, int.MaxValue));
					break;
			}
		}

		w.Flush();
		return stream.ToArray();
	}

	public static float[] Sine(double frequency, int sampleRate, int frames, float amplitude = 0.5f, int channels = 1)
	{
		var samples = new float[frames * channels];
		for (var i = 0; i < frames; i++)
		{
			var v = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
			for (var c = 0; c < channels; c++) samples[i * channels + c] = v;
		}
		return samples;
	}

	/// <summary>Estimates the frequency of one channel from its rising zero crossings (with linear interpolation).</summary>
	public static double Frequency(ReadOnlySpan<float> interleaved, int channels, int channel, int sampleRate, int skipFrames = 0)
	{
		var frames = interleaved.Length / channels;
		double first = -1, last = -1;
		var crossings = 0;
		for (var i = skipFrames + 1; i < frames - skipFrames; i++)
		{
			var a = interleaved[(i - 1) * channels + channel];
			var b = interleaved[i * channels + channel];
			if (a < 0 && b >= 0)
			{
				var t = i - 1 + a / (a - b);
				if (first < 0) first = t;
				else crossings++;
				last = t;
			}
		}

		return crossings == 0 ? 0 : crossings * sampleRate / (last - first);
	}

	public static float Peak(ReadOnlySpan<float> interleaved, int channels, int channel, int skipFrames = 0)
	{
		var peak = 0f;
		for (var i = skipFrames; i < interleaved.Length / channels - skipFrames; i++) peak = Math.Max(peak, Math.Abs(interleaved[i * channels + channel]));
		return peak;
	}

	public static AudioMixer Mixer(int maxVoices = 64, AudioInterpolation interpolation = AudioInterpolation.Linear, VoiceStealing stealing = VoiceStealing.Oldest, int commandCapacity = 1024) =>
		new(new AudioConfig { OutputRate = Rate, MaxVoices = maxVoices, Interpolation = interpolation, VoiceStealing = stealing, CommandCapacity = commandCapacity });

	/// <summary>Flushes the game side and renders <paramref name="frames"/> frames.</summary>
	public static float[] Render(AudioMixer mixer, int frames)
	{
		mixer.Flush();
		var buffer = new float[frames * 2];
		mixer.Render(buffer);
		return buffer;
	}

	public static SoundEffect Constant(float value, int frames, int channels = 1) =>
		new("constant", Enumerable.Repeat(value, frames * channels).ToArray(), channels, Rate);

	/// <summary>A mono sound whose frame i has the value i / scale.</summary>
	public static SoundEffect Ramp(int frames, float scale) =>
		new("ramp", Enumerable.Range(0, frames).Select(i => i / scale).ToArray(), 1, Rate);
}
