using Microsoft.Extensions.DependencyInjection;

using Arch.Core;
using Arch.Core.Extensions;
using MagicPhysX;
using MagicPhysX.Toolkit;

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

builder.Services.AddSingleton<MouseCaptureSystem>();
builder.Services.AddSingleton<SpriteRendererSystem>();
builder.Services.AddScoped(services => World.Create());
builder.Services.AddScoped<ScoreSystem>();
builder.Services.AddScoped<SoundEffectsSystem>();
builder.Services.AddScoped<PaddleSystem>();
builder.Services.AddScoped<BallSystem>();
builder.Services.AddScoped<BlockSystem>();
builder.Services.AddScoped(services => new PhysicsSystem(true));
builder.Services.AddScoped(services => services.GetRequiredService<PhysicsSystem>().CreateScene(Vector3.Zero));
builder.Services.AddScoped<BreakoutPhysicsSystem>();

var game = builder.Build();
game.UseIon()
	.UseSystem<MouseCaptureSystem>()
	.UseSystem<BreakoutPhysicsSystem>()
	//.UseSystem<SoundEffectsSystem>()
	.UseSystem<PaddleSystem>()
	.UseSystem<BallSystem>()
	.UseSystem<BlockSystem>()
	.UseSystem<SpriteRendererSystem>()
	.UseSystem<ScoreSystem>()
	;

//Thread.Sleep(10 * 1000); // Delay to let diagnostics warm up.

game.Run();

public record struct DynamicRigidBody(Rigidbody Body);
public record struct KinematicRigidBody(Rigidbody Body);
public record struct StaticBody(Rigidstatic Body);
public record struct Sprite(Texture2D Texture, Vector2 Size);
public record struct Position(Vector2 Value);
public record struct GridLocation(int Row, int Column);
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
			spriteBatch.Draw(sprite.Texture, new RectangleF(position.Value - (sprite.Size / 2), sprite.Size));
		});

		next(dt);
	}
}

public class BreakoutPhysicsSystem(IWindow window, World world, PhysicsScene physicsScene)
{
	private readonly QueryDescription _kinematicQuery = new QueryDescription().WithAll<KinematicRigidBody, Position>();
	private readonly QueryDescription _dynamicQuery = new QueryDescription().WithAll<DynamicRigidBody, Position>();

	[Init]
	public unsafe void Init(GameTime dt, GameLoopDelegate next)
	{
		var wallMaterial = physicsScene.CreateMaterial(0.5f, 0.5f, 1.1f);
		var sideWallHalfExtent = new Vector3(5, window.Height / 2, 5);
		var leftWallPosition = new Vector3(-5, window.Height / 2, 0);
		var rightWallPosition = new Vector3(window.Width + 5, window.Height / 2, 0);

		var topWallHalfExtent = new Vector3(window.Width / 2, 5, 5);
		var topWallPosition = new Vector3(window.Width / 2, 0, 0);

		var leftWall = physicsScene.AddStaticBox(sideWallHalfExtent, leftWallPosition, Quaternion.Identity, wallMaterial);
		var rightWall = physicsScene.AddStaticBox(sideWallHalfExtent, rightWallPosition, Quaternion.Identity, wallMaterial);
		var topWall = physicsScene.AddStaticBox(topWallHalfExtent, topWallPosition, Quaternion.Identity, wallMaterial);

		next(dt);
	}

	[FixedUpdate]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		world.Query(in _kinematicQuery, (ref KinematicRigidBody rigidBody, ref Position position) =>
		{
			var position3d = new Vector3(position.Value, 0);
			rigidBody.Body.position = position3d;
		});

		physicsScene.Update(dt.Delta);

