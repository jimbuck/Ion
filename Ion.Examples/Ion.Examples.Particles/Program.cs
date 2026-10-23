using System.Numerics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion;
using Ion.Examples.Particles;
using Ion.Extensions.Graphics;

// GPU particles: a fountain of 65,536 particles simulated by a compute shader and drawn as additive quads, all in one
// storage buffer that never leaves the GPU. Needs compute (Vulkan, or OpenGL ES 3.1 and later); on a device without it
// the sample logs a warning and shows the clear color.
var builder = IonApplication.CreateBuilder(args);
builder.AddIon(graphics => graphics.ClearColor = new Color(0x0a0a12)).AddSystem<ParticleSystem>();
builder.Services.AddSingleton(ParticleSettings.From(builder.Configuration));

using var game = builder.Build();
game.UseIon().UseSystem<ParticleSystem>();
game.Run();

namespace Ion.Examples.Particles
{
	/// <summary>The sample's settings: <c>Particles:Count</c> (65,536), <c>Particles:Size</c> (0.012 world units), <c>Particles:Seed</c> (1).</summary>
	public sealed record ParticleSettings(int Count, float Size, int Seed)
	{
		/// <summary>Reads the settings from configuration.</summary>
		public static ParticleSettings From(IConfiguration config) => new(
			int.TryParse(config["Particles:Count"], out var count) ? Math.Clamp(count, 1, 4 * 1024 * 1024) : 65_536,
			float.TryParse(config["Particles:Size"], System.Globalization.CultureInfo.InvariantCulture, out var size) ? size : 0.012f,
			int.TryParse(config["Particles:Seed"], out var seed) ? seed : 1);
	}

	/// <summary>
	/// Creates the particles once the device exists (Init, default order, after the graphics Init step), then steps and
	/// draws them every Render. Without a GPU (null graphics) or without compute it does nothing.
	/// </summary>
	public sealed class ParticleSystem(ParticleSettings settings, IServiceProvider services, ILogger<ParticleSystem> logger) : IDisposable
	{
		private const float Lifetime = 3f;
		private const float Speed = 1.6f;
		private static readonly Vector2 Gravity = new(0, -1.6f);

		private IGraphicsFrame? _frame;
		private float _time;

		/// <summary>The GPU particles, or null when there is no GPU or no compute.</summary>
		public GpuParticles? Particles { get; private set; }

		/// <summary>The emitter's position at <paramref name="time"/> seconds, in world units: it sweeps left and right above the floor.</summary>
		public static Vector2 EmitterAt(float time, float aspect) => new(MathF.Sin(time * 0.6f) * 0.6f * aspect, -0.55f);

		[Init]
		public void Init(GameTime dt)
		{
			_frame = services.GetService<IGraphicsFrame>();
			if (_frame is null)
			{
				logger.LogInformation("No GPU (null graphics): the particles are not simulated.");
				return;
			}

			var device = _frame.Device;
			if (!device.Limits.ComputeShaders)
			{
				logger.LogWarning("{Backend} on {Adapter} has no compute shaders (OpenGL ES needs 3.1): the particles are off.", device.Backend, device.AdapterName);
				return;
			}

			Particles = new GpuParticles(device, _frame.ColorFormat, Seed(settings.Count, settings.Seed, Aspect(_frame)));
			logger.LogInformation("{Count} GPU particles on {Adapter} ({Backend}), {Groups} workgroups of {Size} per frame.",
				Particles.Count, device.AdapterName, device.Backend, (Particles.Count + GpuParticles.WorkgroupSize - 1) / GpuParticles.WorkgroupSize, GpuParticles.WorkgroupSize);
		}

		[Render]
		public void Render(GameTime dt)
		{
			if (Particles is null || _frame is not { IsRendering: true } frame) return;
			var delta = Math.Clamp(dt.Delta, 0f, 1f / 20f);
			_time += delta;
			var aspect = Aspect(frame);
			var simulation = new SimulationUniforms(EmitterAt(_time, aspect), Gravity, delta, _time, Particles.Count, (uint)dt.Frame, Lifetime, Speed);
			var view = new ViewUniforms(new Vector2(1f / aspect, 1f), settings.Size);
			Particles.StepAndDraw(frame, simulation, view);
		}

		/// <summary>Releases the GPU resources before the device goes (Destroy, default order).</summary>
		[Destroy]
		public void Destroy(GameTime dt) => Dispose();

		/// <inheritdoc/>
		public void Dispose()
		{
			Particles?.Dispose();
			Particles = null;
		}

		/// <summary>
		/// The starting particles: already in flight from the emitter's start, with lives spread over (0, 1] so they respawn
		/// at a steady rate instead of all at once.
		/// </summary>
		public static Particle[] Seed(int count, int seed, float aspect)
		{
			var random = new Random(seed);
			var emitter = EmitterAt(0, aspect);
			var particles = new Particle[count];
			for (var i = 0; i < count; i++)
			{
				var life = 1f - random.NextSingle() * 0.999f;
				var age = (1f - life) * Lifetime;
				// The same cone and speeds as a respawn in particles.comp.
				var angle = MathF.PI / 2 + (random.NextSingle() - 0.5f) * 0.8f;
				var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Speed * (0.7f + 0.5f * random.NextSingle());
				var position = emitter + velocity * age + 0.5f * Gravity * age * age;
				velocity += Gravity * age;
				if (position.Y < -1f)
				{
					// Already landed: rest on the floor.
					position.Y = -1f;
					velocity = new Vector2(velocity.X * 0.3f, 0);
				}

				particles[i] = new Particle(new Vector4(position, velocity.X, velocity.Y), new Vector4(0.3f, 0.6f, 1f, life));
			}

			return particles;
		}

		private static float Aspect(IGraphicsFrame frame) => frame.Width / (float)Math.Max(1, frame.Height);
	}
}
