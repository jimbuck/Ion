using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Web;

// Run with --Ion:Headless=true for the headless backends. The web server (Ion:Web in appsettings.json) serves the
// controller page from wwwroot on http://127.0.0.1:15780/.
var builder = IonApplication.CreateBuilder(args);
CompanionApp.Configure(builder);

using var game = builder.Build();
CompanionApp.Use(game);

game.Run();

/// <summary>The game setup, shared with the tests (Ion.Examples.Companion.Tests).</summary>
public static class CompanionApp
{
	/// <summary>Registers the engine (<c>AddIon</c>), the web module, scripted input and the game.</summary>
	public static IonApplicationBuilder Configure(IonApplicationBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.Services.AddIon(builder.Configuration, graphics => graphics.ClearColor = new Color(0x10, 0x14, 0x1C, 0xFF));
		// The web server; its routes are the [Http] and [WebSocket] methods of CompanionEndpoints (generated table).
		builder.Services.AddWeb(builder.Configuration);
		// The phones' input arrives as a virtual gamepad on the scripted input path.
		builder.Services.AddScriptedInput();
		builder.Services.AddSingleton<PaddleGame>()
			.AddSingleton<PaddleSystem>()
			.AddSingleton<BallSystem>()
			.AddSingleton<DrawSystem>()
			.AddSingleton<CompanionEndpoints>();
		return builder;
	}

	/// <summary>Adds the engine's systems (<c>UseIon</c>), the web system and the game.</summary>
	public static IIonApplication Use(IIonApplication game)
	{
		ArgumentNullException.ThrowIfNull(game);
		return game.UseIon()
			.UseWeb()
			.UseSystem<PaddleSystem>()
			.UseSystem<BallSystem>()
			.UseSystem<DrawSystem>()
			.UseSystem<CompanionEndpoints>();
	}
}

/// <summary>The game state: a paddle at the bottom, a ball, the score (paddle hits) and the misses.</summary>
public sealed class PaddleGame
{
	/// <summary>The paddle's size in pixels.</summary>
	public static readonly Vector2 PaddleSize = new(140, 18);

	/// <summary>The ball's size in pixels.</summary>
	public const float BallSize = 16;

	/// <summary>The paddle's speed at full deflection, in pixels per second.</summary>
	public const float PaddleSpeed = 600;

	/// <summary>The paddle's center x.</summary>
	public float PaddleX = 400;

	/// <summary>The ball's top-left corner.</summary>
	public Vector2 Ball = new(392, 200);

	/// <summary>The ball's velocity in pixels per second.</summary>
	public Vector2 BallVelocity = new(180, 240);

	/// <summary>Paddle hits.</summary>
	public int Score;

	/// <summary>Balls that fell past the paddle.</summary>
	public int Misses;

	/// <summary>The number of connected phones.</summary>
	public int Controllers;
}

/// <summary>
/// Moves the paddle from input: arrows or A/D, and the left stick or D-pad of any gamepad, which includes the phones'
/// virtual gamepads. The game does not know about the web module.
/// </summary>
public sealed class PaddleSystem(PaddleGame game, IInputState input, IWindow window)
{
	[Update]
	public void Move(GameTime dt)
	{
		var direction = 0f;
		if (input.Down(Key.Left) || input.Down(Key.A)) direction -= 1;
		if (input.Down(Key.Right) || input.Down(Key.D)) direction += 1;
		var pads = input.Gamepads;
		for (var i = 0; i < pads.Count; i++)
		{
			var pad = pads[i];
			if (!pad.IsConnected) continue;
			direction += pad.LeftStick.X;
			if (pad.Down(GamepadButton.DPadLeft)) direction -= 1;
			if (pad.Down(GamepadButton.DPadRight)) direction += 1;
		}

		direction = Math.Clamp(direction, -1, 1);
		var half = PaddleGame.PaddleSize.X / 2;
		game.PaddleX = Math.Clamp(game.PaddleX + direction * PaddleGame.PaddleSpeed * dt.Delta, half, window.Width - half);
	}
}

/// <summary>Bounces the ball off the walls and the paddle in fixed steps; a paddle hit scores, a fall past it is a miss.</summary>
public sealed class BallSystem(PaddleGame game, IWindow window)
{
	[FixedUpdate]
	public void Step(GameTime dt)
	{
		var ball = game.Ball + game.BallVelocity * dt.Delta;
		var velocity = game.BallVelocity;
		if (ball.X < 0 || ball.X + PaddleGame.BallSize > window.Width)
		{
			velocity.X = -velocity.X;
			ball.X = Math.Clamp(ball.X, 0, window.Width - PaddleGame.BallSize);
		}

		if (ball.Y < 0)
		{
			velocity.Y = -velocity.Y;
			ball.Y = 0;
		}

		var paddleTop = window.Height - 40;
		var half = PaddleGame.PaddleSize.X / 2;
		if (velocity.Y > 0 && ball.Y + PaddleGame.BallSize >= paddleTop && ball.Y + PaddleGame.BallSize <= paddleTop + PaddleGame.PaddleSize.Y
			&& ball.X + PaddleGame.BallSize >= game.PaddleX - half && ball.X <= game.PaddleX + half)
		{
			velocity.Y = -velocity.Y;
			// Where it hit the paddle steers it.
			velocity.X += (ball.X + PaddleGame.BallSize / 2 - game.PaddleX) * 3;
			ball.Y = paddleTop - PaddleGame.BallSize;
			game.Score++;
		}
		else if (ball.Y > window.Height)
		{
			game.Misses++;
			ball = new Vector2(window.Width / 2f, 120);
			velocity = new Vector2(velocity.X >= 0 ? 180 : -180, 240);
		}

		game.Ball = ball;
		game.BallVelocity = velocity;
	}
}