		world.Query(in _dynamicQuery, (ref DynamicRigidBody rigidBody, ref Position position) =>
		{
			var position3d = rigidBody.Body.transform.position;
			position.Value = new Vector2(position3d.X, position3d.Y);
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

public class PaddleSystem(IWindow window, World world, IInputState input, IEventListener events, IAssetManager assets, PhysicsScene physicsScene)
{
	private EntityReference _paddle = EntityReference.Null;
	private Rigidbody _paddleBody = default!;

	[Init]
	public unsafe void Init(GameTime dt, GameLoopDelegate next)
	{
		var paddleTexture = assets.Load<Texture2D>("49-Breakout-Tiles.png");
		var paddlePosition = new Vector2(window.Width / 2f, window.Size.Y - (BreakoutConstants.BLOCK_SIZE.Y + BreakoutConstants.BOTTOM_GAP));
		
		var material = physicsScene.CreateMaterial(0.5f, 0.5f, 1.2f);

		var paddlePosition3d = new Vector3(paddlePosition, 0);
		_paddleBody = physicsScene.AddKinematicCapsule(BreakoutConstants.PADDLE_SIZE.Y / 2f, (BreakoutConstants.PADDLE_SIZE.X - BreakoutConstants.PADDLE_SIZE.Y) / 2f, paddlePosition3d, Quaternion.Identity, 1, material);
		_paddle = world.Create(new Paddle(true), new Position(paddlePosition), new Sprite(paddleTexture, BreakoutConstants.PADDLE_SIZE), new KinematicRigidBody(_paddleBody)).Reference();

		next(dt);
	}

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		if (window.IsMouseGrabbed)
		{
			ref var paddleComponent = ref _paddle.Entity.Get<Paddle>();
			ref var paddlePosition = ref _paddle.Entity.Get<Position>();

			paddlePosition.Value = new Vector2(input.MousePosition.X, paddlePosition.Value.Y);

			if (paddleComponent.HasBall && input.Pressed(MouseButton.Left))
			{
				events.Emit(new LaunchBallCommand());
			}
		}

		next(dt);
	}
}

public unsafe class BallSystem(IWindow window, World world, IEventListener events, IAssetManager assets, PhysicsScene physicsScene)
{	
	private Texture2D _ballTexture = default!;
	private PxMaterial* _ballMaterial = default!;
	private EntityReference _ball = EntityReference.Null;
	private EntityReference _paddle = EntityReference.Null;

	private QueryDescription _ballQuery = new QueryDescription().WithAll<Ball>();
	private QueryDescription _paddleQuery = new QueryDescription().WithAll<Paddle>();

	private readonly Vector2 _paddleBallOffset = new(0, -((BreakoutConstants.BALL_SIZE.Y + BreakoutConstants.PADDLE_SIZE.Y) / 2) - 1);

	[Init]
	public unsafe void Init(GameTime dt, GameLoopDelegate next)
	{
		_ballTexture = assets.Load<Texture2D>("58-Breakout-Tiles.png");
		_ballMaterial = physicsScene.CreateMaterial(0.5f, 0.5f, 1f);

		_createBall(new Vector2(window.Width / 2, window.Size.Y - (BreakoutConstants.BLOCK_SIZE.Y + BreakoutConstants.BOTTOM_GAP)));

		next(dt);

		world.Query(in _ballQuery, (Entity entity) =>
		{
			_ball = entity.Reference();
		});

		world.Query(in _paddleQuery, (Entity entity) =>
		{
			_paddle = entity.Reference();
		});
	}

	[Update]
	public void PositionUpdate(GameTime dt, GameLoopDelegate next)
	{
		ref var ballPosition = ref _ball.Entity.Get<Position>();
		ref var paddle = ref _paddle.Entity.Get<Paddle>();

		if (paddle.HasBall)
		{
			var paddlePosition = _paddle.Entity.Get<Position>().Value;
			ballPosition.Value = paddlePosition + _paddleBallOffset;

			if (events.OnLatest<LaunchBallCommand>())
			{
				var ballPosition3d = new Vector3(ballPosition.Value, 0);
				var ballBody = physicsScene.AddDynamicSphere(BreakoutConstants.BALL_SIZE.X / 2f, ballPosition3d, Quaternion.Identity, 1, _ballMaterial);
				ballBody.constraints = RigidbodyConstraints.FreezePositionZ;
				_ball.Entity.Add(new DynamicRigidBody(ballBody));
				ballBody.AddForce(new Vector3(100f, -300, 0), ForceMode.VelocityChange);

				paddle.HasBall = false;
			}
		}

		next(dt);
	}

	private Entity _createBall(Vector2 position)
	{
		return world.Create(new Ball(), new Position(position), new Sprite(_ballTexture, BreakoutConstants.BALL_SIZE));		
	}
}


public unsafe class BlockSystem(IEventListener events, IAssetManager assets, World world, PhysicsScene physicsScene)
{
	private readonly QueryDescription _blockQuery = new QueryDescription().WithAll<GridLocation, Sprite, Position>();
	private readonly QueryDescription _paddleQuery = new QueryDescription().WithAll<Paddle>();
	private EntityReference _paddle = EntityReference.Null;

	private PxMaterial* _blockMaterial = default!;

	[Init]
	public void SetupBlocks(GameTime dt, GameLoopDelegate next)
	{
		_blockMaterial = physicsScene.CreateMaterial(0.5f, 0.5f, 1f);

		_resetBlocks();

		next(dt);

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

			paddle.HasBall = true;

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

	private unsafe void _resetBlocks()
	{
		var blockTexture = assets.Load<Texture2D>("15-Breakout-Tiles.png");

		var blockHalfExtent = new Vector3(BreakoutConstants.BLOCK_SIZE.X / 2f, BreakoutConstants.BLOCK_SIZE.Y / 2f, 5);

		// Setup blocks in rows and columns across the window each with different colors:
		for (int row = 0; row < BreakoutConstants.ROWS; row++)
		{
			var rowOffset = BreakoutConstants.BLOCK_GAP + blockHalfExtent.Y + (row * (BreakoutConstants.BLOCK_SIZE.Y + BreakoutConstants.BLOCK_GAP));
			for (int col = 0; col < BreakoutConstants.COLS; col++)
			{
				var colOffset = BreakoutConstants.BLOCK_GAP + blockHalfExtent.X + (col * (BreakoutConstants.BLOCK_SIZE.X + BreakoutConstants.BLOCK_GAP));

				var position = new Position(new Vector2(colOffset, rowOffset));
				var position3d = new Vector3(position.Value, 0);
				var body = physicsScene.AddStaticBox(blockHalfExtent, position3d, Quaternion.Identity, _blockMaterial);
				
				var blockEntity = world.Create(position, new GridLocation(row, col), new Sprite(blockTexture, BreakoutConstants.BLOCK_SIZE), new StaticBody(body));
			}
		}
	}
}