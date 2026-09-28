---
title: Messages and replication
description: Replicate ECS components with [Replicated], send typed [NetworkMessage] structs with INetworkMessages and NetworkReader, and understand the generated serializers and the ION201 to ION210 diagnostics.
sidebar:
  order: 11
---

Ion's multiplayer module moves game data in two ways, and most games use both:

| Mechanism | For | Declared with | Sent |
|---|---|---|---|
| **Replication** | Persistent state: positions, health, the score, which blocks exist | `[Replicated]` on an ECS component | Automatically, in delta snapshots every tick (or at `SendRate`) |
| **Messages** | Discrete things: inputs, "launch a ball", chat, "a block broke", match state changes | `[NetworkMessage]` on a struct | When you call `Send`, `Broadcast` or `SendToServer` |

Both are `unmanaged` structs whose serializers are written at compile time by the networking generator. The wire never
carries type names, reflection is never used, and NativeAOT publishes stay warning-free.

## Replicated components

Mark an ECS component with `[Replicated]` and every entity that has it becomes networked on the server:

```csharp
using Ion.Extensions.Networking;

[Replicated]                                          // server authority (the default)
public record struct Health(int Current, int Max);

[Replicated]
public record struct Scoreboard(int Score, int BallsLost, int Round);

[Replicated(Authority = Authority.Owner)]             // the owning client writes it
public record struct Aim(float Angle);

[Replicated(Authority = Authority.Owner), Predicted]  // simulated on both sides from the owner's inputs
public record struct PaddleControl(float X);

[Replicated, Interpolated]                            // blended between snapshots on clients
public record struct Position(Vector2 Value);
```

Then create entities on the server exactly as you would offline. There is nothing to register:

```csharp
if (session.IsServer)
{
    world.Create(new Scoreboard(0, 0, 1));                     // gets a server-owned NetworkId at capture
    world.Create(new Block(row, column), new Transform2D(position));
}
```

Clients receive a spawn record with the full components the first time, then only the members that changed, then a
despawn record when the entity is destroyed. On the client the entity is created in the root ECS world (see
[Entities and commands](/Ion/ecs/entities-and-commands/)), a `NetworkId` component is added, and a
`NetworkEntitySpawned` event is raised. Local-only components (sprites, audio handles) are added by client systems as
entities arrive, as Breakout Net does:

```csharp
private static readonly QueryDescription BallsWithoutSprite =
    new QueryDescription().WithAll<Ball, Transform2D>().WithNone<Sprite>();

[Update]
public void AddSprites(GameTime dt)
{
    if (world.CountEntities(in BallsWithoutSprite) > 0)
        world.Add(in BallsWithoutSprite, new Sprite(_ball, Field.BallSize, depth: 1));
}
```

### Authority

| `Authority` | Who writes | Use for |
|---|---|---|
| `Server` (default) | The server. A client's local edit lasts only until the server's value changes. | Almost everything. |
| `Owner` | The client that owns the entity (`NetworkId.OwnerPeer`). It sends its values; the server checks ownership and forwards them. For server-owned entities it is the same as `Server`. | Cosmetic, client-trusted state (aim direction, emotes). |
| `Owner` + `[Predicted]` | Both sides simulate it from the owner's inputs; the server's result wins. | Player movement. See [Prediction](/Ion/networking/multiplayer/prediction/). |

Owner authority means trusting the client. Anything that affects fairness should be server authority or predicted.

### Replicating a type you do not own

