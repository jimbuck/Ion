using System.Diagnostics;
using System.Numerics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion;
using Ion.Examples.Sprites100k;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering2D;

// 100,000 sprites across 16 textures bouncing around the window, drawn with SpriteSortMode.Texture (one draw call per
// texture). Logs frames per second, frame time, sprites and draw calls every second.
var builder = IonApplication.CreateBuilder(args);
SpritesApp.Configure(builder);

using var game = builder.Build();
SpritesApp.Use(game);
game.Run();

namespace Ion.Examples.Sprites100k
{
	/// <summary>The sample's setup, shared with the tests.</summary>
	public static class SpritesApp
	{
		/// <summary>Registers the engine and the stress system.</summary>
		public static IonApplicationBuilder Configure(IonApplicationBuilder builder)
		{
			builder.Services.AddIon(builder.Configuration, graphics => graphics.ClearColor = new Color(0x202020));
			builder.Services.AddSingleton(StressSettings.From(builder.Configuration));
			builder.Services.AddSingleton<StressSystem>();
			return builder;
		}

		/// <summary>Adds the engine's systems and the stress system.</summary>
		public static IIonApplication Use(IIonApplication app) => app.UseIon().UseSystem<StressSystem>();
	}

	/// <summary>The stress settings: <c>Sprites:Count</c> (100,000), <c>Sprites:Textures</c> (16), <c>Sprites:Frames</c> (0: run until closed).</summary>
	public sealed record StressSettings(int Count, int Textures, int Frames)
	{
		/// <summary>Reads the settings from configuration.</summary>
		public static StressSettings From(IConfiguration config) => new(
			int.TryParse(config["Sprites:Count"], out var count) ? count : 100_000,
			int.TryParse(config["Sprites:Textures"], out var textures) ? Math.Clamp(textures, 1, 64) : 16,
			int.TryParse(config["Sprites:Frames"], out var frames) ? frames : 0);
	}

	/// <summary>Frame timing of a run.</summary>
	/// <param name="Frames">Frames measured.</param>
	/// <param name="AverageMilliseconds">Mean frame time.</param>
	/// <param name="CpuRenderMilliseconds">Mean time of the Render stage's sprite recording (the Draw loop).</param>
	/// <param name="LastFrame">The sprite batch statistics of the last frame.</param>
	public readonly record struct StressReport(long Frames, double AverageMilliseconds, double CpuRenderMilliseconds, SpriteBatchStatistics LastFrame);

	/// <summary>Creates the sprites and textures, moves them in Update and draws them in Render.</summary>
	public sealed class StressSystem(StressSettings settings, ISpriteBatch spriteBatch, IWindow window, IServiceProvider services, IEvents events, ILogger<StressSystem> logger)
	{
		private const float SpriteSize = 16f;

		private Vector2[] _positions = [];
		private Vector2[] _velocities = [];
		private byte[] _textureOf = [];
		private ITexture2D[] _textures = [];
		private readonly Stopwatch _frameClock = new();
		private readonly Stopwatch _reportClock = new();
		private double _frameTotal;
		private double _recordTotal;
		private long _frames;
		private long _reportFrames;
		private double _reportTime;

		/// <summary>The timing of the frames run so far (the first frames excluded).</summary>
		public StressReport Report => new(_frames, _frames == 0 ? 0 : _frameTotal / _frames, _frames == 0 ? 0 : _recordTotal / _frames, (spriteBatch as ISpriteBatchStatistics)?.LastFrameStatistics ?? default);

