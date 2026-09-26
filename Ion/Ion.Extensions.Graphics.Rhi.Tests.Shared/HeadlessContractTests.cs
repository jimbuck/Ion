using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Ion.Core;
using Ion.Examples.Quad;
using Ion.Testing;

namespace Ion.Extensions.Graphics.Rhi.Tests;

/// <summary>
/// The RHI contract without a surface (the headless path), run on every backend: a backend's test project derives a
/// sealed class marked with <see cref="RhiBackendAttribute"/>. Every device runs with validation on (Vulkan: the Khronos
/// layer when installed; GLES: <c>glGetError</c> after every submission) and a test fails on logged errors. Pixel results
/// are compared with one set of golden PNGs shared by all backends, so the backends render identically.
/// </summary>
public abstract class HeadlessContractTests
{
	private static readonly Vector4 Black = new(0, 0, 0, 1);

	/// <summary>The backend of the concrete test class (its <see cref="RhiBackendAttribute"/>).</summary>
	protected GraphicsBackend Backend => GetType().GetCustomAttribute<RhiBackendAttribute>()?.Backend
		?? throw new InvalidOperationException($"{GetType().Name} needs an [RhiBackend] attribute.");

	/// <summary>Creates a validated headless device on <see cref="Backend"/>.</summary>
	protected virtual IGraphicsDevice CreateDevice(ValidationLog log, int framesInFlight = 2) => log.CreateDevice(Backend, framesInFlight);

	/// <summary>Configures a test host to render headless on <see cref="Backend"/>.</summary>
	protected virtual IonTestHost ConfigureHost(IonTestHost host) => host.WithConfiguration("Ion:Graphics:PreferredBackend", Backend.ToString());

