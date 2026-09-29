using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Ion.Extensions.Graphics;

namespace Ion.Testing;

/// <summary>
/// Golden-image comparison for rendering tests: compares a <see cref="Screenshot"/> with a reference PNG, pixel by pixel,
/// with a per-channel tolerance (GPU drivers differ in rounding and rasterization rules).
/// </summary>
/// <remarks>
/// <para>
/// Updating: when the golden file does not exist, <see cref="AssertMatches"/> writes the actual image there and fails (so a
/// missing golden never passes silently on CI); review the image and commit it. Set the environment variable
/// <see cref="UpdateVariable"/> to <c>1</c> to overwrite goldens with the actual images and pass.
/// </para>
/// <para>
/// On a mismatch, <c>&lt;golden&gt;.actual.png</c> and <c>&lt;golden&gt;.diff.png</c> (mismatching pixels in red over a dimmed copy
/// of the golden) are written next to the golden file.
/// </para>
/// </remarks>
public static class GoldenImage
{
	/// <summary>The environment variable that turns on golden updates: <c>ION_UPDATE_GOLDEN=1</c>.</summary>
	public const string UpdateVariable = "ION_UPDATE_GOLDEN";

	/// <summary>
	/// Compares <paramref name="actual"/> with <paramref name="expected"/>: a pixel mismatches when any channel differs by
	/// more than <paramref name="tolerance"/> (0 to 255).
	/// </summary>
	public static GoldenComparison Compare(Screenshot actual, Screenshot expected, int tolerance = 2)
	{
		ArgumentNullException.ThrowIfNull(actual);
		ArgumentNullException.ThrowIfNull(expected);
		ArgumentOutOfRangeException.ThrowIfNegative(tolerance);
		if (actual.Width != expected.Width || actual.Height != expected.Height)
		{
			return new GoldenComparison(false, actual.Width * actual.Height, 255, (actual.Width, actual.Height), (expected.Width, expected.Height), null);
		}

		var mismatched = 0;
		var maxDifference = 0;
		(int X, int Y)? first = null;
		for (var y = 0; y < actual.Height; y++)
		{
			for (var x = 0; x < actual.Width; x++)
			{
				var difference = actual.GetPixel(x, y).MaxChannelDifference(expected.GetPixel(x, y));
				maxDifference = Math.Max(maxDifference, difference);
				if (difference <= tolerance) continue;
				mismatched++;
				first ??= (x, y);
			}
		}

		return new GoldenComparison(true, mismatched, maxDifference, (actual.Width, actual.Height), (expected.Width, expected.Height), first);
	}

	/// <summary>
	/// Asserts that <paramref name="actual"/> matches the PNG at <paramref name="goldenPath"/>: at most
	/// <paramref name="maxMismatchRatio"/> of the pixels may differ by more than <paramref name="tolerance"/> per channel.
	/// </summary>
	/// <exception cref="GoldenImageException">The images differ, or the golden file was missing (it has now been written).</exception>
	public static GoldenComparison AssertMatches(Screenshot actual, string goldenPath, int tolerance = 2, double maxMismatchRatio = 0)
	{
		ArgumentNullException.ThrowIfNull(actual);
		ArgumentException.ThrowIfNullOrEmpty(goldenPath);

		var update = Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true";
		if (update || !File.Exists(goldenPath))
		{
			Save(actual, goldenPath);
			if (update) return new GoldenComparison(true, 0, 0, (actual.Width, actual.Height), (actual.Width, actual.Height), null);
			throw new GoldenImageException($"Golden image '{goldenPath}' did not exist; the actual image was written there. Review it, commit it and run the test again.");
		}

		var expected = Load(goldenPath);
		var comparison = Compare(actual, expected, tolerance);
		var allowed = (int)Math.Floor(maxMismatchRatio * actual.Width * actual.Height);
		if (comparison.SameSize && comparison.MismatchedPixels <= allowed) return comparison;

		var actualPath = Path.ChangeExtension(goldenPath, ".actual.png");
		Save(actual, actualPath);
		if (comparison.SameSize) SaveDiff(actual, expected, tolerance, Path.ChangeExtension(goldenPath, ".diff.png"));
		throw new GoldenImageException($"Image does not match golden '{goldenPath}': {comparison}. Actual image written to '{actualPath}'.");
	}

	/// <summary>Reads a PNG (or any format ImageSharp decodes) as a screenshot.</summary>
	public static Screenshot Load(string path)
	{
		using var image = Image.Load<Rgba32>(path);
		var pixels = new byte[image.Width * image.Height * 4];
		image.CopyPixelDataTo(pixels);
		return new Screenshot(image.Width, image.Height, pixels);
	}

	/// <summary>Writes <paramref name="screenshot"/> as a PNG, creating the directory.</summary>
	public static void Save(Screenshot screenshot, string path)
	{
		ArgumentNullException.ThrowIfNull(screenshot);
		var directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		using var image = Image.LoadPixelData<Rgba32>(screenshot.Rgba, screenshot.Width, screenshot.Height);
		image.SaveAsPng(path);
	}

	private static void SaveDiff(Screenshot actual, Screenshot expected, int tolerance, string path)
	{
		var pixels = new byte[actual.Rgba.Length];
		for (var y = 0; y < actual.Height; y++)
		{
			for (var x = 0; x < actual.Width; x++)
			{
				var i = (y * actual.Width + x) * 4;
				var e = expected.GetPixel(x, y);
				var bad = actual.GetPixel(x, y).MaxChannelDifference(e) > tolerance;
				pixels[i] = bad ? (byte)255 : (byte)(e.R / 4);
				pixels[i + 1] = bad ? (byte)0 : (byte)(e.G / 4);
				pixels[i + 2] = bad ? (byte)0 : (byte)(e.B / 4);
				pixels[i + 3] = 255;
			}
		}

		Save(new Screenshot(actual.Width, actual.Height, pixels), path);
	}
}

/// <summary>The result of <see cref="GoldenImage.Compare"/>.</summary>
/// <param name="SameSize">Whether the images have the same size (when not, every pixel counts as mismatched).</param>
/// <param name="MismatchedPixels">The number of pixels outside the tolerance.</param>
/// <param name="MaxChannelDifference">The largest per-channel difference seen.</param>
/// <param name="ActualSize">The actual image size.</param>
/// <param name="ExpectedSize">The golden image size.</param>
/// <param name="FirstMismatch">The first mismatching pixel (row-major), if any.</param>
public readonly record struct GoldenComparison(bool SameSize, int MismatchedPixels, int MaxChannelDifference, (int Width, int Height) ActualSize, (int Width, int Height) ExpectedSize, (int X, int Y)? FirstMismatch)
{
	/// <inheritdoc/>
	public override string ToString() => SameSize
		? $"{MismatchedPixels} mismatched pixels, max channel difference {MaxChannelDifference}{(FirstMismatch is { } p ? $", first at ({p.X}, {p.Y})" : "")}"
		: $"size {ActualSize.Width}x{ActualSize.Height} differs from golden {ExpectedSize.Width}x{ExpectedSize.Height}";
}

/// <summary>A golden-image assertion failed.</summary>
public sealed class GoldenImageException(string message) : Exception(message);
