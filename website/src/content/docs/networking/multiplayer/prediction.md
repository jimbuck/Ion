---
title: Client-side prediction
description: Make a player's own entity respond instantly with INetworkPrediction, sharing one deterministic step between client and server, and let reconciliation correct the client when the server disagrees.
sidebar:
  order: 13
---

Without prediction, a client that presses a key waits a full round trip before it sees its own paddle move: the input
travels to the server, the server simulates it, and the result comes back in a snapshot. At 100 ms of latency that
feels broken. **Client-side prediction** applies the input locally at once, and **reconciliation** fixes the local
value when the server's authoritative result turns out different.

Ion's prediction is scoped: you register one deterministic **step** per (component, input) pair, and the module runs
it on both sides, stores inputs and predicted values per tick, sends the inputs, and replays them after a correction.

## The three pieces

1. A **predicted component**: replicated with owner authority and `[Predicted]`.
2. An **input message**: a `[NetworkMessage]` clients may send.
3. A **step**: a pure function that applies one tick of input to the component.

```csharp title="Components.cs"
using Ion.Extensions.Networking;

[Replicated(Authority = Authority.Owner), Predicted]
public record struct PaddleControl(float X);

[NetworkMessage(Direction = MessageDirection.ClientToServer, Delivery = Delivery.Unreliable)]
public record struct PaddleInput(float TargetX);

public static class Field
{
    public const float PaddleSpeed = 1500f;

    // Deterministic, and depends only on its arguments.
    public static void MovePaddle(ref PaddleControl control, in PaddleInput input, float delta)
    {
        var step = PaddleSpeed * delta;
        control.X += Math.Clamp(input.TargetX - control.X, -step, step);
    }
}
```

The step has the `PredictionStep<TComponent, TInput>` delegate shape:

```csharp
public delegate void PredictionStep<TComponent, TInput>(ref TComponent component, in TInput input, float delta)
    where TComponent : unmanaged where TInput : unmanaged;
```

## Registering the step

Register it on **every** peer, from the same game code, with `INetworkPrediction.Register`. Pass a `sample` function on
peers that have a local player (clients and a listen server); a dedicated server passes none.

```csharp title="PaddlePredictionSystem.cs"
public sealed class PaddleInputSource
{
    public float TargetX { get; set; }
}

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

Your input code writes to a plain object (`PaddleInputSource` here) from `Update`; the prediction step samples it once
per fixed step:

```csharp
public sealed class PlayerInputSystem(IInputState input, PaddleInputSource source)
{
    [Update]
    public void Read(GameTime dt) => source.TargetX = input.MousePosition.X;
}
```

`Register` throws `InvalidOperationException` when the component is not a registered replicated type, the input is not
a registered message, the input's `Direction` does not let clients send it, or the component already has a step.

## What runs when

| Where | When | What happens |
|---|---|---|
| Client | Every fixed step, `FixedUpdate` at `StageOrder.Network + 10` (-860) | Calls `sample()`, stores the input for the current tick, sends it **with the two previous inputs** (so a lost packet costs nothing), applies the step to each entity it owns, and records the predicted value for the tick. |
| Server | Every fixed step, same order | Buffers received inputs by the tick the client stamped them with, then applies each client's input **for this tick** to that client's entities. If the input for the tick is late or lost, it uses the latest earlier one. |
| Listen server | Every fixed step | Samples its own input and applies it to the entities owned by `NetworkPeer.Server`, like a client that never mispredicts. |
| Client | `First` at -860, after a new complete snapshot arrived | **Reconciles**: for each owned entity, compares the server's value at the snapshot tick with the value it predicted for that tick. |

Because the step runs before your own `FixedUpdate` steps (order 0), your systems see the predicted value in the same
tick. Breakout Net drives each paddle's kinematic physics body from `PaddleControl` in a step just before the physics
step.

## Reconciliation

When a snapshot for server tick `T` arrives, the client looks at each entity it owns that has the predicted component:

1. If it recorded a prediction for tick `T` and it equals the server's value bit for bit, nothing happens.
2. Otherwise it takes the server's value, then **replays** every stored input from `T + 1` to the current tick through
   the step, recording the new predicted values, and writes the result. A mismatch counts as a correction
   (`NetworkStats.Corrections`, metric `net_corrections`).

Only the registered steps are replayed, not the whole fixed schedule. That keeps reconciliation cheap and
allocation-free, but it means a predicted component must be fully determined by its step: if another system also
changes it (collisions, knockback), the server's result will differ and the client will be corrected every time.

```text
client tick:    100  101  102  103  104  105
input sent:      i100 i101 i102 i103 i104 i105
snapshot for 102 arrives at client tick 105:
  predicted[102] == server[102] ?  yes -> keep
                                    no  -> value = server[102]; step(i103); step(i104); step(i105)
