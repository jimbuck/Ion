---
title: Lag compensation
description: Judge hits fairly on the server by rewinding replicated state to the tick a client saw, with TryGetRewindTick, GetAtTick and WithWorldAtTick bounded by MaxRewindTicks and the peer's round trip.
sidebar:
  order: 15
---

A client draws remote entities in the past: `InterpolationDelay` ticks behind the newest snapshot, which itself left the
server half a round trip ago. When a player aims at a moving target and fires, they aim at where the target *was*. If
the server checks the shot against where the target is *now*, fast targets become impossible to hit on any real
network. **Lag compensation** lets the server check the shot against the state the client actually saw.

Ion's snapshot ring already holds the last `SnapshotHistory` ticks of every replicated component, so lag compensation
is a read of that ring, with limits that stop a client from claiming it acted arbitrarily far in the past.

## The flow

1. The client includes the tick it saw in the action message. That is its `INetworkWorld.InterpolationTick`: the server
   tick remote entities were drawn at.
2. The server asks `TryGetRewindTick(peer, claimedTick, out tick)` whether the claim is plausible.
3. If it is, the server reads the past state (`GetAtTick`/`TryGetAtTick`) or rewinds the whole replicated world for a
   moment (`WithWorldAtTick`) and resolves the action against it.

```csharp title="Messages"
[NetworkMessage(Direction = MessageDirection.ClientToServer)]
public record struct Fire(uint SeenTick, Vector2 Origin, Vector2 Direction);

[Replicated, Interpolated]
public record struct Position(Vector2 Value);

[Replicated]
public record struct Health(int Current, int Max);
```

```csharp title="Client: send what you saw"
public sealed class ShootSystem(IInputState input, INetworkSession session, INetworkWorld network, INetworkMessages messages)
{
    [Update]
    public void Shoot(GameTime dt)
    {
        if (!session.IsClient || session.State != NetworkState.Connected) return;
        if (!input.Pressed(MouseButton.Left)) return;
        var seen = (uint)Math.Floor(network.InterpolationTick);
        messages.SendToServer(new Fire(seen, _muzzle, _aim));
    }

    private Vector2 _muzzle, _aim;   // from your player state
}
```

```csharp title="Server: rewind and resolve"
public sealed class HitSystem(World world, INetworkSession session, INetworkWorld network, INetworkMessages messages)
{
    private static readonly QueryDescription Targets = new QueryDescription().WithAll<NetworkId, Position, Health>();
    private NetworkReader<Fire> _fires = messages.Reader<Fire>();

    [FixedUpdate]
    public void Resolve(GameTime dt)
    {
        if (!session.IsServer)
        {
            _fires.Skip();
            return;
        }

        while (_fires.TryRead(out var from, out var fire))
        {
            // Implausible or too old: resolve against the present instead (or drop the shot).
            var tick = network.TryGetRewindTick(from, fire.SeenTick, out var rewound) ? rewound : network.CurrentTick;

            foreach (ref var chunk in world.Query(in Targets))
            {
                var ids = chunk.GetSpan<NetworkId>();
                for (var i = 0; i < chunk.Count; i++)
                {
                    var entity = chunk.Entity(i);
                    if (ids[i].OwnerPeer == from.Id) continue;                         // no self hits
                    if (!network.TryGetAtTick<Position>(entity, tick, out var then)) continue;
                    if (RayHits(fire.Origin, fire.Direction, then.Value, radius: 16f))
                        world.Get<Health>(entity).Current -= 10;                        // damage applies now
                }
            }
        }
    }

    private static bool RayHits(Vector2 origin, Vector2 direction, Vector2 center, float radius)
    {
        var d = Vector2.Normalize(direction);
        var along = Vector2.Dot(center - origin, d);
        return along > 0 && Vector2.DistanceSquared(origin + d * along, center) <= radius * radius;
    }
}
```

The hit test reads positions at the rewound tick; the damage is applied to the present. That is the usual rule: *where*
comes from the past, *what happens* happens now.

## The API

All on `INetworkWorld` (server side):

| Member | What it does |
|---|---|
| `TryGetRewindTick(peer, claimedTick, out tick)` | Whether a claim is plausible. Returns the tick to rewind to. Refusals count in `NetworkStats.RewindsRejected` (metric `net_rewinds_rejected`). |
| `TryGetAtTick<T>(entity, tick, out value)` | The value of a replicated component on an entity at a tick, if the ring has it. |
| `GetAtTick<T>(entity, tick)` | The same by `ref readonly`; throws `InvalidOperationException` if the ring does not have it. Two array indexings. |
| `WithWorldAtTick(tick, action)` | Writes every replicated component of every networked entity at `tick` into the world, runs `action(world)`, then restores the current values in a `finally`. Counts in `NetworkStats.Rewinds`. |
| `CurrentTick`, `OldestTick` | The range of ticks in the ring. |

### When is a claim plausible?

`TryGetRewindTick` refuses a claim when any of these holds:

