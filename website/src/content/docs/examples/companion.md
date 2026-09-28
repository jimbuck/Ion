---
title: Companion
description: A paddle game that serves a phone controller page and turns each phone into a virtual gamepad over a WebSocket, using the web server module's generated routes.
sidebar:
  order: 4
---

**Source:** [`Ion.Examples/Ion.Examples.Companion`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Companion)
and its tests in [`Ion.Examples.Companion.Tests`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Companion.Tests).

A single-screen paddle game you can play with the keyboard, a gamepad or your phone. The game embeds the web server
module: it serves a controller page (plain HTML and JavaScript, no build step), accepts each phone on a WebSocket
endpoint as a virtual gamepad, answers `GET /score`, and pushes the score back to every phone when it changes.

The game code itself knows nothing about the web: it reads gamepads through `IInputState`, and the phones' input
arrives on the same scripted input path a real device would use.

## What it shows

- `AddWeb()`/`UseWeb()` and web endpoints as ordinary methods on a system: `[Http("GET", "/score")]` and
  `[WebSocket("/paddle")]`, turned into a static route table by the routing generator.
- Handlers called on the game thread at the end of a frame (`StageOrder.Web`), so they touch game state freely.
- JSON with System.Text.Json source generation (`[WebJson(typeof(CompanionJson))]`), NativeAOT-safe.
- Per-connection state on `WebSocketClient.State`, closing a connection with a status code, and push channels
  (`IWebServer.Channel(path).BroadcastJson(...)`).
- Static files from `wwwroot` (`Ion:Web:StaticFiles`).
- Injecting input with `ScriptedInput` and `InputEvent.ForGamepadConnection` / `ForGamepadAxis`.
- An allocation-free JSON reader for the hot path (`Utf8JsonReader` over the message bytes).

## Run it

```bash
dotnet run --project Ion.Examples/Ion.Examples.Companion
npm run example:companion         # the same, in Release
```

Then open [http://127.0.0.1:15780/](http://127.0.0.1:15780/) in a browser (a phone emulator in your browser's dev tools
works well) and slide your finger or mouse on the pad. The arrow keys, A and D, and any gamepad's left stick or D-pad
move the paddle too.

The web server is configured in the sample's `appsettings.json`:

```json title="appsettings.json"
{
  "Ion": {
    "Title": "Ion Companion Example",
    "MaxFPS": 60,
    "Window": { "Width": 800, "Height": 600 },
    "Web": { "Enabled": true, "Port": 15780, "StaticFiles": "wwwroot" }
  }
}
```

### Playing from a real phone

By default the server binds to `127.0.0.1` only. To reach it from a phone on the same network, bind to the LAN
explicitly:

```bash
dotnet run --project Ion.Examples/Ion.Examples.Companion -- --Ion:Web:Bind=0.0.0.0 --Ion:Web:AllowNonLoopback=true
```

On a non-loopback bind the server generates a per-run token for mutating endpoints (WebSocket endpoints included) and
prints the LAN address with the token in the URL fragment, for example
`Ion web: on the network at http://192.168.1.20:15780/#token=...`. Open that URL on the phone. The page reads the token
from the fragment (browsers never send the fragment to the server) and offers it as a WebSocket subprotocol
(`bearer.<token>`), since browsers cannot set headers on a WebSocket.

:::caution[LAN exposure]
`AllowNonLoopback` is an explicit, logged opt-in. Anyone on the network can load the page; only holders of the token can
connect as a controller. Do not add `AllowAnonymousMutations` on an untrusted network. See
[Web security](/Ion/networking/web-security/).
:::

## Program.cs

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon(graphics => graphics.ClearColor = new Color(0x10, 0x14, 0x1C, 0xFF)).AddWeb().AddScriptedInput()
	.AddSystem<PaddleSystem>().AddSystem<BallSystem>().AddSystem<DrawSystem>().AddSystem<CompanionEndpoints>();
builder.Services.AddSingleton<PaddleGame>();

using var game = builder.Build();
game.UseIon().UseWeb().UseSystem<PaddleSystem>().UseSystem<BallSystem>().UseSystem<DrawSystem>().UseSystem<CompanionEndpoints>();
game.Run();
```

- `AddWeb()` registers the web server (it registers no other module; the HTTP listener is `Ion.Extensions.Http`).
  The server only listens when `Ion:Web:Enabled` is true.
- `AddScriptedInput()` attaches a thread-safe `ScriptedInput` to the input tracker, which the endpoints use to inject
  gamepad events.
- `PaddleGame` is a plain singleton holding the state that every system shares.

## The game systems

`PaddleSystem` moves the paddle from any input. The phones are just more gamepads:

```csharp title="Program.cs"
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
```

`BallSystem` bounces the ball in `[FixedUpdate]` steps and counts paddle hits as score and falls as misses. `DrawSystem`
draws the paddle and ball with `DrawRect` and the status line with `DrawString`, rebuilding the text only when a number
changed.

## The endpoints

```csharp title="Program.cs"
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ScoreInfo))]
public sealed partial class CompanionJson : JsonSerializerContext;

