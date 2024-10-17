using Microsoft.Extensions.DependencyInjection;

using Arch.Core;
using Arch.Core.Extensions;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Audio;

var builder = IonApplication.CreateBuilder(args);

builder.Services.AddIon(builder.Configuration, graphics =>
{
	graphics.Output = GraphicsOutput.Window;
	graphics.ClearColor = new Color(0x333);
});

builder.Services.AddSingleton(services => World.Create());
builder.Services.AddSingleton<MouseCaptureSystem>();
builder.Services.AddSingleton<SpriteRendererSystem>();
builder.Services.AddSingleton<ScoreSystem>();
builder.Services.AddSingleton<SoundEffectsSystem>();
builder.Services.AddSingleton<PaddleSystem>();
builder.Services.AddSingleton<BallSystem>();
builder.Services.AddSingleton<BlockSystem>();

var game = builder.Build();
game.UseIon()
	.UseSystem<MouseCaptureSystem>()
	.UseSystem<SpriteRendererSystem>()
	.UseSystem<ScoreSystem>()
	//.UseSystem<SoundEffectsSystem>()
	.UseSystem<PaddleSystem>()
	.UseSystem<BallSystem>()
	.UseSystem<BlockSystem>()
	;

//Thread.Sleep(10 * 1000); // Delay to let diagnostics warm up.

game.Run();


public record struct Position(Vector2 Value);
public record struct GridLocation(int Row, int Column);
public record struct Velocity(Vector2 Direction, float Speed);
public record struct Sprite(Texture2D Texture, Vector2 Size);
public record struct Paddle(bool HasBall);
public record struct Ball();

public enum CollisionDirection
{
	Unkown = 0,
	Top,
	Bottom,
	Left,
	Right
}

public record struct PaddleHitEvent(float PaddleOffset);
public record struct BlockHitEvent(CollisionDirection Direction);
public record struct WallHitEvent(CollisionDirection Direction);
public record struct BallLostEvent();
public record struct BlocksClearedEvent();
public record struct LaunchBallCommand();

public static class BreakoutConstants
{
	public const int ROWS = 10;
	public const int COLS = 10;

	public static readonly float BLOCK_GAP = 10f;
	public static readonly float PLAYER_GAP = 150f;
	public static readonly float BOTTOM_GAP = 20f;

	public static readonly Vector2 BLOCK_SIZE = new(192f, 64f);
	public static readonly Vector2 PADDLE_SIZE = new(244f, 64f);

	public static readonly Vector2 BALL_SIZE = new(32f, 32f);
	public static readonly float INITIAL_BALL_SPEED = 400f;

	public static readonly Vector2 PADDLE_BOUNCE_MIN = Vector2.Normalize(new Vector2(-1, -0.75f));
	public static readonly Vector2 PADDLE_BOUNCE_MAX = Vector2.Normalize(new Vector2(+1, -0.75f));
}

public class MouseCaptureSystem(IWindow window, IInputState input)
{
	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		window.Size = new Vector2((BreakoutConstants.COLS * BreakoutConstants.BLOCK_SIZE.X) + ((BreakoutConstants.COLS + 1) * BreakoutConstants.BLOCK_GAP), (BreakoutConstants.ROWS * BreakoutConstants.BLOCK_SIZE.Y) + ((BreakoutConstants.ROWS + 1) * BreakoutConstants.BLOCK_GAP) + BreakoutConstants.PLAYER_GAP + BreakoutConstants.BLOCK_SIZE.Y + BreakoutConstants.BOTTOM_GAP);
		window.IsResizable = false;

		next(dt);
	}

	[Last]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		var isMouseGrabbed = window.IsMouseGrabbed;

		if (input.Pressed(Key.Escape) && isMouseGrabbed)
		{
			window.IsMouseGrabbed = false;
			window.IsCursorVisible = true;
		}

		if (input.Pressed(MouseButton.Left) && !isMouseGrabbed)
		{
			window.IsMouseGrabbed = true;
			window.IsCursorVisible = false;
		}

		next(dt);
	}
}

public class SpriteRendererSystem(ISpriteBatch spriteBatch, World world)
{
	private QueryDescription _spriteQuery = new QueryDescription().WithAll<Sprite, Position>();

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		world.Query(in _spriteQuery, (Entity entity, ref Sprite sprite, ref Position position) => {
			spriteBatch.Draw(sprite.Texture, new RectangleF(position.Value, sprite.Size));
		});

		next(dt);
	}
}

