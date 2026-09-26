using Microsoft.Extensions.DependencyInjection;

using Arch.Core;
using Arch.Core.Extensions;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Extensions.Audio;
using Ion.Extensions.Metrics;

using World = Arch.Core.World;
using Vector2 = System.Numerics.Vector2;

using Ion.Extensions.Physics2D;
using Ion.Examples.Breakout.ECS;
using Ion.Examples.Breakout.ECS.Common;

// The game setup lives in BreakoutGame so tests (Ion.Examples.Breakout.ECS.Tests) can build exactly the same game.
// Run with --Ion:Headless=true to use the headless graphics and audio backends (no GPU, window or audio device), and
// --Ion:Seed=<n> to change the random seed.
var builder = IonApplication.CreateBuilder(args);
BreakoutGame.Configure(builder);

using var game = builder.Build();
BreakoutGame.Use(game);

#if TRACY
// Built with -p:IonTracy=true: stream every span, frame mark and counter to a Tracy server.
Ion.Extensions.Metrics.Tracy.TracyExtensions.UseMetricsTracy(game);
#endif

game.Run();

public record struct Block(int Row, int Column);
public record struct Paddle(bool HasBall);
public record struct Ball();

public record struct PaddleHitEvent(float PaddleOffset);
public record struct BlockHitEvent(Entity Block);
public record struct WallHitEvent();
public record struct BallLostEvent();
public record struct BlocksClearedEvent();
public record struct LaunchBallCommand();

public static class BreakoutConstants
{
	public const int ROWS = 10;
	public const int COLS = 10;

	public const int MAX_BALLS = 300;

	public static readonly float BLOCK_GAP = 10f;
	public static readonly float PLAYER_GAP = 150f;
	public static readonly float BOTTOM_GAP = 20f;

	public static readonly Vector2 BLOCK_SIZE = new(192f, 64f);
	public static readonly Vector2 PADDLE_SIZE = new(244f, 64f);

	public static readonly Vector2 BALL_SIZE = new(32f, 32f);
	public static readonly float INITIAL_BALL_SPEED = 400f;
}

/// <summary>A wall around the play field (a physics body without a sprite).</summary>
public record struct Wall();

public static class BreakoutPhysics
{
	/// <summary>
	/// Pixels per meter for the physics world: a ball (32 px) is half a meter and a block three meters, the sizes Box2D is
	/// tuned for.
	/// </summary>
	public const float PixelsPerMeter = 64f;

	/// <summary>A ball: a bouncy, frictionless circle.</summary>
	public static Collider2D BallCollider(float radius) => Collider2D.Circle(radius) with { Restitution = 1.05f, Friction = 0f };

	/// <summary>A wall or a block: a perfectly bouncy, frictionless box.</summary>
	public static Collider2D BoxCollider(Vector2 size) => Collider2D.Box(size) with { Restitution = 1f, Friction = 0f };

	/// <summary>The paddle: a pill (a rectangle with two half circles), perfectly bouncy and frictionless.</summary>
	public static Collider2D PaddleCollider(Vector2 size) => Collider2D.Capsule(size) with { Restitution = 1f, Friction = 0f };
}

public class LevelSystem(IWindow window, World world)
{
	[Init]
	public void Init(GameTime dt)
	{
		var wallThickness = BreakoutConstants.BALL_SIZE.X * 2;
		var windowHalfExtent = window.Size / 2f;

		var sideWallSize = new Vector2(wallThickness, window.Height);
		var leftWallPosition = new Vector2((-wallThickness / 2f) + 1, windowHalfExtent.Y);
		var rightWallPosition = new Vector2(window.Width + wallThickness / 2f, windowHalfExtent.Y);

		var topWallSize = new Vector2(window.Width, wallThickness);
		var topWallPosition = new Vector2(windowHalfExtent.X, -wallThickness / 2f);
		var bottomWallPosition = new Vector2(windowHalfExtent.X, window.Height + (wallThickness / 2f) - 1f);

		// Static bodies: a collider without a RigidBody2D. Their contacts become WallHitEvents in CollisionEventSystem.
		world.Create(new Wall(), new Transform2D(leftWallPosition), BreakoutPhysics.BoxCollider(sideWallSize));
		world.Create(new Wall(), new Transform2D(rightWallPosition), BreakoutPhysics.BoxCollider(sideWallSize));
		world.Create(new Wall(), new Transform2D(topWallPosition), BreakoutPhysics.BoxCollider(topWallSize));
		world.Create(new Wall(), new Transform2D(bottomWallPosition), BreakoutPhysics.BoxCollider(topWallSize));
	}
}

