# Ion Network Plugin — Client-Side Prediction & Lag Compensation (v2)

This document builds on the [Ion Network Plugin Design](ion-network-plugin.md) (Option 3: Hybrid). It describes how client-side prediction, server reconciliation, entity interpolation, and server-side lag compensation fit into the existing snapshot ring buffer architecture.

## Prerequisites from v1

v1 delivers the foundation that v2 builds on:

- **Snapshot ring buffer** — `INetworkWorld.CaptureSnapshot()` stores N ticks of component state for all `[Networked]` entities
- **Tick counter** — every `FixedUpdate` increments a deterministic tick number, included in all network packets
- **`[Networked]` component replication** — server sends authoritative state to clients each tick
- **`INetworkEventBus`** — explicit messages carry tick-stamped player inputs

## Concepts

### The Fundamental Problem

Network latency means the client always sees the server's state *in the past*. Without compensation:

- **Input delay**: player presses "move right", waits 80ms for the server to process it and send the new position back. Feels sluggish.
- **Stale targets**: player shoots at an enemy they see on screen, but the enemy has already moved on the server. Feels unfair.

### The Solutions

| Problem | Solution | Where it runs |
|---|---|---|
| Input feels delayed | Client-side prediction | Client |
| Server corrects wrong predictions | Server reconciliation | Client |
| Remote entities stutter | Entity interpolation | Client |
| Shots miss because targets moved | Lag compensation | Server |

---

## 1. Client-Side Prediction

### What It Does

The client applies its own inputs immediately to `[Networked(Authority = Owner)]` components without waiting for the server. When the server's authoritative state arrives, the client checks if its prediction was correct. If it was, nothing happens — the player never felt any delay. If it was wrong, the client corrects.

### Architecture

```
Client timeline:
  Tick 100: Player presses "right"
            → Send PlayerInput(Tick=100, Move=Right) to server
            → Immediately apply Move=Right to local Transform2D
            → Store input in PredictionInputBuffer[100]

  Tick 101: Player presses "right" again
            → Send PlayerInput(Tick=101, Move=Right) to server
            → Apply locally, store in buffer

  ... 4 ticks of latency ...

  Tick 104: Receive server state for Tick 100
            → Compare server's Transform2D with our snapshot at Tick 100
            → If match: discard inputs <= 100 from buffer, done
            → If mismatch: RECONCILE (see next section)
```

### Key Components

```csharp
/// <summary>
/// Circular buffer of inputs the client has sent but the server hasn't acknowledged yet.
/// Indexed by tick number. Holds the raw input + the predicted component state after applying it.
/// </summary>
public class PredictionInputBuffer<TInput> where TInput : unmanaged
{
    private readonly TInput[] _inputs;
    private readonly int _capacity;

    public void Store(uint tick, in TInput input);
    public ref TInput Get(uint tick);
    public void DiscardUpTo(uint tick);
    public uint OldestTick { get; }
    public uint NewestTick { get; }
}
```

```csharp
/// <summary>
/// Marks a component type as client-predicted. The client applies inputs locally
/// and reconciles when authoritative server state arrives.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public class PredictedAttribute : Attribute { }
```

Usage:

```csharp
[Networked(Authority = NetworkAuthority.Owner)]
[Predicted]
public record struct Transform2D(Vector2 Position, float Rotation = 0);
```

### Prediction System (Client-Side Middleware)

```csharp
public class PredictionSystem(
    INetworkWorld netWorld,
    INetworkEventBus eventBus,
    IInputState input)
{
    private readonly PredictionInputBuffer<PlayerInput> _inputBuffer = new(256);
    private uint _lastAcknowledgedTick = 0;

    [FixedUpdate]
    public void Predict(GameTime dt, GameLoopDelegate next)
    {
        var currentTick = netWorld.CurrentTick;

        // 1. Capture and send input
        var playerInput = new PlayerInput(input.MoveAxis, input.FirePressed);
        _inputBuffer.Store(currentTick, playerInput);
        eventBus.Send(serverPeer, new PlayerInputMessage(currentTick, playerInput));

        // 2. Apply input locally (prediction)
        ApplyInput(playerInput);

        next(dt); // rest of game simulation runs on predicted state
    }
}
```

---

## 2. Server Reconciliation

### What It Does

When the client receives an authoritative server snapshot, it compares the server's state at tick N with its own recorded prediction at tick N. If they differ beyond a threshold, the client:

1. Rolls back to the server's authoritative state at tick N
2. Re-applies all inputs from tick N+1 through the current tick
3. The player sees a correction, ideally small enough to be imperceptible

