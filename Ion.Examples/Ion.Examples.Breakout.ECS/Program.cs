using Microsoft.Extensions.DependencyInjection;

using Arch.Core;
using Arch.Core.Extensions;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Audio;

using World = Arch.Core.World;
using Vector2 = System.Numerics.Vector2;
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Collision.Shapes;
using nkast.Aether.Physics2D.Common;
using nkast.Aether.Physics2D.Dynamics.Contacts;

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
builder.Services.AddScoped<PhysicsService>();
builder.Services.AddScoped<PhysicsSystem>();
builder.Services.AddScoped<LevelSystem>();

var game = builder.Build();
game.UseIon()
	.UseSystem<MouseCaptureSystem>()
	.UseSystem<PhysicsSystem>()
	.UseSystem<LevelSystem>()
	.UseSystem<SoundEffectsSystem>()
	.UseSystem<PaddleSystem>()
	.UseSystem<BallSystem>()
	.UseSystem<BlockSystem>()
	.UseSystem<SpriteRendererSystem>()
	.UseSystem<ScoreSystem>()
	;

//Thread.Sleep(10 * 1000); // Delay to let diagnostics warm up.

game.Run();

public record struct DynamicRigidBody(Body Body);
public record struct KinematicRigidBody(Body Body);
public record struct StaticBody(Body Body);
public record struct Sprite(Texture2D Texture, Vector2 Size);
public record struct Transform2D(Vector2 Position, float Rotation = 0);
public record struct GridLocation(int Row, int Column);
public record struct Paddle(bool HasBall);
public record struct Ball();

public record struct PaddleHitEvent(float PaddleOffset);

public record struct BlockHitEvent(EntityReference Block);
public record struct WallHitEvent();
public record struct BallLostEvent();
public record struct BlocksClearedEvent();
public record struct LaunchBallCommand();

public static class BreakoutConstants
{
	public const int ROWS = 10;
	public const int COLS = 10;

	public const float PHYSICS_SCALE = 10f;

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
		window.Size = new Vector2((BreakoutConstants.COLS * BreakoutConstants.BLOCK_SIZE.X) + ((BreakoutConstants.COLS + 1) * BreakoutConstants.BLOCK_GAP), (BreakoutConstants.ROWS * BreakoutConstants.BLOCK_SIZE.Y) + ((BreakoutConstants.ROWS + 1) * BreakoutConstants.BLOCK_GAP) + BreakoutConstants.PLAYER_GAP + BreakoutConstants.PADDLE_SIZE.Y + BreakoutConstants.BOTTOM_GAP);
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

public class SpriteRendererSystem(ISpriteBatch spriteBatch, World world, IWindow window)
{
	private QueryDescription _spriteQuery = new QueryDescription().WithAll<Sprite, Transform2D>();

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		var windowHeight = window.Height;
		world.Query(in _spriteQuery, (Entity entity, ref Sprite sprite, ref Transform2D transform) => {
			var halfExtent = sprite.Size / 2f;
			var centerPosition = transform.Position;
			var rotation = transform.Rotation;

			var topLeft = new Vector2(
							-halfExtent.X * MathF.Cos(rotation) + halfExtent.Y * MathF.Sin(rotation),
							-halfExtent.X * MathF.Sin(rotation) - halfExtent.Y * MathF.Cos(rotation)
						) + centerPosition;

			spriteBatch.Draw(sprite.Texture, topLeft, sprite.Size, rotation: transform.Rotation);
		});

		next(dt);
	}
}

public class PhysicsService(ISpriteBatch spriteBatch) : IDisposable
{
	public readonly AetherWorld World = new(new AetherVector2(0));

	public void Init()
	{
		World.ContactManager.VelocityConstraintsMultithreadThreshold = 256;
		World.ContactManager.PositionConstraintsMultithreadThreshold = 256;
		World.ContactManager.CollideMultithreadThreshold = 256;
	}

	public void Step(GameTime dt)
	{
		World.Step(dt.Delta);
	}

