using Ion.Examples.Quad;
using Ion.Extensions.Graphics.Rhi;
using Ion.Testing;

namespace Ion.Extensions.Graphics.Vulkan.Tests;

/// <summary>
/// The Vulkan backend without a surface (the headless path), on whatever Vulkan driver is installed (Mesa lavapipe on CI).
/// Every test runs with validation on (when the Khronos layer is installed) and fails on validation errors, including
/// objects leaked past the device.
/// </summary>
public class HeadlessRenderingTests
{
	private static readonly Vector4 Black = new(0, 0, 0, 1);

	private static Screenshot Read(VulkanDevice device, ITexture target)
	{
		var size = target.Width * target.Height * 4;
		using var readback = device.CreateBuffer(new BufferDescriptor(size, BufferUsage.MapRead | BufferUsage.CopyDst));
		var encoder = device.CreateCommandEncoder();
		encoder.CopyTextureToBuffer(target, TextureRegion.Whole(target), readback, 0, target.Width * 4);
		device.Queue.Submit(encoder.Finish());
		var pixels = new byte[size];
		readback.Read(0, pixels);
		return new Screenshot((int)target.Width, (int)target.Height, pixels);
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void RendersTheCheckerboardQuadIntoAnOffscreenTarget()
	{
		var log = new ValidationLog();
		Screenshot shot;
		using (var device = log.CreateDevice())
		{
			Assert.Null(device.Surface);
			Assert.Equal(ShaderLanguage.SpirV, device.ShaderLanguage);

			using var target = device.CreateTexture(new TextureDescriptor(64, 64, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));
			using var quad = new TexturedQuad(device, TextureFormat.Rgba8Unorm);

			var encoder = device.CreateCommandEncoder();
			quad.Draw(encoder, new RenderPassColorAttachment(target.DefaultView, LoadOp.Clear, StoreOp.Store, Black));
			device.Queue.Submit(encoder.Finish());

			shot = Read(device, target);
		}

		log.AssertClean();
		QuadAssert.Checkerboard(shot, 64, new Rgba8(0, 0, 0, 255));
		GoldenImage.AssertMatches(shot, TestEnvironment.GoldenPath("textured_quad_64.png"), tolerance: 2);
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void WriteBufferIsOrderedWithSubmissions()
	{
		var log = new ValidationLog();
		using (var device = log.CreateDevice())
		{
			using var source = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.CopySrc | BufferUsage.CopyDst));
			using var readback = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.MapRead | BufferUsage.CopyDst));

			device.Queue.WriteBuffer(source, 0, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
			var first = device.CreateCommandEncoder();
			first.CopyBufferToBuffer(source, 0, readback, 0, 16);
			device.Queue.Submit(first.Finish());

			// A write after the submission must not be seen by it, only by later ones; two writes to one buffer in the same
			// upload batch apply in order.
			device.Queue.WriteBuffer(source, 4, [98, 98]);
			device.Queue.WriteBuffer(source, 4, [99, 99]);
			var bytes = new byte[16];
			readback.Read(0, bytes);
			Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, bytes);

			var second = device.CreateCommandEncoder();
			second.CopyBufferToBuffer(source, 0, readback, 0, 16);
			device.Queue.Submit(second.Finish());
			readback.Read(0, bytes);
			Assert.Equal(new byte[] { 1, 2, 3, 4, 99, 99, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, bytes);
		}

