using Arch.Core;

using Microsoft.Extensions.Logging;

using Ion.Extensions.Assets;
using Ion.Extensions.Audio;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Extensions.Networking;
using Ion.Extensions.Physics2D;
using Ion.Extensions.Scenes;

using World = Arch.Core.World;

namespace Ion.Examples.Breakout.Net;

/// <summary>The local player's wanted paddle position, written by the input (or the autopilot) and sampled by the prediction step every tick.</summary>
public sealed class PaddleInputSource
{
	/// <summary>Where the local paddle should go (field x).</summary>
	public float TargetX { get; set; } = Field.Width / 2f;
}

/// <summary>
/// Registers the paddle's prediction step on every peer: a client samples its input, sends it and moves its own paddle at
/// once; the server applies every player's input to that player's paddle; a listen server also samples its own. The
/// step runs in every live networked world, so it reaches the paddles of the level while it is loaded.
/// </summary>
public sealed class PaddlePredictionSystem(INetworkPrediction prediction, INetworkSession session, PaddleInputSource input)
{
	[Init]
	public void Register(GameTime dt)
	{
		var samples = session.Mode is NetworkMode.Client or NetworkMode.ListenServer;
		prediction.Register<PaddleControl, PaddleInput>(Field.MovePaddle, samples ? Sample : null);
	}

	private PaddleInput Sample() => new(input.TargetX);
}

/// <summary>
/// The flow of the game, in the root world: menu, level, menu, level... The server owns the replicated
/// <see cref="Stage"/> and <see cref="Scoreboard"/> and moves the stage on (the menu counts down once someone plays; a
/// level ends when its blocks are gone, or when its time is up if levels are timed). Every peer, the server included,
/// follows the stage with its own scene change; the server never tracks the clients' scenes.
/// </summary>
public sealed class FlowSystem(World world, INetworkSession session, IEvents events, SceneSystem scenes, BreakoutSettings settings, ILogger<FlowSystem> logger)
{
	private static readonly QueryDescription Stages = new QueryDescription().WithAll<Stage>();
	private static readonly QueryDescription Scoreboards = new QueryDescription().WithAll<Scoreboard>();

	private Entity _stage = Entity.Null;
	private Entity _scoreboard = Entity.Null;
	private bool _cleared;

	/// <summary>The stage this peer knows (on a client, the replicated one; the menu before the first snapshot).</summary>
	public Stage Stage => Find(Stages, out Stage stage) ? stage : new Stage(GameScene.Menu, 0);

	/// <summary>The score this peer knows.</summary>
	public Scoreboard Score => Find(Scoreboards, out Scoreboard score) ? score : default;

	[Init]
	public void Init(GameTime dt)
	{
		if (!session.IsServer) return;
		_stage = world.Create(new Stage(GameScene.Menu, settings.MenuSeconds));
		_scoreboard = world.Create(new Scoreboard(0, 0, 1));
	}

	/// <summary>The level's game reports a broken block.</summary>
	public void AddScore(int points) => world.Get<Scoreboard>(_scoreboard).Score += points;

	/// <summary>The level's game reports lost balls.</summary>
	public void LoseBalls(int count) => world.Get<Scoreboard>(_scoreboard).BallsLost += count;

	/// <summary>The level's game reports that every block is broken: back to the menu.</summary>
	public void LevelCleared() => _cleared = true;

	/// <summary>Server: moves the stage on.</summary>
	[FixedUpdate]
	public void Advance(GameTime dt)
	{
		if (!session.IsServer || _stage == Entity.Null) return;
		ref var stage = ref world.Get<Stage>(_stage);
		ref var score = ref world.Get<Scoreboard>(_scoreboard);
		switch (stage.Scene)
		{
			case GameScene.Menu:
				// Someone has to play: a listen server's own player, or a client.
				if (session.Mode != NetworkMode.ListenServer && session.Peers.Count == 0) break;
				stage.SecondsLeft -= dt.Delta;
				if (stage.SecondsLeft > 0) break;
				stage = new Stage(GameScene.Level, settings.LevelSeconds);
				logger.LogInformation("Level {Round}.", score.Round);
				break;

			case GameScene.Level:
				if (settings.LevelSeconds > 0) stage.SecondsLeft -= dt.Delta;
				if (!_cleared && !(settings.LevelSeconds > 0 && stage.SecondsLeft <= 0)) break;
				logger.LogInformation("Level {Round} over: score {Score}.", score.Round, score.Score);
				_cleared = false;
				score.Round++;
				stage = new Stage(GameScene.Menu, settings.MenuSeconds);
				break;
		}
	}

