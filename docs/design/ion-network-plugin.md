# Ion Network Plugin Design

## Context

Ion is a middleware-pipeline game engine built on .NET 8, using Arch ECS for entity management and Microsoft.Extensions.DependencyInjection for service wiring. Systems are registered as middleware with lifecycle attributes (`[Init]`, `[First]`, `[FixedUpdate]`, `[Update]`, `[Render]`, `[Last]`, `[Destroy]`). Events are allocation-free unmanaged structs stored in ring buffers.

The engine already has a precedent for wrapping external libraries: `PhysicsManager` wraps Aether Physics2D behind a service registered via DI, with `PhysicsSystem` driving it through the middleware pipeline. The network plugin follows this same pattern.

### Design Goals

- Wrap an established transport library (LiteNetLib) behind an Ion abstraction
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

## Approach: Replicated Components + Network Events (Hybrid)

Automatic component replication handles the common case (syncing `Transform2D`, `Health`, etc.), while explicit network events handle RPCs, commands, and anything that doesn't fit the snapshot model (chat messages, ability activations, match state transitions).

### Architecture

```
Ion.Extensions.Network.Abstractions   ← INetworkTransport, INetworkWorld, INetworkEventBus
Ion.Extensions.Network                ← Replication + event bus + NetworkSystem
Ion.Extensions.Network.LiteNetLib     ← Transport implementation
```

### Registration

```csharp
// Program.cs
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

**Channel 1: Automatic Component Replication**

Mark components with `[Networked]` and the plugin snapshots, serializes, and replicates their state automatically.

```csharp
[Networked]
public record struct Transform2D(Vector2 Position, float Rotation = 0);

[Networked]
public record struct Health(int Current, int Max);
```

**Channel 2: Explicit Network Events**

For discrete actions, RPCs, and anything that doesn't belong in persistent ECS state.

```csharp
public record struct ChatMessage(uint SenderId, FixedString64 Text);
public record struct AbilityActivated(uint CasterId, byte AbilitySlot, uint TargetId);
public record struct MatchStateChanged(byte NewState);

// In a system:
net.Broadcast(new ChatMessage(myId, text), DeliveryMethod.Reliable);
if (net.On<AbilityActivated>(out var msg)) { /* handle */ }
```

---

## Key Abstractions

### Transport Layer

Swappable transport behind a common interface:

```csharp
public interface INetworkTransport : IDisposable
{
    void Start(NetworkConfig config);
    void Poll();                           // drain incoming packets
    void Send(NetworkPeer peer, ReadOnlySpan<byte> data, DeliveryMethod method);
    void Broadcast(ReadOnlySpan<byte> data, DeliveryMethod method);
    event Action<NetworkPeer> OnPeerConnected;
    event Action<NetworkPeer, DisconnectReason> OnPeerDisconnected;
}
```

### Network Event Bus

Mirrors Ion's existing `IEventListener`/`IEventEmitter` pattern:

```csharp
public interface INetworkEventBus
{
    void Send<T>(NetworkPeer peer, T message, DeliveryMethod delivery = DeliveryMethod.Reliable)
        where T : unmanaged;
    void Broadcast<T>(T message, DeliveryMethod delivery = DeliveryMethod.Reliable)
        where T : unmanaged;
    bool On<T>() where T : unmanaged;
    bool On<T>(out NetworkMessage<T> message) where T : unmanaged;
}

public readonly record struct NetworkMessage<T>(NetworkPeer Sender, T Data) where T : unmanaged;
```

### Network World

Manages snapshot capture, incoming state application, and outgoing replication:

```csharp
public interface INetworkWorld
{
    uint CurrentTick { get; }
    void CaptureSnapshot();                    // copy current ECS state into ring buffer
    void ApplyIncoming();                      // apply received state to ECS
    void SendOutgoing();                       // diff against last-acked snapshot, serialize, send

    // v2: lag compensation & prediction
    ref T GetComponentAtTick<T>(Entity entity, uint tick) where T : unmanaged;
    void Rollback(uint toTick);
    void Resimulate(uint fromTick, uint toTick);
}
```

Because components are `record struct` (blittable), snapshots are just `memcpy` of component arrays — extremely fast.

---

## Serialization

**Raw blitting** via `MemoryMarshal.AsBytes()` for v1. Ion already constrains components to `unmanaged record struct`, so blitting is natural, zero-allocation, and zero-processing. Versioning only matters when shipping updates to a live game — not a v1 concern.

A clean `INetworkSerializer` interface keeps the door open for MemoryPack or similar in the future:

```csharp
public interface INetworkSerializer
{
    int Serialize<T>(in T value, Span<byte> buffer) where T : unmanaged;
    T Deserialize<T>(ReadOnlySpan<byte> buffer) where T : unmanaged;
}
```

---

## Network Identity

**Automatic.** The plugin assigns a `NetworkId` component to every entity that has any `[Networked]` component. Only the server (or the entity's owner) can create networked entities.

```csharp
public record struct NetworkId(uint Id, NetworkPeer Owner);
```

On the server, `NetworkId` is assigned at entity creation time. On the client, when a replicated entity arrives, the plugin creates the local entity with the matching `NetworkId`. Developers can reference `NetworkId.Id` in explicit network events (e.g., `AbilityActivated { TargetId = netId.Id }`).

---

## Authority Model

**Server-authoritative by default, with opt-in owner authority** per component via the `[Networked]` attribute:

```csharp
[Networked(Authority = NetworkAuthority.Server)]  // default — server writes, clients receive
public record struct Health(int Current, int Max);

[Networked(Authority = NetworkAuthority.Owner)]   // owning client writes, others receive
public record struct Transform2D(Vector2 Position, float Rotation = 0);
```

- `NetworkAuthority.Server` (default): Server is source of truth. Clients can predict locally, but server value wins on conflict.
- `NetworkAuthority.Owner`: The owning client drives this component. Server and other clients receive updates. Server can still reject/override via validation.

---

## NetworkSystem (Middleware)

```csharp
public class NetworkSystem(
    INetworkTransport transport,
    INetworkWorld netWorld,
    INetworkEventBus eventBus)
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

    [Destroy]
    public void Shutdown(GameTime dt, GameLoopDelegate next)
    {
        transport.Dispose();
        next(dt);
    }
}
```

---

## Transport: LiteNetLib

For v1, **LiteNetLib** is the transport implementation:

- **Mature & battle-tested** — used by Mirror (Unity's community networking), among others
- **Pure C#** — no native dependencies, works on all .NET platforms
- **UDP with reliability layers** — unreliable, reliable ordered, reliable unordered, sequenced
- **NAT punch-through** built in
- **MIT licensed**
- **Small API surface** — easy to wrap behind `INetworkTransport`

Steam Networking Sockets can be added later as `Ion.Extensions.Network.Steam` behind the same interface.

---

## v1 vs v2 Scope

**Generic core, action-game-ready architecture.** The snapshot ring buffer is included in v1 because it's the foundation that prediction, rollback, and lag compensation are built on — but those features ship in v2. See [ion-network-prediction-lagcomp.md](ion-network-prediction-lagcomp.md) for the v2 design.

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
