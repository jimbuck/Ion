# Ion Network Plugin Design

## Context

Ion is a middleware-pipeline game engine built on .NET 8, using Arch ECS for entity management and Microsoft.Extensions.DependencyInjection for service wiring. Systems are registered as middleware with lifecycle attributes (`[Init]`, `[First]`, `[FixedUpdate]`, `[Update]`, `[Render]`, `[Last]`, `[Destroy]`). Events are allocation-free unmanaged structs stored in ring buffers.

The engine already has a precedent for wrapping external libraries: `PhysicsManager` wraps Aether Physics2D behind a service registered via DI, with `PhysicsSystem` driving it through the middleware pipeline. The network plugin should follow this same pattern.

### Design Goals

- Wrap an established transport library (LiteNetLib, Riptide, or Steam Networking) behind an Ion abstraction
- Integrate naturally with the ECS and middleware pipeline
- Make lag compensation and client-side prediction *possible* in the architecture without requiring them in v1
- Support authoritative client-server topology (peer-to-peer can come later)
- Enable headless dedicated servers via the existing `Ion.Extensions.Graphics.Null` backend

### Constraints

- Ion components are `record struct` (unmanaged, copyable, no heap references)
- Ion events are `where T : unmanaged` — no strings or reference types
- The `FixedUpdate` loop runs at a configurable fixed timestep, ideal for deterministic networking
- No existing serialization framework — one must be chosen or built

---

## Option 1: Replicated World (Component-Driven)

**Concept:** Mark entities and components as networked. The plugin automatically snapshots, diffs, serializes, and replicates component state between server and clients. The developer thinks in terms of ECS — they create entities and mutate components, and the network layer handles the rest.

### Architecture

```
Ion.Extensions.Network.Abstractions   ← Interfaces (INetworkTransport, INetworkWorld, etc.)
Ion.Extensions.Network                ← Core replication logic, snapshot system
Ion.Extensions.Network.LiteNetLib     ← LiteNetLib transport implementation
Ion.Extensions.Network.Riptide        ← (future) Riptide transport implementation
```

### Registration

```csharp
// Program.cs
builder.Services.AddIonNetworking(builder.Configuration, net => {
    net.TickRate = 60;          // network tick rate (can differ from physics)
    net.Mode = NetworkMode.Server; // or .Client
});
builder.Services.AddLiteNetLibTransport(); // swappable transport

var game = builder.Build();
game.UseIon()
    .UseNetworking()             // registers NetworkSystem into middleware
    .UseSystem<MyGameSystem>();
```

### Key Abstractions

```csharp
// Transport layer — swappable
public interface INetworkTransport : IDisposable
{
    void Start(NetworkConfig config);
    void Poll();                           // drain incoming packets
    void Send(NetworkPeer peer, ReadOnlySpan<byte> data, DeliveryMethod method);
    void Broadcast(ReadOnlySpan<byte> data, DeliveryMethod method);
    event Action<NetworkPeer> OnPeerConnected;
    event Action<NetworkPeer, DisconnectReason> OnPeerDisconnected;
}

// Replication marker — applied to component types
[AttributeUsage(AttributeTargets.Struct)]
public class NetworkedAttribute : Attribute
{
    public SyncDirection Direction { get; init; } = SyncDirection.ServerToClient;
    public DeliveryMethod Delivery { get; init; } = DeliveryMethod.Unreliable;
}

// Usage on components
[Networked]
public record struct Transform2D(Vector2 Position, float Rotation = 0);

[Networked(Direction = SyncDirection.ClientToServer, Delivery = DeliveryMethod.Reliable)]
public record struct PlayerInput(Vector2 Move, bool Fire);
```

### NetworkSystem (Middleware)

```csharp
public class NetworkSystem(INetworkTransport transport, INetworkWorld netWorld)
{
    [Init]
    public void Init(GameTime dt, GameLoopDelegate next)
    {
        transport.Start(config);
        next(dt);
    }

    [First]
    public void PollNetwork(GameTime dt, GameLoopDelegate next)
    {
        transport.Poll();           // receive packets
        netWorld.ApplyIncoming();   // deserialize & apply to ECS world
        next(dt);
    }

    [FixedUpdate]
    public void FixedUpdate(GameTime dt, GameLoopDelegate next)
    {
        next(dt);                   // let game systems run
        netWorld.CaptureSnapshot(); // snapshot networked components
    }

    [Last]
    public void SendState(GameTime dt, GameLoopDelegate next)
    {
        next(dt);
        netWorld.SendOutgoing();    // diff, serialize, transmit
    }

    [Destroy]
    public void Shutdown(GameTime dt, GameLoopDelegate next)
    {
        transport.Dispose();
        next(dt);
    }
}
```