### Architecture

```
Reconciliation (on client, when server state for tick 100 arrives):

  1. Compare: server.Transform2D[tick=100] vs localSnapshot[tick=100]
  2. If difference > threshold:
     a. Rollback: overwrite ECS state with server's authoritative values
     b. Set world tick to 100
     c. Re-apply inputs 101, 102, 103, 104 (all unacknowledged)
        → For each: run FixedUpdate simulation step
     d. World is now at tick 104 with corrected state
  3. Discard inputs <= 100 from PredictionInputBuffer
```

### Key Interface Additions to INetworkWorld

```csharp
public interface INetworkWorld
{
    // --- v1 (already exists) ---
    uint CurrentTick { get; }
    void CaptureSnapshot();
    void ApplyIncoming();
    void SendOutgoing();

    // --- v2: Prediction & Reconciliation ---

    /// <summary>
    /// Restore the ECS world to the state captured at the given tick.
    /// Only affects [Predicted] components on locally-owned entities.
    /// </summary>
    void Rollback(uint toTick);

    /// <summary>
    /// Re-run the FixedUpdate pipeline from fromTick to toTick,
    /// applying stored inputs for each tick. Used after Rollback
    /// to fast-forward back to the present.
    /// </summary>
    void Resimulate(uint fromTick, uint toTick, Func<uint, PlayerInput> getInputForTick);

    /// <summary>
    /// Compare a received authoritative snapshot against the local
    /// prediction at the same tick. Returns true if they differ
    /// beyond the configured threshold.
    /// </summary>
    bool NeedsReconciliation(uint tick);
}
```

### Reconciliation System (Client-Side Middleware)

```csharp
public class ReconciliationSystem(INetworkWorld netWorld, PredictionInputBuffer<PlayerInput> inputBuffer)
{
    [First]
    public void Reconcile(GameTime dt, GameLoopDelegate next)
    {
        // Check if we received a new authoritative snapshot
        if (netWorld.LastReceivedServerTick > _lastReconciledTick)
        {
            var serverTick = netWorld.LastReceivedServerTick;

            if (netWorld.NeedsReconciliation(serverTick))
            {
                // Rollback to server's authoritative state
                netWorld.Rollback(serverTick);

                // Re-apply all unacknowledged inputs
                netWorld.Resimulate(
                    serverTick + 1,
                    netWorld.CurrentTick,
                    tick => inputBuffer.Get(tick)
                );
            }

            inputBuffer.DiscardUpTo(serverTick);
            _lastReconciledTick = serverTick;
        }

        next(dt);
    }
}
```

### Handling the Correction Visually

A hard snap after reconciliation is jarring. Two strategies:

**A. Visual smoothing (recommended):**

Separate *simulation position* from *render position*. The simulation snaps to the corrected value, but the rendered position lerps toward it over a few frames.

```csharp
[Networked(Authority = NetworkAuthority.Owner)]
[Predicted]
public record struct Transform2D(Vector2 Position, float Rotation = 0);

// Not networked — purely local visual state
public record struct RenderTransform(Vector2 DisplayPosition, float DisplayRotation);

// In the Render phase:
[Render]
public void SmoothRender(GameTime dt, GameLoopDelegate next)
{
    world.Query(in _predictedQuery, (ref Transform2D sim, ref RenderTransform vis) =>
    {
        vis.DisplayPosition = Vector2.Lerp(vis.DisplayPosition, sim.Position, 0.3f);
        vis.DisplayRotation = MathHelper.Lerp(vis.DisplayRotation, sim.Rotation, 0.3f);
    });

    next(dt);
}
```

**B. Threshold-based snap:** If the correction is small (< 2 pixels), just snap. If it's large, lerp. Prevents visual jitter on tiny corrections.

---

## 3. Entity Interpolation

### What It Does

For entities the client does NOT own (other players, NPCs), the client receives server snapshots at the network tick rate. Rendering these directly produces stuttery movement. Instead, the client renders these entities *slightly in the past*, interpolating between the two most recent server snapshots.

### Architecture

```
Server sends snapshots at 60Hz. Client renders at 144Hz.

Client buffer (for remote entity):
  Snapshot[tick=98]: Position = (10, 0)
  Snapshot[tick=99]: Position = (11, 0)
  Snapshot[tick=100]: Position = (12, 0)  ← most recent from server

Render time = server_time - interpolation_delay (e.g., 2 ticks behind)

At render time between tick 98 and 99:
  t = 0.5 (halfway)
  Rendered position = Lerp((10,0), (11,0), 0.5) = (10.5, 0)
```

