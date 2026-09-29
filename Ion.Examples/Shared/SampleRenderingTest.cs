using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion.Extensions.Graphics;
using Ion.Testing;

using Xunit;

namespace Ion.Examples.Tests;

/// <summary>A test that needs a Vulkan driver; skipped when there is none.</summary>
public sealed class VulkanFactAttribute : FactAttribute
{
	/// <summary>Skips the test when no Vulkan driver is installed.</summary>
	public VulkanFactAttribute()
	{
		if (!RenderingEnvironment.HasVulkan) Skip = "No Vulkan driver (on Linux install Mesa lavapipe: mesa-vulkan-drivers).";
	}
}

/// <summary>A test that renders headless with OpenGL ES through EGL; skipped when there is no EGL OpenGL ES 3 driver.</summary>
public sealed class GlesFactAttribute : FactAttribute
{
	/// <summary>Skips the test when EGL cannot create an OpenGL ES 3 context.</summary>
	public GlesFactAttribute()
	{
		if (!RenderingEnvironment.HasHeadlessGles) Skip = "No EGL OpenGL ES 3 driver (on Linux install Mesa: libegl1 libegl-mesa0).";
	}
}

/// <summary>A test that opens a window and renders with Vulkan; skipped without a display or a Vulkan driver.</summary>
public sealed class WindowedVulkanFactAttribute : FactAttribute
{
	/// <summary>Skips the test when there is no display or no Vulkan driver.</summary>
	public WindowedVulkanFactAttribute()
	{
		if (!RenderingEnvironment.HasDisplay) Skip = "No display (run under xvfb-run on Linux).";
		else if (!RenderingEnvironment.HasVulkan) Skip = "No Vulkan driver.";
	}
}

/// <summary>What a windowed run measured.</summary>
/// <param name="Frames">Frames run.</param>
/// <param name="AverageFrameMilliseconds">Mean wall-clock time per frame (after the first frames).</param>
/// <param name="WorstFrameMilliseconds">The slowest frame.</param>
/// <param name="LastFrame">The sprite batch statistics of the last frame.</param>
public readonly record struct WindowedRun(int Frames, double AverageFrameMilliseconds, double WorstFrameMilliseconds, SpriteBatchStatistics LastFrame);

/// <summary>
/// Runs a sample's own <c>Program.cs</c> in a real window (Silk.NET GLFW, the Vulkan swapchain, validation on) for some
/// frames, shuts it down and fails on any logged error, teardown included.
/// </summary>
public static class SampleWindowed
{
	/// <summary>
	/// Runs the entry point of <typeparamref name="TProgram"/>'s assembly up to its <c>Run()</c> call (see
	/// <see cref="IonEntryPoint"/>), with <paramref name="settings"/> over the windowed test settings, then runs
	/// <paramref name="frames"/> frames of the application it built, on the real clock.
	/// </summary>
	public static WindowedRun Run<TProgram>(int frames, params (string Key, string Value)[] settings)
	{
		var log = new ErrorLog();
		var values = new Dictionary<string, string?>
		{
			["Ion:Graphics:Validation"] = "true",
			["Ion:Graphics:VSync"] = "false",
			["Ion:Assets:HotReload"] = "false",
			["Ion:Metrics:Profiling"] = "false",
			["Ion:MaxFPS"] = "0",
		};
		foreach (var (key, value) in settings) values[key] = value;

		using var program = IonEntryPoint.Start<TProgram>(
			configure: builder => builder.Configuration.AddInMemoryCollection(values),
			beforeBuild: builder => builder.Services.AddLogging(logging => logging.AddProvider(log)));
		var app = program.Application;
		var loop = app.Build();
		var times = new List<double>(frames);
		SpriteBatchStatistics last = default;
		try
		{
			loop.Initialize();
			var watch = new System.Diagnostics.Stopwatch();
			for (var i = 0; i < frames && !loop.IsExitRequested; i++)
			{
				watch.Restart();
				loop.Step();
				times.Add(watch.Elapsed.TotalMilliseconds);
			}

			if (app.Services.GetService<ISpriteBatch>() is ISpriteBatchStatistics stats) last = stats.LastFrameStatistics;
		}
		finally
		{
			loop.Shutdown();
			program.Dispose();
		}

		log.AssertClean();
		var steady = times.Count > 10 ? times.Skip(5).ToList() : times;
		return new WindowedRun(times.Count, steady.Average(), steady.Max(), last);
	}
}

/// <summary>
/// Runs a sample headless with rendering on (the Vulkan backend into an offscreen target, validation on) for some frames
/// on <c>backend</c> and returns the last frame; fails on any logged error (validation included, teardown too).
/// </summary>
public static class SampleRendering
{
	/// <summary>Steps <paramref name="host"/> (already given the game) for <paramref name="frames"/> frames at <paramref name="width"/> by <paramref name="height"/>.</summary>
	public static Screenshot Capture(IonTestHost host, uint width, uint height, int frames, Action<IonTestHost>? inspect = null, GraphicsBackend backend = GraphicsBackend.Vulkan)
	{
		var log = new ErrorLog();
		Screenshot shot;
		using (host)
		{
			log.Attach(host).WithRendering(width, height).WithConfiguration("Ion:Graphics:PreferredBackend", backend.ToString());
			host.Step(frames);
			shot = host.Screenshot();
			Assert.Equal(backend, host.Get<IGraphicsFrame>().Device.Backend);
			inspect?.Invoke(host);
		}

		log.AssertClean();
		return shot;
	}
}
