using System.Numerics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion;
using Ion.Examples.Model;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

// A glTF 2.0 model with the PBR material: Microsoft's CC0 "Avocado" (base color, metallic-roughness and normal maps),
// a row of spheres from rough to polished metal, a sun with shadows, two point lights and a skybox that also lights the
// scene (image based ambient from its mips). The camera circles the scene.
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
		/// <summary>Registers the engine, the 3D renderer and the sample's system.</summary>
		public static IonApplicationBuilder Configure(IonApplicationBuilder builder)
		{
			builder.Services.AddIon(builder.Configuration);
			builder.Services.AddRendering3D(builder.Configuration);
			builder.Services.AddSingleton<ModelSystem>();
			return builder;
		}

		/// <summary>Adds the engine's systems, the 3D renderer's and the sample's.</summary>
		public static IIonApplication Use(IIonApplication app) => app.UseIon().UseRendering3D().UseSystem<ModelSystem>();
	}

	/// <summary>Loads the model and the skybox, creates the spheres and submits the scene every frame.</summary>
	public sealed class ModelSystem(IRenderer3D renderer, ISpriteBatch sprites, IAssetManager assets, IConfiguration config, IServiceProvider services, IEvents events, ILogger<ModelSystem> logger)
	{
		private const float AvocadoScale = 40f;

		private readonly long _frames = long.TryParse(config["Model:Frames"], out var frames) ? frames : 0;
		private readonly string? _screenshot = config["Model:Screenshot"];
		private IModel? _model;
		private MeshHandle _sphere;
		private MeshHandle _pedestal;
		private readonly MaterialHandle[] _sphereMaterials = new MaterialHandle[5];
		private MaterialHandle _pedestalMaterial;
		private IFont? _font;
		private long _rendered;
		private bool _done;

		/// <summary>The loaded model.</summary>
		public IModel? Model => _model;

		/// <summary>The camera's orbit angle at time zero, in radians.</summary>
		public float StartAngle { get; set; } = 0.35f;

		/// <summary>The orbit speed in radians per second.</summary>
		public float OrbitSpeed { get; set; } = 0.15f;

		/// <summary>Loads the model and the skybox and creates the spheres.</summary>
		[Init]
		public void Init(GameTime dt)
		{
			_model = assets.Load<IModel>("Avocado/Avocado.gltf");
			var skybox = assets.Load<ICubemap>("Skybox");
			renderer.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0x40, 0x48, 0x58), AmbientIntensity = 0.15f, Skybox = skybox.Handle, SkyboxIntensity = 1f });

			_sphere = renderer.CreateMesh(MeshPrimitives.Sphere(0.45f, 48, 24));
			_pedestal = renderer.CreateMesh(MeshPrimitives.Cylinder(4.5f, 0.3f, 64));
			for (var i = 0; i < _sphereMaterials.Length; i++)
			{
				var roughness = 0.1f + 0.2f * i;
				_sphereMaterials[i] = renderer.CreateMaterial(new PbrMaterial(new Color(0xE6, 0xB8, 0x5C), metallic: 1f, roughness: roughness));
			}

			_pedestalMaterial = renderer.CreateMaterial(new PbrMaterial(new Color(0xB0, 0xB4, 0xBC), metallic: 0f, roughness: 0.6f));
			_font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(20);
			logger.LogInformation("Loaded {Model}: {Nodes} nodes, {Meshes} meshes, {Materials} materials, {Textures} textures, bounds {Bounds}.",
				_model.Name, _model.Nodes.Count, _model.Meshes.Count, _model.Materials.Count, _model.Textures.Count, _model.Bounds);
		}

		/// <summary>Submits the camera, the lights, the model, the spheres and the pedestal.</summary>
		[Render]
		public void Render(GameTime dt)
		{
			var t = (float)dt.Elapsed.TotalSeconds;
			var angle = StartAngle + t * OrbitSpeed;
			// Circling around +Z, so the camera mostly looks towards -Z, where the sun is.
			var eye = new Vector3(MathF.Sin(angle) * 6.5f, 2.0f, MathF.Cos(angle) * 6.5f);
			renderer.SetCamera(new Camera { FieldOfView = MathF.PI / 4, Near = 0.1f, Far = 100f, Clear = CameraClear.Skybox },
				Transform.LookAt(eye, new Vector3(0, 1.1f, 0)));

			// The sun matches the skybox's sun (low in the -Z direction); two point lights warm and cool.
			renderer.AddLight(new DirectionalLight(new Color(0xFF, 0xF0, 0xDC), intensity: 2.5f), -Vector3.Normalize(new Vector3(0.35f, 0.55f, -1f)));
			renderer.AddLight(new PointLight(new Color(0xFF, 0x90, 0x40), intensity: 2.5f, range: 5f), new Vector3(2.5f, 1.8f, 1.5f));
			renderer.AddLight(new PointLight(new Color(0x50, 0x90, 0xFF), intensity: 2.5f, range: 5f), new Vector3(-2.5f, 1.2f, 1f));

			renderer.Draw(_pedestal, _pedestalMaterial, Matrix4x4.CreateTranslation(0, -0.15f, 0));
			if (_model is not null)
			{
				var world = Matrix4x4.CreateScale(AvocadoScale) * Matrix4x4.CreateRotationY(t * 0.4f);
				renderer.Draw(_model, world);
			}

			for (var i = 0; i < _sphereMaterials.Length; i++)
			{
				var x = (i - 2) * 1.2f;
				renderer.Draw(_sphere, _sphereMaterials[i], Matrix4x4.CreateTranslation(x, 0.45f, -2.2f));
			}

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
