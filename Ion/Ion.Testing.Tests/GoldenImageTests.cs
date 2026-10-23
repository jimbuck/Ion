using Ion.Extensions.Graphics;

namespace Ion.Testing.Tests;

public class GoldenImageTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ion-golden-{Guid.NewGuid():N}");

	public void Dispose()
	{
		if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
	}

	private static Screenshot Solid(int width, int height, byte r, byte g, byte b)
	{
		var pixels = new byte[width * height * 4];
		for (var i = 0; i < pixels.Length; i += 4)
		{
			pixels[i] = r;
			pixels[i + 1] = g;
			pixels[i + 2] = b;
			pixels[i + 3] = 255;
		}

		return new Screenshot(width, height, pixels);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void IdenticalImagesMatch()
	{
		var comparison = GoldenImage.Compare(Solid(4, 4, 10, 20, 30), Solid(4, 4, 10, 20, 30));
		Assert.True(comparison.SameSize);
		Assert.Equal(0, comparison.MismatchedPixels);
		Assert.Null(comparison.FirstMismatch);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DifferencesWithinTheToleranceMatch()
	{
		var comparison = GoldenImage.Compare(Solid(4, 4, 10, 20, 30), Solid(4, 4, 12, 20, 29), tolerance: 2);
		Assert.Equal(0, comparison.MismatchedPixels);
		Assert.Equal(2, comparison.MaxChannelDifference);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DifferencesBeyondTheToleranceAreCounted()
	{
		var actual = Solid(4, 4, 10, 20, 30);
		actual.Rgba[(2 * 4 + 1) * 4] = 200; // (1, 2)
		var comparison = GoldenImage.Compare(actual, Solid(4, 4, 10, 20, 30), tolerance: 2);
		Assert.Equal(1, comparison.MismatchedPixels);
		Assert.Equal((1, 2), comparison.FirstMismatch);
		Assert.Contains("1 mismatched", comparison.ToString());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DifferentSizesNeverMatch()
	{
		var comparison = GoldenImage.Compare(Solid(4, 4, 0, 0, 0), Solid(4, 5, 0, 0, 0));
		Assert.False(comparison.SameSize);
		Assert.Contains("4x5", comparison.ToString());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AMissingGoldenIsWrittenAndFails()
	{
		var path = Path.Combine(_directory, "new.png");
		Assert.Throws<GoldenImageException>(() => GoldenImage.AssertMatches(Solid(3, 2, 1, 2, 3), path));
		Assert.True(File.Exists(path));

		// Now it exists: the same image passes.
		var comparison = GoldenImage.AssertMatches(Solid(3, 2, 1, 2, 3), path);
		Assert.Equal(0, comparison.MismatchedPixels);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AMismatchWritesTheActualAndDiffImages()
	{
		var path = Path.Combine(_directory, "golden.png");
		GoldenImage.Save(Solid(4, 4, 0, 0, 0), path);

		var ex = Assert.Throws<GoldenImageException>(() => GoldenImage.AssertMatches(Solid(4, 4, 255, 255, 255), path));
		Assert.Contains("16 mismatched", ex.Message);
		Assert.True(File.Exists(Path.Combine(_directory, "golden.actual.png")));
		Assert.True(File.Exists(Path.Combine(_directory, "golden.diff.png")));

		// A mismatch ratio lets some pixels differ.
		var actual = Solid(4, 4, 0, 0, 0);
		actual.Rgba[0] = 255;
		GoldenImage.AssertMatches(actual, path, maxMismatchRatio: 1.0 / 16);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void PngRoundTripsThePixels()
	{
		var path = Path.Combine(_directory, "roundtrip.png");
		var image = Solid(5, 3, 7, 8, 9);
		image.Rgba[4] = 250;
		GoldenImage.Save(image, path);

		var loaded = GoldenImage.Load(path);
		Assert.Equal((5, 3), (loaded.Width, loaded.Height));
		Assert.Equal(image.Rgba, loaded.Rgba);
	}
}