/// <summary>
/// Turns the physics module's <see cref="Collision2D"/> events into the game's events: a ball touching a wall, the paddle
/// (with where on the paddle it hit) or a block. Runs in every fixed step right after the physics step
/// (<see cref="StageOrder.Physics"/>) and before the game's own fixed steps (order 0), which react to the game events.
/// </summary>
public class CollisionEventSystem(World world, IEvents events)
{
	private EventReader<Collision2D> _collisions = events.Reader<Collision2D>();

	[FixedUpdate(Order = -10)]
	public void Translate(GameTime dt)
	{
		foreach (ref readonly var collision in _collisions.Read())
		{
			if (collision.Phase != ContactPhase.Begin) continue;
			if (!world.IsAlive(collision.A) || !world.IsAlive(collision.B)) continue;

			var ball = world.Has<Ball>(collision.A) ? collision.A : world.Has<Ball>(collision.B) ? collision.B : Entity.Null;
			if (ball == Entity.Null) continue;
			var other = collision.Other(ball);

			if (world.Has<Block>(other))
			{
				events.Emit(new BlockHitEvent(other));
			}
			else if (world.Has<Paddle>(other))
			{
				// Where on the paddle the ball hit, from -1 (left end) to 1 (right end).
				var offset = (world.Get<Transform2D>(ball).Position.X - world.Get<Transform2D>(other).Position.X) / (BreakoutConstants.PADDLE_SIZE.X / 2f);
				events.Emit(new PaddleHitEvent(Math.Clamp(offset, -1f, 1f)));
			}
			else if (world.Has<Wall>(other))
			{
				events.Emit(new WallHitEvent());
			}
		}
	}
}

public class ScoreSystem(IEvents events, IAssetManager assets, ISpriteBatch spriteBatch, World world, IMetrics metrics)
{
	// Game metrics: registered once by name, updated through the handles (frame log, overlay, dotnet-counters).
	private readonly MetricsGauge _ballsMetric = metrics.Gauge("balls");
	private readonly MetricsCounter _blocksHitMetric = metrics.Counter("blocks_hit");

	private EventReader<BlockHitEvent> _blockHits = events.Reader<BlockHitEvent>();
	private EventReader<BallLostEvent> _ballsLost = events.Reader<BallLostEvent>();

	private readonly QueryDescription _ballQuery = new QueryDescription().WithAll<Ball>();

	private IFontSet _scoreFontSet = default!;
	private IFont _scoreFont = default!;
	private int _score = 0;

	private int _ballCount = 0;
	private int _lost = 0;

	/// <summary>The current score: 10 points per block hit.</summary>
	public int Score => _score;

	/// <summary>The number of balls that fell past the paddle.</summary>
	public int BallsLost => _lost;

	[Init]
	public void Init(GameTime dt)
	{
		_scoreFontSet = assets.Load<IFontSet>("Bungee-Regular.ttf");
		_scoreFont = _scoreFontSet.CreateStyle(24);
	}

	[First]
	public void UpdateScore(GameTime dt)
	{
		var hits = _blockHits.Read().Length;
		_score += 10 * hits;
		_blocksHitMetric.Add(hits);
		_lost += _ballsLost.Read().Length;
	}

	[Update]
	public void Update(GameTime dt)
	{
		_ballCount = world.CountEntities(in _ballQuery);
		_ballsMetric.Set(_ballCount);
	}

	[Render]
	public void RenderScore(GameTime dt)
	{
		spriteBatch.DrawString(_scoreFont, $"Score:  {_score}", new Vector2(20f), Color.Red);
		spriteBatch.DrawString(_scoreFont, $"Balls:  {_ballCount}", new Vector2(20f, 44), Color.Red);
		if (_lost > 0) spriteBatch.DrawString(_scoreFont, $"Lost:  {_lost}", new Vector2(20f, 68), Color.Red);
	}
}

public class SoundEffectsSystem(IAssetManager assets, IEvents events, IAudioManager audio, BreakoutSettings settings)
{
	private EventReader<WallHitEvent> _wallHits = events.Reader<WallHitEvent>();
	private EventReader<PaddleHitEvent> _paddleHits = events.Reader<PaddleHitEvent>();
	private EventReader<BlockHitEvent> _blockHits = events.Reader<BlockHitEvent>();

