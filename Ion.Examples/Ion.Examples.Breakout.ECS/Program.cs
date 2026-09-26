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
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;

using Ion.Examples.Breakout.ECS.Physics;
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

public static class PhysicsManagerExtensions
{
	public static Body AddDynamicSphere(this PhysicsManager physics, float radius, Vector2 position, float rotation = 0)
	{
		var body = physics.CreateBody(position, rotation, BodyType.Dynamic);
		body.IsBullet = true;
		var fixture = body.CreateCircle(radius, 10f);
		fixture.Restitution = 1.05f;
		fixture.Friction = 0f;

		return body;
	}

	public static Body AddStaticBox(this PhysicsManager physics, Vector2 size, Vector2 position, float rotation = 0)
	{
		var body = physics.CreateBody(position, rotation, BodyType.Static);
		var fixture = body.CreateRectangle(size.X, size.Y, 0.9f, AetherVector2.Zero);
		fixture.Restitution = 1f;
		fixture.Friction = 0f;

		return body;
	}

	public static Body AddKinematicPaddle(this PhysicsManager physics, Vector2 size, Vector2 position, float rotation = 0)
	{
		var radius = size.Y / 2f;
		var rectSizeX = size.X - size.Y;
		var restitution = 1f;
		var friction = 0f;

		var body = physics.CreateBody(position, rotation, BodyType.Kinematic);
		var rect = body.CreateRectangle(rectSizeX, size.Y, 0.9f, AetherVector2.Zero);
		rect.Restitution = restitution;
		rect.Friction = friction;

		var circleR = body.CreateCircle(radius, 10f, new AetherVector2(rectSizeX / 2f, 0));
		circleR.Restitution = restitution;
		circleR.Friction = friction;

		var circleL = body.CreateCircle(radius, 10f, new AetherVector2(-rectSizeX / 2f, 0));
		circleL.Restitution = restitution;
		circleL.Friction = friction;

		return body;
	}
}

public class LevelSystem(IWindow window, PhysicsManager physics, IEvents events)
{
	[Init]
	public unsafe void Init(GameTime dt)
	{
		var wallThickness = BreakoutConstants.BALL_SIZE.X * 2;
		var windowHalfExtent = window.Size / 2f;

		var sideWallSize = new Vector2(wallThickness, window.Height);
		var leftWallPosition = new Vector2((-wallThickness / 2f) + 1, windowHalfExtent.Y);
		var rightWallPosition = new Vector2(window.Width + wallThickness / 2f, windowHalfExtent.Y);

		var topWallSize = new Vector2(window.Width, wallThickness);
		var topWallPosition = new Vector2(windowHalfExtent.X, -wallThickness / 2f);
		var bottomWallPosition = new Vector2(windowHalfExtent.X, window.Height + (wallThickness / 2f) - 1f);

		_addWall(physics.AddStaticBox(sideWallSize / physics.PhysicsScale, leftWallPosition / physics.PhysicsScale));
		_addWall(physics.AddStaticBox(sideWallSize / physics.PhysicsScale, rightWallPosition / physics.PhysicsScale));
		_addWall(physics.AddStaticBox(topWallSize / physics.PhysicsScale, topWallPosition / physics.PhysicsScale));
		_addWall(physics.AddStaticBox(topWallSize / physics.PhysicsScale, bottomWallPosition / physics.PhysicsScale));
	}

	private void _addWall(Body wall)
	{
		wall.OnCollision += (Fixture sender, Fixture other, Contact contact) =>
		{
			events.Emit(new WallHitEvent());
			return true;
		};
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

public class PaddleSystem(IWindow window, World world, IInputState input, IEvents events, IAssetManager assets, PhysicsManager physics)
{
	private Entity _paddle = Entity.Null;

	[Init]
	public unsafe void Init(GameTime dt)
	{
		var paddleTexture = assets.Load<ITexture2D>("49-Breakout-Tiles.png");
		var paddlePosition = new Vector2(window.Width / 2f, window.Height - (BreakoutConstants.BOTTOM_GAP + (BreakoutConstants.PADDLE_SIZE.Y/2)));

		var paddleBody = physics.AddKinematicPaddle(BreakoutConstants.PADDLE_SIZE / physics.PhysicsScale, paddlePosition / physics.PhysicsScale);
		paddleBody.OnCollision += (Fixture sender, Fixture other, Contact contact) =>
		{
			// Where on the paddle the ball hit, from -1 (left end) to 1 (right end).
			var offset = (other.Body.Position.X - sender.Body.Position.X) * physics.PhysicsScale / (BreakoutConstants.PADDLE_SIZE.X / 2f);
			events.Emit(new PaddleHitEvent(Math.Clamp(offset, -1f, 1f)));
			return true;
		};
		
		_paddle = world.Create(new Paddle(true), new Transform2D(paddlePosition), new Sprite(paddleTexture, BreakoutConstants.PADDLE_SIZE), new KinematicRigidBody(paddleBody));
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
public unsafe partial class BallSystem(IWindow window, World world, IEvents events, IAssetManager assets, PhysicsManager physics)
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
	public unsafe void Init(GameTime dt)
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
			var entity = _createBall(ballTransform);

			var ballBody = physics.AddDynamicSphere(radius / physics.PhysicsScale, ballTransform / physics.PhysicsScale);

			entity.Add(new DynamicRigidBody(ballBody));
			ballBody.LinearVelocity = new AetherVector2(0, -100f / physics.PhysicsScale);
		}

		_clearing = _cleared.Read().Length > 0;
	}

	/// <summary>A ball that fell below the window loses its body (the paddle gets a ball back).</summary>
	[Update(Order = 1), Query, All<Ball>]
	private void CheckLost(Entity entity, in Transform2D transform, in DynamicRigidBody body, Commands commands)
	{
		if (transform.Position.Y <= window.Height) return;

		physics.Remove(body.Body);
		commands.Remove<DynamicRigidBody>(entity);
		_paddle.Get<Paddle>().HasBall = true;
		events.Emit(new BallLostEvent());
	}

	/// <summary>When every block is gone, the balls in play are removed.</summary>
	[Update(Order = 2), Query, All<Ball>]
	private void ClearBall(Entity entity, in DynamicRigidBody body, Commands commands)
	{
		if (!_clearing) return;

		physics.Remove(body.Body);
		commands.Destroy(entity);
	}

	private Entity _createBall(Vector2 position)
	{
		// In front of the blocks and the paddle (depth 0).
		return world.Create(new Ball(), new Transform2D(position), new Sprite(_ballTexture, BreakoutConstants.BALL_SIZE, depth: 1));
	}
}


public unsafe class BlockSystem(IEvents events, IAssetManager assets, World world, PhysicsManager physics, BreakoutSettings settings)
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
			if (entity.IsAlive() && entity.Has<StaticBody>())
			{
				ref var fixture = ref entity.Get<StaticBody>();

				physics.Remove(fixture.Body);
				world.Destroy(entity);
			}
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

	private unsafe void _resetBlocks()
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
				var body = physics.AddStaticBox(BreakoutConstants.BLOCK_SIZE / physics.PhysicsScale, transform.Position / physics.PhysicsScale, transform.Rotation);
				
				var blockEntity = world.Create(transform, new Block(row, col), new Sprite(blockTexture, BreakoutConstants.BLOCK_SIZE), new StaticBody(body));

				body.OnCollision += (Fixture sender, Fixture other, Contact contact) => {
					events.Emit(new BlockHitEvent(blockEntity));
					return true;
				};
			}
		}
	}
}