### Snapshot & Diff System (enables lag compensation later)

```csharp
// Stores N frames of component state for all networked entities
public interface INetworkWorld
{
    void CaptureSnapshot();                    // copy current ECS state into ring buffer
    void ApplyIncoming();                      // apply received state to ECS
    void SendOutgoing();                       // diff against last-acked snapshot, serialize, send

    // Future: lag compensation
    ref T GetComponentAtTick<T>(Entity entity, uint tick) where T : unmanaged;
    void Rollback(uint toTick);
    void Resimulate(uint fromTick, uint toTick);
}
```

Because components are `record struct` (blittable), snapshots are just `memcpy` of component arrays — extremely fast.

### Serialization

Since components are unmanaged, they can be serialized by blitting their memory directly:

```csharp
// For a known component type T where T : unmanaged
Span<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref component, 1));
```

Delta compression compares current vs last-acknowledged snapshot byte-by-byte and only sends changed regions.

### Why This Works for Prediction & Lag Compensation

- **Snapshot history** is built into the architecture from day one
- **Rollback:** restore ECS world to a previous tick's snapshot, re-run `FixedUpdate` N times
- **Client-side prediction:** client runs `FixedUpdate` locally on inputs, server corrects via snapshots
- **Server-side lag compensation:** `GetComponentAtTick()` lets the server query where an entity *was* at the time the client fired

### Tradeoffs

| Pros | Cons |
|------|------|
| Developer doesn't manually send/receive — just write ECS code | Magic: harder to debug what's going over the wire |
| Snapshot architecture naturally enables rollback/prediction | Requires reflection or source generators to discover `[Networked]` components |
| Blittable components make serialization trivial and fast | All networked data must be unmanaged (no strings in components) |
| Follows the PhysicsManager precedent closely | Higher initial implementation complexity |

---

## Option 2: Network Events (Explicit Message-Passing)

**Concept:** Extend Ion's existing event system to work across the network. The developer defines network message types as `record struct` and explicitly sends/receives them. No automatic replication — full control over what crosses the wire.

### Architecture

```
Ion.Extensions.Network.Abstractions   ← INetworkTransport, INetworkEventBus
Ion.Extensions.Network                ← NetworkSystem, serialization
Ion.Extensions.Network.LiteNetLib     ← Transport implementation
```

### Registration

```csharp
builder.Services.AddIonNetworking(builder.Configuration);
builder.Services.AddLiteNetLibTransport();

var game = builder.Build();
game.UseIon()
    .UseNetworking()
    .UseSystem<MyGameSystem>();
```

### Key Abstractions

```csharp
// Network-aware event bus — mirrors IEventListener/IEventEmitter pattern
public interface INetworkEventBus
{
    // Send to specific peer
    void Send<T>(NetworkPeer peer, T message, DeliveryMethod delivery = DeliveryMethod.Reliable)
        where T : unmanaged;

    // Send to all peers
    void Broadcast<T>(T message, DeliveryMethod delivery = DeliveryMethod.Reliable)
        where T : unmanaged;

    // Check for received messages (same API as Ion events)
    bool On<T>() where T : unmanaged;
    bool On<T>(out NetworkMessage<T> message) where T : unmanaged;
}

public readonly record struct NetworkMessage<T>(NetworkPeer Sender, T Data) where T : unmanaged;
```

### Usage in Game Systems

```csharp
// Shared message definitions
public record struct PlayerInputMessage(uint Tick, Vector2 Move, bool Fire);
public record struct EntitySpawnMessage(uint EntityId, Vector2 Position);
public record struct EntityStateMessage(uint EntityId, Vector2 Position, float Rotation);

// Server system
public class ServerGameSystem(INetworkEventBus net, World world)
{
    [FixedUpdate]
    public void ProcessInputs(GameTime dt, GameLoopDelegate next)
    {
        // Process client inputs
        while (net.On<PlayerInputMessage>(out var msg))
        {
            ApplyInput(msg.Sender, msg.Data);
        }

        next(dt); // run game simulation

        // Send state to clients
        world.Query(in _syncQuery, (Entity entity, ref Transform2D t, ref NetworkId netId) =>
        {
            net.Broadcast(new EntityStateMessage(netId.Id, t.Position, t.Rotation));
        });
    }
}

// Client system
public class ClientGameSystem(INetworkEventBus net, World world, IInputState input)
{
    [FixedUpdate]
    public void Update(GameTime dt, GameLoopDelegate next)
    {
        // Send input to server
        net.Send(serverPeer, new PlayerInputMessage(tick, input.MoveAxis, input.FirePressed));

        next(dt);

        // Apply server state
        while (net.On<EntityStateMessage>(out var msg))
        {
            // Find entity by network ID, update transform
        }
    }
}
```

