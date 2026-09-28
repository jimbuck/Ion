---
title: Interpolation
description: Draw remote entities smoothly between server snapshots with [Interpolated] components, tune InterpolationDelay and MaxExtrapolationTicks, and understand the adaptive interpolation clock.
sidebar:
  order: 14
---

Snapshots arrive at the tick rate at best, and on a real network they arrive late, in bursts, or not at all. If a
client simply applied each snapshot, other players and moving objects would jump from position to position. Snapshot
**interpolation** draws remote entities slightly in the past, blended between the two snapshots around that time, so
their motion is smooth even when packets are not.

## Turning it on

Mark a replicated component `[Interpolated]`:

```csharp
[Replicated, Interpolated]
public record struct Position(Vector2 Value);
```

or, for a component declared elsewhere such as the ECS module's `Transform2D`:

```csharp
[assembly: ReplicateComponent(typeof(Transform2D), Interpolated = true)]
```

That is all. The generator writes a **blend** for the type, and the client's `Interpolate` step (`Render` stage at
`StageOrder.Network`, -870) writes the blended value into the component of every entity the client does **not** own,
before your render steps and before transform propagation.

`[Interpolated]` without `[Replicated]` is `ION209`.

## How values are blended

The generated blend works member by member:

| Member type | Blend |
|---|---|
| `float`, `double`, `Vector2`, `Vector3`, `Vector4` | Linear |
| `Quaternion` | Spherical (slerp) |
| Anything else (integers, `bool`, enums, `FixedString`, `NetworkId`) | Switches from the older to the newer value at the midpoint |
| Nested structs | Member by member, by the same rules |


## The interpolation clock

The client keeps a fractional server tick, `INetworkWorld.InterpolationTick`, at which it draws remote entities:

```text
target = newest complete snapshot tick + (time since it arrived * tick rate) - InterpolationDelay
```

Each frame the interpolation tick advances at the tick rate and then closes a tenth of the remaining error to the
target, so it adapts smoothly to changing latency instead of snapping. If it is more than 4 ticks off, it jumps to the
target. The client then finds the complete snapshots just before and just after that tick and blends between them.

```text
server ticks:     96    97    98    99    100   (newest complete snapshot)
                                  ^
                          InterpolationTick = 98.4  (delay 2 ticks, minus the arrival adjustment)
drawn value   =  blend(snapshot 98, snapshot 99, t = 0.4)
```

| Setting | Default | Effect |
|---|---|---|
| `Ion:Network:InterpolationDelay` | `2` (ticks) | How far behind the newest snapshot remote entities are drawn. Larger survives more loss and jitter; smaller shows remote entities closer to the present. |
| `Ion:Network:MaxExtrapolationTicks` | `2` (ticks) | How far past the newest snapshot the client extrapolates when snapshots stop arriving, before it holds. |
| `Ion:Network:SendRate` | `0` (every tick) | Fewer snapshots per second mean wider gaps to interpolate across; raise `InterpolationDelay` with it. |

As a rule of thumb, keep `InterpolationDelay` at least the number of ticks between two snapshots plus one, so the
client almost always has a snapshot on each side. With `TickRate` 60 and `SendRate` 20 (a snapshot every 3 ticks), a
delay of 3 to 4 ticks works well.

```json title="appsettings.json"
{
  "Ion": {
    "Network": {
      "SendRate": 20,
      "InterpolationDelay": 4,
      "MaxExtrapolationTicks": 2
    }
  }
}
```

### Extrapolation and holding

When the interpolation tick passes the newest snapshot (the network stalled), the client extrapolates from the last two
snapshots for at most `MaxExtrapolationTicks`, then holds the entity where it is until new snapshots arrive. Short
stalls stay smooth; long ones freeze rather than letting entities drift off.

## What is interpolated, and what is not

- Only **clients** interpolate. A server (dedicated or listen) holds the authoritative values.
- Only entities the client does **not own**. The local player's own entities show either the replicated value or, for
  predicted components, the predicted one (see [Prediction](/Ion/networking/multiplayer/prediction/)).
- Only `[Interpolated]` components. Other replicated components take the newest snapshot's value as soon as it is
  applied in `First`.
- An entity that did not exist in both surrounding snapshots is not blended.

:::caution[The component holds the drawn value]
Interpolation writes into the same ECS component that replication writes. On a client, an `[Interpolated]` component
of a remote entity holds the value drawn last frame, which is in the past by the interpolation delay. When client
gameplay code needs the newest server value instead (for example to aim at where a target is now), read it from the
snapshot ring:

```csharp
var tick = network.LastReceivedServerTick;
if (network.TryGetAtTick<Position>(entity, tick, out var newest))
{
    // newest.Value is the server's value in the newest complete snapshot
}
```
:::

## Interpolated rendering of predicted entities

A common setup, used by Breakout Net: `Transform2D` is replicated and interpolated (so remote paddles, balls and blocks
move smoothly), while each paddle also has a predicted `PaddleControl`. A render step before transform propagation
overwrites the local player's paddle transform with its predicted position, so the local paddle responds instantly and
remote paddles glide.

```csharp
[Render(Order = StageOrder.TransformPropagation - 10)]
public void PlaceOwnPaddle(GameTime dt)
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
```

## Interpolation and lag compensation

Because clients see remote entities `InterpolationDelay` ticks in the past, a player who shoots at what they see is
shooting at where the target *was*. The server corrects for this with lag compensation: the client reports the
`InterpolationTick` it saw, and the server rewinds to it. See
[Lag compensation](/Ion/networking/multiplayer/lag-compensation/).

## Debugging

- `INetworkWorld.InterpolationTick` and `LastReceivedServerTick` show how far behind the client draws.
- `NetworkStats.SnapshotsLost` (metric `net_snapshots_lost`) counts snapshot ticks the client never completed; a high
  rate means you need a larger delay, a lower `SendRate`, or fewer entities per snapshot.
- Test under simulated conditions with the loopback transport (`Ion:Network:Simulate:Latency`, `Jitter`, `Loss`,
  `Reorder`; see [Transports](/Ion/networking/multiplayer/transports/)).

## See also

- [Messages and replication](/Ion/networking/multiplayer/messages/)
- [Prediction](/Ion/networking/multiplayer/prediction/)
- [Transforms](/Ion/ecs/transforms/)
- [Multiplayer overview](/Ion/networking/multiplayer/overview/)
