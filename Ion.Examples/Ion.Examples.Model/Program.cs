using System.Numerics;

using Arch.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion;
using Ion.Examples.Model;
using Ion.Extensions.Assets;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

// A glTF 2.0 model with the PBR material: Microsoft's CC0 "Avocado" (base color, metallic-roughness and normal maps),
// a row of spheres from rough to polished metal, a sun with shadows, two point lights and a skybox that also lights the
// scene (image based ambient from its mips). The camera circles the scene. Everything is an entity of the ECS module: the
// model is spawned as one entity per glTF node, and the 3D extraction (AddEcsRendering3D) submits the entities every frame.
//   --Model:Frames=<n>          exit after n frames
//   --Model:Screenshot=<file>   with Frames, save the last frame as PNG
//   --Ion:Headless=true         no window (add --Ion:Headless:Render=true to render offscreen)
var builder = IonApplication.CreateBuilder(args);
ModelApp.Configure(builder);

using var game = builder.Build();
ModelApp.Use(game);
game.Run();

namespace Ion.Examples.Model
{
	/// <summary>The sample's setup, shared with the tests.</summary>
	public static class ModelApp
	{
		/// <summary>Registers the engine, the 3D renderer, the ECS module with its 3D extraction, and the sample's system.</summary>
		public static IonApplicationBuilder Configure(IonApplicationBuilder builder)
		{
			builder.Services.AddIon(builder.Configuration);
			builder.Services.AddRendering3D(builder.Configuration);
			builder.Services.AddEcs().AddEcsRendering3D();
			builder.Services.AddSingleton<ModelSystem>();
			return builder;
		}

		/// <summary>Adds the engine's systems, the 3D renderer's, the ECS module's (with the 3D extraction) and the sample's.</summary>
		public static IIonApplication Use(IIonApplication app) => app.UseIon().UseRendering3D().UseEcs().UseEcsRendering3D().UseSystem<ModelSystem>();
	}

	/// <summary>Turns an entity about the y axis at <see cref="Speed"/> radians per second (the spawned model's root).</summary>
	/// <param name="Speed">The angular speed.</param>
	public readonly record struct Spin(float Speed);

	/// <summary>
	/// Loads the model and the skybox, spawns the model as entities and creates the spheres, the pedestal, the camera and
	/// the lights as entities; turns the model with a <c>[Query]</c> step and moves the camera through its entity. The ECS
	/// extraction submits the entities to the renderer.
	/// </summary>
	public sealed partial class ModelSystem(World world, IRenderer3D renderer, ISpriteBatch sprites, IAssetManager assets, IConfiguration config, IServiceProvider services, IEvents events, ILogger<ModelSystem> logger)
	{
		private const float AvocadoScale = 40f;

		private readonly long _frames = long.TryParse(config["Model:Frames"], out var frames) ? frames : 0;
		private readonly string? _screenshot = config["Model:Screenshot"];
		private IModel? _model;
		private Entity _camera;
		private Entity _avocado;
		private IFont? _font;
		private long _rendered;
		private bool _done;

		/// <summary>The loaded model.</summary>
		public IModel? Model => _model;

		/// <summary>The root entity of the spawned model.</summary>
		public Entity ModelEntity => _avocado;

		/// <summary>The camera's orbit angle at time zero, in radians.</summary>
		public float StartAngle { get; set; } = 0.35f;

		/// <summary>The orbit speed in radians per second.</summary>
		public float OrbitSpeed { get; set; } = 0.15f;