	public void DebugRender(GameTime dt, float scale)
	{
		foreach (var body in World.BodyList)
		{
			var position = new Vector2(body.Position.X, body.Position.Y);
			var rotation = body.Rotation;

			foreach( var fixture in body.FixtureList)
			{
				var shape = fixture.Shape;
				var color = fixture.Body.BodyType switch
				{
					BodyType.Static => Color.Blue,
					BodyType.Dynamic => Color.Red,
					BodyType.Kinematic => Color.Green,
					_ => Color.White
				};

				if (shape is PolygonShape polygon)
				{
					var vertices = polygon.Vertices;
					var count = polygon.Vertices.Count;

					for (var i = 0; i < count; i++)
					{
						var localStart = new Vector2(vertices[i].X, vertices[i].Y);
						var localEnd = new Vector2(vertices[(i + 1) % count].X, vertices[(i + 1) % count].Y);

						// Apply rotation to the vertices
						var rotatedStart = new Vector2(
							localStart.X * MathF.Cos(rotation) - localStart.Y * MathF.Sin(rotation),
							localStart.X * MathF.Sin(rotation) + localStart.Y * MathF.Cos(rotation)
						);

						var rotatedEnd = new Vector2(
							localEnd.X * MathF.Cos(rotation) - localEnd.Y * MathF.Sin(rotation),
							localEnd.X * MathF.Sin(rotation) + localEnd.Y * MathF.Cos(rotation)
						);

						var start = new Vector2(rotatedStart.X + position.X, rotatedStart.Y + position.Y);
						var end = new Vector2(rotatedEnd.X + position.X, rotatedEnd.Y + position.Y);

						spriteBatch.DrawLine(color, start * scale, end * scale);
					}
				}
				else if (shape is CircleShape circle)
				{
					var center = new Vector2(circle.Position.X, circle.Position.Y) + position;
					var radius = circle.Radius;
					var segments = 10f;
					var increment = MathHelper.TwoPi / segments;

					for(var line = 0; line < segments; line++)
					{
						var angle = line * increment;
						var nextAngle = (line + 1) * increment;

						var localStart = new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius) + center;
						var localEnd = new Vector2(MathF.Cos(nextAngle) * radius, MathF.Sin(nextAngle) * radius) + center;

						var start = new Vector2(localStart.X, localStart.Y);
						var end = new Vector2(localEnd.X, localEnd.Y);

						spriteBatch.DrawLine(color, start * scale, end * scale);
					}

					// Draw radius line based on rotation:
					var radiusEnd = new Vector2(MathF.Cos(rotation) * radius, MathF.Sin(rotation) * radius) + center;
					spriteBatch.DrawLine(color, center * scale, radiusEnd * scale);
				}
			}

			var jointEdge = body.JointList;
			while (jointEdge is not null)
			{
				// Draw boxes for joints
				var anchorA = jointEdge.Joint.WorldAnchorA * scale;
				var anchorB = jointEdge.Joint.WorldAnchorB * scale;

				// Draw rotated rectangle from anchorA to anchorB
				spriteBatch.DrawLine(Color.Yellow, new Vector2(anchorA.X, anchorA.Y), new Vector2(anchorB.X, anchorB.Y));
				jointEdge = jointEdge.Next;
			}
		}
	}

	public Body AddDynamicSphere(float radius, Vector2 position, float rotation = 0)
	{
		var body = World.CreateBody(new AetherVector2(position.X, position.Y), rotation, BodyType.Dynamic);
		body.IsBullet = true;
		var fixture = body.CreateCircle(radius, 10f);
		fixture.Restitution = 1.05f;
		fixture.Friction = 0f;

		return body;
	}

	public Body AddStaticBox(Vector2 size, Vector2 position, float rotation = 0)
	{
		var body = World.CreateBody(new AetherVector2(position.X, position.Y), rotation, BodyType.Static);
		var fixture = body.CreateRectangle(size.X, size.Y, 0.9f, AetherVector2.Zero);
		fixture.Restitution = 1f;
		fixture.Friction = 0f;

		return body;
	}

	public Fixture AddDynamicBox(Vector2 size, Vector2 position, float rotation = 0)
	{
		var body = World.CreateBody(new AetherVector2(position.X, position.Y), rotation, BodyType.Dynamic);
		var fixture = body.CreateRectangle(size.X, size.Y, 0.9f, AetherVector2.Zero);
		fixture.Restitution = 1f;
		fixture.Friction = 0f;

		return fixture;
	}

	public Body AddKinematicPaddle(Vector2 size, Vector2 position, float rotation = 0)
	{
		var radius = size.Y / 2f;
		var rectSizeX = size.X - size.Y;

		var body = World.CreateBody(new AetherVector2(position.X, position.Y), rotation, BodyType.Kinematic);
		var rect = body.CreateRectangle(rectSizeX, size.Y, 0.9f, AetherVector2.Zero);
		rect.Restitution = 1f;
		rect.Friction = 0f;

		var circleR = body.CreateCircle(radius, 10f, new AetherVector2(rectSizeX / 2f, 0));
		circleR.Restitution = 1f;
		circleR.Friction = 0f;

		var circleL = body.CreateCircle(radius, 10f, new AetherVector2(-rectSizeX / 2f, 0));
		circleL.Restitution = 1f;
		circleL.Friction = 0f;

		return body;
	}

	public void Remove(Body body)
	{
		World.Remove(body);
	}

	public void Dispose()
	{
		World.Clear();
	}
}

