using System.Numerics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion;
using Ion.Examples.Quad;
using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Vulkan;
using Ion.Extensions.Windowing;

// A spinning checkerboard quad on the Silk.NET stack: Silk.NET window (GLFW or SDL, Ion:Window:Platform), Vulkan RHI
// backend, shaders compiled at build time. Options (command line or appsettings):
//   --Ion:Headless=true         render offscreen, no window
//   --Quad:Frames=<n>           exit after n frames
//   --Quad:Screenshot=<file>    save the last frame as PNG on exit (headless, or windowed with Ion:Graphics:RetainLastFrame=true)
//   --Ion:Window:Platform=Sdl   use SDL instead of GLFW
var builder = IonApplication.CreateBuilder(args);
QuadApp.Configure(builder.Services, builder.Configuration);

using var app = builder.Build();
QuadApp.Use(app);
app.Run();

namespace Ion.Examples.Quad
{
	/// <summary>The app setup, shared with the tests.</summary>
	public static class QuadApp
	{
		/// <summary>Registers the window (unless headless), the Vulkan backend and the quad system.</summary>
		public static void Configure(IServiceCollection services, IConfiguration config)
		{
			if (IsHeadless(config))
			{
				services.AddInputTracker();
				services.AddVulkanGraphics(config, VulkanGraphicsMode.Offscreen);
			}
			else
			{
				services.AddSilkWindowing(config);
				services.AddVulkanGraphics(config);
			}

			services.AddSingleton<QuadSystem>();
		}

		/// <summary>Adds the systems.</summary>
		public static void Use(IIonApplication app)
		{
			app.UseEvents();
			if (!IsHeadless(app.Configuration)) app.UseSilkWindowing();
			app.UseVulkanGraphics();
			app.UseSystem<QuadSystem>();
		}

		private static bool IsHeadless(IConfiguration config) => bool.TryParse(config["Ion:Headless"], out var headless) && headless;
	}

	/// <summary>
	/// Creates the quad after the device exists (Init, default order, after <see cref="StageOrder.Graphics"/>), spins it in
	/// Update, draws it in Render, and handles <c>Quad:Frames</c> and <c>Quad:Screenshot</c>.
	/// </summary>
	public sealed class QuadSystem(IGraphicsFrame frame, IScreenshotSource screenshots, IEvents events, IConfiguration config, ILogger<QuadSystem> logger) : IDisposable
	{
		private readonly long _frames = long.TryParse(config["Quad:Frames"], out var frames) ? frames : 0;
		private readonly string? _screenshot = config["Quad:Screenshot"];
		private TexturedQuad? _quad;
		private float _angle;
		private long _rendered;
		private bool _done;

		[Init]
		public void Init(GameTime dt)
		{
			_quad = new TexturedQuad(frame.Device, frame.ColorFormat, frame.DepthFormat);
			logger.LogInformation("Quad ready on {Adapter} ({Backend}), target {Width}x{Height} {Format}.", frame.Device.AdapterName, frame.Device.Backend, frame.Width, frame.Height, frame.ColorFormat);
		}

		[Update]
		public void Update(GameTime dt)
		{
			_angle += dt.Delta;
		}

		[Render]
		public void Render(GameTime dt)
		{
			if (_quad is null || !frame.IsRendering) return;
			var aspect = frame.Width / (float)Math.Max(1, frame.Height);
			_quad.SetTransform(Matrix4x4.CreateRotationZ(_angle) * Matrix4x4.CreateScale(1f / aspect, 1f, 1f));
			_quad.Draw(frame);
			_rendered++;
		}

		[Last]
		public void Last(GameTime dt)
		{
			if (_done || _frames <= 0 || _rendered < _frames) return;
			_done = true;
			if (!string.IsNullOrEmpty(_screenshot))
			{
				screenshots.SaveScreenshot(_screenshot);
				logger.LogInformation("Saved {Path}.", Path.GetFullPath(_screenshot));
			}

			events.Emit<ExitGameEvent>();
		}

		/// <summary>Releases the quad's GPU resources before the device goes (Destroy, default order).</summary>
		[Destroy]
		public void Destroy(GameTime dt) => Dispose();

		/// <summary>Releases the quad's GPU resources.</summary>
		public void Dispose()
		{
			_quad?.Dispose();
			_quad = null;
		}
	}
}