	/// <summary>Every peer: changes to the stage's scene when it differs from the loaded one (before the scene step runs).</summary>
	[First(Order = StageOrder.Scenes - 10)]
	public void Follow(GameTime dt)
	{
		var scene = (int)Stage.Scene;
		if (scenes.IsLoading || (scenes.HasScene && scenes.CurrentSceneId == scene)) return;
		events.Emit(new ChangeSceneEvent(scene));
	}

	private bool Find<T>(QueryDescription query, out T value) where T : unmanaged
	{
		var found = default(T);
		var any = false;
		world.Query(in query, (ref T component) =>
		{
			found = component;
			any = true;
		});
		value = found;
		return any;
	}
}

/// <summary>
/// The authoritative level, in the level scene on the server only (dedicated or listen): walls, blocks, a paddle per
/// player, launches, the physics collisions and lost balls. Its world is the scene's (networked as "level"): everything
/// it creates with replicated components is networked automatically and goes to the clients that are in their level
/// scene; walls are <see cref="NetworkLocal"/>. The score and the flow are the root world's (<see cref="FlowSystem"/>).
/// </summary>
public sealed class ServerGameSystem(World world, INetworkSession session, INetworkWorld network, INetworkMessages messages, IEvents events, FlowSystem flow, ILogger<ServerGameSystem> logger)
{
	private static readonly QueryDescription Paddles = new QueryDescription().WithAll<Paddle, PaddleControl, Transform2D>();
	private static readonly QueryDescription Balls = new QueryDescription().WithAll<Ball, Transform2D>();
	private static readonly QueryDescription Blocks = new QueryDescription().WithAll<Block>();

	private EventReader<PeerConnected> _joined = events.Reader<PeerConnected>();
	private EventReader<PeerDisconnected> _left = events.Reader<PeerDisconnected>();
	private EventReader<Collision2D> _collisions = events.Reader<Collision2D>();
	private NetworkReader<LaunchBall> _launches = messages.Reader<LaunchBall>();
	private readonly List<Entity> _scratch = [];
	private readonly Random _random = new(2026);
	private bool _over;

	[Init]
	public void Init(GameTime dt)
	{
		if (!session.IsServer) return;
		CreateWalls();
		CreateBlocks();
		if (session.Mode == NetworkMode.ListenServer) CreatePaddle(NetworkPeer.Server);
		foreach (var peer in session.Peers) CreatePaddle(peer);
		logger.LogInformation("Level ready: {Blocks} blocks, {Players} players, field {Width}x{Height}.", Field.Columns * Field.Rows, session.Peers.Count + (session.Mode == NetworkMode.ListenServer ? 1 : 0), Field.Width, Field.Height);
	}

	/// <summary>A paddle for every player that joins during the level; a player's paddle leaves with it.</summary>
	[First(Order = 10)]
	public void Players(GameTime dt)
	{
		if (!session.IsServer)
		{
			_joined.Skip();
			_left.Skip();
			return;
		}

		foreach (ref readonly var joined in _joined.Read())
		{
			if (joined.Peer.IsClient) CreatePaddle(joined.Peer);
		}

		foreach (ref readonly var left in _left.Read())
		{
			_scratch.Clear();
			foreach (ref var chunk in world.Query(in Paddles))
			{
				var paddles = chunk.GetSpan<Paddle>();
				for (var i = 0; i < chunk.Count; i++)
				{
					if (paddles[i].Player == left.Peer.Id) _scratch.Add(chunk.Entity(i));
				}
			}

			foreach (var entity in _scratch) world.Destroy(entity);
		}
	}

	/// <summary>The paddles' predicted x drives their kinematic bodies (after the prediction step, before the physics step).</summary>
	[FixedUpdate(Order = StageOrder.Physics - 10)]
	public void DrivePaddles(GameTime dt)
	{
		if (!session.IsServer) return;
		foreach (ref var chunk in world.Query(in Paddles))
		{
			var controls = chunk.GetSpan<PaddleControl>();
			var transforms = chunk.GetSpan<Transform2D>();
			for (var i = 0; i < chunk.Count; i++) transforms[i].Position = new Vector2(controls[i].X, Field.PaddleY);
		}
	}