public class ScoreSystem(IEventListener events, IAssetManager assets, ISpriteBatch spriteBatch)
{
	private FontSet _scoreFontSet = default!;
	private Font _scoreFont = default!;
	private int _score = 0;

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		_scoreFontSet = assets.Load<FontSet>("BungeeRegular", "Bungee-Regular.ttf");
		_scoreFont = _scoreFontSet.CreateStyle(24);

		next(dt);
	}

	[First]
	public void UpdateScore(GameTime dt, GameLoopDelegate next)
	{
		while (events.On<BlockHitEvent>(out var e))
		{
			_score += 10;
		}

		next(dt);
	}

	[Render]
	public void RenderScore(GameTime dt, GameLoopDelegate next)
	{
		spriteBatch.DrawString(_scoreFont, $"Score:  {_score}", new Vector2(20f), Color.Red);
	}
}

public class SoundEffectsSystem(IAssetManager assets, IEventListener events, IAudioManager audio)
{
	private readonly Random _rand = new(6014);

	private SoundEffect _bonkSound = default!;
	private SoundEffect _pingSound = default!;

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		_bonkSound =  assets.Load<SoundEffect>("bonk.wav");
		_pingSound =  assets.Load<SoundEffect>("ping.wav");

		next(dt);
	}

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		next(dt);

		if (events.OnLatest<WallHitEvent>() || events.OnLatest<PaddleHitEvent>()) audio.Play(_bonkSound, pitchShift: (_rand.NextSingle() - 0.5f) / 16f);
		if (events.OnLatest<BlockHitEvent>()) audio.Play(_pingSound, pitchShift: (_rand.NextSingle() - 0.5f) / 4f);
	}
}

public class PaddleSystem(IWindow window, World world, IInputState input, IEventListener events, IAssetManager assets)
{
	private EntityReference _paddle = default!;

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var paddleTexture = assets.Load<Texture2D>("49-Breakout-Tiles.png");
		var paddleLocation = new Vector2(Math.Clamp(input.MousePosition.X - (BreakoutConstants.PADDLE_SIZE.Y / 2f), 0, window.Width - BreakoutConstants.PADDLE_SIZE.X), window.Size.Y - (BreakoutConstants.BLOCK_SIZE.Y + BreakoutConstants.BOTTOM_GAP));
		_paddle = world.Create(new Paddle(true), new Position(paddleLocation), new Sprite(paddleTexture, BreakoutConstants.PADDLE_SIZE)).Reference();

		next(dt);
	}

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		if (window.IsMouseGrabbed)
		{
			ref var paddleComponent = ref _paddle.Entity.Get<Paddle>();
			ref var paddlePosition = ref _paddle.Entity.Get<Position>();

			paddlePosition.Value = new Vector2(input.MousePosition.X - (BreakoutConstants.PADDLE_SIZE.X / 2), paddlePosition.Value.Y);

			if (paddleComponent.HasBall && input.Pressed(MouseButton.Left))
			{
				events.Emit(new LaunchBallCommand());
			}
		}

		next(dt);
	}
}

public class BallSystem(World world, IEventListener events, IWindow window, IAssetManager assets)
{	
	private EntityReference _ball = default!;
	private EntityReference _paddle = default!;
	private QueryDescription _paddleQuery = new QueryDescription().WithAll<Paddle, Position>();
	private readonly QueryDescription _blockQuery = new QueryDescription().WithAll<GridLocation, Sprite, Position>();

	private readonly Vector2 _paddleBallOffset = new((BreakoutConstants.PADDLE_SIZE.X - BreakoutConstants.BALL_SIZE.X) / 2f, -(BreakoutConstants.BALL_SIZE.Y + 1));

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var ballTexture = assets.Load<Texture2D>("58-Breakout-Tiles.png");

		_ball = world.Create(new Ball(), new Velocity(Vector2.UnitY, 0), new Position(Vector2.Zero), new Sprite(ballTexture, BreakoutConstants.BALL_SIZE)).Reference();

		next(dt);