public class PhysicsSystem(World world, PhysicsService physics)
{
	private readonly QueryDescription _kinematicQuery = new QueryDescription().WithAll<KinematicRigidBody, Transform2D>();
	private readonly QueryDescription _dynamicQuery = new QueryDescription().WithAll<DynamicRigidBody, Transform2D>();

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		physics.Init();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		next(dt);

		//physics.DebugRender(dt, BreakoutConstants.PHYSICS_SCALE);
	}

	[FixedUpdate]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		world.Query(in _kinematicQuery, ((ref KinematicRigidBody kineticComponent, ref Transform2D transform, ref Sprite sprite) =>
		{
			var currentPos = kineticComponent.Body.GetTransform().p;
			var targetPos = new AetherVector2(transform.Position.X / BreakoutConstants.PHYSICS_SCALE, transform.Position.Y / BreakoutConstants.PHYSICS_SCALE);

			kineticComponent.Body.LinearVelocity = (targetPos - currentPos) * 100f;
		}));

		physics.Step(dt);

		world.Query(in _dynamicQuery, (ref DynamicRigidBody rigidBody, ref Transform2D transform, ref Sprite sprite) =>
		{
			var position2d = rigidBody.Body.Position;
			transform.Position = new Vector2(position2d.X * BreakoutConstants.PHYSICS_SCALE, position2d.Y * BreakoutConstants.PHYSICS_SCALE);
		});

		next(dt);
	}
}

public class LevelSystem(IWindow window, PhysicsService physics)
{
	[Init]
	public unsafe void Init(GameTime dt, GameLoopDelegate next)
	{
		var wallThickness = BreakoutConstants.BALL_SIZE.X * 2;
		var windowHalfExtent = window.Size / 2f;

		var sideWallSize = new Vector2(wallThickness, window.Height);
		var leftWallPosition = new Vector2((-wallThickness / 2f) + 1, windowHalfExtent.Y);
		var rightWallPosition = new Vector2(window.Width + wallThickness / 2f, windowHalfExtent.Y);

		var topWallSize = new Vector2(window.Width, wallThickness);
		var topWallPosition = new Vector2(windowHalfExtent.X, -wallThickness / 2f);
		var bottomWallPosition = new Vector2(windowHalfExtent.X, window.Height + (wallThickness / 2f) - 1f);

		physics.AddStaticBox(sideWallSize / BreakoutConstants.PHYSICS_SCALE, leftWallPosition / BreakoutConstants.PHYSICS_SCALE);
		physics.AddStaticBox(sideWallSize / BreakoutConstants.PHYSICS_SCALE, rightWallPosition / BreakoutConstants.PHYSICS_SCALE);
		physics.AddStaticBox(topWallSize / BreakoutConstants.PHYSICS_SCALE, topWallPosition / BreakoutConstants.PHYSICS_SCALE);
		physics.AddStaticBox(topWallSize / BreakoutConstants.PHYSICS_SCALE, bottomWallPosition / BreakoutConstants.PHYSICS_SCALE);

		next(dt);
	}
}

