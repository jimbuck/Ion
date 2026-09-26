using System.Numerics;

using Arch.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion;
using Ion.Examples.Cubes;
using Ion.Extensions.Assets;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

// 3D on the ECS module: 1,000 cube entities in a 40 x 25 grid, two materials (so two instanced draws per pass), a sun
// entity with shadows onto a ground plane, a camera entity orbiting the grid, and a HUD drawn by the sprite batch on top.
// The 3D extraction (AddEcsRendering3D) submits the entities to the renderer every frame.
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
		/// <summary>Registers the engine, the 3D renderer, the ECS module with its 3D extraction, and the sample's system.</summary>
		public static IonApplicationBuilder Configure(IonApplicationBuilder builder)
		{
			builder.Services.AddIon(builder.Configuration);
			builder.Services.AddRendering3D(builder.Configuration);
			builder.Services.AddEcs().AddEcsRendering3D();
			builder.Services.AddSingleton<CubesSystem>();
			return builder;
		}

		/// <summary>Adds the engine's systems, the 3D renderer's, the ECS module's (with the 3D extraction) and the sample's.</summary>
		public static IIonApplication Use(IIonApplication app) => app.UseIon().UseRendering3D().UseEcs().UseEcsRendering3D().UseSystem<CubesSystem>();
	}

	/// <summary>A cube of the grid: its place in the grid (the wave animates its height and turn from it).</summary>
	/// <param name="X">The x position.</param>
	/// <param name="Z">The z position.</param>
	public readonly record struct GridCube(float X, float Z);

	/// <summary>
	/// Creates the meshes, the materials and the scene's entities (the ground, 1,000 cubes, the camera and the sun), moves
	/// the cubes with a <c>[Query]</c> step and the camera through its entity, and draws the HUD. The ECS extraction submits
	/// the entities to the renderer.
	/// </summary>
	public sealed partial class CubesSystem(World world, IRenderer3D renderer, ISpriteBatch sprites, IAssetManager assets, IConfiguration config, IServiceProvider services, IEvents events, ILogger<CubesSystem> logger)
	{
		/// <summary>Cubes along x.</summary>
		public const int Columns = 40;

		/// <summary>Cubes along z.</summary>
		public const int Rows = 25;

		private const float Spacing = 1.25f;

		private readonly long _frames = long.TryParse(config["Cubes:Frames"], out var frames) ? frames : 0;
		private readonly string? _screenshot = config["Cubes:Screenshot"];
		private Entity _camera;
		private IFont? _font;
		private string _statsText = "";
		private Rendering3DStatistics _shownStats;
		private long _rendered;
		private bool _done;

		/// <summary>The camera's orbit angle at time zero, in radians.</summary>
		public float StartAngle { get; set; } = 0.6f;

		/// <summary>The orbit speed in radians per second.</summary>
		public float OrbitSpeed { get; set; } = 0.2f;

		/// <summary>The camera entity.</summary>
		public Entity CameraEntity => _camera;

		/// <summary>Creates the meshes, the materials, the entities and the HUD font.</summary>
		[Init]
		public void Init(GameTime dt)
		{
			var cube = renderer.CreateMesh(MeshPrimitives.Cube(0.8f));
			var ground = renderer.CreateMesh(MeshPrimitives.Plane(80f, 8));
			var red = renderer.CreateMaterial(new PbrMaterial(new Color(0xE0, 0x4A, 0x3B), metallic: 0f, roughness: 0.45f));
			var blue = renderer.CreateMaterial(new PbrMaterial(new Color(0x3B, 0x8E, 0xE0), metallic: 0.6f, roughness: 0.3f));
			var groundMaterial = renderer.CreateMaterial(new PbrMaterial(new Color(0x8A, 0x8A, 0x80), metallic: 0f, roughness: 0.9f));
			world.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0x9C, 0xB0, 0xD0), AmbientIntensity = 0.35f });

			_camera = world.Create(Orbit(0f), new Camera { FieldOfView = MathF.PI / 4, Near = 0.5f, Far = 200f, ClearColor = new Color(0x87, 0xA9, 0xD6) }, new EntityName("camera"));
			// The sun shines along its entity's -Z.
			world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-0.65f, -0.6f, -0.3f), Vector3.UnitY)),
				new DirectionalLight(new Color(0xFF, 0xF4, 0xE0), intensity: 3f), new EntityName("sun"));
			world.Create(Transform.Identity, new MeshRenderer(ground, groundMaterial), new EntityName("ground"));

			for (var z = 0; z < Rows; z++)
			{
				for (var x = 0; x < Columns; x++)
				{
					var px = (x - (Columns - 1) * 0.5f) * Spacing;
					var pz = (z - (Rows - 1) * 0.5f) * Spacing;
					var material = ((x + z) & 1) == 0 ? red : blue;
					var grid = new GridCube(px, pz);
					world.Create(Wave(grid, 0f), new MeshRenderer(cube, material), grid);
				}
			}

			_font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(20);
			logger.LogInformation("{Count} cubes in {Columns}x{Rows}, 2 materials.", Columns * Rows, Columns, Rows);
		}

		/// <summary>Moves the camera along its orbit.</summary>
		[Update]
		public void MoveCamera(GameTime dt)
		{
			if (world.IsAlive(_camera)) world.Get<Transform>(_camera) = Orbit((float)dt.Elapsed.TotalSeconds);
		}

		/// <summary>Moves every cube with the wave (a generated chunk loop over the cubes).</summary>
		[Update, Query]
		private static void Animate(ref Transform transform, in GridCube cube, GameTime dt) => transform = Wave(cube, (float)dt.Elapsed.TotalSeconds);

		private Transform Orbit(float t)
		{
			var angle = StartAngle + t * OrbitSpeed;
			var eye = new Vector3(MathF.Cos(angle) * 34f, 20f, MathF.Sin(angle) * 34f);
			return Transform.LookAt(eye, new Vector3(0, 0, 0));
		}

		private static Transform Wave(in GridCube cube, float t)
		{
			var y = 0.4f + 1.2f * (0.5f + 0.5f * MathF.Sin(cube.X * 0.35f + t * 1.5f) * MathF.Cos(cube.Z * 0.3f + t));
			return new Transform(new Vector3(cube.X, y, cube.Z), Quaternion.CreateFromAxisAngle(Vector3.UnitY, cube.X * 0.1f + t * 0.5f));
		}

		/// <summary>Draws the HUD (the scene itself is extracted from the entities).</summary>
		[Render]
		public void Render(GameTime dt)
		{
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
