global using System.Numerics;

global using Microsoft.Extensions.DependencyInjection;

global using Xunit;

global using static Ion.Tests.TestConstants;

using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

using Ion.Testing;

using Ion.Examples.Quad;
using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Vulkan;

namespace Ion.Extensions.Graphics.Vulkan.Tests;

/// <summary>What the machine running the tests offers.</summary>
internal static class TestEnvironment
{
	private static readonly Lazy<bool> Vulkan = new(VulkanDevice.IsAvailable);

	/// <summary>A Vulkan driver with a device (lavapipe on CI).</summary>
	public static bool HasVulkan => Vulkan.Value;

	/// <summary>A display to open windows on (X11/Wayland on Linux, always on Windows and macOS).</summary>
	public static bool HasDisplay => !OperatingSystem.IsLinux()
		|| !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
		|| !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

	/// <summary>The path of a golden image in this project's <c>Golden</c> folder (the source folder, so updates land in the repository).</summary>
	public static string GoldenPath(string name, [CallerFilePath] string sourceFile = "")
	{
		var sourceDirectory = Path.GetDirectoryName(sourceFile);
		if (!string.IsNullOrEmpty(sourceDirectory) && Directory.Exists(sourceDirectory)) return Path.Combine(sourceDirectory, "Golden", name);
		return Path.Combine(AppContext.BaseDirectory, "Golden", name);
	}
}

/// <summary>
/// Collects error logs (Vulkan validation errors among them) so a test can assert that a device ran clean. Validation is
/// on for every test device; without the Khronos validation layer installed nothing is reported and the checks pass.
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

	/// <summary>A validated headless device that logs here.</summary>
	public VulkanDevice CreateDevice(int framesInFlight = 2) =>
		VulkanDevice.Create(new VulkanDeviceOptions { Validation = true, FramesInFlight = framesInFlight }, this);

	/// <summary>Turns validation on in <paramref name="host"/> and routes its error logs here.</summary>
	public IonTestHost Attach(IonTestHost host) => host
		.WithConfiguration("Ion:Graphics:Validation", "true")
		.Configure(services => services.AddLogging(logging => logging.AddProvider(this)));

	public void Dispose() { }
}

/// <summary>A test that needs a Vulkan driver; skipped when there is none.</summary>
public sealed class VulkanFactAttribute : FactAttribute
{
	/// <summary>Skips the test when no Vulkan driver is installed.</summary>
	public VulkanFactAttribute()
	{
		if (!TestEnvironment.HasVulkan) Skip = "No Vulkan driver (on Linux install Mesa lavapipe: mesa-vulkan-drivers).";
	}
}

/// <summary>A test that opens a window and renders with Vulkan; skipped without a display or a Vulkan driver.</summary>
public sealed class WindowedVulkanTheoryAttribute : TheoryAttribute
{
	/// <summary>Skips the test when there is no display or no Vulkan driver.</summary>
	public WindowedVulkanTheoryAttribute()
	{
		if (!TestEnvironment.HasDisplay) Skip = "No display (run under xvfb-run on Linux).";
		else if (!TestEnvironment.HasVulkan) Skip = "No Vulkan driver.";
	}
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
internal static class QuadAssert
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
