using System.Numerics;

using Arch.Core;

using Microsoft.Extensions.Logging;

using Ion;
using Ion.Examples.Puppet;
using Ion.Extensions.Assets;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

// Skinned meshes and skeletal animation: a glTF character (Assets/puppet.gltf, 12 joints and three clips, written by
// tools/make_puppet.py) spawned three times as entities. The left one idles, the right one walks in a circle, and the
// one in the middle cross-fades from clip to clip every few seconds. Each spawned model's root has an Animator (its
// AnimationPlayer); the ECS module advances the players and the 3D extraction draws the skinned meshes with their
// palettes, shadows included.
var builder = IonApplication.CreateBuilder(args);
builder.AddEcsRendering3D().AddSystem<PuppetSystem>();

using var game = builder.Build();
game.UseEcsRendering3D().UseSystem<PuppetSystem>();
game.Run();

namespace Ion.Examples.Puppet
{
	/// <summary>
	/// Loads the puppet, spawns it three times, starts their clips, walks the right one in a circle, cross-fades the middle
	/// one every <see cref="ClipSeconds"/>, and draws the HUD.
	/// </summary>
	public sealed class PuppetSystem(World world, IRenderer3D renderer, ISpriteBatch sprites, IAssetManager assets, ILogger<PuppetSystem> logger)
	{
		/// <summary>How long the middle puppet plays each clip.</summary>
		public const float ClipSeconds = 3f;

		/// <summary>How long a cross-fade takes.</summary>
		public const float FadeSeconds = 0.4f;

		/// <summary>The radius of the walker's circle.</summary>
		public const float WalkRadius = 1.6f;

		/// <summary>The walker's speed along its circle, in units per second (matched to the Walk clip's stride).</summary>
		public const float WalkSpeed = 1.1f;

		private static readonly string[] Sequence = ["Idle", "Walk", "Wave"];

		private IFont? _font;
		private float _time;
		private int _clip;

		/// <summary>The loaded model.</summary>
		public IModel? Model { get; private set; }

		/// <summary>The idling puppet (left).</summary>
		public Entity Idler { get; private set; }

		/// <summary>The puppet that cross-fades between the clips (middle).</summary>
		public Entity Performer { get; private set; }

		/// <summary>The puppet walking in a circle (right).</summary>
		public Entity Walker { get; private set; }

		/// <summary>How many cross-fades the middle puppet started.</summary>
		public int Fades { get; private set; }

		[Init]
		public void Init(GameTime dt)
		{
			var model = assets.Load<IModel>("puppet.gltf");
			Model = model;
			Idler = Spawn(model, new Vector3(-2.2f, 0, 0.6f), "Idle", 0.3f);
			Performer = Spawn(model, Vector3.Zero, Sequence[0], 0f);
			Walker = Spawn(model, WalkerPosition(0), "Walk", 0f);
			world.Get<Transform>(Walker) = WalkerTransform(0);

			var ground = renderer.CreateMesh(MeshPrimitives.Plane(14f, 1));
			world.Create(Transform.Identity, new MeshRenderer(ground, renderer.CreateMaterial(new PbrMaterial(new Color(0x9A, 0xA4, 0x8C), metallic: 0f, roughness: 0.9f))), new EntityName("ground"));
			world.Create(new Transform(new Vector3(0, 1.5f, 6.2f), Transform.LookRotation(new Vector3(0, -0.12f, -1), Vector3.UnitY)),
				new Camera { FieldOfView = MathF.PI / 4, Near = 0.1f, Far = 60f, ClearColor = new Color(0xB8, 0xC8, 0xDC) }, new EntityName("camera"));
			world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-0.45f, -0.8f, -0.4f), Vector3.UnitY)),
				new DirectionalLight(new Color(0xFF, 0xF2, 0xDE), intensity: 3f), new EntityName("sun"));
			world.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0xA8, 0xB8, 0xD8), AmbientIntensity = 0.45f });

			_font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(20);
			var skeleton = model.Skins[0];
			logger.LogInformation("Puppet: {Joints} joints, clips {Clips}.", skeleton.JointCount, string.Join(", ", model.Animations.Select(a => $"{a.Name} ({a.Duration:0.##} s)")));
		}

		private Entity Spawn(IModel model, Vector3 position, string clip, float startTime)
		{
			var root = world.SpawnModel(model, new Transform(position));
			var player = world.Get<Animator>(root).Player;
			player.Play(model.FindAnimation(clip) ?? throw new InvalidOperationException($"The puppet has no '{clip}' clip."));
			player.Time = startTime;
			return root;
		}

		/// <summary>Cross-fades the middle puppet to the next clip every <see cref="ClipSeconds"/>; walks the right one.</summary>
		[Update]
		public void Animate(GameTime dt)
		{
			_time += dt.Delta;
			var clip = (int)(_time / ClipSeconds) % Sequence.Length;
			if (clip != _clip && Model is { } model)
			{
				_clip = clip;
				world.Get<Animator>(Performer).Player.CrossFade(model.FindAnimation(Sequence[clip])!, FadeSeconds);
				Fades++;
			}

			world.Get<Transform>(Walker) = WalkerTransform(_time);
		}

		/// <summary>Where the walker is at <paramref name="time"/>: on a circle around (3.4, 0, -2.4).</summary>
		public static Vector3 WalkerPosition(float time)
		{
			var angle = time * WalkSpeed / WalkRadius;
			return new Vector3(3.4f + MathF.Cos(angle) * WalkRadius, 0, -2.4f + MathF.Sin(angle) * WalkRadius);
		}

		/// <summary>The walker at <paramref name="time"/>, facing along the circle (the puppet faces +Z).</summary>
		public static Transform WalkerTransform(float time)
		{
			var angle = time * WalkSpeed / WalkRadius;
			var forward = new Vector3(-MathF.Sin(angle), 0, MathF.Cos(angle));
			return new Transform(WalkerPosition(time), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(forward.X, forward.Z)));
		}

		[Render]
		public void Render(GameTime dt)
		{
			if (_font is null) return;
			var player = world.Get<Animator>(Performer).Player;
			var playing = player.Next is { } next ? $"{player.Clip?.Name} to {next.Name} ({player.FadeWeight:P0})" : player.Clip?.Name ?? "rest pose";
			sprites.DrawRect(new Color(0, 0, 0, 0.45f), new Vector2(8, 8), new Vector2(520, 64));
			sprites.DrawString(_font, "Ion 3D: skinned meshes and animation", new Vector2(18, 14), Color.White);
			sprites.DrawString(_font, $"Middle: {playing}", new Vector2(18, 42), new Color(0xD0, 0xE0, 0xFF), scale: 0.7f);
		}
	}
}