	private readonly Random _rand = settings.CreateRandom(1);

	private ISoundEffect _bonkSound = default!;
	private ISoundEffect _pingSound = default!;

	[Init]
	public void Init(GameTime dt)
	{
		_bonkSound =  assets.Load<ISoundEffect>("bonk.wav");
		_pingSound =  assets.Load<ISoundEffect>("ping.wav");
	}

	// After the gameplay steps of the frame (order 0), so the sounds match what they did.
	[Update(Order = 10)]
	public void Update(GameTime dt)
	{
		// Read both channels every frame (a short-circuit would leave paddle hits for the next frame).
		var bonks = _wallHits.Read().Length + _paddleHits.Read().Length;
		if (bonks > 0) audio.Play(_bonkSound, pitchShift: (_rand.NextSingle() - 0.5f) / 16f);
		if (_blockHits.Read().Length > 0) audio.Play(_pingSound, pitchShift: (_rand.NextSingle() - 0.5f) / 4f);
	}
}

public class PaddleSystem(IWindow window, World world, IInputState input, IEvents events, IAssetManager assets)
{
	private Entity _paddle = Entity.Null;

	[Init]
	public void Init(GameTime dt)
	{
		var paddleTexture = assets.Load<ITexture2D>("49-Breakout-Tiles.png");
		var paddlePosition = new Vector2(window.Width / 2f, window.Height - (BreakoutConstants.BOTTOM_GAP + (BreakoutConstants.PADDLE_SIZE.Y/2)));

		// A kinematic body: the physics step drives it to its Transform2D, which Update below moves with the mouse.
		_paddle = world.Create(new Paddle(true), new Transform2D(paddlePosition), new Sprite(paddleTexture, BreakoutConstants.PADDLE_SIZE),
			BreakoutPhysics.PaddleCollider(BreakoutConstants.PADDLE_SIZE), RigidBody2D.Kinematic());
	}

	[FixedUpdate]
	public void Update(GameTime dt)
	{
		if (window.IsMouseGrabbed)
		{
			ref var paddleTransform = ref _paddle.Get<Transform2D>();

			paddleTransform.Position = new Vector2(input.MousePosition.X, paddleTransform.Position.Y);

			if (input.Pressed(MouseButton.Left))
			{
				events.Emit(new LaunchBallCommand());
			}
		}
	}
}

/// <summary>
/// Launches balls from the paddle and handles lost balls. Launching creates the entity directly (outside any query, so
/// the score counts it this frame); the per-ball checks are [Query] steps that record their structural changes with
/// <see cref="Commands"/>, played back at the end of Update.
/// </summary>
public partial class BallSystem(IWindow window, World world, IEvents events, IAssetManager assets)
{
	private EventReader<LaunchBallCommand> _launches = events.Reader<LaunchBallCommand>();
	private EventReader<BlocksClearedEvent> _cleared = events.Reader<BlocksClearedEvent>();
	
	private ITexture2D _ballTexture = default!;
	private Entity _paddle = Entity.Null;

	private readonly QueryDescription _ballQuery = new QueryDescription().WithAll<Ball>();
	private readonly QueryDescription _paddleQuery = new QueryDescription().WithAll<Paddle>();

	private readonly Vector2 _paddleBallOffset = new(0f, -(BreakoutConstants.BALL_SIZE.Y + 20));

	// Whether the blocks were cleared this frame (read in PositionUpdate, used by the ClearBall query).
	private bool _clearing;

	// The paddle entity exists once PaddleSystem's Init step has run.
	[Init, After<PaddleSystem>]
	public void Init(GameTime dt)
	{
		_ballTexture = assets.Load<ITexture2D>("58-Breakout-Tiles.png");

		world.Query(in _paddleQuery, (Entity entity) =>
		{
			_paddle = entity;
		});
	}

	[Update]
	public void PositionUpdate(GameTime dt)
	{
		var totalBalls = world.CountEntities(in _ballQuery);

		if (_launches.TryReadLatest(out _) && totalBalls < BreakoutConstants.MAX_BALLS)
		{
			ref var paddleTransform = ref _paddle.Get<Transform2D>();
			var radius = BreakoutConstants.BALL_SIZE.X / 2f;

			var ballTransform = paddleTransform.Position + _paddleBallOffset;
			_createBall(ballTransform, radius);
		}

		_clearing = _cleared.Read().Length > 0;
	}