	private IonTestHost NewHost(ValidationLog? log = null) => ConfigureHost(log is null ? new IonTestHost() : log.Attach(new IonTestHost()));

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void RendersTheCheckerboardQuadIntoAnOffscreenTarget()
	{
		var log = new ValidationLog();
		Screenshot shot;
		using (var device = CreateDevice(log))
		{
			Assert.Null(device.Surface);
			Assert.Equal(Backend, device.Backend);
			Assert.Equal(RhiBackends.ShaderLanguageOf(Backend), device.ShaderLanguage);

			using var target = device.CreateTexture(new TextureDescriptor(64, 64, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));
			using var quad = new TexturedQuad(device, TextureFormat.Rgba8Unorm);

			var encoder = device.CreateCommandEncoder();
			quad.Draw(encoder, new RenderPassColorAttachment(target.DefaultView, LoadOp.Clear, StoreOp.Store, Black));
			device.Queue.Submit(encoder.Finish());

			shot = Readback.Read(device, target);
		}

		log.AssertClean();
		QuadAssert.Checkerboard(shot, 64, new Rgba8(0, 0, 0, 255));
		GoldenImage.AssertMatches(shot, TestEnvironment.GoldenPath("textured_quad_64.png"), tolerance: 2);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void WriteBufferIsOrderedWithSubmissions()
	{
		var log = new ValidationLog();
		using (var device = CreateDevice(log))
		{
			using var source = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.CopySrc | BufferUsage.CopyDst));
			using var readback = device.CreateBuffer(new BufferDescriptor(16, BufferUsage.MapRead | BufferUsage.CopyDst));

			device.Queue.WriteBuffer(source, 0, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
			var first = device.CreateCommandEncoder();
			first.CopyBufferToBuffer(source, 0, readback, 0, 16);
			device.Queue.Submit(first.Finish());

			// A write after the submission must not be seen by it, only by later ones; two writes to one buffer apply in order.
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

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void AWriteBeforeSubmissionIsSeenByCommandsRecordedEarlier()
	{
		// Recording does not execute: a write made after recording but before submission is seen (WebGPU queue order).
		var log = new ValidationLog();
		using (var device = CreateDevice(log))
		{
			using var source = device.CreateBuffer(new BufferDescriptor(4, BufferUsage.CopySrc | BufferUsage.CopyDst));
			using var readback = device.CreateBuffer(new BufferDescriptor(4, BufferUsage.MapRead | BufferUsage.CopyDst));
			device.Queue.WriteBuffer(source, 0, [1, 1, 1, 1]);
			var encoder = device.CreateCommandEncoder();
			encoder.CopyBufferToBuffer(source, 0, readback, 0, 4);
			var commands = encoder.Finish();
			device.Queue.WriteBuffer(source, 0, [7, 7, 7, 7]);
			device.Queue.Submit(commands);

			var bytes = new byte[4];
			readback.Read(0, bytes);
			Assert.Equal(new byte[] { 7, 7, 7, 7 }, bytes);
		}

		log.AssertClean();
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void WriteTextureHonoursTheRegionAndRowPitch()
	{
		var log = new ValidationLog();
		Screenshot shot;
		using (var device = CreateDevice(log))
		{
			using var texture = device.CreateTexture(new TextureDescriptor(4, 4, TextureFormat.Rgba8Unorm, TextureUsage.CopyDst | TextureUsage.CopySrc));
			device.Queue.WriteTexture(texture, new byte[4 * 4 * 4]);

			// A 2x2 block at (1, 2), given with a padded row pitch of 16 bytes (8 used).
			var data = new byte[16 * 2];
			for (var i = 0; i < 8; i++) data[i] = data[16 + i] = 200;
			device.Queue.WriteTexture(texture, data, 16, new TextureRegion(1, 2, 2, 2));

			shot = Readback.Read(device, texture);
		}

		log.AssertClean();
		Assert.Equal(new Rgba8(0, 0, 0, 0), shot.GetPixel(0, 2));
		Assert.Equal(new Rgba8(200, 200, 200, 200), shot.GetPixel(1, 2));
		Assert.Equal(new Rgba8(200, 200, 200, 200), shot.GetPixel(2, 3));
		Assert.Equal(new Rgba8(0, 0, 0, 0), shot.GetPixel(3, 3));
		Assert.Equal(new Rgba8(0, 0, 0, 0), shot.GetPixel(1, 1));
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void FramesInFlightRecycleResources()
	{
		var log = new ValidationLog();
		var seen = new HashSet<int>();
		Screenshot shot;
		using (var device = CreateDevice(log, framesInFlight: 3))
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

			shot = Readback.Read(device, target);
		}

		log.AssertClean();
		Assert.Equal([0, 1, 2], seen.Order());
		QuadAssert.AssertPixel(shot, 4, 4, new Rgba8(230, 0, 0, 255), tolerance: 1);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void RenderingIntoATextureAndSamplingItKeepsRowZeroAtTheTop()
	{
		// Pass 1 renders the checkerboard into a texture, pass 2 samples that texture over the whole target: the result must
		// equal rendering the checkerboard directly (no vertical flip anywhere, whatever the API's native origin).
		var log = new ValidationLog();
		Screenshot shot;
		using (var device = CreateDevice(log))
		{
			using var intermediate = device.CreateTexture(new TextureDescriptor(64, 64, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.TextureBinding));
			using var target = device.CreateTexture(new TextureDescriptor(64, 64, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));
			using var quad = new TexturedQuad(device, TextureFormat.Rgba8Unorm);
			using var sampler = device.CreateSampler(SamplerDescriptor.PointClamp);
			using var copy = new QuadRenderer(device, TextureFormat.Rgba8Unorm, intermediate.DefaultView, sampler);

			var encoder = device.CreateCommandEncoder();
			quad.Draw(encoder, new RenderPassColorAttachment(intermediate.DefaultView, LoadOp.Clear, StoreOp.Store, Black));
			device.Queue.Submit(encoder.Finish());
			copy.Draw(target, new Vector4(1, 1, 1, 1));

			shot = Readback.Read(device, target);
		}

		log.AssertClean();
		QuadAssert.Checkerboard(shot, 64, new Rgba8(0, 0, 0, 255));
		GoldenImage.AssertMatches(shot, TestEnvironment.GoldenPath("textured_quad_64.png"), tolerance: 2);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void ViewportsAndScissorsTakeTopLeftOrigins()
	{
		var log = new ValidationLog();
		Screenshot viewport, scissor;
		using (var device = CreateDevice(log))
		{
			using var texture = device.CreateTexture(new TextureDescriptor(1, 1, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst));
			device.Queue.WriteTexture(texture, [255, 255, 255, 255]);
			using var sampler = device.CreateSampler(SamplerDescriptor.PointClamp);
			using var target = device.CreateTexture(new TextureDescriptor(64, 64, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));
			using var quad = new QuadRenderer(device, TextureFormat.Rgba8Unorm, texture.DefaultView, sampler);

			// The full-target quad into the top-left quadrant's viewport.
			quad.Draw(target, Black, viewport: (0, 0, 32, 32));
			viewport = Readback.Read(device, target);

			// The full-target quad clipped to the top-right quadrant.
			quad.Draw(target, Black, scissor: (32, 0, 32, 32));
			scissor = Readback.Read(device, target);
		}

		log.AssertClean();
		var white = new Rgba8(255, 255, 255, 255);
		var black = new Rgba8(0, 0, 0, 255);
		QuadAssert.AssertPixel(viewport, 8, 8, white);
		QuadAssert.AssertPixel(viewport, 40, 8, black);
		QuadAssert.AssertPixel(viewport, 8, 40, black);
		QuadAssert.AssertPixel(scissor, 40, 8, white);
		QuadAssert.AssertPixel(scissor, 8, 8, black);
		QuadAssert.AssertPixel(scissor, 40, 40, black);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void FaceCullingFollowsTheWebGpuWinding()
	{
		// The quad's indices wind clockwise in y-up clip space: a back face under the default counter-clockwise front face.
		var log = new ValidationLog();
		Screenshot cullBack, cullFront;
		using (var device = CreateDevice(log))
		{
			using var texture = device.CreateTexture(new TextureDescriptor(1, 1, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst));
			device.Queue.WriteTexture(texture, [255, 255, 255, 255]);
			using var sampler = device.CreateSampler(SamplerDescriptor.PointClamp);
			using var target = device.CreateTexture(new TextureDescriptor(16, 16, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));

			using (var back = new QuadRenderer(device, TextureFormat.Rgba8Unorm, texture.DefaultView, sampler, CullMode.Back))
			{
				back.Draw(target, Black);
				cullBack = Readback.Read(device, target);
			}

			using var front = new QuadRenderer(device, TextureFormat.Rgba8Unorm, texture.DefaultView, sampler, CullMode.Front);
			front.Draw(target, Black);
			cullFront = Readback.Read(device, target);
		}

		log.AssertClean();
		QuadAssert.AssertPixel(cullBack, 8, 8, new Rgba8(0, 0, 0, 255));
		QuadAssert.AssertPixel(cullFront, 8, 8, new Rgba8(255, 255, 255, 255));
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void IndexedDrawsHonourTheBaseVertex()
	{
		var log = new ValidationLog();
		Screenshot shot;
		using (var device = CreateDevice(log))
		{
			using var texture = device.CreateTexture(new TextureDescriptor(1, 1, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst));
			device.Queue.WriteTexture(texture, [0, 255, 0, 255]);
			using var sampler = device.CreateSampler(SamplerDescriptor.PointClamp);
			using var target = device.CreateTexture(new TextureDescriptor(16, 16, TextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc));
			using var quad = new QuadRenderer(device, TextureFormat.Rgba8Unorm, texture.DefaultView, sampler);

			// Base vertex 4: the second quad in the buffer, the right half of the target.
			quad.Draw(target, Black, baseVertex: 4);
			shot = Readback.Read(device, target);
		}

		log.AssertClean();
		QuadAssert.AssertPixel(shot, 4, 8, new Rgba8(0, 0, 0, 255));
		QuadAssert.AssertPixel(shot, 12, 8, new Rgba8(0, 255, 0, 255));
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void TestHostRendersAndCapturesWithHeadlessRendering()
	{
		var log = new ValidationLog();
		Screenshot shot;
		using (var host = NewHost(log)
			.WithRendering(64, 64)
			.WithConfiguration("Ion:Graphics:ClearColorHex", "#202020")
			.WithSystem<QuadTestSystem>())
		{
			host.Step(3);

			Assert.Equal(3, host.Get<QuadTestSystem>().Draws);
			Assert.Equal(Backend, host.Get<IGraphicsFrame>().Device.Backend);
			shot = host.Screenshot();
		}

		log.AssertClean();
		QuadAssert.Checkerboard(shot, 64, new Rgba8(0x20, 0x20, 0x20, 255));
		GoldenImage.AssertMatches(shot, TestEnvironment.GoldenPath("test_host_quad_64.png"), tolerance: 2);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void AFrameWithoutPassesIsClearedToTheClearColor()
	{
		var log = new ValidationLog();
		Screenshot shot;
		using (var host = NewHost(log).WithRendering(16, 8).WithConfiguration("Ion:Graphics:ClearColorHex", "#336699"))
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

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void CaptureBeforeTheFirstFrameThrows()
	{
		using var host = NewHost().WithRendering(8, 8).Start();
		Assert.Throws<InvalidOperationException>(() => host.Screenshot());
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void SaveScreenshotWritesAPng()
	{
		var path = Path.Combine(Path.GetTempPath(), $"ion-shot-{Guid.NewGuid():N}.png");
		try
		{
			using (var host = NewHost().WithRendering(32, 32).WithSystem<QuadTestSystem>())
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

	[RhiFact, Trait(CATEGORY, E2E)]
	public void TheQuadSampleMatchesItsGolden()
	{
		var log = new ValidationLog();
		var shot = QuadSample.Run(Backend, headless: true, log, ConfigureSample);
		log.AssertClean();
		GoldenImage.AssertMatches(shot, TestEnvironment.GoldenPath("quad_sample_320x240.png"), tolerance: 2);
	}

	/// <summary>Extra configuration for the quad sample app (for example a GLES feature level).</summary>
	protected virtual void ConfigureSample(IDictionary<string, string?> settings) { }

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void TheFrameExposesTheDeviceAndTargets()
	{
		using var host = NewHost().WithRendering(24, 16);
		host.Start();
		var frame = host.Get<IGraphicsFrame>();

		Assert.False(frame.IsRendering);
		Assert.Equal(Backend, frame.Device.Backend);
		Assert.Equal(TextureFormat.Rgba8Unorm, frame.ColorFormat);
		Assert.Equal(TextureFormat.Depth32Float, frame.DepthFormat);
		Assert.Equal((24u, 16u), (frame.Width, frame.Height));
	}
}

/// <summary>Runs the quad sample app (<see cref="QuadApp"/>) for a few frames and captures the last one.</summary>
public static class QuadSample
{
	/// <summary>
	/// Builds the sample exactly as its Program does (windowed or headless) on <paramref name="backend"/> at 320x240 with the
	/// quad standing still, runs 3 frames and returns the last frame.
	/// </summary>
	public static Screenshot Run(GraphicsBackend backend, bool headless, ValidationLog log, Action<IDictionary<string, string?>>? configure = null)
	{
		var settings = new Dictionary<string, string?>
		{
			["Ion:Headless"] = headless ? "true" : "false",
			["Ion:Graphics:PreferredBackend"] = backend.ToString(),
			["Ion:Graphics:Validation"] = "true",
			["Ion:Graphics:RetainLastFrame"] = "true",
			["Ion:Graphics:VSync"] = "false",
			["Ion:Graphics:ClearColorHex"] = "#203040",
			["Ion:Window:Width"] = "320",
			["Ion:Window:Height"] = "240",
			["Ion:Window:Platform"] = "Glfw",
			["Quad:Spin"] = "false",
		};
		configure?.Invoke(settings);
		var builder = IonApplication.CreateBuilder();
		builder.Configuration.AddInMemoryCollection(settings);
		builder.Services.AddLogging(logging => logging.ClearProviders().AddProvider(log));
		QuadApp.Configure(builder.Services, builder.Configuration);
		using var app = builder.Build();
		QuadApp.Use(app);
		var loop = app.Build();
		loop.Initialize();
		try
		{
			for (var i = 0; i < 3; i++) loop.Step();
			Assert.Equal(backend, app.Services.GetRequiredService<IGraphicsFrame>().Device.Backend);
			return app.Services.GetRequiredService<IScreenshotSource>().Capture();
		}
		finally
		{
			loop.Shutdown();
		}
	}
}