		log.AssertClean();
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void WriteTextureHonoursTheRegionAndRowPitch()
	{
		var log = new ValidationLog();
		Screenshot shot;
		using (var device = log.CreateDevice())
		{
			using var texture = device.CreateTexture(new TextureDescriptor(4, 4, TextureFormat.Rgba8Unorm, TextureUsage.CopyDst | TextureUsage.CopySrc));
			device.Queue.WriteTexture(texture, new byte[4 * 4 * 4]);

			// A 2x2 block at (1, 2), given with a padded row pitch of 16 bytes (8 used).
			var data = new byte[16 * 2];
			for (var i = 0; i < 8; i++) data[i] = data[16 + i] = 200;
			device.Queue.WriteTexture(texture, data, 16, new TextureRegion(1, 2, 2, 2));

			shot = Read(device, texture);
		}

		log.AssertClean();
		Assert.Equal(new Rgba8(0, 0, 0, 0), shot.GetPixel(0, 2));
		Assert.Equal(new Rgba8(200, 200, 200, 200), shot.GetPixel(1, 2));
		Assert.Equal(new Rgba8(200, 200, 200, 200), shot.GetPixel(2, 3));
		Assert.Equal(new Rgba8(0, 0, 0, 0), shot.GetPixel(3, 3));
		Assert.Equal(new Rgba8(0, 0, 0, 0), shot.GetPixel(1, 1));
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void FramesInFlightRecycleResources()
	{
		var log = new ValidationLog();
		var seen = new HashSet<int>();
		Screenshot shot;
		using (var device = log.CreateDevice(framesInFlight: 3))
		{
			Assert.Equal(3, device.FramesInFlight);
			using var target = device.CreateTexture(new TextureDescriptor(8, 8, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));

			for (var frame = 0; frame < 10; frame++)
			{
				device.BeginFrame();
				seen.Add(device.FrameIndex);

				// A per-frame buffer disposed while in use: destroyed once the frame slot comes round again.
				var buffer = device.CreateBuffer(new BufferDescriptor(256, BufferUsage.Uniform | BufferUsage.CopyDst));
				device.Queue.WriteBuffer(buffer, 0, new byte[256]);
				var encoder = device.CreateCommandEncoder();
				var pass = encoder.BeginRenderPass(new RenderPassDescriptor([new RenderPassColorAttachment(target.DefaultView, LoadOp.Clear, StoreOp.Store, new Vector4(frame / 10f, 0, 0, 1))]));
				pass.End();
				device.Queue.Submit(encoder.Finish());
				buffer.Dispose();

				device.EndFrame();
			}

			shot = Read(device, target);
		}

		log.AssertClean();
		Assert.Equal([0, 1, 2], seen.Order());
		QuadAssert.AssertPixel(shot, 4, 4, new Rgba8(230, 0, 0, 255), tolerance: 1);
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void TestHostRendersAndCapturesWithHeadlessRendering()
	{
		var log = new ValidationLog();
		Screenshot shot;
		using (var host = log.Attach(new IonTestHost())
			.WithRendering(64, 64)
			.WithConfiguration("Ion:Graphics:ClearColorHex", "#202020")
			.WithSystem<QuadTestSystem>())
		{
			host.Step(3);

			Assert.Equal(3, host.Get<QuadTestSystem>().Draws);
			shot = host.Screenshot();
		}

		log.AssertClean();
		QuadAssert.Checkerboard(shot, 64, new Rgba8(0x20, 0x20, 0x20, 255));
		GoldenImage.AssertMatches(shot, TestEnvironment.GoldenPath("test_host_quad_64.png"), tolerance: 2);
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void AFrameWithoutPassesIsClearedToTheClearColor()
	{
		var log = new ValidationLog();
		Screenshot shot;
		using (var host = log.Attach(new IonTestHost()).WithRendering(16, 8).WithConfiguration("Ion:Graphics:ClearColorHex", "#336699"))
		{
			host.Step();
			shot = host.Screenshot();
		}

		log.AssertClean();
		Assert.Equal((16, 8), (shot.Width, shot.Height));
		for (var y = 0; y < shot.Height; y++)
		{
			for (var x = 0; x < shot.Width; x++) QuadAssert.AssertPixel(shot, x, y, new Rgba8(0x33, 0x66, 0x99, 0xFF), tolerance: 0);
		}
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void CaptureBeforeTheFirstFrameThrows()
	{
		using var host = new IonTestHost().WithRendering(8, 8).Start();
		Assert.Throws<InvalidOperationException>(() => host.Screenshot());
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void SaveScreenshotWritesAPng()
	{
		var path = Path.Combine(Path.GetTempPath(), $"ion-shot-{Guid.NewGuid():N}.png");
		try
		{
			using (var host = new IonTestHost().WithRendering(32, 32).WithSystem<QuadTestSystem>())
			{
				host.Step();
				host.Get<IScreenshotSource>().SaveScreenshot(path);
			}

			var loaded = GoldenImage.Load(path);
			QuadAssert.Checkerboard(loaded, 32, new Rgba8(0, 0, 0, 255));
		}
		finally
		{
			File.Delete(path);
		}
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void TheFrameExposesTheDeviceAndTargets()
	{
		using var host = new IonTestHost().WithRendering(24, 16);
		host.Start();
		var frame = host.Get<IGraphicsFrame>();

		Assert.False(frame.IsRendering);
		Assert.Equal(GraphicsBackend.Vulkan, frame.Device.Backend);
		Assert.Equal(TextureFormat.Rgba8Unorm, frame.ColorFormat);
		Assert.Equal(TextureFormat.Depth32Float, frame.DepthFormat);
		Assert.Equal((24u, 16u), (frame.Width, frame.Height));
	}
}