public readonly record struct ScoreInfo(int Score, int Misses, float Paddle, int Controllers);

[WebJson(typeof(CompanionJson))]
public sealed class CompanionEndpoints(PaddleGame game, ScriptedInput script, IServiceProvider services)
{
	public const int FirstPad = 1, LastPad = 3;

	[Http("GET", "/score")]
	public ScoreInfo GetScore() => new(game.Score, game.Misses, game.PaddleX, game.Controllers);

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
}
```

- The class is a system like any other: registered with `AddSystem`, added with `UseSystem`. The routing generator
  finds its `[Http]` and `[WebSocket]` methods at compile time; there is no reflection.
- `[WebJson(typeof(CompanionJson))]` names the JSON context for every endpoint on the class. Returning `ScoreInfo`
  without one is error `ION405`; a context missing the type is warning `ION406`.
- One WebSocket method receives every event of a connection: `Connected`, `Text`, `Binary` and `Disconnected`.
- Phones take gamepad slots 1 to 3; slot 0 is left for a real controller. A fourth phone is closed with status 1013
  ("try again later"), and the page retries every second.
- Input arrives through `ScriptedInput` and is applied at the start of the next frame, exactly as a device's would.

### Pushing the score

```csharp title="Program.cs"
[Update(Order = 10)]
public void Push(GameTime dt)
{
	if (game.Score == _pushedScore && game.Misses == _pushedMisses) return;
	(_pushedScore, _pushedMisses) = (game.Score, game.Misses);
	_server ??= services.GetService(typeof(IWebServer)) as IWebServer;
	if (_server is { IsRunning: true } server) server.Channel("/paddle").BroadcastJson(GetScore(), CompanionJson.Default.ScoreInfo);
}
```

`IWebServer.Channel(path)` addresses every client connected to a WebSocket endpoint. The system resolves the server
lazily because it runs in tests where the server may not be listening.

### Reading the stick without allocating

```csharp title="Program.cs"
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
```

The page sends about 30 `{"x": -1..1}` messages a second, only when the value changes. Anything else is ignored.

## The controller page

`wwwroot/index.html`, `controller.css` and `controller.js` are served as static files. The script opens
`ws://<host>/paddle` with the `ion` subprotocol (plus `bearer.<token>` when the URL has `#token=...`), sends the stick
position while a finger is on the pad, shows the score it receives, and reconnects after a second when the connection
closes.

## The tests

[`CompanionTests`](https://github.com/jimbuck/Ion/blob/main/Ion.Examples/Ion.Examples.Companion.Tests/CompanionTests.cs)
start the real program headless on a free port and step it on a background thread, then act like a phone:

```csharp title="CompanionTests.cs"
_host = new IonTestHost()
	.WithConfiguration(new Dictionary<string, string?> { ["Ion:Web:Port"] = "0", ["Ion:Web:PrintUrl"] = "false" })
	.UseEntryPoint<Program>()
	.Start();
Server = _host.Get<IWebServer>();
```

| Test | Checks |
|---|---|
| `APhoneOnTheWebSocketMovesThePaddleAndScoreAnswers` | Connecting greets the phone with the score (`controllers: 1`); `{"x":1}` and `{"x":-1}` move the paddle; `{"x":0}` stops it; `GET /score` matches the game; closing the socket disconnects the pad. |
| `TheControllerPageIsServed` | `/` is HTML that loads `controller.js`, which talks to `/paddle` with a bearer subprotocol. |
| `AtMostThreePhonesPlay` | The fourth connection is closed with 1013 and three controllers remain. |
| `TheStickParserReadsOnlyX` | The parser takes `x` among other keys, clamps it, and rejects strings and invalid JSON. |

## Ideas to extend it

**A second endpoint.** Let a phone reset the score with a `POST`. Mutating endpoints need the token when the server has
one:

```csharp
[Http("POST", "/reset")]
public void Reset()
{
	game.Score = 0;
	game.Misses = 0;
}
```

**Two paddles.** Give each phone its own paddle: keep a paddle x per gamepad slot in `PaddleGame` and read
`input.Gamepad(index)` for each connected slot instead of summing them.

**Serve the page from a folder you edit live.** Point `Ion:Web:StaticFiles` at your source `wwwroot` (an absolute path; relative paths resolve against the executable's folder) while you work on
the page, so a browser refresh picks up changes without rebuilding.

## See also

- [Web server overview](/Ion/networking/overview/), [Web endpoints](/Ion/networking/web-endpoints/),
  [WebSockets](/Ion/networking/websockets/) and [HTTP server](/Ion/networking/http-server/).
- [Input overview](/Ion/interaction/input/overview/) and [Gamepad](/Ion/interaction/input/gamepad/).
- [Configuration reference](/Ion/reference/configuration/#web-server-ionweb): every `Ion:Web` key.