/// <summary>Draws the paddle, the ball and the score.</summary>
public sealed class DrawSystem(PaddleGame game, ISpriteBatch spriteBatch, IAssetManager assets, IWindow window)
{
	private IFont _font = default!;
	private int _shownScore = -1, _shownMisses = -1, _shownControllers = -1;
	private string _text = "";

	[Init]
	public void Load(GameTime dt) => _font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(22);

	[Render]
	public void Draw(GameTime dt)
	{
		var paddleTop = window.Height - 40;
		spriteBatch.DrawRect(new Color(0x4C, 0xC9, 0xF0, 0xFF), new Vector2(game.PaddleX - PaddleGame.PaddleSize.X / 2, paddleTop), PaddleGame.PaddleSize);
		spriteBatch.DrawRect(Color.White, game.Ball, new Vector2(PaddleGame.BallSize));
		if (game.Score != _shownScore || game.Misses != _shownMisses || game.Controllers != _shownControllers)
		{
			(_shownScore, _shownMisses, _shownControllers) = (game.Score, game.Misses, game.Controllers);
			_text = FormattableString.Invariant($"Score {game.Score}   Misses {game.Misses}   Phones {game.Controllers}");
		}

		spriteBatch.DrawString(_font, _text, new Vector2(16), Color.White);
	}
}

/// <summary>What <c>GET /score</c> returns and what the phones are pushed.</summary>
/// <param name="Score">Paddle hits.</param>
/// <param name="Misses">Balls missed.</param>
/// <param name="Paddle">The paddle's center x in pixels.</param>
/// <param name="Controllers">Connected phones.</param>
public readonly record struct ScoreInfo(int Score, int Misses, float Paddle, int Controllers);

/// <summary>The JSON of the endpoints (System.Text.Json source generation, NativeAOT-safe).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ScoreInfo))]
public sealed partial class CompanionJson : JsonSerializerContext;

/// <summary>
/// The web endpoints: ordinary methods on a system, routed by the generated table and called on the game thread at the
/// end of a frame. A phone on <c>/paddle</c> becomes a virtual gamepad (indexes 1 to 3; 0 is left to a real one): its
/// messages <c>{"x": -1..1}</c> set the left stick through Input v2's scripted path, exactly as a device would.
/// </summary>
[WebJson(typeof(CompanionJson))]
public sealed class CompanionEndpoints(PaddleGame game, ScriptedInput script, IServiceProvider services)
{
	/// <summary>The virtual gamepad indexes phones get.</summary>
	public const int FirstPad = 1, LastPad = 3;

	private readonly bool[] _padInUse = new bool[LastPad + 1];
	private IWebServer? _server;
	private int _pushedScore = -1, _pushedMisses = -1;

	/// <summary>The score, the misses, the paddle and the phones.</summary>
	[Http("GET", "/score")]
	public ScoreInfo GetScore() => new(game.Score, game.Misses, game.PaddleX, game.Controllers);

	/// <summary>A phone controller: connects as a virtual gamepad, moves the stick, disconnects.</summary>
	[WebSocket("/paddle")]
	public void Paddle(in WebSocketMessage message)
	{
		switch (message.Kind)
		{
			case WebSocketMessageKind.Connected:
				var pad = FreePad();
				if (pad < 0)
				{
					message.Client.Close(1013); // try again later: every virtual gamepad is taken
					return;
				}

				_padInUse[pad] = true;
				message.Client.State = pad;
				game.Controllers++;
				script.Enqueue(InputEvent.ForGamepadConnection(pad, true));
				message.Client.SendJson(GetScore(), CompanionJson.Default.ScoreInfo);
				break;
			case WebSocketMessageKind.Text when message.Client.State is int index:
				if (TryReadStick(message.Data, out var x)) script.Enqueue(InputEvent.ForGamepadAxis(index, GamepadAxis.LeftX, x));
				break;
			case WebSocketMessageKind.Disconnected when message.Client.State is int index:
				_padInUse[index] = false;
				game.Controllers--;
				script.Enqueue(InputEvent.ForGamepadAxis(index, GamepadAxis.LeftX, 0));
				script.Enqueue(InputEvent.ForGamepadConnection(index, false));
				break;
		}
	}

	/// <summary>Pushes the score to the phones when it changes.</summary>
	[Update(Order = 10)]
	public void Push(GameTime dt)
	{
		if (game.Score == _pushedScore && game.Misses == _pushedMisses) return;
		(_pushedScore, _pushedMisses) = (game.Score, game.Misses);
		_server ??= services.GetService(typeof(IWebServer)) as IWebServer;
		if (_server is { IsRunning: true } server) server.Channel("/paddle").BroadcastJson(GetScore(), CompanionJson.Default.ScoreInfo);
	}

	private int FreePad()
	{
		for (var i = FirstPad; i <= LastPad; i++)
		{
			if (!_padInUse[i]) return i;
		}

		return -1;
	}

	/// <summary>Reads <c>x</c> from <c>{"x": 0.5}</c> without allocating; false for anything else.</summary>
	public static bool TryReadStick(ReadOnlySpan<byte> json, out float x)
	{
		x = 0;
		try
		{
			var reader = new Utf8JsonReader(json);
			if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
			while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
			{
				var isX = reader.ValueTextEquals("x"u8);
				if (!reader.Read()) return false;
				if (isX && reader.TokenType == JsonTokenType.Number && reader.TryGetSingle(out var value) && float.IsFinite(value))
				{
					x = Math.Clamp(value, -1, 1);
					return true;
				}

				reader.Skip();
			}
		}
		catch (JsonException)
		{
		}

		return false;
	}
}