		world.Query(in _paddleQuery, (Entity entity) =>
		{
			_paddle = entity.Reference();
		});
	}

	[Update]
	public void PositionUpdate(GameTime dt, GameLoopDelegate next)
	{
		ref var ballPosition = ref _ball.Entity.Get<Position>();
		ref var ballVelocity = ref _ball.Entity.Get<Velocity>();
		ref var paddle = ref _paddle.Entity.Get<Paddle>();

		if (paddle.HasBall)
		{
			ballPosition.Value = _paddle.Entity.Get<Position>().Value + _paddleBallOffset;

			if (events.OnLatest<LaunchBallCommand>())
			{
				ballVelocity.Direction = -Vector2.UnitY;
				ballVelocity.Speed = BreakoutConstants.INITIAL_BALL_SPEED;
				paddle.HasBall = false;
			}
		}
		else
		{
			ballPosition.Value += ballVelocity.Direction * ballVelocity.Speed * dt.Delta;
		}

		_handleWallHits(ref ballPosition, ref ballVelocity, ref paddle);
		_handlePaddleAndBlockHits(ref ballPosition, ref ballVelocity);

		next(dt);
	}	

	private void _handleWallHits(ref Position ballPosition, ref Velocity ballVelocity, ref Paddle paddle)
	{
		CollisionDirection? wallHitDirection = null;
		if (ballPosition.Value.X < 0)
		{
			ballPosition.Value = new Vector2(0, ballPosition.Value.Y);
			ballVelocity.Direction *= new Vector2(-1, 1);
			wallHitDirection = CollisionDirection.Left;
		}

		if (ballPosition.Value.X >= window.Width - BreakoutConstants.BALL_SIZE.X)
		{
			ballPosition.Value = new Vector2(window.Width - (BreakoutConstants.BALL_SIZE.X + 1), ballPosition.Value.Y);
			ballVelocity.Direction *= new Vector2(-1, 1);
			wallHitDirection = CollisionDirection.Right;
		}

		if (ballPosition.Value.Y <= 0)
		{
			ballPosition.Value = new Vector2(ballPosition.Value.X, 1);
			ballVelocity.Direction *= new Vector2(1, -1);
			wallHitDirection = CollisionDirection.Top;
		}

		if (ballPosition.Value.Y > window.Height)
		{
			ballVelocity.Direction = Vector2.UnitY;
			ballVelocity.Speed = BreakoutConstants.INITIAL_BALL_SPEED;
			paddle.HasBall = true;
			events.Emit(new BallLostEvent());
		}

		if (wallHitDirection is not null) {
			ballVelocity.Speed += 5f;
			events.Emit(new WallHitEvent(wallHitDirection.Value));
		}
	}

	private void _handlePaddleAndBlockHits(ref Position ballPosition, ref Velocity ballVelocity)
	{
		ref var paddlePosition = ref _paddle.Entity.Get<Position>();
		var paddleRect = new RectangleF(paddlePosition.Value, BreakoutConstants.PADDLE_SIZE);
		var ballRect = new RectangleF(ballPosition.Value, BreakoutConstants.BALL_SIZE);

		RectangleF.Intersect(ref ballRect, ref paddleRect, out var paddleIntersection);

		if (!paddleIntersection.IsEmpty)
		{
			ballVelocity.Speed += 5f;
			var direction = _getIntersectionDirection(ref paddleIntersection, ref ballRect);
			if (direction == CollisionDirection.Bottom)
			{
				var hitSide = (ballRect.X - (paddleRect.X - ballRect.Width)) / (ballRect.Width + paddleRect.Width);
				ballVelocity.Direction = Vector2.Normalize(Vector2.Lerp(BreakoutConstants.PADDLE_BOUNCE_MIN, BreakoutConstants.PADDLE_BOUNCE_MAX, hitSide));
				ballPosition.Value = new Vector2(ballPosition.Value.X, paddleRect.Top - (ballRect.Height + 1));
			}
			else
			{
				ballVelocity.Direction = _recalculateBallDirection(ballVelocity, direction);
				ballVelocity.Direction = new Vector2(ballVelocity.Direction.X, -MathF.Abs(ballVelocity.Direction.Y));
				ballPosition.Value = new Vector2(direction == CollisionDirection.Left ? paddleRect.Right + 1 : paddleRect.Left - (ballRect.Width + 1), ballPosition.Value.Y - 1);
			}
			events.Emit(new PaddleHitEvent((paddleIntersection.Left - paddleRect.Left) / paddleRect.Width));
		}

		var collisions = new List<CollisionDirection>();
		world.Query(in _blockQuery, (Entity entity, ref GridLocation gridLocation, ref Sprite sprite, ref Position position) =>
		{
			var i = (gridLocation.Row * BreakoutConstants.COLS) + gridLocation.Column;
			var blockRect = new RectangleF(position.Value, BreakoutConstants.BLOCK_SIZE);

			RectangleF.Intersect(ref ballRect, ref blockRect, out var blockIntersection);
			if (blockIntersection.IsEmpty) return;

			var direction = _getIntersectionDirection(ref blockIntersection, ref ballRect);
			collisions.Add(direction);
			world.Destroy(entity);
			events.Emit(new BlockHitEvent(direction));
		});

		if (collisions.Count > 0)
		{
			if (collisions.Count > 1) Console.WriteLine("Multiple collisions: " + string.Join(", ", collisions));

			var direction = collisions.Select(collisions => collisions switch
			{
				CollisionDirection.Top or CollisionDirection.Bottom => new Vector2(1, -1),
				CollisionDirection.Left or CollisionDirection.Right => new Vector2(-1, 1),
				_ => Vector2.Zero
			}).Aggregate(Vector2.One , (totalDir, dir) => totalDir * dir);

			ballVelocity.Direction *= direction;
		}
	}

	private CollisionDirection _getIntersectionDirection(ref RectangleF intersection, ref RectangleF ballRect)
	{
		if (intersection.Width > intersection.Height) {
			if (intersection.Top == ballRect.Top) return CollisionDirection.Top;
			if (intersection.Bottom == ballRect.Bottom) return CollisionDirection.Bottom;
		} else {
			if (intersection.Left == ballRect.Left) return CollisionDirection.Left;
			if (intersection.Right == ballRect.Right) return CollisionDirection.Right;
		}	
		
		return CollisionDirection.Unkown;
	}

	private Vector2 _recalculateBallDirection(Velocity ballVelocity, CollisionDirection collisionDirection)
	{
		return collisionDirection switch
		{
			CollisionDirection.Top or CollisionDirection.Bottom => new Vector2(ballVelocity.Direction.X, -ballVelocity.Direction.Y),
			CollisionDirection.Left or CollisionDirection.Right => new Vector2(-ballVelocity.Direction.X, ballVelocity.Direction.Y),
			_ => ballVelocity.Direction,
		};
	}
}