- the claimed tick is 0 or in the future (`> CurrentTick`);
- it is more than `MaxRewindTicks` (12 by default) behind the current tick;
- it is older than the peer's measured round trip allows: more than `ceil(RTT in ticks + InterpolationDelay +
  InputLeadTicks + 2)` ticks behind;
- the ring no longer has that tick.

The round-trip bound stops a client with a 20 ms ping from claiming it saw the world 200 ms ago, which would let a
cheater shoot targets that already found cover. `MaxRewindTicks` caps what even a high-latency player gets.

| Setting | Default | Effect |
|---|---|---|
| `Ion:Network:MaxRewindTicks` | `12` | Hard cap on how far back the server rewinds (200 ms at 60 ticks). |
| `Ion:Network:SnapshotHistory` | `128` | Ticks kept in the ring; must exceed `MaxRewindTicks`. |
| `Ion:Network:InterpolationDelay`, `InputLeadTicks` | `2`, `2` | Part of the plausibility bound. |

## Rewinding the whole world

`WithWorldAtTick` is for checks that need many entities at once, or code that reads components directly from the
world:

```csharp
network.WithWorldAtTick(tick, w =>
{
    // Inside: every networked entity's replicated components hold their values at `tick`.
    foreach (ref var chunk in w.Query(in Targets))
    {
        var positions = chunk.GetSpan<Position>();
        // ... test against positions
    }
});
// Outside: the current values are back.
```

Details:

- The tick is clamped to `MaxRewindTicks` back; a tick at or after the last captured one runs the action on the
  current world unchanged.
- Entities that did not exist at that tick keep their current values. Entities that existed then but are gone now are
  not recreated.
- Only replicated components are rewound. Non-replicated components, and state outside the ECS, are not.
- Restoring happens in a `finally`, so an exception in the action does not leave the world rewound.
- The action is an `Action<World>`; a lambda that captures locals allocates a closure. For per-frame use in hot paths,
  prefer `TryGetAtTick` reads.

### Physics queries

The physics engines keep bodies of their own (Box2D in the 2D module, BepuPhysics in the 3D one), which the components
do not move by themselves. `WithWorldAtTick` therefore tells every `IWorldRewindListener` service after rewinding the
components, and again after restoring them. The 2D and 3D physics modules register one, so inside the action **every
body is where its entity was at the tick**, and physics queries (`RayCast`, the overlap and shape casts) see that pose:

```csharp
// The server checks a shot: a ray from the shooter, against the targets where the shooter saw them.
if (network.TryGetRewindTick(peer, shot.SeenTick, out var tick))
{
    network.WithWorldAtTick(tick, _ =>
    {
        if (physics.RayCast(shot.Origin, shot.Direction * range, out var hit, mask: Layers.Players)) Damage(hit.Entity);
    });
}
```

- Each body whose entity's rewound `Transform2D` or `Transform` differs from its current pose is moved there, with its
  broadphase bounds updated, without stepping the simulation. Parented bodies use their rewound world pose. Afterwards
  each is put back exactly where it was.
- Only poses move. Velocities, contacts and sleeping are left alone, and the next physics step continues from the
  present.
- The transforms must be replicated (`[assembly: ReplicateComponent(typeof(Transform2D))]`, or `Transform` in 3D) to be
  rewound at all.
- Anything else that keeps its own copy of entity state (a spatial grid, a navigation mesh) can follow rewinds the
  same way: implement `IWorldRewindListener` (`Rewound(world)`, `Restored(world)`) and register it as a singleton.

## Tuning and fairness

- **Favor the shooter, within limits.** Lag compensation makes hitting feel right for the shooter at the cost of the
  target occasionally being hit "behind cover" on their screen. `MaxRewindTicks` bounds how bad that can get.
- **Keep `InterpolationDelay` small.** Every tick of delay is a tick the server must rewind.
- **Send `SeenTick` from the frame the input happened.** Take it in the same `Update` as the input read, not later.
- **Watch the counters.** A high `net_rewinds_rejected` rate means clients claim ticks that are too old: check their
  RTT, or raise `MaxRewindTicks` if your game tolerates it.

## Tests

`PredictionTests.LagCompensationRewindsWithinTheLimitAndRestores` checks that a claim five ticks back is plausible and
reads the old position, that `WithWorldAtTick` shows it and restores the present afterwards, that claims beyond
`MaxRewindTicks` or in the future are refused, and that `WithWorldAtTick` clamps an older tick.
`LagCompensationPhysicsTests` run a networked server with the 2D and with the 3D physics module and a target that
slides every tick: a ray cast inside `WithWorldAtTick` hits where the target was 20 ticks earlier, misses there
outside it, and hits the present position afterwards, from which the physics steps on.

## See also

- [Interpolation](/Ion/networking/multiplayer/interpolation/) for what the client saw.
- [Prediction](/Ion/networking/multiplayer/prediction/).
- [Messages and replication](/Ion/networking/multiplayer/messages/).
- [Multiplayer overview](/Ion/networking/multiplayer/overview/).
- Source: [NetworkWorld.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Networking/NetworkWorld.cs)