### NetworkSystem (Middleware)

```csharp
public class NetworkSystem(INetworkTransport transport, INetworkEventBus eventBus)
{
    [Init]
    public void Init(GameTime dt, GameLoopDelegate next)
    {
        transport.Start(config);
        next(dt);
    }

    [First]
    public void Poll(GameTime dt, GameLoopDelegate next)
    {
        transport.Poll();           // receive packets
        eventBus.ProcessIncoming(); // deserialize into typed message queues
        next(dt);
    }

    [Last]
    public void Flush(GameTime dt, GameLoopDelegate next)
    {
        next(dt);
        eventBus.FlushOutgoing();   // serialize and send queued messages
    }
}
```

### Why This Works for Prediction & Lag Compensation

- **Client-side prediction:** client keeps a buffer of sent inputs, applies them locally, reconciles when server state arrives
- **Lag compensation:** server stores received state history manually, can rewind and check
- **Input buffer:** developer manages their own input queue with tick numbers — full control over timing

These features are possible but require more game-side code since the plugin doesn't manage snapshots automatically.

### Tradeoffs

| Pros | Cons |
|------|------|
| Familiar event-based API, mirrors existing `IEventListener` | Developer manually writes all send/receive logic |
| Easy to debug — you see exactly what messages cross the wire | More boilerplate for common patterns (entity sync) |
| No source generators or reflection needed | Prediction/lag comp require building your own snapshot system |
| Simple to implement in v1 | Risk of inconsistency if developer forgets to sync something |
| Works well with Ion's existing event philosophy | |

---

## Option 3: Hybrid — Replicated Components + Network Events

**Concept:** Combine Options 1 and 2. Automatic component replication handles the common case (syncing `Transform2D`, `Health`, etc.), while explicit network events handle RPCs, commands, and anything that doesn't fit the snapshot model (chat messages, ability activations, match state transitions).

### Architecture

```
Ion.Extensions.Network.Abstractions   ← INetworkTransport, INetworkWorld, INetworkEventBus
Ion.Extensions.Network                ← Replication + event bus + NetworkSystem
Ion.Extensions.Network.LiteNetLib     ← Transport implementation
```

### Registration

```csharp
builder.Services.AddIonNetworking(builder.Configuration, net => {
    net.Mode = NetworkMode.Server;
    net.SnapshotHistorySize = 128;  // for lag compensation
});
builder.Services.AddLiteNetLibTransport();

var game = builder.Build();
game.UseIon()
    .UseNetworking()
    .UseSystem<MyGameSystem>();
```

### Two Channels

**Channel 1: Automatic Component Replication** (same as Option 1)
```csharp
[Networked]
public record struct Transform2D(Vector2 Position, float Rotation = 0);

[Networked(Direction = SyncDirection.ClientToServer)]
public record struct PlayerInput(Vector2 Move, bool Fire);
```

**Channel 2: Explicit Network Events** (same as Option 2)
```csharp
// For things that don't fit the ECS model
public record struct ChatMessage(uint SenderId, FixedString64 Text);
public record struct AbilityActivated(uint CasterId, byte AbilitySlot, uint TargetId);
public record struct MatchStateChanged(byte NewState);

// In a system:
net.Broadcast(new ChatMessage(myId, text), DeliveryMethod.Reliable);
if (net.On<AbilityActivated>(out var msg)) { /* handle */ }
```

### NetworkSystem (Middleware)

```csharp
public class NetworkSystem(
    INetworkTransport transport,
    INetworkWorld netWorld,
    INetworkEventBus eventBus)
{
    [First]
    public void Poll(GameTime dt, GameLoopDelegate next)
    {
        transport.Poll();
        netWorld.ApplyIncoming();       // apply replicated component state
        eventBus.ProcessIncoming();     // queue up network events
        next(dt);
    }

    [FixedUpdate]
    public void Tick(GameTime dt, GameLoopDelegate next)
    {
        next(dt);                       // game systems run
        netWorld.CaptureSnapshot();     // snapshot for replication & rollback
    }

    [Last]
    public void Send(GameTime dt, GameLoopDelegate next)
    {
        next(dt);
        netWorld.SendOutgoing();        // replicate component state
        eventBus.FlushOutgoing();       // send explicit messages
    }
}
```

### Why This Is the Most Flexible for Prediction & Lag Compensation

Inherits all the prediction/rollback capabilities of Option 1 (snapshot history, `Rollback()`, `Resimulate()`), plus the explicit event channel lets you handle things like:
- Input commands with tick stamping (for server reconciliation)
- One-shot events that don't belong in ECS state (explosions, sound cues)
- Lobby/matchmaking messages outside of gameplay

