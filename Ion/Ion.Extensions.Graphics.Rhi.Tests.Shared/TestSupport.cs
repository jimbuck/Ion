global using System.Numerics;

global using Microsoft.Extensions.DependencyInjection;

global using Xunit;

global using static Ion.Tests.TestConstants;

using System.Runtime.CompilerServices;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Ion.Testing;

using Ion.Examples.Quad;
using Ion.Extensions.Graphics.GLES;
using Ion.Extensions.Graphics.Rhi;
using Ion.Extensions.Graphics.Vulkan;

namespace Ion.Extensions.Graphics.Rhi.Tests;

/// <summary>What the machine running the tests offers.</summary>
public static class TestEnvironment
{
	private static readonly Lazy<bool> Vulkan = new(VulkanDevice.IsAvailable);
	private static readonly Lazy<bool> Gles = new(GlesDevice.IsHeadlessAvailable);

	/// <summary>A Vulkan driver with a device (lavapipe on CI).</summary>
	public static bool HasVulkan => Vulkan.Value;

	/// <summary>EGL with an OpenGL ES 3 driver for headless contexts (llvmpipe on CI).</summary>
	public static bool HasHeadlessGles => Gles.Value;

	/// <summary>A display to open windows on (X11/Wayland on Linux, always on Windows and macOS).</summary>
	public static bool HasDisplay => !OperatingSystem.IsLinux()
		|| !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
		|| !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

	/// <summary>Why a test on <paramref name="backend"/> cannot run here, or null when it can.</summary>
	public static string? SkipReason(GraphicsBackend backend, bool windowed)
	{
		if (windowed && !HasDisplay) return "No display (run under xvfb-run on Linux).";
		return backend switch
		{
			GraphicsBackend.Vulkan when !HasVulkan => "No Vulkan driver (on Linux install Mesa lavapipe: mesa-vulkan-drivers).",
			// Windowed GLES needs only the window's context; headless needs EGL.
			GraphicsBackend.OpenGLES when !windowed && !HasHeadlessGles => "No EGL OpenGL ES 3 driver (on Linux install Mesa: libegl1 libegl-mesa0).",
			_ => null,
		};
	}

	/// <summary>
	/// The path of a golden image in the shared <c>Golden</c> folder of this project (the source folder, so updates land in
	/// the repository): one set of goldens for every backend.
	/// </summary>
	public static string GoldenPath(string name) => _goldenPath(name);

	private static string _goldenPath(string name, [CallerFilePath] string sourceFile = "")
	{
		var sourceDirectory = Path.GetDirectoryName(sourceFile);
		if (!string.IsNullOrEmpty(sourceDirectory) && Directory.Exists(sourceDirectory)) return Path.Combine(sourceDirectory, "Golden", name);
		return Path.Combine(AppContext.BaseDirectory, "Golden", name);
	}
}

/// <summary>
/// The backends under test: creating devices, configuring test hosts and windowed apps for one backend.
/// </summary>
public static class RhiBackends
{
	/// <summary>A validated headless device on <paramref name="backend"/> that logs to <paramref name="logger"/>.</summary>
	public static IGraphicsDevice CreateDevice(GraphicsBackend backend, ILogger logger, int framesInFlight = 2, GlesFeatureLevel glesLevel = GlesFeatureLevel.Es32) => backend switch
	{
		GraphicsBackend.Vulkan => VulkanDevice.Create(new VulkanDeviceOptions { Validation = true, FramesInFlight = framesInFlight }, logger),
		GraphicsBackend.OpenGLES => GlesDevice.Create(new GlesDeviceOptions { Validation = true, FramesInFlight = framesInFlight, MaxFeatureLevel = glesLevel }, logger),
		_ => throw new NotSupportedException($"No RHI backend for {backend}."),
	};

	/// <summary>The shader language of <paramref name="backend"/>.</summary>
	public static ShaderLanguage ShaderLanguageOf(GraphicsBackend backend) => backend == GraphicsBackend.OpenGLES ? ShaderLanguage.GlslEs : ShaderLanguage.SpirV;

	/// <summary>Registers the windowed backend (after <c>AddSilkWindowing</c>).</summary>
	public static void AddWindowed(GraphicsBackend backend, IServiceCollection services, IConfiguration config)
	{
		if (backend == GraphicsBackend.OpenGLES) services.AddGlesGraphics(config);
		else services.AddVulkanGraphics(config);
	}