	/// <summary>Collisions of the physics step: a block a ball touched breaks.</summary>
	[FixedUpdate(Order = -10)]
	public void Collisions(GameTime dt)
	{
		if (!session.IsServer)
		{
			_collisions.Skip();
			return;
		}

		foreach (ref readonly var collision in _collisions.Read())
		{
			if (collision.Phase != ContactPhase.Begin || !world.IsAlive(collision.A) || !world.IsAlive(collision.B)) continue;
			var block = world.Has<Block>(collision.A) ? collision.A : world.Has<Block>(collision.B) ? collision.B : Entity.Null;
			if (block == Entity.Null) continue;
			messages.Broadcast(new BlockBroken(world.Get<Transform2D>(block).Position));
			world.Destroy(block);
			flow.AddScore(10);
		}
	}

	/// <summary>Launches and lost balls; the level is over when its blocks are gone.</summary>
	[FixedUpdate]
	public void Play(GameTime dt)
	{
		if (!session.IsServer || _over)
		{
			_launches.Skip();
			return;
		}

		while (_launches.TryRead(out var from, out _))
		{
			if (world.CountEntities(in Balls) >= Field.MaxBalls) continue;
			if (FindPaddle(from, out var x)) CreateBall(new Vector2(x, Field.PaddleY - Field.PaddleSize.Y), from);
		}

		_scratch.Clear();
		foreach (ref var chunk in world.Query(in Balls))
		{
			var transforms = chunk.GetSpan<Transform2D>();
			for (var i = 0; i < chunk.Count; i++)
			{
				if (transforms[i].Position.Y > Field.Height + Field.BallSize.Y) _scratch.Add(chunk.Entity(i));
			}
		}

		foreach (var ball in _scratch) world.Destroy(ball);
		if (_scratch.Count > 0) flow.LoseBalls(_scratch.Count);

		if (world.CountEntities(in Blocks) == 0)
		{
			_over = true;
			flow.LevelCleared();
		}
	}

	private bool FindPaddle(NetworkPeer player, out float x)
	{
		foreach (ref var chunk in world.Query(in Paddles))
		{
			var paddles = chunk.GetSpan<Paddle>();
			var controls = chunk.GetSpan<PaddleControl>();
			for (var i = 0; i < chunk.Count; i++)
			{
				if (paddles[i].Player != player.Id) continue;
				x = controls[i].X;
				return true;
			}
		}

		x = 0;
		return false;
	}

	private void CreatePaddle(NetworkPeer player)
	{
		// The id is allocated by the level's network world for the player, so the player owns the paddle (and predicts its
		// PaddleControl).
		var x = Field.Width / 2f;
		world.Create(network.Allocate(player), new Paddle(player.Id), new PaddleControl(x), new Transform2D(new Vector2(x, Field.PaddleY)),
			Collider2D.Capsule(Field.PaddleSize) with { Restitution = 1f, Friction = 0f }, RigidBody2D.Kinematic());
		logger.LogInformation("Paddle for {Player}.", player);
	}

	private void CreateBall(Vector2 position, NetworkPeer owner)
	{
		var angle = (_random.NextSingle() - 0.5f) * 0.8f;
		var velocity = new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * Field.BallSpeed;
		world.Create(new Ball(owner.Id), new Transform2D(position),
			Collider2D.Circle(Field.BallSize.X / 2f) with { Restitution = 1f, Friction = 0f },
			RigidBody2D.Dynamic(velocity) with { IsBullet = true, FixedRotation = true });
	}

	private void CreateBlocks()
	{
		var half = Field.BlockSize / 2f;
		for (var row = 0; row < Field.Rows; row++)
		{
			for (var column = 0; column < Field.Columns; column++)
			{
				var position = new Vector2(
					half.X + Field.Gap + column * (Field.BlockSize.X + Field.Gap),
					half.Y + Field.Gap + row * (Field.BlockSize.Y + Field.Gap));
				world.Create(new Block(row, column), new Transform2D(position), Collider2D.Box(Field.BlockSize) with { Restitution = 1f, Friction = 0f });
			}
		}
	}

