using System.Numerics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion;
using Ion.Examples.Cubes;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

// Immediate-mode 3D without ECS: 1,000 cubes in a 40 x 25 grid, two materials (so two instanced draws per pass), a sun
// with shadows onto a ground plane, a camera orbiting the grid, and a HUD drawn by the sprite batch on top.
//   --Cubes:Frames=<n>          exit after n frames
//   --Cubes:Screenshot=<file>   with Frames, save the last frame as PNG (headless, or windowed with Ion:Graphics:RetainLastFrame=true)
//   --Ion:Headless=true         no window (add --Ion:Headless:Render=true to render offscreen)
var builder = IonApplication.CreateBuilder(args);
CubesApp.Configure(builder);

using var game = builder.Build();
CubesApp.Use(game);
game.Run();

namespace Ion.Examples.Cubes
{
	/// <summary>The sample's setup, shared with the tests.</summary>
	public static class CubesApp
	{
		/// <summary>Registers the engine, the 3D renderer and the sample's system.</summary>
		public static IonApplicationBuilder Configure(IonApplicationBuilder builder)
		{
			builder.Services.AddIon(builder.Configuration);
			builder.Services.AddRendering3D(builder.Configuration);
			builder.Services.AddSingleton<CubesSystem>();
			return builder;
		}

		/// <summary>Adds the engine's systems, the 3D renderer's and the sample's.</summary>
		public static IIonApplication Use(IIonApplication app) => app.UseIon().UseRendering3D().UseSystem<CubesSystem>();
	}

	/// <summary>Creates the meshes and materials, submits the scene every frame and draws the HUD.</summary>
	public sealed class CubesSystem(IRenderer3D renderer, ISpriteBatch sprites, IAssetManager assets, IConfiguration config, IServiceProvider services, IEvents events, ILogger<CubesSystem> logger)
	{
		/// <summary>Cubes along x.</summary>
		public const int Columns = 40;

		/// <summary>Cubes along z.</summary>
		public const int Rows = 25;

		private const float Spacing = 1.25f;

		private readonly long _frames = long.TryParse(config["Cubes:Frames"], out var frames) ? frames : 0;
		private readonly string? _screenshot = config["Cubes:Screenshot"];
		private MeshHandle _cube;
		private MeshHandle _ground;
		private MaterialHandle _red;
		private MaterialHandle _blue;
		private MaterialHandle _groundMaterial;
		private IFont? _font;
		private string _statsText = "";
		private Rendering3DStatistics _shownStats;
		private long _rendered;
		private bool _done;

		/// <summary>The camera's orbit angle at time zero, in radians.</summary>
		public float StartAngle { get; set; } = 0.6f;

		/// <summary>The orbit speed in radians per second.</summary>
		public float OrbitSpeed { get; set; } = 0.2f;

		/// <summary>Creates the meshes, materials and the HUD font.</summary>
		[Init]
		public void Init(GameTime dt)
		{
			_cube = renderer.CreateMesh(MeshPrimitives.Cube(0.8f));
			_ground = renderer.CreateMesh(MeshPrimitives.Plane(80f, 8));
			_red = renderer.CreateMaterial(new PbrMaterial(new Color(0xE0, 0x4A, 0x3B), metallic: 0f, roughness: 0.45f));
			_blue = renderer.CreateMaterial(new PbrMaterial(new Color(0x3B, 0x8E, 0xE0), metallic: 0.6f, roughness: 0.3f));
			_groundMaterial = renderer.CreateMaterial(new PbrMaterial(new Color(0x8A, 0x8A, 0x80), metallic: 0f, roughness: 0.9f));
			renderer.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0x9C, 0xB0, 0xD0), AmbientIntensity = 0.35f });
			_font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(20);
			logger.LogInformation("{Count} cubes in {Columns}x{Rows}, 2 materials.", Columns * Rows, Columns, Rows);
		}

		/// <summary>Submits the camera, the light, the ground and the cubes, and draws the HUD.</summary>
		[Render]
		public void Render(GameTime dt)
		{
			var t = (float)dt.Elapsed.TotalSeconds;
			var angle = StartAngle + t * OrbitSpeed;
			var eye = new Vector3(MathF.Cos(angle) * 34f, 20f, MathF.Sin(angle) * 34f);
			renderer.SetCamera(new Camera { FieldOfView = MathF.PI / 4, Near = 0.5f, Far = 200f, ClearColor = new Color(0x87, 0xA9, 0xD6) },
				Transform.LookAt(eye, new Vector3(0, 0, 0)));
			renderer.AddLight(new DirectionalLight(new Color(0xFF, 0xF4, 0xE0), intensity: 3f), Vector3.Normalize(new Vector3(-0.65f, -0.6f, -0.3f)));

			renderer.Draw(_ground, _groundMaterial, Matrix4x4.Identity);
			var cube = new MeshRenderer(_cube, _red);
			for (var z = 0; z < Rows; z++)
			{
				for (var x = 0; x < Columns; x++)
				{
					var px = (x - (Columns - 1) * 0.5f) * Spacing;
					var pz = (z - (Rows - 1) * 0.5f) * Spacing;
					var y = 0.4f + 1.2f * (0.5f + 0.5f * MathF.Sin(px * 0.35f + t * 1.5f) * MathF.Cos(pz * 0.3f + t));
					cube.Material = ((x + z) & 1) == 0 ? _red : _blue;
					var world = Matrix4x4.CreateRotationY(px * 0.1f + t * 0.5f) * Matrix4x4.CreateTranslation(px, y, pz);
					renderer.Submit(cube, world);
				}
			}

			if (_font is not null)
			{
				var stats = renderer.LastFrameStatistics;
				if (stats with { Frame = 0 } != _shownStats with { Frame = 0 })
				{
					_shownStats = stats;
					_statsText = $"{stats.Visible} visible, {stats.Batches} batches, {stats.DrawCalls} draw calls, {stats.Triangles:N0} triangles";
				}

				sprites.DrawRect(new Color(0, 0, 0, 0.45f), new Vector2(8, 8), new Vector2(560, 64));
				sprites.DrawString(_font, "Ion 3D: 1,000 instanced cubes", new Vector2(18, 14), Color.White);
				sprites.DrawString(_font, _statsText, new Vector2(18, 42), new Color(0xD0, 0xE0, 0xFF), scale: 0.7f);
			}

			_rendered++;
		}

		/// <summary>Handles <c>Cubes:Frames</c> and <c>Cubes:Screenshot</c>.</summary>
		[Last]
		public void Last(GameTime dt)
		{
			if (_done || _frames <= 0 || _rendered < _frames) return;
			_done = true;
			if (!string.IsNullOrEmpty(_screenshot) && services.GetService<IScreenshotSource>() is { } screenshots)
			{
				screenshots.SaveScreenshot(_screenshot);
				logger.LogInformation("Saved {Path}.", Path.GetFullPath(_screenshot));
			}

			var stats = renderer.LastFrameStatistics;
			logger.LogInformation("Done after {Frames} frames: {Visible} visible, {Batches} batches, {DrawCalls} draw calls, {Triangles} triangles, {Casters} shadow casters.",
				_rendered, stats.Visible, stats.Batches, stats.DrawCalls, stats.Triangles, stats.ShadowCasters);
			events.Emit<ExitGameEvent>();
		}
	}
}