### Key Components

```csharp
/// <summary>
/// Stores recent authoritative snapshots for a remote entity, enabling
/// smooth interpolation between them during rendering.
/// </summary>
public class InterpolationBuffer<T> where T : unmanaged
{
    private readonly (uint tick, T state)[] _buffer;

    public void Push(uint tick, in T state);

    /// <summary>
    /// Sample the buffer at a given fractional tick, interpolating between
    /// the two surrounding snapshots.
    /// </summary>
    public T Sample(float renderTick);
}
```

```csharp
/// <summary>
/// Configuration for how far behind real-time the client renders
/// remote entities. Measured in ticks.
/// </summary>
public class InterpolationConfig
{
    /// <summary>
    /// How many ticks behind the latest server snapshot to render.
    /// Higher = smoother but more perceived latency. Default: 2.
    /// </summary>
    public uint InterpolationDelay { get; set; } = 2;
}
```

### Interpolation System (Client-Side Middleware)

```csharp
public class InterpolationSystem(INetworkWorld netWorld, InterpolationConfig config)
{
    [Render]
    public void Interpolate(GameTime dt, GameLoopDelegate next)
    {
        // Render time is slightly in the past
        float renderTick = netWorld.CurrentTick - config.InterpolationDelay + dt.Alpha;

        // For each remote networked entity, sample interpolated state
        world.Query(in _remoteEntityQuery,
            (ref Transform2D transform, ref InterpolationBuffer<Transform2D> buffer) =>
        {
            transform = buffer.Sample(renderTick);
        });

        next(dt);
    }
}
```

### Extrapolation (Fallback)

If a server snapshot is late (jitter, packet loss), the interpolation buffer runs dry. Options:

1. **Freeze** — hold the last known position. Simple, looks bad.
2. **Extrapolate** — continue the entity's last known velocity for a few ticks. Looks better but can overshoot.
3. **Adaptive delay** — dynamically increase `InterpolationDelay` when jitter is detected.

Recommended: extrapolate for up to 3 ticks, then freeze. The `InterpolationBuffer.Sample()` method handles this internally.

---

## 4. Server-Side Lag Compensation

### What It Does

When the server processes a hit-scan (e.g., a gunshot), the shooting player saw the game world *in the past* due to latency. The server needs to rewind the world to what the shooter saw at the time they fired, perform the hit check, then restore the present.

### Architecture

```
Timeline:
  Server tick 200 (now): Enemy is at position (50, 0)

  Client fired at tick 196 (received now, 4 ticks of latency):
    "I shot at tick 196, aiming at (45, 0)"

  Server rewinding:
    1. Look up enemy's Transform2D at tick 196 from snapshot history
    2. Enemy was at (45, 0) at tick 196 — HIT
    3. No actual world rollback needed — just a read from the snapshot buffer
```

### Key Interface

```csharp
public interface INetworkWorld
{
    // --- v1 (exists) ---
    uint CurrentTick { get; }
    void CaptureSnapshot();

    // --- v2: Lag Compensation ---

    /// <summary>
    /// Read a component's value at a specific past tick without modifying
    /// the current world state. Uses the snapshot ring buffer.
    /// </summary>
    T GetComponentAtTick<T>(Entity entity, uint tick) where T : unmanaged;

    /// <summary>
    /// Execute a callback with the world temporarily rewound to the given tick.
    /// All [Networked] components are restored, the callback runs, then the
    /// world is restored to the current tick. Used for physics queries (raycasts)
    /// that need the full world state, not just individual components.
    /// </summary>
    void WithWorldAtTick(uint tick, Action callback);
}
```

### Usage — Hit-Scan Weapon (Server System)

```csharp
public class CombatSystem(INetworkWorld netWorld, INetworkEventBus eventBus, PhysicsManager physics)
{
    [FixedUpdate]
    public void ProcessShots(GameTime dt, GameLoopDelegate next)
    {
        while (eventBus.On<FireWeaponMessage>(out var msg))
        {
            var shooterTick = msg.Data.Tick;
            var origin = msg.Data.Origin;
            var direction = msg.Data.Direction;

            // Simple case: check a single entity's position in the past
            var targetPos = netWorld.GetComponentAtTick<Transform2D>(targetEntity, shooterTick);
            if (HitCheck(origin, direction, targetPos)) { /* apply damage */ }

            // Complex case: full-world rewind for physics raycast
            netWorld.WithWorldAtTick(shooterTick, () =>
            {
                // Physics world is now at the state the shooter saw
                if (physics.Raycast(origin, direction, out var hit))
                {
                    // hit.Entity was where the shooter saw it
                    ApplyDamage(hit.Entity, msg.Data.Damage);
                }
            });
        }

        next(dt);
    }
}
```

