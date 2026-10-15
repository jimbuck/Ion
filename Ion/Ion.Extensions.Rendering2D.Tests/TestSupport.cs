global using System.Numerics;

global using Microsoft.Extensions.DependencyInjection;

global using Xunit;

global using Ion.Extensions.Graphics;
global using Ion.Testing;

global using static Ion.Tests.TestConstants;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering2D.Tests;

/// <summary>A test that needs a Vulkan driver; skipped when there is none.</summary>
public sealed class VulkanFactAttribute : FactAttribute
{
	/// <summary>Skips the test when no Vulkan driver is installed.</summary>
	public VulkanFactAttribute()
	{
		if (!RenderingEnvironment.HasVulkan) Skip = "No Vulkan driver (on Linux install Mesa lavapipe: mesa-vulkan-drivers).";
	}
}

/// <summary>A theory that needs a Vulkan driver; skipped when there is none.</summary>
public sealed class VulkanTheoryAttribute : TheoryAttribute
{
	/// <summary>Skips the test when no Vulkan driver is installed.</summary>
	public VulkanTheoryAttribute()
	{
		if (!RenderingEnvironment.HasVulkan) Skip = "No Vulkan driver (on Linux install Mesa lavapipe: mesa-vulkan-drivers).";
	}
}

/// <summary>A CPU-only <see cref="IGraphicsFrame"/> for exercising the sprite batch's recording path without a GPU.</summary>
internal sealed class NoGpuFrame : IGraphicsFrame
{
	public IGraphicsDevice Device => throw new InvalidOperationException("No GPU in this test.");
	public bool IsRendering => false;
	public ITexture? ColorTarget => null;
	public ITexture? DepthTarget => null;
	public TextureFormat ColorFormat => TextureFormat.Rgba8Unorm;
	public TextureFormat DepthFormat => TextureFormat.Undefined;
	public uint Width => 64;
	public uint Height => 64;
	public Vector4 ClearColor => Vector4.Zero;
	public RenderPassColorAttachment ColorAttachment() => throw new InvalidOperationException();
	public RenderPassDepthStencilAttachment? DepthAttachment() => null;
}

internal static class CpuTexture
{
	/// <summary>A texture with an explicit size and no GPU resource, for the recording path.</summary>
	public static SpriteTexture Create(string name, uint width, uint height) => new Texture2D(name, null, width, height, 1, tracker: null);
}

/// <summary>Writes small PNG test images into the output's Assets folder, which the asset storage reads.</summary>
internal static class TestImages
{
	public static readonly Rgba32 Red = new(255, 0, 0, 255);
	public static readonly Rgba32 Green = new(0, 255, 0, 255);
	public static readonly Rgba32 Blue = new(0, 0, 255, 255);
	public static readonly Rgba32 HalfWhite = new(255, 255, 255, 128);

	/// <summary>Writes a <paramref name="width"/> by <paramref name="height"/> image filled by <paramref name="pixel"/> and returns its asset path.</summary>
	public static string Write(string name, int width, int height, Func<int, int, Rgba32> pixel)
	{
		var directory = Path.Combine(AppContext.BaseDirectory, "Assets");
		Directory.CreateDirectory(directory);
		using var image = new Image<Rgba32>(width, height);
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++) image[x, y] = pixel(x, y);
		}

		image.SaveAsPng(Path.Combine(directory, name));
		return name;
	}

	/// <summary>A 2x2 image: red, green / blue, half-transparent white.</summary>
	public static string Quadrants(string name = "quadrants.png") =>
		Write(name, 2, 2, (x, y) => (x, y) switch { (0, 0) => Red, (1, 0) => Green, (0, 1) => Blue, _ => HalfWhite });

	/// <summary>Asserts that the pixel at (<paramref name="x"/>, <paramref name="y"/>) is within <paramref name="tolerance"/> of the expected color.</summary>
	public static void AssertPixel(Screenshot shot, int x, int y, byte r, byte g, byte b, int tolerance = 2)
	{
		var actual = shot.GetPixel(x, y);
		var expected = new Rgba8(r, g, b, actual.A);
		Assert.True(actual.MaxChannelDifference(expected) <= tolerance, $"Pixel ({x}, {y}) is {actual}, expected {expected}.");
	}
}