	private void CreateWalls()
	{
		var thickness = Field.BallSize.X * 2;
		var width = Field.Width;
		var height = Field.Height;
		Wall(new Vector2(-thickness / 2f, height / 2f), new Vector2(thickness, height * 2));
		Wall(new Vector2(width + thickness / 2f, height / 2f), new Vector2(thickness, height * 2));
		Wall(new Vector2(width / 2f, -thickness / 2f), new Vector2(width + thickness * 2, thickness));

		void Wall(Vector2 position, Vector2 size) =>
			world.Create(new Wall(), new NetworkLocal(), new Transform2D(position), Collider2D.Box(size) with { Restitution = 1f, Friction = 0f });
	}
}

/// <summary>
/// What a player sees of the level (clients and a listen server; nothing on a dedicated server), in the level scene:
/// sprites for the replicated entities, its own paddle drawn at the predicted position, and a sound when a block breaks.
/// </summary>
public sealed class PresentationSystem(World world, INetworkSession session, INetworkWorld network, INetworkMessages messages, IAssetManager assets, IAudioManager audio)
{
	private static readonly QueryDescription BallsWithoutSprite = new QueryDescription().WithAll<Ball, Transform2D>().WithNone<Sprite>();
	private static readonly QueryDescription BlocksWithoutSprite = new QueryDescription().WithAll<Block, Transform2D>().WithNone<Sprite>();
	private static readonly QueryDescription PaddlesWithoutSprite = new QueryDescription().WithAll<Paddle, Transform2D>().WithNone<Sprite>();
	private static readonly QueryDescription OwnPaddles = new QueryDescription().WithAll<NetworkId, PaddleControl, Transform2D>();

	private NetworkReader<BlockBroken> _broken = messages.Reader<BlockBroken>();
	private ITexture2D _ball = default!, _block = default!, _paddle = default!;
	private ISoundEffect _ping = default!;

	private bool Shows => session.Mode is NetworkMode.Client or NetworkMode.ListenServer;

	[Init]
	public void Init(GameTime dt)
	{
		if (!Shows) return;
		_ball = assets.Load<ITexture2D>("58-Breakout-Tiles.png");
		_block = assets.Load<ITexture2D>("15-Breakout-Tiles.png");
		_paddle = assets.Load<ITexture2D>("49-Breakout-Tiles.png");
		_ping = assets.Load<ISoundEffect>("ping.wav");
	}

	/// <summary>Replicated entities arrive without sprites (textures are local): they get one by what they are.</summary>
	[Update]
	public void AddSprites(GameTime dt)
	{
		if (!Shows)
		{
			_broken.Skip();
			return;
		}

		if (world.CountEntities(in BallsWithoutSprite) > 0) world.Add(in BallsWithoutSprite, new Sprite(_ball, Field.BallSize, depth: 1));
		if (world.CountEntities(in BlocksWithoutSprite) > 0) world.Add(in BlocksWithoutSprite, new Sprite(_block, Field.BlockSize));
		if (world.CountEntities(in PaddlesWithoutSprite) > 0) world.Add(in PaddlesWithoutSprite, new Sprite(_paddle, Field.PaddleSize));

		if (_broken.Read().Length > 0) audio.Play(_ping);
	}

	/// <summary>The local paddle is drawn where the prediction put it (remote ones where the interpolation did), before the transform propagation.</summary>
	[Render(Order = StageOrder.TransformPropagation - 10)]
	public void PlaceOwnPaddle(GameTime dt)
	{
		if (!Shows) return;
		foreach (ref var chunk in world.Query(in OwnPaddles))
		{
			var ids = chunk.GetSpan<NetworkId>();
			var controls = chunk.GetSpan<PaddleControl>();
			var transforms = chunk.GetSpan<Transform2D>();
			for (var i = 0; i < chunk.Count; i++)
			{
				if (network.IsOwned(ids[i])) transforms[i].Position = new Vector2(controls[i].X, Field.PaddleY);
			}
		}
	}
}

/// <summary>
/// The heads-up display, in the root schedule so it shows in every scene (clients and a listen server): the score, and in
/// the menu the next level's countdown. Sizes the window to the field.
/// </summary>
public sealed class HudSystem(INetworkSession session, FlowSystem flow, IAssetManager assets, ISpriteBatch spriteBatch, IWindow window)
{
	private IFont _font = default!, _title = default!;
	private Scoreboard _shown = new(-1, -1, -1);
	private string _text = "";