		[Init]
		public void Init(GameTime dt)
		{
			var random = new Random(100_000);
			var factory = services.GetService<TextureFactory>();
			_textures = new ITexture2D[settings.Textures];
			for (var t = 0; t < _textures.Length; t++)
			{
				_textures[t] = factory?.Create($"sprite{t}", 16, 16, Pattern(t)) ?? new NullTexture2D($"sprite{t}", 16, 16);
			}

			var width = Math.Max(1, window.Width);
			var height = Math.Max(1, window.Height);
			_positions = new Vector2[settings.Count];
			_velocities = new Vector2[settings.Count];
			_textureOf = new byte[settings.Count];
			for (var i = 0; i < settings.Count; i++)
			{
				_positions[i] = new Vector2(random.NextSingle() * (width - SpriteSize), random.NextSingle() * (height - SpriteSize));
				var angle = random.NextSingle() * MathF.Tau;
				_velocities[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (40f + random.NextSingle() * 160f);
				_textureOf[i] = (byte)(i % _textures.Length);
			}

			logger.LogInformation("{Count} sprites across {Textures} textures in {Width}x{Height}.", settings.Count, _textures.Length, width, height);
			_reportClock.Start();
		}

		[Update]
		public void Update(GameTime dt)
		{
			var max = new Vector2(window.Width - SpriteSize, window.Height - SpriteSize);
			var delta = dt.Delta;
			var positions = _positions;
			var velocities = _velocities;
			for (var i = 0; i < positions.Length; i++)
			{
				var p = positions[i] + velocities[i] * delta;
				ref var v = ref velocities[i];
				if (p.X < 0 || p.X > max.X) { v.X = -v.X; p.X = Math.Clamp(p.X, 0, max.X); }
				if (p.Y < 0 || p.Y > max.Y) { v.Y = -v.Y; p.Y = Math.Clamp(p.Y, 0, max.Y); }
				positions[i] = p;
			}
		}

		[Render]
		public void Render(GameTime dt)
		{
			var start = Stopwatch.GetTimestamp();
			var size = new Vector2(SpriteSize);
			spriteBatch.Begin(new SpriteBatchOptions { SortMode = SpriteSortMode.Texture, SamplerMode = SpriteSamplerMode.PointClamp });
			var textures = _textures;
			var positions = _positions;
			var textureOf = _textureOf;
			for (var i = 0; i < positions.Length; i++) spriteBatch.Draw(textures[textureOf[i]], positions[i], size);
			spriteBatch.End();
			var recorded = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

			// Frame time: from the previous Render to this one (the first frames warm up and are not counted).
			if (_frameClock.IsRunning && dt.Frame > 5)
			{
				_frameTotal += _frameClock.Elapsed.TotalMilliseconds;
				_recordTotal += recorded;
				_frames++;
			}

			_frameClock.Restart();
			_reportFrames++;
			if (_reportClock.Elapsed.TotalSeconds - _reportTime >= 1)
			{
				var seconds = _reportClock.Elapsed.TotalSeconds - _reportTime;
				var stats = (spriteBatch as ISpriteBatchStatistics)?.LastFrameStatistics ?? default;
				logger.LogInformation("{Fps:F1} fps, {Ms:F2} ms/frame ({Record:F2} ms recording), {Sprites} sprites, {DrawCalls} draw calls.",
					_reportFrames / seconds, seconds * 1000 / _reportFrames, recorded, stats.Sprites, stats.DrawCalls);
				_reportTime = _reportClock.Elapsed.TotalSeconds;
				_reportFrames = 0;
			}

			if (settings.Frames > 0 && dt.Frame + 1 >= settings.Frames)
			{
				var report = Report;
				logger.LogInformation("Done: {Frames} frames, {Ms:F2} ms/frame average, {Record:F2} ms recording.", report.Frames, report.AverageMilliseconds, report.CpuRenderMilliseconds);
				events.Emit<ExitGameEvent>();
			}
		}

		/// <summary>A 16x16 RGBA8 pattern: a ring in a hue picked by <paramref name="index"/>, transparent corners.</summary>
		public static byte[] Pattern(int index)
		{
			var pixels = new byte[16 * 16 * 4];
			var hue = index / 16f * 6f;
			var (r, g, b) = Hue(hue);
			for (var y = 0; y < 16; y++)
			{
				for (var x = 0; x < 16; x++)
				{
					var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(8, 8));
					var o = (y * 16 + x) * 4;
					var inside = d < 7.5f;
					var shade = d < 4f ? 1f : 0.7f;
					pixels[o] = (byte)(r * shade);
					pixels[o + 1] = (byte)(g * shade);
					pixels[o + 2] = (byte)(b * shade);
					pixels[o + 3] = inside ? (byte)255 : (byte)0;
				}
			}

			return pixels;
		}

		private static (float R, float G, float B) Hue(float h)
		{
			var x = 255f * (1 - Math.Abs(h % 2 - 1));
			return ((int)h % 6) switch
			{
				0 => (255, x, 0),
				1 => (x, 255, 0),
				2 => (0, 255, x),
				3 => (0, x, 255),
				4 => (x, 0, 255),
				_ => (255, 0, x),
			};
		}
	}
}
