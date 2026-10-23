using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ion.Tools;

/// <summary>The result of <see cref="ImageDiff.Compare"/>.</summary>
/// <param name="SameSize">Whether both images have the same size.</param>
/// <param name="Width">The actual image's width.</param>
/// <param name="Height">The actual image's height.</param>
/// <param name="MismatchedPixels">Pixels whose largest channel difference exceeds the tolerance (every pixel when the sizes differ).</param>
/// <param name="MaxChannelDifference">The largest channel difference seen (0 to 255).</param>
/// <param name="DiffPath">The diff image written, if any.</param>
public readonly record struct ImageDiffResult(bool SameSize, int Width, int Height, int MismatchedPixels, int MaxChannelDifference, string? DiffPath)
{
	/// <summary>The fraction of mismatched pixels.</summary>
	public double MismatchRatio => Width * Height == 0 ? 1 : (double)MismatchedPixels / (Width * Height);

	/// <summary>Whether the images match within the tolerance and <paramref name="maxMismatchRatio"/>.</summary>
	public bool Matches(double maxMismatchRatio = 0) => SameSize && MismatchRatio <= maxMismatchRatio;
}

/// <summary>
/// Golden-image comparison for the CLI and the MCP server, with the same rule as <c>Ion.Testing.GoldenImage</c>: a pixel
/// mismatches when any RGBA channel differs by more than the tolerance; the diff image shows mismatching pixels in red over a
/// dimmed copy of the expected image.
/// </summary>
public static class ImageDiff
{
	/// <summary>Compares the PNG at <paramref name="actualPath"/> with <paramref name="expectedPath"/>.</summary>
	public static ImageDiffResult Compare(string actualPath, string expectedPath, int tolerance = 2, string? diffPath = null)
	{
		using var actual = Image.Load<Rgba32>(actualPath);
		using var expected = Image.Load<Rgba32>(expectedPath);
		if (actual.Width != expected.Width || actual.Height != expected.Height)
		{
			return new ImageDiffResult(false, actual.Width, actual.Height, actual.Width * actual.Height, 255, null);
		}

		var mismatched = 0;
		var max = 0;
		using var diff = new Image<Rgba32>(actual.Width, actual.Height);
		for (var y = 0; y < actual.Height; y++)
		{
			for (var x = 0; x < actual.Width; x++)
			{
				var a = actual[x, y];
				var e = expected[x, y];
				var d = Math.Max(Math.Max(Math.Abs(a.R - e.R), Math.Abs(a.G - e.G)), Math.Max(Math.Abs(a.B - e.B), Math.Abs(a.A - e.A)));
				max = Math.Max(max, d);
				var bad = d > tolerance;
				if (bad) mismatched++;
				diff[x, y] = bad ? new Rgba32(255, 0, 0, 255) : new Rgba32((byte)(e.R / 4), (byte)(e.G / 4), (byte)(e.B / 4), 255);
			}
		}

		string? written = null;
		if (diffPath is not null)
		{
			var full = Path.GetFullPath(diffPath);
			Directory.CreateDirectory(Path.GetDirectoryName(full)!);
			diff.SaveAsPng(full);
			written = full;
		}

		return new ImageDiffResult(true, actual.Width, actual.Height, mismatched, max, written);
	}
}
