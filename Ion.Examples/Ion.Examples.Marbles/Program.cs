using System.Numerics;

using Arch.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion;
using Ion.Examples.Marbles;
using Ion.Extensions.Assets;
using Ion.Extensions.Audio;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Physics3D;
using Ion.Extensions.Rendering3D;

// 3D physics on the ECS module: marbles dropped onto a rippled bowl of terrain. The terrain is one mesh, drawn by the 3D
// renderer and, from the same triangles, a static triangle-mesh collider the marbles roll on. Landing marbles click
// from where they land, heard from the orbiting camera (positional audio).
//   --Marbles:Count=<n>   the number of marbles (default 80)
//   --Marbles:Seed=<n>    where they are dropped (default 1)
//   --Ion:Physics3D:DebugDraw=true   draw the colliders
var builder = IonApplication.CreateBuilder(args);
builder.AddIon().AddRendering3D().AddEcs().AddEcsRendering3D().AddPhysics3D().AddSystem<MarblesSystem>();
builder.Services.AddSingleton(MarbleSettings.From(builder.Configuration));

using var game = builder.Build();
game.UseIon().UseRendering3D().UseEcs().UseEcsRendering3D().UsePhysics3D().UseEcsAudio().UseSystem<MarblesSystem>();
game.Run();

namespace Ion.Examples.Marbles
{
	/// <summary>The sample's settings: <c>Marbles:Count</c> (80) and <c>Marbles:Seed</c> (1).</summary>
	public sealed record MarbleSettings(int Count, int Seed)
	{
		/// <summary>Reads the settings from configuration.</summary>
		public static MarbleSettings From(IConfiguration config) => new(
			int.TryParse(config["Marbles:Count"], out var count) ? Math.Clamp(count, 1, 2000) : 80,
			int.TryParse(config["Marbles:Seed"], out var seed) ? seed : 1);
	}

	/// <summary>A marble: its index (its color and its drop position come from it).</summary>
	public readonly record struct Marble(int Index);

	/// <summary>The terrain: a rippled bowl, as a function and as a mesh.</summary>
	public static class Terrain
	{
		/// <summary>The width and depth of the terrain.</summary>
		public const float Size = 40f;

		/// <summary>The quads along each side of the mesh.</summary>
		public const int Subdivisions = 64;

		/// <summary>The collision layer of the terrain (the marbles are on <see cref="MarblesSystem.MarbleLayer"/>).</summary>
		public const uint Layer = 1;

		/// <summary>The height of the terrain at (<paramref name="x"/>, <paramref name="z"/>): a bowl with ripples.</summary>
		public static float Height(float x, float z) => 0.012f * (x * x + z * z) + 0.7f * MathF.Sin(x * 0.45f) * MathF.Cos(z * 0.4f);

		/// <summary>
		/// The terrain mesh: the renderer's plane (front faces up) with each vertex lifted to <see cref="Height"/>, normals
		/// and tangents recomputed. The same triangles make the collider.
		/// </summary>
		public static MeshData CreateMesh()
		{
			var mesh = MeshPrimitives.Plane(Size, Subdivisions);
			mesh.Name = "Terrain";
			var positions = mesh.Positions;
			for (var i = 0; i < positions.Length; i++) positions[i].Y = Height(positions[i].X, positions[i].Z);
			mesh.ComputeNormals();
			mesh.ComputeTangents();
			return mesh;
		}
	}

	/// <summary>
	/// Creates the terrain (drawn, and a static mesh collider from the same mesh), the marbles (dynamic spheres), a sun and
	/// an orbiting camera (the audio listener); clicks where marbles land; puts back any marble that leaves the terrain;
	/// draws the HUD.
	/// </summary>
	public sealed class MarblesSystem(World world, IRenderer3D renderer, IPhysicsWorld3D physics, ISpatialAudio spatial, IEvents events, MarbleSettings settings, ISpriteBatch sprites, IAssetManager assets, ILogger<MarblesSystem> logger)
	{
		/// <summary>The most clicks started in one fixed step (a whole bowl landing at once would fill the voice pool).</summary>
		public const int MaxClicksPerStep = 4;

		/// <summary>How a click falls off: full volume within 4 units, inverse beyond.</summary>
		public static readonly Attenuation ClickFalloff = new(MinDistance: 4f, MaxDistance: 80f);

		private EventReader<Collision3D> _collisions = events.Reader<Collision3D>();
		private ISoundEffect? _click;
		/// <summary>The marbles' radius.</summary>
		public const float Radius = 0.5f;

		/// <summary>The marbles' collision layer.</summary>
		public const uint MarbleLayer = 2;

		private static readonly QueryDescription Marbles = new QueryDescription().WithAll<Marble, Transform, RigidBody3D>();

		private static readonly Color[] Palette =
		[
			new(0xE0, 0x4A, 0x3B), new(0x3B, 0x8E, 0xE0), new(0xF2, 0xC1, 0x4E), new(0x5C, 0xC2, 0x7A), new(0xB0, 0x6A, 0xD8),
		];

		private Entity _camera;
		private IFont? _font;
		private string _text = "";
		private float _time;

		/// <summary>The terrain entity.</summary>
		public Entity TerrainEntity { get; private set; }

		/// <summary>How many marbles were put back after leaving the terrain.</summary>
		public int Respawns { get; private set; }

		/// <summary>How many marbles were resting at the last count (speed under 0.2 units per second).</summary>
		public int Resting { get; private set; }

		/// <summary>How many clicks were played.</summary>
		public int Clicks { get; private set; }