		/// <summary>Loads the model and the skybox and creates the entities.</summary>
		[Init]
		public void Init(GameTime dt)
		{
			_model = assets.Load<IModel>("Avocado/Avocado.gltf");
			var skybox = assets.Load<ICubemap>("Skybox");
			world.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0x40, 0x48, 0x58), AmbientIntensity = 0.15f, Skybox = skybox.Handle, SkyboxIntensity = 1f });

			var sphere = renderer.CreateMesh(MeshPrimitives.Sphere(0.45f, 48, 24));
			var pedestal = renderer.CreateMesh(MeshPrimitives.Cylinder(4.5f, 0.3f, 64));
			for (var i = 0; i < 5; i++)
			{
				var roughness = 0.1f + 0.2f * i;
				var material = renderer.CreateMaterial(new PbrMaterial(new Color(0xE6, 0xB8, 0x5C), metallic: 1f, roughness: roughness));
				world.Create(new Transform(new Vector3((i - 2) * 1.2f, 0.45f, -2.2f)), new MeshRenderer(sphere, material), new EntityName($"sphere {i}"));
			}

			var pedestalMaterial = renderer.CreateMaterial(new PbrMaterial(new Color(0xB0, 0xB4, 0xBC), metallic: 0f, roughness: 0.6f));
			world.Create(new Transform(new Vector3(0, -0.15f, 0)), new MeshRenderer(pedestal, pedestalMaterial), new EntityName("pedestal"));

			// The model: a root entity (scaled and turned by Spin) with one entity per glTF node under it.
			_avocado = world.SpawnModel(_model, new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(AvocadoScale)));
			world.Add(_avocado, new Spin(0.4f));

			_camera = world.Create(Orbit(0f), new Camera { FieldOfView = MathF.PI / 4, Near = 0.1f, Far = 100f, Clear = CameraClear.Skybox }, new EntityName("camera"));
			// The sun matches the skybox's sun (low in the -Z direction) and shines along its entity's -Z; two point lights warm and cool.
			world.Create(new Transform(Vector3.Zero, Transform.LookRotation(-Vector3.Normalize(new Vector3(0.35f, 0.55f, -1f)), Vector3.UnitY)),
				new DirectionalLight(new Color(0xFF, 0xF0, 0xDC), intensity: 2.5f), new EntityName("sun"));
			world.Create(new Transform(new Vector3(2.5f, 1.8f, 1.5f)), new PointLight(new Color(0xFF, 0x90, 0x40), intensity: 2.5f, range: 5f), new EntityName("warm light"));
			world.Create(new Transform(new Vector3(-2.5f, 1.2f, 1f)), new PointLight(new Color(0x50, 0x90, 0xFF), intensity: 2.5f, range: 5f), new EntityName("cool light"));

			_font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(20);
			logger.LogInformation("Loaded {Model}: {Nodes} nodes, {Meshes} meshes, {Materials} materials, {Textures} textures, bounds {Bounds}.",
				_model.Name, _model.Nodes.Count, _model.Meshes.Count, _model.Materials.Count, _model.Textures.Count, _model.Bounds);
		}

		/// <summary>Moves the camera along its orbit.</summary>
		[Update]
		public void MoveCamera(GameTime dt)
		{
			if (world.IsAlive(_camera)) world.Get<Transform>(_camera) = Orbit((float)dt.Elapsed.TotalSeconds);
		}

		/// <summary>Turns every entity with a <see cref="Spin"/> (a generated chunk loop).</summary>
		[Update, Query]
		private static void Turn(ref Transform transform, in Spin spin, GameTime dt) =>
			transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)dt.Elapsed.TotalSeconds * spin.Speed);

		private Transform Orbit(float t)
		{
			var angle = StartAngle + t * OrbitSpeed;
			// Circling around +Z, so the camera mostly looks towards -Z, where the sun is.
			var eye = new Vector3(MathF.Sin(angle) * 6.5f, 2.0f, MathF.Cos(angle) * 6.5f);
			return Transform.LookAt(eye, new Vector3(0, 1.1f, 0));
		}

		/// <summary>Draws the title (the scene itself is extracted from the entities).</summary>
		[Render]
		public void Render(GameTime dt)
		{
			if (_font is not null)
			{
				sprites.DrawString(_font, "Ion 3D: glTF 2.0, PBR, shadows, skybox", new Vector2(18, 14), Color.White);
			}

			_rendered++;
		}

		/// <summary>Handles <c>Model:Frames</c> and <c>Model:Screenshot</c>.</summary>
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

			events.Emit<ExitGameEvent>();
		}
	}
}
