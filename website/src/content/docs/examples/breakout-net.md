---
title: Breakout Net
description: Multiplayer Breakout with a server-authoritative simulation, replicated and interpolated components, a predicted paddle, network messages and a dedicated server mode.
sidebar:
  order: 3
---

**Source:** [`Ion.Examples/Ion.Examples.Breakout.Net`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Breakout.Net)
and its tests in [`Ion.Examples.Breakout.Net.Tests`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Breakout.Net.Tests).

Breakout for several players over the network. The server simulates balls, blocks and paddles with the 2D physics
module; clients draw the replicated state, interpolate remote entities and predict their own paddle so it responds
immediately. The same executable is a listen server, a dedicated server or a client, depending on configuration.

## What it shows

- The networking module on the LiteNetLib UDP transport: `AddNetworking`, `AddLiteNetLibTransport`, `UseNetworking`.
- Components marked `[Replicated]`, `[Predicted]` and `[Interpolated]`, including replicating a type declared in
  another assembly (`[assembly: ReplicateComponent(typeof(Transform2D), Interpolated = true)]`).
- Owner authority and client-side prediction with `INetworkPrediction.Register`.
- Typed `[NetworkMessage]` structs sent client to server (reliable and unreliable) and broadcast server to clients.
- Server-only logic gated on `INetworkSession`, and entities kept local with `NetworkLocal`.
- Peer join and leave events, per-player entities with `INetworkWorld.Allocate(player)`.
- Testing a server and a client in one process over a deterministic loopback, a lossy loopback and real UDP.

## Run it

```bash
dotnet run --project Ion.Examples/Ion.Examples.Breakout.Net                                             # a listen server: server and first player
dotnet run --project Ion.Examples/Ion.Examples.Breakout.Net -- --Ion:Headless=true --Ion:Network:Mode=Server   # a dedicated server
dotnet run --project Ion.Examples/Ion.Examples.Breakout.Net -- --Ion:Network:Mode=Client                # a client of Ion:Network:Connect
```

Start a listen server, then one or more clients in other terminals. Click to capture the mouse and click again to
launch a ball from your paddle. Headless peers (clients or a listen server) play by themselves with the
`NetAutopilotSystem`.

| Setting | Default in the sample | Meaning |
|---|---|---|
| `Ion:Network:Mode` | `ListenServer` when unset | `Offline`, `Client`, `Server` or `ListenServer`. The sample picks `ListenServer` only when no mode is configured. |
| `Ion:Network:Port` | `7777` (`appsettings.json`) | The server's port, and the port a client connects to. `0` on a server picks a free port. |
| `Ion:Network:Connect` | `127.0.0.1` | The server a client connects to. |
| `Ion:Network:Bind` | `127.0.0.1` | The address a server listens on. Any other address is logged as a public bind. |
| `Ion:Network:JoinSecret` | none | Set on both sides to require a secret (an HMAC challenge; the secret never crosses the network). |
| `Ion:Network:GameId` | `ion-breakout-net` (set in code) | Compared in the handshake. |
| `Ion:Network:Simulate:Latency`, `Jitter`, `Loss`, `Reorder`, `Seed` | none | A simulated bad network for local testing. |

To play across machines, bind the server to the LAN address and point clients at it:

```bash
dotnet run --project Ion.Examples/Ion.Examples.Breakout.Net -- --Ion:Network:Bind=0.0.0.0 --Ion:Network:JoinSecret=letmein
dotnet run --project Ion.Examples/Ion.Examples.Breakout.Net -- --Ion:Network:Mode=Client --Ion:Network:Connect=192.168.1.20 --Ion:Network:JoinSecret=letmein
```

## Program.cs

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);

// NativeAOT needs every stored component registered with Arch up front (the networking generator registers the
// replicated ones, AddPhysics2D the physics ones).
EcsComponents.Register<Wall>();

// Without a configured Ion:Network:Mode the game is a listen server.
var modeConfigured = builder.Configuration[$"{NetworkConfig.Section}:Mode"] is not null;
var headless = builder.Configuration.IsHeadless();

builder.AddIon(graphics => graphics.ClearColor = new Color(0x223))
	.AddEcsRendering()
	.AddPhysics2D(physics =>
	{
		physics.GravityY = 0;
		physics.UnitsPerMeter = Field.PixelsPerMeter;
	})
	.AddNetworking(network =>
	{
		if (!modeConfigured) network.Mode = NetworkMode.ListenServer;
		if (string.IsNullOrEmpty(network.GameId)) network.GameId = "ion-breakout-net";
	})
	.AddLiteNetLibTransport()
	.AddSystem<PaddlePredictionSystem>()
	.AddSystem<ServerGameSystem>()
	.AddSystem<PresentationSystem>()
	.AddSystem<PlayerInputSystem>();
builder.Services.AddSingleton<PaddleInputSource>();
if (headless) builder.AddSystem<NetAutopilotSystem>();