A component declared in another assembly (for example the ECS module's `Transform2D`) can be replicated with an
assembly attribute. The generator writes its serializer into your assembly; its serialized members must be public and
writable:

```csharp
[assembly: ReplicateComponent(typeof(Transform2D), Interpolated = true)]
```

`ReplicateComponent` has `Authority`, `Predicted` and `Interpolated` properties that mirror the attributes.

### Keeping entities local

Add `NetworkLocal` to an entity that has replicated components but should not be networked, for example level geometry
that every peer builds for itself:

```csharp
world.Create(new Wall(), new NetworkLocal(), new Transform2D(position), Collider2D.Box(size));
```

### What is sent

Each snapshot is encoded **per peer** against the tick that peer last acknowledged (every client packet header carries
its newest complete snapshot tick):

- **spawn records**: id, owner and the full components of a new entity,
- **despawn records**,
- per changed entity, **component operations**: full, delta (only changed members), or remove.

If the peer has no usable baseline (it just joined, or its acknowledged tick fell out of the ring), a full snapshot is
sent (`net_full_snapshots`). Snapshots are split into unreliable parts of at most `MtuBytes` (1,200); a snapshot that
would need more than 4,096 parts is not sent and the server logs an error once (replicate fewer entities, use an
interest policy, or raise `MtuBytes`). A client applies only the newest complete snapshot; late or partial ones are
dropped (`net_snapshots_lost`).

`SendRate` (snapshots per second per client) thins snapshots below the tick rate to save bandwidth; `0` sends one per
tick.

### Interest management

Set `INetworkWorld.InterestPolicy` to decide which entities each peer receives. The server asks it per peer and
snapshot; an entity that stops being relevant is despawned on that peer and spawned again when it becomes relevant.

```csharp
public sealed class NearbyOnly(float radius) : IInterestPolicy
{
    private Vector2 _center;

    public void BeginPeer(NetworkPeer peer, World world)
    {
        _center = FindPlayerPosition(peer, world);   // your lookup
    }

    public bool IsRelevant(NetworkPeer peer, Entity entity, in NetworkId id) =>
        id.OwnerPeer == peer.Id || Vector2.Distance(_center, PositionOf(entity)) < radius;
}

// Set it on the server before clients connect.
network.InterestPolicy = new NearbyOnly(800);
```

:::note[Planned]
No ready-made grid or spatial policy ships yet; the default (null) sends every entity to every peer.
:::

## Network messages

Declare a message as an `unmanaged` struct with `[NetworkMessage]`:

```csharp
[NetworkMessage(Direction = MessageDirection.ClientToServer, Delivery = Delivery.Unreliable)]
public record struct PaddleInput(float TargetX);

[NetworkMessage(Direction = MessageDirection.ClientToServer)]      // reliable ordered by default
public record struct LaunchBall();

[NetworkMessage(Delivery = Delivery.Unreliable)]                   // server to client by default
public record struct BlockBroken(Vector2 Position);

[NetworkMessage(Direction = MessageDirection.Both, Delivery = Delivery.ReliableOrdered)]
public record struct Chat(byte From, FixedString64 Text);
```

| Property | Default | Values |
|---|---|---|
| `Delivery` | `ReliableOrdered` | See below. |
| `Direction` | `ServerToClient` | `ServerToClient`, `ClientToServer`, `Both`. The server drops (and counts as a violation) a message a client may not send; sending one from the wrong side throws `InvalidOperationException`. |

### Delivery

| `Delivery` | Guarantee | Use for |
|---|---|---|
| `Unreliable` | May be lost or arrive out of order. Cheapest. | Inputs sent every tick, cosmetic events. |
| `Sequenced` | May be lost; never arrives after a newer message of the same channel. | Latest-value-wins updates. |
| `ReliableUnordered` | Always arrives (resent until acknowledged), in any order. | Independent commands. |
| `ReliableOrdered` | Always arrives, in the order sent. | Chat, match state, requests. |

`Send` has an overload that takes a `Delivery` to override the type's default for one call.

### Sending

Inject `INetworkMessages`:

| Method | On a server | On a client |
|---|---|---|
| `Send(peer, in message)` | To one client (to `NetworkPeer.Server`: delivered locally) | To the server |
| `Send(peer, in message, delivery)` | Same, with a delivery override | Same |
| `Broadcast(in message)` | To every connected client | To the server |
| `SendToServer(in message)` | Delivered locally (listen servers) | To the server |

```csharp
public sealed class PlayerInputSystem(IInputState input, INetworkSession session, INetworkMessages messages)
{
    [Update]
    public void Read(GameTime dt)
    {
        if (session.State != NetworkState.Connected) return;
        if (input.Pressed(MouseButton.Left)) messages.SendToServer(new LaunchBall());
    }
}
```

Messages are packed per peer and delivery into MTU-sized packets and sent in `Last` (`StageOrder.NetworkSend`). Sending
while the session is not connected does nothing. A single message larger than one packet is dropped with a warning.

### Reading

Create a `NetworkReader<T>` **once**, in the constructor or a field initializer, and keep it in a mutable (not
`readonly`) field. Each reader is its own cursor: it sees each message exactly once.

```csharp
public sealed class LaunchSystem(INetworkSession session, INetworkMessages messages, World world)
{
    private NetworkReader<LaunchBall> _launches = messages.Reader<LaunchBall>();

    [FixedUpdate]
    public void Play(GameTime dt)
    {
        if (!session.IsServer)
        {
            _launches.Skip();      // clients never receive it; keep the cursor current anyway
            return;
        }

        while (_launches.TryRead(out var from, out _))
            SpawnBallFor(from);
    }
}
```

| `NetworkReader<T>` member | Meaning |
|---|---|
| `TryRead(out NetworkPeer from, out T message)` | The oldest unread message and its sender. |
| `TryRead(out NetworkMessageReceived<T> message)` | The same with the sender's tick. |
| `Read()` | Every unread message as a span, oldest first (valid until the end of the frame). |
| `Count`, `Any()` | Unread messages. |
| `Skip()` | Mark everything visible as read. |

Inbound messages are decoded in `First`, before any game step, and are visible for that frame and the next, including
in the frame's fixed steps, exactly like [events](/Ion/concepts/events/). Messages are never boxed.

### Messages on the event bus

Every inbound message is also emitted on `IEvents` as `NetworkMessageReceived<T>(From, Tick, Message)`.
`NetworkReader<T>` is a thin wrapper over that event channel, so everything that works with events works with messages:
coroutines can wait for them ([Coroutines](/Ion/ecs/coroutines/)), and tests can collect them with
`IonTestHost.Collect<T>()`:

```csharp
var chats = client.Collect<NetworkMessageReceived<Chat>>();
server.Step(); client.Step();
Assert.Contains(chats, m => m.Message.Text == "hi");
```

## Supported member types

Replicated components and messages must be `unmanaged` and every serialized member must be one of:

- primitives (`bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `char`),
- enums,
- `System.Numerics` vectors (`Vector2`, `Vector3`, `Vector4`) and `Quaternion`,
- `FixedString32`, `FixedString64`, `FixedString128` (inline UTF-8 text with a length byte, 31, 63 and 127 bytes),
- `NetworkId` (to refer to another networked entity),
- structs of those (nested), up to 64 serialized members per struct.

Members marked `[NetworkIgnore]` are not sent and keep their previous value on the receiver. Serialized members must be
writable from your assembly.

```csharp
[Replicated]
public record struct Profile(
    FixedString32 Name,
    Team Team,
    Stats Stats,                               // a nested struct
    Quaternion Facing,
    double Score,
    [property: NetworkIgnore] int LocalOnly);  // not sent
```

`FixedString32/64/128` convert implicitly from `string` (text longer than the capacity is truncated at a UTF-8
boundary) and `ToString()` decodes them.

:::caution[Entity handles are not portable]
An `Entity` is an index into one process's world and means nothing on another peer. The generator warns (`ION210`) and
does not send it. Store a `NetworkId` and resolve it on the receiver with `INetworkWorld.TryGetEntity(id, out entity)`.
:::

## The generated code and the registry

For every `[Replicated]` struct, every `[assembly: ReplicateComponent]` type and every `[NetworkMessage]` struct, the
generator writes a `NetSerializer<T>` with:

- a **full** form,
- a field-wise **delta** form: a varint change mask with one bit per serialized member, then the changed members,
- a **bitwise comparison** (floats compared by their bits) used to detect changes,
- for `[Interpolated]` components, a **blend**: linear for floats, doubles and vectors, spherical for quaternions,
  switch at the midpoint otherwise.

A module initializer registers each assembly's types with `NetworkRegistry` (and calls the registrations of referenced
assemblies, so types from a shared game library are included). The registry sorts components and messages by full name
to assign wire ordinals, so client and server builds of the same code agree. Its FNV-1a 64 hash covers the protocol
version and each type's name, layout, authority, prediction, interpolation, delivery and direction; clients with a
different hash are refused (`RegistryMismatch`).

:::tip[NativeAOT]
Under NativeAOT, Arch needs every stored component type registered up front. The networking generator registers the
replicated ones; register your other components yourself (`EcsComponents.Register<Wall>();`). See
[Native AOT](/Ion/platforms/native-aot/).
:::

## Diagnostics

The networking generator reports these under the category `Ion.Networking`:

| Id | Severity | When |
|---|---|---|
| `ION201` | Error | A replicated component or message is not `unmanaged` (it holds a reference). Use `FixedString32/64/128` for text. |
| `ION202` | Error | `[Predicted]` without `Authority = Authority.Owner`. |
| `ION203` | Error | A replicated component serializes to more than 1,024 bytes (including per-entity overhead). |
| `ION204` | Warning | A `[Replicated]` type is never used as an ECS component in the project (`World.Create/Add/Set/Get`, `Commands`, `[Query]` parameters), so it will never be replicated. |
| `ION205` | Error | A member cannot be serialized: unsupported type, generic struct, fixed-size buffer, not writable, nested too deeply, or more than 64 members. |
| `ION206` | Warning | A `NetworkReader<T>` is created inside a stage method: it would restart from the oldest visible message on every call. Create it once. |
| `ION207` | Warning | A message is sent but never read (no reader or prediction registration). Executables only. |
| `ION208` | Warning | A message is read but never sent. Executables only. |
| `ION209` | Error | `[Predicted]` or `[Interpolated]` without `[Replicated]`. |
| `ION210` | Warning | A member is an `Entity`: it is not sent. Use `NetworkId`. |

```csharp
// ION201: string is a reference
[NetworkMessage] public record struct Chat(string Text);          // use FixedString64

// ION202: predicted needs owner authority
[Replicated, Predicted] public record struct Move(Vector2 V);     // [Replicated(Authority = Authority.Owner), Predicted]

// ION206: reader created per call
[Update] public void U(GameTime dt) { var r = messages.Reader<Chat>(); }   // move to a field initializer
```

ION207 and ION208 only run in executables because a library cannot see the whole game. Methods that send or read on
your behalf can be annotated with `[SendsNetworkMessage]` and `[ReadsNetworkMessage]` so their call sites count (the
prediction API does this).

## See also

- [Multiplayer overview](/Ion/networking/multiplayer/overview/)
- [Prediction](/Ion/networking/multiplayer/prediction/) and [Interpolation](/Ion/networking/multiplayer/interpolation/)
- [Components and serialization](/Ion/ecs/components-and-serialization/)
- [Diagnostics reference](/Ion/reference/diagnostics/)
- Source: [Attributes.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Networking.Abstractions/Attributes.cs),
  [INetworkMessages.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Networking.Abstractions/INetworkMessages.cs)