	/// <summary>A ball that fell below the window loses its body (the paddle gets a ball back).</summary>
	[Update(Order = 1), Query, All<Ball>]
	private void CheckLost(Entity entity, in Transform2D transform, in RigidBody2D body, Commands commands)
	{
		if (transform.Position.Y <= window.Height) return;

		// Without its collider the physics step removes the body; the ball stays where it is, drawn but out of play.
		commands.Remove<RigidBody2D>(entity);
		commands.Remove<Collider2D>(entity);
		_paddle.Get<Paddle>().HasBall = true;
		events.Emit(new BallLostEvent());
	}

	/// <summary>When every block is gone, the balls in play are removed.</summary>
	[Update(Order = 2), Query, All<Ball>]
	private void ClearBall(Entity entity, in RigidBody2D body, Commands commands)
	{
		if (!_clearing) return;

		// Destroying the entity removes its body at the next physics step.
		commands.Destroy(entity);
	}

	private Entity _createBall(Vector2 position, float radius)
	{
		// In front of the blocks and the paddle (depth 0); launched upwards as a bullet (continuous collision against the
		// other moving bodies too).
		return world.Create(new Ball(), new Transform2D(position), new Sprite(_ballTexture, BreakoutConstants.BALL_SIZE, depth: 1),
			BreakoutPhysics.BallCollider(radius), RigidBody2D.Dynamic(new Vector2(0, -100f)) with { IsBullet = true });
	}
}


public class BlockSystem(IEvents events, IAssetManager assets, World world, BreakoutSettings settings)
{
	private EventReader<BlockHitEvent> _blockHits = events.Reader<BlockHitEvent>();
	private EventReader<BlocksClearedEvent> _cleared = events.Reader<BlocksClearedEvent>();

	private readonly QueryDescription _blockQuery = new QueryDescription().WithAll<Block>();
	private readonly QueryDescription _paddleQuery = new QueryDescription().WithAll<Paddle>();
	private Entity _paddle = Entity.Null;

	private readonly Random _rand = settings.CreateRandom(2);

	[Init, After<PaddleSystem>]
	public void SetupBlocks(GameTime dt)
	{
		_resetBlocks();

		world.Query(in _paddleQuery, (Entity entity) => {
			_paddle = entity;
		});
	}

	[FixedUpdate]
	public void FixedUpdate(GameTime dt)
	{
		foreach (ref readonly var e in _blockHits.Read())
		{
			var entity = e.Block;
			// Destroying the block removes its body at the next physics step.
			if (entity.IsAlive() && entity.Has<Block>()) world.Destroy(entity);
		}
	}

	[Update]
	public void Update(GameTime dt)
	{
		if (_cleared.Read().Length > 0)
		{
			ref var paddle = ref _paddle.Get<Paddle>();

			paddle.HasBall = true;

			Console.WriteLine("You Win!");

			_resetBlocks();
		}
	}

	[Last]
	public void Last(GameTime dt)
	{
		if (world.CountEntities(in _blockQuery) == 0) events.Emit(new BlocksClearedEvent());
	}

	private void _resetBlocks()
	{
		var blockTexture = assets.Load<ITexture2D>("15-Breakout-Tiles.png");

		var blockHalfExtent = BreakoutConstants.BLOCK_SIZE / 2f;

		var rows = BreakoutConstants.ROWS;
		var cols = BreakoutConstants.COLS;
		var maxTilt = MathF.PI / 8f;

		// Setup blocks in rows and columns across the window each with different colors:
		for (int row = 0; row < rows; row++)
		{
			var rowOffset = blockHalfExtent.Y + BreakoutConstants.BLOCK_GAP + (row * (BreakoutConstants.BLOCK_SIZE.Y + BreakoutConstants.BLOCK_GAP));
			for (int col = 0; col < cols; col++)
			{
				var colOffset = blockHalfExtent.X + BreakoutConstants.BLOCK_GAP + (col * (BreakoutConstants.BLOCK_SIZE.X + BreakoutConstants.BLOCK_GAP));

				var transform = new Transform2D(new Vector2(colOffset, rowOffset), ((float)_rand.NextDouble() - 0.5f) * maxTilt);

				// A static body; its contacts become BlockHitEvents in CollisionEventSystem.
				world.Create(transform, new Block(row, col), new Sprite(blockTexture, BreakoutConstants.BLOCK_SIZE), BreakoutPhysics.BoxCollider(BreakoutConstants.BLOCK_SIZE));
			}
		}
	}
}