public class BlockSystem(IEventListener events, IAssetManager assets, World world)
{
	private readonly QueryDescription _blockQuery = new QueryDescription().WithAll<GridLocation, Sprite, Position>();
	private readonly QueryDescription _ballQuery = new QueryDescription().WithAll<Ball, Velocity>();
	private readonly QueryDescription _paddleQuery = new QueryDescription().WithAll<Paddle>();
	private EntityReference _paddle = default!;
	private EntityReference _ball = default!;

	[Init]
	public void SetupBlocks(GameTime dt, GameLoopDelegate next)
	{
		_resetBlocks();

		next(dt);

		world.Query(in _ballQuery, (Entity entity) => {
			_ball = entity.Reference();
		});

		world.Query(in _paddleQuery, (Entity entity) => {
			_paddle = entity.Reference();
		});
	}

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		next(dt);

		if (events.On<BlocksClearedEvent>())
		{
			ref var paddle = ref _paddle.Entity.Get<Paddle>();
			ref var ballVelocity = ref _ball.Entity.Get<Velocity>();

			paddle.HasBall = true;
			ballVelocity.Direction = Vector2.Zero;
			ballVelocity.Speed = BreakoutConstants.INITIAL_BALL_SPEED;

			Console.WriteLine("You Win!");

			_resetBlocks();
		}
	}

	[Last]
	public void Last(GameTime dt, GameLoopDelegate next)
	{
		next(dt);

		if (world.CountEntities(in _blockQuery) == 0)
		{
			events.Emit(new BlocksClearedEvent());
		}
	}

	private void _resetBlocks()
	{
		var blockTexture = assets.Load<Texture2D>("15-Breakout-Tiles.png");

		var edgeOffset = new Vector2(BreakoutConstants.BLOCK_GAP);

		// Setup blocks in rows and columns across the window each with different colors:
		for (int row = 0; row < BreakoutConstants.ROWS; row++)
		{
			for (int col = 0; col < BreakoutConstants.COLS; col++)
			{
				var position = new Position(edgeOffset + new Vector2(col * (BreakoutConstants.BLOCK_SIZE.X + BreakoutConstants.BLOCK_GAP), row * (BreakoutConstants.BLOCK_SIZE.Y + BreakoutConstants.BLOCK_GAP)));
				world.Create(position, new GridLocation(row, col), new Sprite(blockTexture, BreakoutConstants.BLOCK_SIZE));
			}
		}
	}
}