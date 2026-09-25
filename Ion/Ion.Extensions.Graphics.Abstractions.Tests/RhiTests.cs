using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics.Abstractions.Tests;

public class RhiTests
{
	[Theory]
	[InlineData(TextureFormat.R8Unorm, 1)]
	[InlineData(TextureFormat.Rg8Unorm, 2)]
	[InlineData(TextureFormat.Rgba8Unorm, 4)]
	[InlineData(TextureFormat.Bgra8UnormSrgb, 4)]
	[InlineData(TextureFormat.Rgba16Float, 8)]
	[InlineData(TextureFormat.Rgba32Float, 16)]
	[InlineData(TextureFormat.Depth32Float, 4)]
	public void BytesPerPixel(TextureFormat format, int expected)
	{
		Assert.Equal(expected, format.BytesPerPixel());
	}

	[Fact]
	public void EveryDefinedFormatHasASize()
	{
		foreach (var format in Enum.GetValues<TextureFormat>())
		{
			if (format == TextureFormat.Undefined) continue;
			Assert.True(format.BytesPerPixel() > 0, format.ToString());
		}
	}

	[Fact]
	public void DepthAndStencilFormats()
	{
		Assert.True(TextureFormat.Depth32Float.IsDepth());
		Assert.False(TextureFormat.Depth32Float.HasStencil());
		Assert.True(TextureFormat.Depth24PlusStencil8.HasStencil());
		Assert.False(TextureFormat.Rgba8Unorm.IsDepth());
		Assert.True(TextureFormat.Rgba8UnormSrgb.IsSrgb());
	}

	[Fact]
	public void VertexFormatSizes()
	{
		Assert.Equal(8u, VertexFormat.Float32x2.Size());
		Assert.Equal(16u, VertexFormat.Float32x4.Size());
		Assert.Equal(4u, VertexFormat.Unorm8x4.Size());
	}

	[Fact]
	public void SamplerDefaultsAreNearestClamp()
	{
		var sampler = new SamplerDescriptor();
		Assert.Equal(FilterMode.Nearest, sampler.MinFilter);
		Assert.Equal(AddressMode.ClampToEdge, sampler.AddressModeU);
		Assert.Equal(FilterMode.Linear, SamplerDescriptor.LinearClamp.MagFilter);
		Assert.Null(sampler.Compare);
	}

	[Fact]
	public void BlendPresets()
	{
		Assert.Equal(BlendFactor.SrcAlpha, BlendState.AlphaBlend.Color.SrcFactor);
		Assert.Equal(BlendFactor.OneMinusSrcAlpha, BlendState.AlphaBlend.Color.DstFactor);
		Assert.Equal(BlendFactor.One, BlendState.PremultipliedAlpha.Color.SrcFactor);
		Assert.Equal(BlendFactor.One, BlendState.Additive.Color.DstFactor);
	}

	[Fact]
	public void RenderPassDescriptorTakesAStackSpan()
	{
		var descriptor = new RenderPassDescriptor([]);
		Assert.True(descriptor.ColorAttachments.IsEmpty);
		Assert.Null(descriptor.DepthStencilAttachment);
	}

	[Fact]
	public void ScreenshotReadsPixelsTopLeftOrigin()
	{
		var rgba = new byte[2 * 2 * 4];
		rgba[(1 * 2 + 1) * 4 + 0] = 10; // (1, 1) red
		rgba[(1 * 2 + 1) * 4 + 3] = 255;
		var shot = new Screenshot(2, 2, rgba);

		Assert.Equal(new Rgba8(10, 0, 0, 255), shot.GetPixel(1, 1));
		Assert.Equal(new Rgba8(0, 0, 0, 0), shot.GetPixel(0, 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => shot.GetPixel(2, 0));
		Assert.Throws<ArgumentException>(() => new Screenshot(2, 2, new byte[3]));
	}

	[Fact]
	public void Rgba8Difference()
	{
		Assert.Equal(7, new Rgba8(10, 20, 30, 40).MaxChannelDifference(new Rgba8(12, 13, 30, 40)));
		Assert.Equal("#0A141E28", new Rgba8(10, 20, 30, 40).ToString());
	}

	[Fact]
	public void GraphicsConfigDefaults()
	{
		var config = new GraphicsConfig();
		Assert.Equal(2, config.FramesInFlight);
		Assert.Null(config.Validation);
		Assert.True(config.DepthBuffer);
		Assert.False(config.RetainLastFrame);
		Assert.Equal(WindowPlatform.Auto, new WindowConfig().Platform);
		Assert.True(new WindowConfig().Resizable);
	}
}