		[Init]
		public void Init(GameTime dt)
		{
			var terrain = Terrain.CreateMesh();
			var ground = renderer.CreateMesh(terrain);
			var groundMaterial = renderer.CreateMaterial(new PbrMaterial(new Color(0x7E, 0x9A, 0x6A), metallic: 0f, roughness: 0.95f));
			var collider = physics.CreateMesh(terrain.Positions, terrain.Indices);
			TerrainEntity = world.Create(Transform.Identity, new MeshRenderer(ground, groundMaterial), Collider3D.TriangleMesh(collider) with { Layer = Terrain.Layer, Friction = 0.8f }, new EntityName("terrain"));

			var sphere = renderer.CreateMesh(MeshPrimitives.Sphere(Radius, 24, 12));
			var materials = Palette.Select(color => renderer.CreateMaterial(new PbrMaterial(color, metallic: 0.2f, roughness: 0.25f))).ToArray();
			var random = new Random(settings.Seed);
			for (var i = 0; i < settings.Count; i++)
			{
				world.Create(new Transform(DropPosition(random, i)), new MeshRenderer(sphere, materials[i % materials.Length]),
					Collider3D.Sphere(Radius) with { Layer = MarbleLayer, Friction = 0.5f }, RigidBody3D.Dynamic(mass: 1f) with { AngularDamping = 0.4f }, new Marble(i));
			}

			world.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0x9C, 0xB0, 0xD0), AmbientIntensity = 0.35f });
			// The camera is the listener: clicks are heard from where it looks.
			_camera = world.Create(Orbit(0f), new Camera { FieldOfView = MathF.PI / 4, Near = 0.5f, Far = 200f, ClearColor = new Color(0x87, 0xA9, 0xD6) }, new AudioListener(), new EntityName("camera"));
			world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-0.5f, -0.75f, -0.35f), Vector3.UnitY)),
				new DirectionalLight(new Color(0xFF, 0xF4, 0xE0), intensity: 3f), new EntityName("sun"));

			_font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(20);
			_click = assets.Load<ISoundEffect>("ping.wav");
			logger.LogInformation("{Count} marbles on a terrain of {Triangles} triangles.", settings.Count, terrain.TriangleCount);
		}

		/// <summary>Above the terrain, spread over its middle, staggered in height so they do not land at once.</summary>
		public static Vector3 DropPosition(Random random, int index)
		{
			var x = (random.NextSingle() - 0.5f) * 24f;
			var z = (random.NextSingle() - 0.5f) * 24f;
			return new Vector3(x, Terrain.Height(x, z) + 4f + index % 10 * 1.2f, z);
		}

		/// <summary>
		/// A marble that lands (a contact begins) clicks where it touched, louder the faster it goes, a little higher or
		/// lower in pitch per marble.
		/// </summary>
		[FixedUpdate(Order = StageOrder.Physics + 10)]
		public void Click(GameTime dt)
		{
			var started = 0;
			foreach (ref readonly var contact in _collisions.Read())
			{
				if (contact.Phase != ContactPhase3D.Begin || _click is null || started >= MaxClicksPerStep) continue;
				var marble = world.IsAlive(contact.A) && world.Has<Marble>(contact.A) ? contact.A : world.IsAlive(contact.B) && world.Has<Marble>(contact.B) ? contact.B : Entity.Null;
				if (marble == Entity.Null) continue;
				var speed = world.Get<RigidBody3D>(marble).LinearVelocity.Length();
				if (speed < 1f) continue;
				var pitch = (world.Get<Marble>(marble).Index % 7 - 3) * 0.08f;
				spatial.Play(_click, contact.Point, volume: Math.Clamp(speed / 10f, 0.1f, 1f), pitchShift: pitch, attenuation: ClickFalloff);
				started++;
				Clicks++;
			}
		}

		/// <summary>Puts back a marble that fell off the terrain, and counts the resting ones.</summary>
		[FixedUpdate]
		public void Tend(GameTime dt)
		{
			var resting = 0;
			var respawns = 0;
			foreach (ref var chunk in world.Query(in Marbles))
			{
				var transforms = chunk.GetSpan<Transform>();
				var bodies = chunk.GetSpan<RigidBody3D>();
				var marbles = chunk.GetSpan<Marble>();
				for (var i = 0; i < chunk.Count; i++)
				{
					if (bodies[i].LinearVelocity.LengthSquared() < 0.04f) resting++;
					if (transforms[i].Position.Y > -20f) continue;
					var random = new Random(marbles[i].Index + respawns + Respawns);
					transforms[i].Position = DropPosition(random, marbles[i].Index);
					bodies[i].LinearVelocity = Vector3.Zero;
					bodies[i].AngularVelocity = Vector3.Zero;
					respawns++;
				}
			}

			Resting = resting;
			Respawns += respawns;
		}

		/// <summary>Moves the camera along its orbit.</summary>
		[Update]
		public void MoveCamera(GameTime dt)
		{
			_time += dt.Delta;
			if (world.IsAlive(_camera)) world.Get<Transform>(_camera) = Orbit(_time);
		}

		private static Transform Orbit(float t)
		{
			var angle = 0.7f + t * 0.15f;
			var eye = new Vector3(MathF.Cos(angle) * 30f, 18f, MathF.Sin(angle) * 30f);
			return Transform.LookAt(eye, new Vector3(0, 1, 0));
		}

		[Render]
		public void Render(GameTime dt)
		{
			if (_font is null) return;
			_text = $"{settings.Count} marbles, {Resting} resting";
			sprites.DrawRect(new Color(0, 0, 0, 0.45f), new Vector2(8, 8), new Vector2(520, 64));
			sprites.DrawString(_font, "Ion 3D physics: a triangle-mesh terrain", new Vector2(18, 14), Color.White);
			sprites.DrawString(_font, _text, new Vector2(18, 42), new Color(0xD0, 0xE0, 0xFF), scale: 0.7f);
		}
	}
}