public class ScoreSystem(IEventListener events, IAssetManager assets, ISpriteBatch spriteBatch, World world)
{
	private QueryDescription _ballQuery = new QueryDescription().WithAll<Ball>();

	private FontSet _scoreFontSet = default!;
	private Font _scoreFont = default!;
	private int _score = 0;

	private int _ballCount = 0;

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

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		_ballCount = world.CountEntities(in _ballQuery);

		next(dt);
	}

	[Render]
	public void RenderScore(GameTime dt, GameLoopDelegate next)
	{
		spriteBatch.DrawString(_scoreFont, $"Score:  {_score}", new Vector2(20f), Color.Red);
		spriteBatch.DrawString(_scoreFont, $"Balls:  {_ballCount}", new Vector2(20f, 44), Color.Red);
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

public class PaddleSystem(IWindow window, World world, IInputState input, IEventListener events, IAssetManager assets, PhysicsService physics)
{
	private EntityReference _paddle = EntityReference.Null;
	private Body _paddleBody = default!;

	[Init]
	public unsafe void Init(GameTime dt, GameLoopDelegate next)
	{
		var paddleTexture = assets.Load<Texture2D>("49-Breakout-Tiles.png");
		var paddlePosition = new Vector2(window.Width / 2f, window.Height - (BreakoutConstants.BOTTOM_GAP + (BreakoutConstants.PADDLE_SIZE.Y/2)));

		_paddleBody = physics.AddKinematicPaddle(BreakoutConstants.PADDLE_SIZE / BreakoutConstants.PHYSICS_SCALE, paddlePosition / BreakoutConstants.PHYSICS_SCALE);
		
		_paddleBody.Awake = true;
		_paddle = world.Create(new Paddle(true), new Transform2D(paddlePosition), new Sprite(paddleTexture, BreakoutConstants.PADDLE_SIZE), new KinematicRigidBody(_paddleBody)).Reference();

		next(dt);
	}

	[FixedUpdate]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		if (window.IsMouseGrabbed)
		{
			ref var paddleComponent = ref _paddle.Entity.Get<Paddle>();
			ref var paddleTransform = ref _paddle.Entity.Get<Transform2D>();

			paddleTransform.Position = new Vector2(input.MousePosition.X, paddleTransform.Position.Y);

			if (input.Pressed(MouseButton.Left))
			{
				events.Emit(new LaunchBallCommand());
			}
		}

		next(dt);
	}
}

public unsafe class BallSystem(IWindow window, World world, IEventListener events, IAssetManager assets, PhysicsService physics)
{	
	private Texture2D _ballTexture = default!;
	private EntityReference _paddle = EntityReference.Null;

	private QueryDescription _ballQuery = new QueryDescription().WithAll<Ball>();
	private QueryDescription _paddleQuery = new QueryDescription().WithAll<Paddle>();

	private readonly Vector2 _paddleBallOffset = new(0f, -(BreakoutConstants.BALL_SIZE.Y + 20));

	[Init]
	public unsafe void Init(GameTime dt, GameLoopDelegate next)
	{
		_ballTexture = assets.Load<Texture2D>("58-Breakout-Tiles.png");

		next(dt);

		world.Query(in _paddleQuery, (Entity entity) =>
		{
			_paddle = entity.Reference();
		});
	}