### Tradeoffs

| Pros | Cons |
|------|------|
| Best of both worlds: automatic sync + explicit control | Largest API surface — two mental models to learn |
| Covers all use cases without workarounds | Most complex to implement |
| Prediction/rollback built into the core | Need clear guidance on when to use which channel |
| Clean separation: continuous state (replication) vs discrete events (messages) | |

---

## Comparison Matrix

| Concern | Option 1: Replicated World | Option 2: Network Events | Option 3: Hybrid |
|---|---|---|---|
| **ECS integration** | Deep — automatic | Shallow — manual | Deep + manual escape hatch |
| **Developer effort per entity** | Low (add attribute) | High (write send/recv) | Low for state, explicit for events |
| **Debugging** | Harder (implicit) | Easiest (explicit) | Medium |
| **v1 implementation effort** | Medium-High | Low-Medium | High |
| **Prediction/rollback path** | Built-in | DIY | Built-in |
| **Lag compensation path** | Built-in (snapshot history) | DIY | Built-in |
| **Transport agnostic** | Yes | Yes | Yes |
| **Headless server** | Yes (NullGraphics) | Yes | Yes |

---

## Recommended Transport: LiteNetLib

All three options use the same `INetworkTransport` abstraction, so the transport is swappable. For v1, **LiteNetLib** is recommended:

- **Mature & battle-tested** — used by Mirror (Unity's community networking), among others
- **Pure C#** — no native dependencies, works on all .NET platforms
- **UDP with reliability layers** — unreliable, reliable ordered, reliable unordered, sequenced
- **NAT punch-through** built in
- **MIT licensed**
- **Small API surface** — easy to wrap behind `INetworkTransport`

Steam Networking Sockets could be added later as `Ion.Extensions.Network.Steam` behind the same interface.

---

## Chosen Approach: Option 3 (Hybrid)

### Decided: Serialization Format

**Raw blitting** via `MemoryMarshal.AsBytes()` for v1. Ion already constrains components to `unmanaged record struct`, so blitting is natural, zero-allocation, and zero-processing. Versioning only matters when shipping updates to a live game — not a v1 concern.

A clean `INetworkSerializer` interface keeps the door open for MemoryPack or similar in the future:

```csharp
public interface INetworkSerializer
{
    int Serialize<T>(in T value, Span<byte> buffer) where T : unmanaged;
    T Deserialize<T>(ReadOnlySpan<byte> buffer) where T : unmanaged;
}
```

### Decided: Network Identity

**Automatic.** The plugin assigns a `NetworkId` component to every entity that has any `[Networked]` component. Only the server (or the entity's owner) can create networked entities.

```csharp
public record struct NetworkId(uint Id, NetworkPeer Owner);
```

On the server, `NetworkId` is assigned at entity creation time. On the client, when a replicated entity arrives, the plugin creates the local entity with the matching `NetworkId`. Developers can reference `NetworkId.Id` in explicit network events (e.g., `AbilityActivated { TargetId = netId.Id }`).

### Decided: Authority Model

**Server-authoritative by default, with opt-in owner authority** per component via the `[Networked]` attribute:

```csharp
[Networked(Authority = NetworkAuthority.Server)]  // default — server writes, clients receive
public record struct Health(int Current, int Max);

[Networked(Authority = NetworkAuthority.Owner)]   // owning client writes, others receive
public record struct Transform2D(Vector2 Position, float Rotation = 0);
```

- `NetworkAuthority.Server` (default): Server is source of truth. Clients can predict locally, but server value wins on conflict.
- `NetworkAuthority.Owner`: The owning client drives this component. Server and other clients receive updates. Server can still reject/override via validation.

### Decided: v1 Scope

**Generic core, action-game-ready architecture.** The snapshot ring buffer is included in v1 because it's the foundation that prediction, rollback, and lag compensation are built on — but those features ship in v2.

| In v1 | In v2 (architecture supports from day one) |
|---|---|
| `INetworkTransport` + LiteNetLib impl | Steam Networking transport |
| `NetworkSystem` middleware (poll/send) | NAT punch-through helpers |
| `[Networked]` component replication | Delta compression (v1 sends full snapshots) |
| `INetworkEventBus` for explicit messages | Client-side prediction / reconciliation |
| `NetworkId` auto-assignment | Rollback / resimulate API |
| Server + Client mode | Lag compensation queries (`GetComponentAtTick`) |
| Snapshot capture (ring buffer) | Peer-to-peer topology |
| Connection / disconnection events | Lobby / matchmaking |
| `INetworkSerializer` (raw blit impl) | MemoryPack serializer option |