using var game = builder.Build();
game.UseIon()
	.UseEcsRendering()
	.UsePhysics2D()
	.UseNetworking()
	.UseSystem<PaddlePredictionSystem>()
	.UseSystem<ServerGameSystem>()
	.UseSystem<PresentationSystem>()
	.UseSystem<PlayerInputSystem>();
if (headless) game.UseSystem<NetAutopilotSystem>();
game.Run();
```

The `AddNetworking` delegate runs after `Ion:Network` is bound, so it only fills in what configuration left unset.

## Components and messages

```csharp title="Components.cs"
// The ECS module's 2D transform is replicated too: the networking generator writes its serializer into this assembly.
[assembly: ReplicateComponent(typeof(Transform2D), Interpolated = true)]

[Replicated] public record struct Ball(byte Owner);
[Replicated] public record struct Block(int Row, int Column);
[Replicated] public record struct Paddle(byte Player);

/// Predicted: the owning client moves its paddle from its own input immediately, the server applies the same input
/// when it arrives, and the client corrects when the two differ.
[Replicated(Authority = Authority.Owner), Predicted]
public record struct PaddleControl(float X);

[Replicated] public record struct Scoreboard(int Score, int BallsLost, int Round);

[NetworkMessage(Direction = MessageDirection.ClientToServer, Delivery = Delivery.Unreliable)]
public record struct PaddleInput(float TargetX);

[NetworkMessage(Direction = MessageDirection.ClientToServer)]
public record struct LaunchBall();

[NetworkMessage(Delivery = Delivery.Unreliable)]
public record struct BlockBroken(Vector2 Position);

/// A wall around the field: created by the server only, never networked.
public record struct Wall();
```

The networking generator writes a full and a field-wise delta serializer for each of them at compile time and a registry
whose hash both sides compare in the handshake. Components must be unmanaged (`ION201`), a `[Predicted]` component needs
owner authority (`ION202`), and so on: see the [diagnostics reference](/Ion/reference/diagnostics/#networking-ion201-to-ion210).

Sprites are **not** replicated: textures are local. Replicated entities arrive without a `Sprite` and the client adds one
by what they are.

## The systems

| System | Runs on | Job |
|---|---|---|
| `PaddlePredictionSystem` | every peer | Registers the paddle's prediction step at Init; clients and a listen server also sample local input. |
| `ServerGameSystem` | server only (returns early elsewhere) | Walls, blocks, the scoreboard, a paddle per player, launches, block breaks, lost balls and new rounds. |
| `PresentationSystem` | clients and a listen server | Window size, textures, sprites for replicated entities, the predicted own paddle, the score and the block sound. |
| `PlayerInputSystem` | clients and a listen server | Mouse capture, the paddle target and `LaunchBall` messages. |
| `NetAutopilotSystem` | headless clients and listen servers | Follows the lowest ball and launches a ball every 60 frames (up to 20). |

### Prediction

The paddle's movement is one deterministic function shared by client and server:

```csharp title="Components.cs"
public static void MovePaddle(ref PaddleControl control, in PaddleInput input, float delta)
{
	var half = PaddleSize.X / 2f;
	var target = Math.Clamp(input.TargetX, half, Width - half);
	var step = PaddleSpeed * delta;
	control.X = Math.Clamp(control.X + Math.Clamp(target - control.X, -step, step), half, Width - half);
}
```

```csharp title="Systems.cs"
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
```

A client samples its input every tick, sends it, and moves its own paddle at once. The server applies every player's
input to that player's paddle. When the server's snapshot disagrees, the client reconciles by replaying its unacknowledged
inputs. See [Prediction](/Ion/networking/multiplayer/prediction/).

### The authoritative server

`ServerGameSystem` checks `session.IsServer` at the top of every step. Players get a paddle when they join, owned by
them because its network id is allocated for them:

```csharp title="Systems.cs"
private void CreatePaddle(NetworkPeer player)
{
	var x = Field.Width / 2f;
	world.Create(network.Allocate(player), new Paddle(player.Id), new PaddleControl(x), new Transform2D(new Vector2(x, Field.PaddleY)),
		Collider2D.Capsule(Field.PaddleSize) with { Restitution = 1f, Friction = 0f }, RigidBody2D.Kinematic());
}
```

The predicted `PaddleControl.X` drives the kinematic body just before the physics step:

```csharp title="Systems.cs"
[FixedUpdate(Order = StageOrder.Physics - 10)]
public void DrivePaddles(GameTime dt) { /* transforms[i].Position = new Vector2(controls[i].X, Field.PaddleY) */ }
```

Launch requests arrive as messages, read with a `NetworkReader<T>` that also reports the sender:

```csharp title="Systems.cs"
while (_launches.TryRead(out var from, out _))
{
	if (world.CountEntities(in Balls) >= Field.MaxBalls) continue;
	if (FindPaddle(from, out var x)) CreateBall(new Vector2(x, Field.PaddleY - Field.PaddleSize.Y), from);
}
```

A block a ball touches breaks, and the server tells every client with an unreliable message (a lost one is only a
missing sound):

```csharp title="Systems.cs"
messages.Broadcast(new BlockBroken(world.Get<Transform2D>(block).Position));
world.Destroy(block);
world.Get<Scoreboard>(_scoreboard).Score += 10;
```

Walls carry `NetworkLocal`, so they exist on the server only. Everything else the server creates with replicated
components is networked automatically.

:::note[Skip readers you do not use]
On a client, `ServerGameSystem` still owns readers of `Collision2D` and `LaunchBall`. It calls `Skip()` on them so they
do not hold a growing backlog: `_collisions.Skip()` and `_launches.Skip()`.
:::

### Presentation

```csharp title="Systems.cs"
[Update]
public void AddSprites(GameTime dt)
{
	if (world.CountEntities(in BallsWithoutSprite) > 0) world.Add(in BallsWithoutSprite, new Sprite(_ball, Field.BallSize, depth: 1));
	if (world.CountEntities(in BlocksWithoutSprite) > 0) world.Add(in BlocksWithoutSprite, new Sprite(_block, Field.BlockSize));
	if (world.CountEntities(in PaddlesWithoutSprite) > 0) world.Add(in PaddlesWithoutSprite, new Sprite(_paddle, Field.PaddleSize));
	if (_broken.Read().Length > 0) audio.Play(_ping);
}
```

The local paddle is drawn where the prediction put it, before transform propagation
(`Order = StageOrder.TransformPropagation - 10` in Render); remote paddles and balls are drawn where interpolation put
them, a couple of ticks behind the newest snapshot.

## The tests

[`ConvergenceTests`](https://github.com/jimbuck/Ion/blob/main/Ion.Examples/Ion.Examples.Breakout.Net.Tests/ConvergenceTests.cs)
run a headless dedicated server and a headless client of the real `Program.cs` in one process, stepped frame by frame.
The client's replicated state must equal the server's at the tick of the client's newest snapshot, within a few ticks,
all along the game.

| Test | Transport | Checks |
|---|---|---|
| `ServerAndClientConvergeOverLoopback` | `LoopbackTransport` | 900 frames, converging within 10 ticks. |
| `ServerAndClientConvergeOverALossyLaggyLoopback` | loopback with 40 ms latency, 10 ms jitter, 5% loss, 5% reordering | 900 frames, within 30 ticks. |
| `ServerAndClientConvergeOverUdpOnLocalhost` (E2E) | LiteNetLib UDP with a join secret | 900 frames, within 30 ticks. |
| `AListenServerPlaysOnItsOwn` | loopback | 400 frames: the autopilot launches balls and scores. |

The loopback tests swap the transport after the program's own registrations:

```csharp title="ConvergenceTests.cs"
private static IonTestHost Host(LoopbackNetwork? loopback, params string[] args)
{
	var host = new IonTestHost().UseEntryPoint<Program>(args);
	if (loopback is not null) host.Configure(services => services.RemoveAll<INetworkTransport>().AddLoopbackTransport(loopback));
	return host;
}