	[Update]
	public void PositionUpdate(GameTime dt, GameLoopDelegate next)
	{
		var totalBalls = world.CountEntities(in _ballQuery);

		if (events.OnLatest<LaunchBallCommand>() && totalBalls < 40)
		{
			var paddlePosition = _paddle.Entity.Get<Transform2D>().Position;
			var radius = BreakoutConstants.BALL_SIZE.X / 2f;

			var ballTransform = paddlePosition + _paddleBallOffset;
			var entity = _createBall(ballTransform);

			var ballBody = physics.AddDynamicSphere(radius / BreakoutConstants.PHYSICS_SCALE, ballTransform / BreakoutConstants.PHYSICS_SCALE);

			entity.Add(new DynamicRigidBody(ballBody));
			ballBody.LinearVelocity = new AetherVector2(0, -100f / BreakoutConstants.PHYSICS_SCALE);
			ballBody.Awake = true;
		}

		world.Query(in _ballQuery, (Entity entity) =>
		{
			ref var ballTransform = ref entity.Get<Transform2D>();
			ref var paddle = ref _paddle.Entity.Get<Paddle>();

			//if (paddle.HasBall)
			//{
			//	var paddlePosition = _paddle.Entity.Get<Transform2D>().Position;
			//	ballTransform.Position = paddlePosition + _paddleBallOffset;
			//}

			if (ballTransform.Position.Y > window.Height && entity.Has<DynamicRigidBody>())
			{
				ref var rigidBody = ref entity.Get<DynamicRigidBody>();
				physics.Remove(rigidBody.Body);
				entity.Remove<DynamicRigidBody>();
				paddle.HasBall = true;
				events.Emit(new BallLostEvent());
			}
		});

		

		next(dt);
	}

	private Entity _createBall(Vector2 position)
	{
		return world.Create(new Ball(), new Transform2D(position), new Sprite(_ballTexture, BreakoutConstants.BALL_SIZE));		
	}
}


public unsafe class BlockSystem(IEventListener events, IAssetManager assets, World world, PhysicsService physics, ICoroutineRunner coroutine)
{
	private readonly QueryDescription _blockQuery = new QueryDescription().WithAll<GridLocation, Sprite, Transform2D>();
	private readonly QueryDescription _paddleQuery = new QueryDescription().WithAll<Paddle>();
	private EntityReference _paddle = EntityReference.Null;


	[Init]
	public void SetupBlocks(GameTime dt, GameLoopDelegate next)
	{
		_resetBlocks();

		next(dt);

		world.Query(in _paddleQuery, (Entity entity) => {
			_paddle = entity.Reference();
		});
	}

	[FixedUpdate]
	public void FixedUpdate(GameTime dt, GameLoopDelegate next)
	{
		next(dt);

		if (events.On<BlockHitEvent>(out var e))
		{
			var entity = e.Data.Block.Entity;
			if (entity.IsAlive() && entity.Has<StaticBody>())
			{
				ref var fixture = ref entity.Get<StaticBody>();

				physics.Remove(fixture.Body);
				world.Destroy(entity);
			}
		}
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

		var rand = new Random(6014);

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

				var transform = new Transform2D(new Vector2(colOffset, rowOffset), ((float)rand.NextDouble() - 0.5f) * maxTilt);
				var body = physics.AddStaticBox(BreakoutConstants.BLOCK_SIZE / BreakoutConstants.PHYSICS_SCALE, transform.Position / BreakoutConstants.PHYSICS_SCALE, transform.Rotation);
				
				var blockEntity = world.Create(transform, new GridLocation(row, col), new Sprite(blockTexture, BreakoutConstants.BLOCK_SIZE), new StaticBody(body)).Reference();

				body.OnCollision += (Fixture sender, Fixture other, Contact contact) => {
					events.Emit(new BlockHitEvent(blockEntity));
					return true;
				};
			}
		}
	}
}