```

### Why the client runs ahead

A client runs `InputLeadTicks` (2 by default) ahead of its estimate of the server tick, starting a full round trip plus
the lead ahead of the tick in the server's accept. That way its input stamped for tick `N` arrives before the server
simulates `N`. Every snapshot updates a smoothed estimate of the error; when it exceeds 3 ticks (or a single
measurement is more than 12 ticks off), the client resynchronizes its tick (`NetworkStats.Resyncs`, metric
`net_resyncs`) and discards stored predictions, which belonged to the old timeline.

:::note
Drift is corrected by resynchronizing. Nudging the client's clock rate by up to 1 percent, as in the original design, is
not implemented.
:::

## Drawing the predicted entity

A predicted entity usually also has a replicated, interpolated transform written by the server. For the local player,
draw the predicted value instead, before transform propagation:

```csharp
public sealed class PlaceOwnPaddle(World world, INetworkWorld network)
{
    private static readonly QueryDescription OwnPaddles =
        new QueryDescription().WithAll<NetworkId, PaddleControl, Transform2D>();

    [Render(Order = StageOrder.TransformPropagation - 10)]
    public void Place(GameTime dt)
    {
        foreach (ref var chunk in world.Query(in OwnPaddles))
        {
            var ids = chunk.GetSpan<NetworkId>();
            var controls = chunk.GetSpan<PaddleControl>();
            var transforms = chunk.GetSpan<Transform2D>();
            for (var i = 0; i < chunk.Count; i++)
                if (network.IsOwned(ids[i])) transforms[i].Position = new Vector2(controls[i].X, Field.PaddleY);
        }
    }
}
```

Remote players' paddles keep the interpolated transform (see [Interpolation](/Ion/networking/multiplayer/interpolation/)).
Interpolation never touches entities the local peer owns.

## Reading inputs on the server

`INetworkPrediction.TryGetInput<TInput>(peer, out input)` returns the input applied for `peer` in the current tick on
the server, or the local input of the current tick on a client. Use it when other server logic needs the same input
(for example firing when a button is held):

```csharp
if (prediction.TryGetInput<PaddleInput>(peer, out var input) && input.TargetX < 0)
{
    // ...
}
```

It returns false before any input was received.

## Giving the player an entity

Prediction only applies to entities the local peer owns. The server creates them with an id allocated for the player:

```csharp
private void CreatePaddle(NetworkPeer player)
{
    var x = Field.Width / 2f;
    world.Create(network.Allocate(player), new Paddle(player.Id), new PaddleControl(x),
        new Transform2D(new Vector2(x, Field.PaddleY)));
}
```

## Pitfalls

:::caution[Keep the step deterministic]
The step must produce the same result on client and server from the same arguments. Do not read `Random`, the wall
clock, other entities or services inside it. Use the `delta` it is given, which is one fixed step (`1 / TickRate`).
Floating point is deterministic for the same operations on the same architecture; mixing architectures (x64 server,
arm64 client) can produce tiny differences that show up as occasional corrections.
:::

- **Predicted components must be owner authority** (`ION202`). They are not accepted as owner updates from clients;
  the server computes them from inputs.
- **Inputs are unreliable.** Send them with `Delivery.Unreliable`; the module repeats the last two with every packet.
- **A missing sample** (`sample` null on a client) means the client never predicts or sends that input.
- **One step per component.** Registering a second step for the same component throws.

## Tests

`PredictionTests` in `Ion.Extensions.Networking.Tests` cover the owner predicting immediately and matching the server
exactly, a server override being reconciled by replaying the stored inputs, lost inputs being covered by the redundant
copies, and registration checks. The Breakout Net convergence tests check that the predicted paddle lands where the
server puts it. See
[Transports](/Ion/networking/multiplayer/transports/) for the loopback setup.

## See also

- [Messages and replication](/Ion/networking/multiplayer/messages/)
- [Interpolation](/Ion/networking/multiplayer/interpolation/)
- [Lag compensation](/Ion/networking/multiplayer/lag-compensation/)
- [Time and determinism](/Ion/concepts/time-and-determinism/)
- Source: [NetworkPrediction.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Networking/NetworkPrediction.cs)
