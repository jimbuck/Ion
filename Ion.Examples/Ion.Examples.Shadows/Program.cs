using System.Numerics;

using Arch.Core;

using Ion;
using Ion.Examples.Shadows;
using Ion.Extensions.Assets;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

// Cascaded shadow maps: an avenue of pillars and arches from the camera's feet to 150 units away, under one low sun.
// The shadow distance is 150 (appsettings.json), split into four cascades, so the shadows at the camera's feet are as
// sharp as those 2 units away would be with a short shadow distance, and the far ones still exist. Keys 1 to 4 set the
// number of cascades at run time; C tints what each cascade covers (red, green, blue, yellow from near to far).
var builder = IonApplication.CreateBuilder(args);
builder.AddEcsRendering3D().AddSystem<ShadowsSystem>();

using var game = builder.Build();
game.UseEcsRendering3D().UseSystem<ShadowsSystem>();
game.Run();

namespace Ion.Examples.Shadows
{
	/// <summary>Builds the avenue, the sun and the camera; sways the camera; handles the keys; draws the HUD.</summary>
	public sealed class ShadowsSystem(World world, Renderer3D renderer, IInputState input, ISpriteBatch sprites, IAssetManager assets)
	{
		/// <summary>The pairs of pillars along the avenue.</summary>
		public const int Pairs = 30;

		/// <summary>The distance between pairs, in world units.</summary>
		public const float Spacing = 5f;

		/// <summary>How far apart the two rows are.</summary>
		public const float Width = 6f;

		private IFont? _font;
		private float _time;
		private Entity _camera;

		/// <summary>The z of pillar pair <paramref name="i"/> (the first at the camera's feet, the rest down -Z).</summary>
		public static float PairZ(int i) => -2f - i * Spacing;

		[Init]
		public void Init(GameTime dt)
		{
			var cube = renderer.CreateMesh(MeshPrimitives.Cube());
			var ground = renderer.CreateMesh(MeshPrimitives.Plane(400f, 1));
			var stone = renderer.CreateMaterial(new PbrMaterial(new Color(0xD8, 0xCF, 0xC0), metallic: 0f, roughness: 0.85f));
			var grass = renderer.CreateMaterial(new PbrMaterial(new Color(0xA8, 0xB8, 0x90), metallic: 0f, roughness: 0.95f));
			world.Create(new Transform(new Vector3(0, 0, -150)), new MeshRenderer(ground, grass), new EntityName("ground"));

			for (var i = 0; i < Pairs; i++)
			{
				var z = PairZ(i);
				foreach (var x in new[] { -Width / 2, Width / 2 })
				{
					world.Create(new Transform(new Vector3(x, 2f, z), null, new Vector3(0.6f, 4f, 0.6f)), new MeshRenderer(cube, stone));
				}

				// Every other pair carries a lintel, so the ground between the rows gets a shadow too.
				if (i % 2 == 0) world.Create(new Transform(new Vector3(0, 4.3f, z), null, new Vector3(Width + 0.6f, 0.6f, 0.6f)), new MeshRenderer(cube, stone));
			}

			_camera = world.Create(CameraTransform(0), new Camera { FieldOfView = MathF.PI / 3.2f, Near = 0.2f, Far = 300f, ClearColor = new Color(0xB4, 0xC8, 0xE0) }, new EntityName("camera"));
			// A low sun from the side, so the shadows reach across the avenue.
			world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-0.8f, -0.45f, -0.25f), Vector3.UnitY)),
				new DirectionalLight(new Color(0xFF, 0xEC, 0xD0), intensity: 3f), new EntityName("sun"));
			world.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0xA0, 0xB4, 0xD8), AmbientIntensity = 0.4f });
			_font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(20);
		}

		/// <summary>The camera at <paramref name="time"/>: at head height between the first pillars, swaying slowly, looking down the avenue.</summary>
		public static Transform CameraTransform(float time)
		{
			var sway = MathF.Sin(time * 0.3f) * 1.2f;
			return Transform.LookAt(new Vector3(sway, 1.7f, 3f), new Vector3(sway * 0.3f, 1.2f, -40f));
		}

		/// <summary>Keys 1 to 4 set the cascade count, C toggles the cascade tint; the camera sways.</summary>
		[Update]
		public void Update(GameTime dt)
		{
			for (var n = 1; n <= 4; n++)
			{
				if (input.Pressed(Key.Number1 + (n - 1))) renderer.Options.ShadowCascades = n;
			}

			if (input.Pressed(Key.C)) renderer.Options.ShowShadowCascades = !renderer.Options.ShowShadowCascades;
			_time += dt.Delta;
			world.Get<Transform>(_camera) = CameraTransform(_time);
		}

		[Render]
		public void Render(GameTime dt)
		{
			if (_font is null) return;
			var options = renderer.Options;
			var splits = string.Join(" / ", renderer.ShadowCascadeSplits.ToArray().Select(s => s.ToString("0")));
			sprites.DrawRect(new Color(0, 0, 0, 0.45f), new Vector2(8, 8), new Vector2(560, 64));
			sprites.DrawString(_font, $"Ion 3D: {options.ShadowCascades} shadow cascade{(options.ShadowCascades == 1 ? "" : "s")}", new Vector2(18, 14), Color.White);
			sprites.DrawString(_font, $"ends at {splits} units. 1-4: cascades, C: tint", new Vector2(18, 42), new Color(0xD0, 0xE0, 0xFF), scale: 0.7f);
		}
	}
}