var network = new LoopbackNetwork();
var server = Host(network, ["--Ion:Headless=true", "--Ion:Network:Mode=Server", "--Ion:Network:Port=0"]).Start();
var port = server.Get<NetworkSession>().Transport!.LocalPort.ToString(CultureInfo.InvariantCulture);
var client = Host(network, ["--Ion:Headless=true", "--Ion:Network:Mode=Client", "--Ion:Network:Port=" + port]).Start();
```

The host's services (`Configure`) are added after the program's and win, which is what makes the swap work.

## Publishing

The sample is in the CI AOT lane:

```bash
dotnet publish Ion.Examples/Ion.Examples.Breakout.Net -p:IonTarget=linux-x64
```

The networking module, its generated serializers and the LiteNetLib transport publish without trim or AOT warnings.

## Ideas to extend it

**Per-player colors.** Instead of the bulk `world.Add` for paddles in `AddSprites`, give each new paddle its own sprite
with a color picked from `Paddle.Player` (`new Sprite(_paddle, Field.PaddleSize) with { Color = ... }`), so players can
tell their paddles apart.

**A lobby.** Show "Waiting for players" until two paddles exist, by counting `Paddle` entities on the client, and
start launching only then on the server.

**Lag compensation.** Balls are server-simulated, so a player sees them slightly in the past. For hit tests that must
feel fair (a shot, not a bounce), use the module's lag compensation; see
[Lag compensation](/Ion/networking/multiplayer/lag-compensation/).

**A dedicated server container.** The dedicated server needs no GPU; publish it with `-p:IonTarget=linux-x64` and run
it with `--Ion:Headless=true --Ion:Network:Mode=Server --Ion:Network:Bind=0.0.0.0`. See
[Dedicated server](/Ion/networking/multiplayer/dedicated-server/).

## See also

- [Multiplayer overview](/Ion/networking/multiplayer/overview/), [Messages](/Ion/networking/multiplayer/messages/),
  [Transports](/Ion/networking/multiplayer/transports/) and [Interpolation](/Ion/networking/multiplayer/interpolation/).
- [Breakout ECS](/Ion/examples/breakout-ecs/): the single-player version.
- [Physics determinism](/Ion/physics/determinism/).