### Limits & Safeguards

- **Maximum rewind window**: cap at e.g. 200ms (12 ticks at 60Hz). Don't let extreme-lag players rewind the world half a second.
- **Attacker advantage**: lag compensation inherently favors the shooter. This is the standard tradeoff (Counter-Strike, Overwatch, Valorant all do this).
- **Anti-cheat consideration**: validate that the claimed tick is plausible given the peer's measured RTT. Reject ticks that are suspiciously far in the past.

```csharp
public class LagCompensationConfig
{
    /// <summary>
    /// Maximum number of ticks the server will rewind for lag compensation.
    /// Requests beyond this are clamped. Default: 12 (200ms at 60Hz).
    /// </summary>
    public uint MaxRewindTicks { get; set; } = 12;
}
```

---

## How It All Fits Into the Middleware Pipeline

```
[Init]
  └─ NetworkSystem.Init          → start transport

[First]  (every frame)
  ├─ NetworkSystem.Poll          → receive packets, apply incoming state
  └─ ReconciliationSystem        → compare server snapshot vs prediction, rollback + resim if needed

[FixedUpdate]  (fixed timestep)
  ├─ PredictionSystem            → capture input, apply locally, send to server
  ├─ (game systems)              → physics, movement, combat, etc.
  ├─ CombatSystem (server)       → lag-compensated hit detection
  └─ NetworkSystem.Snapshot      → capture snapshot into ring buffer

[Update]  (variable timestep)
  └─ (game logic)

[Render]  (variable timestep)
  ├─ InterpolationSystem         → smooth remote entities between snapshots
  ├─ VisualSmoothingSystem       → lerp predicted entities toward sim position
  └─ (rendering)

[Last]
  └─ NetworkSystem.Send          → transmit state + events
```

### Client vs Server Registration

The middleware pipeline differs by role:

```csharp
// Server
game.UseIon()
    .UseNetworking()
    .UseSystem<CombatSystem>()       // lag-compensated hit detection
    .UseSystem<MyGameSystem>();

// Client
game.UseIon()
    .UseNetworking()
    .UseNetworkPrediction()          // registers PredictionSystem + ReconciliationSystem
    .UseNetworkInterpolation()       // registers InterpolationSystem
    .UseSystem<MyGameSystem>();
```

---

## Snapshot Ring Buffer — The Shared Foundation

All four features (prediction, reconciliation, interpolation, lag compensation) depend on the same data structure: a ring buffer of past world snapshots.

```csharp
public class SnapshotRingBuffer
{
    private readonly WorldSnapshot[] _snapshots;
    private readonly int _capacity;  // e.g., 128 ticks

    /// <summary>
    /// Store a deep copy of all [Networked] component arrays at the given tick.
    /// Since components are unmanaged, this is a bulk memcpy per archetype.
    /// </summary>
    public void Capture(uint tick, World world);

    /// <summary>
    /// Retrieve a specific component value for a specific entity at a past tick.
    /// O(1) lookup: tick → snapshot index, entity → archetype chunk offset.
    /// </summary>
    public ref readonly T Get<T>(uint tick, Entity entity) where T : unmanaged;

    /// <summary>
    /// Overwrite the current ECS world state with a past snapshot.
    /// Used for rollback.
    /// </summary>
    public void Restore(uint tick, World world);

    public uint OldestTick { get; }
    public uint NewestTick { get; }
}
```

Memory cost: for 128 ticks of history with 1000 entities averaging 64 bytes of networked component data each, that's ~8 MB. Manageable, and configurable via `NetworkConfig.SnapshotHistorySize`.

---

## Summary

| Feature | Runs on | Reads snapshots | Writes snapshots | Modifies ECS world |
|---|---|---|---|---|
| Prediction | Client | No | No (uses input buffer) | Yes (applies input locally) |
| Reconciliation | Client | Yes (compare server vs local) | No | Yes (rollback + resim) |
| Interpolation | Client | Yes (sample between two) | No | Yes (overwrite render state) |
| Lag compensation | Server | Yes (rewind query) | No | Temporarily (WithWorldAtTick) |

All four share the snapshot ring buffer from v1. The v1 investment in capturing snapshots every tick pays off directly as the foundation for all v2 features.