	/// <summary>Adds the windowed backend's system (after <c>UseSilkWindowing</c>).</summary>
	public static void UseWindowed(GraphicsBackend backend, IIonApplication app)
	{
		if (backend == GraphicsBackend.OpenGLES) app.UseGlesGraphics();
		else app.UseVulkanGraphics();
	}
}

/// <summary>
/// Collects error logs (Vulkan validation errors, GL errors) so a test can assert that a device ran clean. Validation is on
/// for every test device.
/// </summary>
public sealed class ValidationLog : ILoggerProvider, ILogger
{
	private readonly System.Collections.Concurrent.ConcurrentQueue<string> _errors = new();

	public IReadOnlyCollection<string> Errors => _errors;

	public ILogger CreateLogger(string categoryName) => this;

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (IsEnabled(logLevel)) _errors.Enqueue(formatter(state, exception));
	}

	/// <summary>Fails when any error was logged.</summary>
	public void AssertClean() => Assert.True(_errors.IsEmpty, "Errors were logged:\n" + string.Join("\n", _errors));

	/// <summary>A validated headless Vulkan device that logs here.</summary>
	public VulkanDevice CreateDevice(int framesInFlight = 2) =>
		VulkanDevice.Create(new VulkanDeviceOptions { Validation = true, FramesInFlight = framesInFlight }, this);

	/// <summary>A validated headless device on <paramref name="backend"/> that logs here.</summary>
	public IGraphicsDevice CreateDevice(GraphicsBackend backend, int framesInFlight = 2, GlesFeatureLevel glesLevel = GlesFeatureLevel.Es32) =>
		RhiBackends.CreateDevice(backend, this, framesInFlight, glesLevel);

	/// <summary>Turns validation on in <paramref name="host"/> and routes its error logs here.</summary>
	public IonTestHost Attach(IonTestHost host) => host
		.WithConfiguration("Ion:Graphics:Validation", "true")
		.Configure(services => services.AddLogging(logging => logging.AddProvider(this)));

	public void Dispose() { }
}

/// <summary>Draws the textured quad every frame through <see cref="IGraphicsFrame"/>.</summary>
public sealed class QuadTestSystem(IGraphicsFrame frame) : IDisposable
{
	private TexturedQuad? _quad;

	public int Draws { get; private set; }

	[Init]
	public void Init(GameTime dt) => _quad = new TexturedQuad(frame.Device, frame.ColorFormat, frame.DepthFormat);

	[Render]
	public void Render(GameTime dt)
	{
		if (_quad is null || !frame.IsRendering) return;
		_quad.Draw(frame);
		Draws++;
	}

	[Destroy]
	public void Destroy(GameTime dt)
	{
		_quad?.Dispose();
		_quad = null;
	}

	public void Dispose() => _quad?.Dispose();
}

/// <summary>Pixel assertions for the 4x4 checkerboard quad spanning the middle half of a square target.</summary>
public static class QuadAssert
{
	/// <summary>
	/// The quad spans [-0.5, 0.5] in clip space, so on a <paramref name="size"/>-pixel square target it covers the middle half
	/// and each checker texel is size / 8 pixels wide. Checks the background and four texels.
	/// </summary>
	public static void Checkerboard(Screenshot shot, int size, Rgba8 background)
	{
		Assert.Equal(size, shot.Width);
		Assert.Equal(size, shot.Height);
		var texel = size / 8;
		var origin = size / 4;
		int Center(int t) => origin + t * texel + texel / 2;

		AssertPixel(shot, 2, 2, background);
		AssertPixel(shot, size - 3, size - 3, background);
		AssertPixel(shot, Center(0), Center(0), TexturedQuad.ColorA); // texel (0, 0), top-left
		AssertPixel(shot, Center(1), Center(0), TexturedQuad.ColorB); // texel (1, 0)
		AssertPixel(shot, Center(0), Center(1), TexturedQuad.ColorB); // texel (0, 1)
		AssertPixel(shot, Center(3), Center(3), TexturedQuad.ColorA); // texel (3, 3), bottom-right
	}

	public static void AssertPixel(Screenshot shot, int x, int y, Rgba8 expected, int tolerance = 1)
	{
		var actual = shot.GetPixel(x, y);
		Assert.True(actual.MaxChannelDifference(expected) <= tolerance, $"Pixel ({x}, {y}) is {actual}, expected {expected}.");
	}
}

/// <summary>Reads textures back through the RHI.</summary>
public static class Readback
{
	/// <summary>Copies an RGBA8 <paramref name="target"/> into a readback buffer and returns its pixels.</summary>
	public static Screenshot Read(IGraphicsDevice device, ITexture target)
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
}