	private bool Shows => session.Mode is NetworkMode.Client or NetworkMode.ListenServer;

	[Init]
	public void Init(GameTime dt)
	{
		if (!Shows) return;
		window.Size = new Vector2(Field.Width, Field.Height);
		window.IsResizable = false;
		var fonts = assets.Load<IFontSet>("Bungee-Regular.ttf");
		_font = fonts.CreateStyle(24);
		_title = fonts.CreateStyle(64);
	}

	[Render]
	public void Draw(GameTime dt)
	{
		if (!Shows) return;
		var score = flow.Score;
		if (score != _shown)
		{
			_shown = score;
			_text = $"Score {score.Score}   Level {score.Round}   Lost {score.BallsLost}";
		}

		spriteBatch.DrawString(_font, _text, new Vector2(20f, Field.Height - 60f), Color.White);

		var stage = flow.Stage;
		if (stage.Scene != GameScene.Menu) return;
		spriteBatch.DrawString(_title, "Breakout Net", new Vector2(Field.Width / 2f - 260f, Field.Height / 2f - 120f), Color.White);
		var line = session.State != NetworkState.Connected ? "Connecting..." : $"Level {score.Round} in {Math.Max(0, Math.Ceiling(stage.SecondsLeft)):0}";
		spriteBatch.DrawString(_font, line, new Vector2(Field.Width / 2f - 120f, Field.Height / 2f), Color.White);
	}
}

/// <summary>The local player's input (windowed): the mouse moves the paddle, a click launches a ball.</summary>
public sealed class PlayerInputSystem(IWindow window, IInputState input, INetworkSession session, INetworkMessages messages, FlowSystem flow, PaddleInputSource source)
{
	[Update]
	public void Read(GameTime dt)
	{
		if (session.Mode is not (NetworkMode.Client or NetworkMode.ListenServer) || session.State != NetworkState.Connected) return;
		if (input.Pressed(Key.Escape))
		{
			window.IsMouseGrabbed = false;
			window.IsCursorVisible = true;
		}

		if (input.Pressed(MouseButton.Left))
		{
			if (!window.IsMouseGrabbed)
			{
				window.IsMouseGrabbed = true;
				window.IsCursorVisible = false;
			}
			else if (flow.Stage.Scene == GameScene.Level)
			{
				messages.SendToServer(new LaunchBall());
			}
		}

		if (window.IsMouseGrabbed) source.TargetX = input.MousePosition.X;
	}
}

/// <summary>
/// Plays when running headless (clients and a listen server): in the level, follows the lowest ball with the paddle and
/// launches a ball every second, so a headless client exercises inputs, prediction, messages and replication. In the
/// root schedule (it reads the level's world through <see cref="INetworkWorlds"/>), so its counters last across levels.
/// </summary>
public sealed class NetAutopilotSystem(INetworkWorlds worlds, INetworkSession session, INetworkMessages messages, PaddleInputSource source)
{
	private static readonly QueryDescription Balls = new QueryDescription().WithAll<Ball, Transform2D>();

	/// <summary>Frames between launches.</summary>
	public int LaunchInterval { get; set; } = 60;

	/// <summary>The most launches.</summary>
	public int MaxLaunches { get; set; } = 20;

	/// <summary>Whether it plays (a test can take over the input).</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Launches requested so far.</summary>
	public int Launches { get; private set; }

	private long _frames;

	[Update(Order = 5)]
	public void Play(GameTime dt)
	{
		if (!Enabled || session.Mode is not (NetworkMode.Client or NetworkMode.ListenServer) || session.State != NetworkState.Connected) return;
		if (!worlds.TryGet("level", out var level)) return;
		_frames++;
		if (_frames % LaunchInterval == 0 && Launches < MaxLaunches)
		{
			messages.SendToServer(new LaunchBall());
			Launches++;
		}

		var lowest = float.MinValue;
		var target = Field.Width / 2f;
		foreach (ref var chunk in level.World.Query(in Balls))
		{
			var transforms = chunk.GetSpan<Transform2D>();
			for (var i = 0; i < chunk.Count; i++)
			{
				if (transforms[i].Position.Y <= lowest) continue;
				lowest = transforms[i].Position.Y;
				target = transforms[i].Position.X;
			}
		}

		source.TargetX = target;
	}
}
