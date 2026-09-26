# Ion Networking: Design (v0.3 revision)

This document replaces the two drafts from the `claude/ion-network-plugin-design-0f2Bb` branch (`ion-network-plugin.md` and `ion-network-prediction-lagcomp.md`). It keeps what those drafts got right and rewrites everything that the engine has since changed underneath: the stage model, the event bus, source generation, the deterministic loop, headless mode, and the security posture of anything that listens on a socket. It is the design that Stage 6b of `docs/plans/2026-09-engine-review-and-roadmap.md` implements.

## 1. What is kept from the original design

- **Hybrid model.** Automatic replication of marked components for persistent state, explicit typed messages for everything discrete (inputs, RPC-style commands, chat, match state). Both drafts were right that one mechanism alone does not fit.
- **The snapshot ring buffer is the foundation.** Capturing every fixed tick into a ring of past states is what prediction, reconciliation, interpolation and lag compensation all read from. It ships in v1 even though those features are v2.
- **Authority.** Server-authoritative by default, owner authority opt-in per component, the server always able to validate or override.
- **Automatic `NetworkId`.** Every entity with a replicated component gets one; only the server (or the owner, for owner-authority spawns) creates networked entities.
- **Transport behind an interface**, with a pure managed UDP transport first and Steam or others later.
- **The four v2 techniques** (prediction, reconciliation, interpolation, lag compensation) and their safeguards: `MaxRewindTicks`, plausibility checks against measured RTT, visual smoothing separate from simulation state, extrapolate briefly then freeze, adaptive interpolation delay.
- **Fixed-tick determinism** as the basis of everything: every packet carries the tick it belongs to.

## 2. What changed in the engine since the drafts, and what that changes here

| Engine change (now merged) | Effect on the networking design |
|---|---|
| Stage model is ordered leaf steps with `Order`, `After<T>`/`Before<T>` and explicit `Begin`/`End` scopes; `next(dt)` is legacy | The draft's `NetworkSystem` used `next()` to run "after the game systems". It becomes leaf steps in the engine order bands: poll in `First` at `StageOrder.Network` (setup band), snapshot capture as an `End` scope on `FixedUpdate` (runs after every user fixed step, in a `finally`), send in `Last` in the teardown band. Users never see middleware. |
| Typed, source-generated event bus (`IEvents`, `EventReader<T>`, compile-time event ids, `ION10x` diagnostics) | The draft's `INetworkEventBus` mirrored the old `On<T>()` listener API. It now mirrors `IEvents`: `INetworkMessages.Send<T>`/`Broadcast<T>` and `NetworkReader<T>` readers created once in constructors. Message type ids are assigned by the generator (stable across the client and server builds of the same game), so the wire format never carries type names. |
| Schedule generator with interceptors, `[StackTraceHidden]`, zero-reflection dispatch, zero Ion AOT warnings | Serialization is generated too: `[Replicated]` components and message structs get generated blit/delta serializers and registries. No `MemoryMarshal.AsBytes` over unknown layouts at runtime, no reflection, no `NetPacketProcessor`. NativeAOT publishes stay warning-free. |
| `IClock` with `FixedStepClock`, `GameLoop.RunFrames`, `IonTestHost`, deterministic seeded samples, fixed-step input and event backlog | A `LoopbackTransport` runs a server and N clients in one process under `IonTestHost` with simulated latency, jitter and loss, stepping all of them deterministically. Network tests are ordinary xUnit tests. Fixed-step input delivery (each edge seen by exactly one fixed step) is what makes "one input message per tick" exact. |
| Headless mode (`Ion:Headless`, null graphics and audio, autopilot samples) | A dedicated server is the same game run with `--Ion:Headless=true --Ion:Network:Mode=Server`. No separate server project. |
| ECS pick is Arch 2.1 (`Ion.Extensions.Ecs`, `[Query]` chunk loops, `Commands` flushed per stage) | Snapshot capture is a chunk-level copy of the replicated component arrays per archetype (memcpy), and restore is the reverse. Structural changes from replication (spawn/despawn) go through `Commands` at stage boundaries, never mid-query. |
| Remote protocol security design (loopback bind, tokens, read/mutate scopes, compiled out of release unless opted in) | The transport gets the same treatment: explicit bind address, connection tokens or a join secret, per-message authority checks, rate limits, and no listening socket unless configured. A networking module that accepts input from the internet is a security boundary and is designed as one. |
| .NET 10, `net10.0-browser` kept as an option | A WebSocket transport (browser client, relay servers) is a first-class second transport, not an afterthought; the message layer is transport-agnostic. |

## 3. Packages

```
Ion.Extensions.Networking.Abstractions   INetworkTransport, INetworkMessages, NetworkReader<T>, INetworkWorld, attributes, config, NetworkPeer, DeliveryMethod
Ion.Extensions.Networking                replication, snapshot ring, message bus, NetworkSystem steps, prediction/interpolation/lag-comp (v2), LoopbackTransport
Ion.Extensions.Networking.LiteNetLib     UDP transport (pure managed, reliability channels, NAT punch-through)
Ion.Extensions.Networking.WebSockets     WebSocket transport (browser clients, relays); server side on the embedded web server module, client side on ClientWebSocket
Ion.Generators                           [Replicated] and [NetworkMessage] serializers, message ids, replication registry, diagnostics ION2xx
```

## 4. Registration and configuration

```csharp
var builder = IonApplication.CreateBuilder(args);
builder.Services.AddIon(builder.Configuration);
builder.Services.AddNetworking(builder.Configuration);       // binds Ion:Network, registers the message bus, world, snapshot ring
builder.Services.AddLiteNetLibTransport();                    // or AddWebSocketTransport(), AddLoopbackTransport()

var game = builder.Build();
game.UseIon().UseNetworking();                                // adds the network steps; role comes from config
game.UseSystem<PaddleSystem>();
game.Run();
```

`Ion:Network` (`NetworkConfig`): `Mode` (`Offline`, `Client`, `Server`, `ListenServer`), `Bind` (server address, default `127.0.0.1`; a non-loopback bind is logged), `Port`, `Connect` (client), `TickRate` (defaults to `GameConfig.FixedUpdateRate`), `SnapshotHistory` (ticks, default 128), `SendRate` (snapshots per second, default = tick rate), `MaxPeers`, `JoinSecret` (required to connect when set), `MaxRewindTicks` (12), `InterpolationDelay` (2 ticks), `MtuBytes`, `Simulate` (`Latency`, `Jitter`, `Loss` for the loopback and for local testing of real transports).

`--Ion:Headless=true --Ion:Network:Mode=Server` is a dedicated server. `ion run --server` (Stage 6 CLI) is sugar for that.

## 5. Replicated components

```csharp
[Replicated]                                   // server authority (default)
public record struct Health(int Current, int Max);

[Replicated(Authority = Authority.Owner)]      // the owning client writes, everyone else receives
public record struct Transform2D(Vector2 Position, float Rotation = 0);

[Replicated(Authority = Authority.Owner), Predicted]   // v2: applied locally before the server confirms
public record struct Velocity(Vector2 Value);
```

Rules enforced by the generator (diagnostics `ION201..`): a replicated component must be `unmanaged` (ION201); no reference fields; strings are `FixedString32/64/128` (inline UTF-8 with a length byte, provided by the abstractions); an `[Predicted]` component must also be `Owner`-authority (ION202); a component larger than the MTU budget for one entity is an error (ION203); `[Replicated]` on a type not used as an ECS component is a warning (ION204).

The generator emits, per game: a `ReplicationRegistry` with a stable ordinal per replicated type (sorted by full name, so client and server builds agree), a blit serializer and a field-wise delta serializer per type (each field a bit in a change mask, only changed fields written), and the archetype capture/restore code for the snapshot ring. The ordinal table is hashed and exchanged in the handshake; a mismatch rejects the connection with a clear reason.

`NetworkId(uint Id, byte OwnerPeer)` is an engine component added automatically. Entities are created on the server through `Commands`; the replication step observes new and destroyed `NetworkId` entities and sends spawn and despawn records.

## 6. Messages

```csharp
[NetworkMessage(Delivery = Delivery.ReliableOrdered)]
public record struct ChatMessage(FixedString64 Text);

[NetworkMessage(Delivery = Delivery.Unreliable)]
public record struct PlayerInput(uint Tick, Vector2 Move, bool Fire);

public sealed class ChatSystem(INetworkMessages net)
{
    private NetworkReader<ChatMessage> _chat = net.Reader<ChatMessage>();   // once, in the constructor (ION103 otherwise)

    [Update]
    public void Update(GameTime dt)
    {
        while (_chat.TryRead(out var from, out var msg)) Log(from, msg.Text);
        if (send) net.Broadcast(new ChatMessage("hi"));
    }
}
```

`INetworkMessages` mirrors `IEvents`: `Send<T>(NetworkPeer, in T)`, `Broadcast<T>(in T)`, `SendToServer<T>(in T)`, `Reader<T>()`. Readers are per-system cursors over a typed inbound channel, so a message is read exactly once per reader and never boxed. Delivery (unreliable, sequenced, reliable unordered, reliable ordered) is declared on the type; a per-call override exists. Every message header carries the sender's tick. Message ids come from the generator, and the same `ION101/102` "emitted but never read" checks apply.

Inbound messages are also exposed to the ordinary event bus as `NetworkMessageReceived<T>` so coroutines (`Wait.For<T>`) and `IonTestHost.Collect<T>()` work unchanged.

## 7. The steps

All engine steps sit in the engine order bands, so user steps at order 0 run between them without any registration-order dependency.

| Stage | Order | Step | What it does |
|---|---|---|---|
| Init | `StageOrder.Network` (-870) | `NetworkSystem.Start` | Start the transport in the configured role; handshake exchanges protocol hash, tick rate, registry hash |
| First | -870 | `NetworkSystem.Poll` | Drain the transport, decode packets into typed channels, apply incoming snapshots to the world through `Commands`, update peer RTT and clock offset |
| First | -860 | `ReconciliationSystem` (client, v2) | Compare the newest authoritative snapshot with the local prediction, roll back and resimulate if needed |
| FixedUpdate | -870 (Begin scope) | `NetworkSystem.BeginTick` | Increment the tick, stamp `INetworkWorld.CurrentTick` |
| FixedUpdate | -860 | `PredictionSystem` (client, v2) | Sample input, store it in the prediction buffer, send `PlayerInput`, apply locally |
| FixedUpdate | 0 | user systems | |
| FixedUpdate | -870 (End scope) | `NetworkSystem.CaptureSnapshot` | Copy replicated component arrays into the ring for this tick; runs in a `finally` |
| Render | -870 | `InterpolationSystem` (client, v2) | Write interpolated remote state into render-side components |
| Last | 870 | `NetworkSystem.Send` | Delta-encode against each peer's last acknowledged snapshot, pack with pending messages, send; flush transport |
| Destroy | 870 | `NetworkSystem.Stop` | Disconnect peers with a reason, dispose the transport |

Because scopes run `End` in a `finally`, a throwing user step never leaves the ring without a snapshot for that tick.

## 8. Snapshot ring and world API

```csharp
public interface INetworkWorld
{
    uint CurrentTick { get; }
    uint OldestTick { get; }
    uint LastAckedTick(NetworkPeer peer);
    uint LastReceivedServerTick { get; }                       // client

    ref readonly T GetAtTick<T>(Entity entity, uint tick) where T : unmanaged;   // lag compensation read
    void WithWorldAtTick(uint tick, Action<World> action);     // full rewind for physics queries; clamped to MaxRewindTicks
    void Rollback(uint toTick);                                // v2, predicted components on owned entities only
    void Resimulate(uint fromTick, uint toTick);               // v2, re-runs the FixedUpdate schedule with stored inputs
}
```

Storage: per replicated type, a ring of `SnapshotHistory` frames, each frame a flat array indexed by a dense entity slot (assigned when `NetworkId` is added, freed on despawn), so `GetAtTick` is two array indexings. Capture is one `Span.CopyTo` per archetype chunk per type. Memory is `history * entities * bytes per entity`; 128 ticks of 1,000 entities at 64 bytes is 8 MB, configurable.

Delta encoding: for each peer, the server keeps the tick the peer last acknowledged; each outgoing snapshot writes only entities whose replicated fields changed since that tick (change masks from the generated delta serializer), plus spawn and despawn records. If the acked tick fell out of the ring, a full snapshot is sent. This is v1 (the draft deferred delta compression; the generated serializers make it cheap enough to ship first).

Interest management (only sending entities near a peer) is a v2 hook: `IInterestPolicy` per peer with a default of "everything", and a grid policy provided for 2D.

## 9. Time

The server tick is authoritative. Clients estimate the server tick from the handshake plus RTT/2 and keep a small lead (`InputLeadTicks`, default 2) so that their `PlayerInput` for tick N arrives before the server simulates N. Clock drift is corrected by nudging the client's `FixedStepClock` step by up to 1 percent, never by skipping ticks. All of this is testable under the loopback transport with simulated latency.

## 10. Security and robustness

- No listening socket unless `Mode` is `Server` or `ListenServer`; `Bind` defaults to loopback and a public bind is an explicit, logged setting.
- Handshake: protocol version, game id, registry hash, optional `JoinSecret`; mismatches are rejected with a reason code and the connection is closed before any game packet is parsed.
- Every inbound message is checked against the sender's authority: clients may only send `[NetworkMessage]` types marked `ClientToServer` and owner-authority component updates for entities they own; anything else is dropped and counted. Repeated violations disconnect.
- Bounds and rate limits on every decode path (no allocation on the receive path; a malformed packet cannot allocate or throw out of the step; decoding uses spans with explicit length checks).
- Per-peer rate limits for messages and bandwidth; `MaxPeers`; idle timeout.
- Lag compensation only rewinds within `MaxRewindTicks` and only if the claimed tick is plausible for the peer's measured RTT.
- Metrics: bytes and packets in/out, RTT, loss, snapshot size, rewind count, rejected messages, exposed through the metrics module and the remote protocol.
- Nothing in this module is compiled out of release builds (games need it), but the transport is off unless configured.

## 11. Testing

- `LoopbackTransport` with a deterministic simulated network (latency, jitter, loss, reordering, seeded) connecting any number of `IonTestHost` instances stepped in lockstep or with skew.
- Tests: handshake and rejection paths; spawn/despawn replication; delta encoding round trip (bit-exact restore from a chain of deltas); full-snapshot fallback; owner authority and server override; message delivery semantics per `Delivery`; tick estimation under latency and jitter; prediction and reconciliation convergence (client state equals server state after the RTT with no visible snap beyond the threshold); lag compensation hit at the rewound position and rejection beyond `MaxRewindTicks`; malformed packets never throw out of `Poll`; receive path allocates 0 B.
- Sample: Breakout ECS in two headless hosts (server plus autopilot client) over loopback with replicated balls and blocks, asserting convergence; the same over LiteNetLib on `127.0.0.1` in an E2E test.

## 12. Transport choice

LiteNetLib stays the first real transport: pure C#, reliability channels, NAT punch-through, MIT, small surface. Two caveats to verify in the first spike: its optional `NetPacketProcessor`/`NetSerializer` use reflection and are not used (Ion generates its own serializers); and a NativeAOT publish of a client must stay warning-free (if LiteNetLib produces trim warnings in its core, the fallback is a small in-house UDP transport with reliability channels, which the design already isolates). `WebSockets` is the second transport and the only one available in the browser target.

## 13. Delivery plan (Stage 6b in the roadmap)

1. Abstractions, generator (`[Replicated]`, `[NetworkMessage]`, registry, delta serializers, ION2xx), snapshot ring, message bus, loopback transport, headless server mode, tests. 
2. LiteNetLib transport, handshake, security checks, metrics, Breakout over loopback and over UDP.
3. Interpolation and prediction/reconciliation (v2 client features), lag compensation API, interest policy hook.
4. WebSocket transport and the browser client (when the browser target is picked up).

## 14. As implemented (Stage 6b, delivery steps 1 to 3, September 2026)

Packages as in section 3, except that the serializers come from a generator of their own, `Ion.Extensions.Networking.Generators` (modelled on the scenes and coroutines generators; `Ion.Generators` is untouched), and that `Ion.Extensions.Networking.LiteNetLib` is a separate package. The WebSocket transport (step 4) is not done. Measurements in [../plans/benchmarks/2026-09-26-stage6b-networking](../plans/benchmarks/2026-09-26-stage6b-networking/README.md). Decisions and deviations:

- *Generated serializers and the registry.* For every `[Replicated]` struct, `[assembly: ReplicateComponent(typeof(T))]` type (the sample replicates the ECS module's `Transform2D` this way) and `[NetworkMessage]` struct, the generator writes a `NetSerializer<T>`: a full form, a field-wise delta (a varint change mask with one bit per serialized member, then the changed members), a bitwise comparison (floats by their bits) and, for `[Interpolated]` components, a blend (linear for floats and vectors, spherical for quaternions, switch at the midpoint otherwise). Supported members: primitives, enums, `System.Numerics` vectors and quaternions, `FixedString32/64/128`, `NetworkId` and structs of those (at most 64 per struct); `[NetworkIgnore]` members are not sent. A module initializer registers each assembly's types with `NetworkRegistry` (and calls the registrations of referenced assemblies). The table sorts components and messages by full name for their wire ordinals; its FNV-1a 64 hash covers the protocol version and each type's name, layout, authority, prediction, interpolation, delivery and direction, and is pinned by a test.
- *Diagnostics.* ION201 not unmanaged, ION202 `[Predicted]` without owner authority, ION203 larger than 1 KB, ION204 never used as an ECS component, ION205 member that cannot be serialized, ION206 `NetworkReader<T>` created in a stage method, ION207/ION208 message sent but never read / read but never sent (executables only), ION209 `[Predicted]`/`[Interpolated]` without `[Replicated]`, ION210 an `Entity` member (not sent; use `NetworkId`).
- *Steps.* `NetworkSystem` (added by `UseNetworking()`): Init `Start` and First `Poll` at `StageOrder.Network` (-870), First `Reconcile` at -860, a FixedUpdate scope at -870 whose begin increments the tick and whose end (in a `finally`, after every other fixed step including the ECS playback) captures the snapshot, FixedUpdate `Predict` at -860, Render `Interpolate` at -870, Last `Send` and Destroy `Stop` at the new `StageOrder.NetworkSend` (870). The session is a singleton bound to the root ECS world; replication inside scene worlds is not supported.
- *Snapshot ring.* Per tick a frame with, per dense slot (the low 20 bits of `NetworkId.Id`, 12 bits of generation above), the id, the owner and one column per replicated type (a flat value array and a presence bitset), so `GetAtTick` is two indexings. Capture is one chunk loop per type. The server gives every entity with a replicated component and no id a server-owned id at capture (`NetworkLocal` opts out), and frees a slot once its entity is gone. A client keeps the snapshots it completed in the same ring, indexed by server tick.
- *Delta snapshots.* Each snapshot is encoded per peer against the tick the peer last acknowledged (every client packet header carries its newest complete snapshot tick): spawn records (id, owner, full components), despawn records, and per changed entity the component operations (full, delta, remove). A missing or too old baseline sends a full snapshot. Snapshots are split into unreliable parts of at most `MtuBytes` (1200); a part index of two bytes allows 4,096 parts, and a snapshot that needs more is flagged and dropped by the client (it logs an error once on the server). The encoder and the client first find the changed slots with a vectorized pass per column, so unchanged entities cost nearly nothing. A client applies only the newest complete snapshot, and writes a component only when it changed since the snapshot it applied before (so a client-side edit of a server-authority component lasts until the server changes it). `SendRate` thins snapshots below the tick rate.
- *Messages.* `INetworkMessages.Send/Broadcast/SendToServer` pack messages per peer and delivery into MTU-sized packets sent in Last; inbound ones are decoded in First and emitted on `IEvents` as `NetworkMessageReceived<T>`, which `NetworkReader<T>` wraps, so readers, the fixed-step backlog, coroutines and `IonTestHost.Collect<T>()` all work unchanged. A listen server's messages to itself are delivered locally. The per-call delivery override exists.
- *Handshake and security.* The server sends a random 16-byte challenge; the client answers with the protocol version, the game id hash (`GameId`, else the game title), the registry hash, the tick rate and HMAC-SHA256(challenge) keyed with `JoinSecret` (compared in constant time), and is refused with a reason (`ProtocolMismatch`, `GameMismatch`, `RegistryMismatch`, `TickRateMismatch`, `WrongSecret`, `ServerFull`) before any game packet is parsed; a connection that does not complete in `HandshakeTimeout` is closed. Clients may send only `ClientToServer`/`Both` messages and owner-authority, non-predicted components of entities they own; everything else is dropped and counted as a violation. Per-peer token buckets for bytes and messages; `MaxViolations` violations disconnect; `IdleTimeout`. Decoding is span-based with explicit bounds: a fuzz test sends thousands of random packets both ways without an exception. `Bind` defaults to loopback and a public bind is logged.
- *Time.* A client starts a full round trip plus `InputLeadTicks` ahead of the server tick in the accept (half a round trip for the tick to arrive, half for its inputs to reach the server) and re-synchronizes when the smoothed error exceeds 3 ticks (`Resyncs`). The clock-rate nudging of section 9 is not done: drift is corrected by resynchronizing.
- *Prediction and reconciliation.* `INetworkPrediction.Register<TComponent, TInput>(step, sample)` on both sides. A client samples its input every fixed step, sends it stamped with its tick together with the two previous ones (a lost packet costs nothing), applies the step to its own entities and records the predicted value per tick; the server applies each client's input for the tick (else the latest before it) to that client's entities, and a listen server samples its own. When a snapshot arrives the client compares the server's value at that tick with its prediction and, when they differ, takes the server's value and replays its stored inputs (`Corrections`). Only the registered steps are replayed, not the whole fixed schedule (`Rollback`/`Resimulate` of section 8 are not exposed).
- *Interpolation.* In Render the client blends `[Interpolated]` components of entities it does not own between the complete snapshots around `InterpolationTick` (the newest snapshot minus `InterpolationDelay`, advanced at the tick rate and pulled a tenth of the error per frame), extrapolates at most `MaxExtrapolationTicks` past the newest, then holds.
- *Lag compensation.* `TryGetRewindTick(peer, claimedTick, out tick)` accepts a claim no older than `MaxRewindTicks` and no older than the peer's round trip plus the interpolation delay and input lead allow; `GetAtTick<T>`/`TryGetAtTick<T>` read the ring; `WithWorldAtTick(tick, action)` writes the replicated components of that tick into the world (clamped to `MaxRewindTicks`), runs the action and restores them in a `finally`.
- *Interest.* `INetworkWorld.InterestPolicy` (`IInterestPolicy.BeginPeer`/`IsRelevant`) is asked per peer and snapshot; relevance is remembered per tick so an entity that stops being relevant is despawned on that peer and spawned again later. No grid policy ships.
- *Transports.* `LoopbackTransport` on a `LoopbackNetwork`: packets are stamped with the sender's clock and delivered when the receiver's clock reaches the stamp plus the seeded latency and jitter; unreliable and sequenced packets can be lost, unreliable ones reordered, reliable-ordered ones never overtake each other; buffers come from a free list. `LiteNetLibTransport` uses LiteNetLib 2.1.4's `LiteNetManager` (its reflection-based `NetPacketProcessor`/`NetSerializer` are not used), events polled on the game thread into a queue of pooled buffers, the MTU fixed at 1200 bytes (an oversized unreliable packet goes reliable), `Flush` triggering an immediate send. The NativeAOT publish of the Breakout Net sample has no LiteNetLib and no `Ion.*` warnings.
- *Metrics and remote.* `net_*` counters and gauges on the metrics module (bytes, packets and messages in and out, rejected, malformed, rate limited, refused handshakes, snapshots, full snapshots, lost snapshots, rewinds, corrections, resyncs, snapshot size, peers, RTT) and a watchable `network.status` read method on the remote protocol.
- *Allocation.* A frame of a server and a client over the loopback (200 moving entities, messages both ways, prediction, interpolation) allocates 0 bytes in steady state, receive path and whole frame; over UDP the receive path allocates nothing except when a burst larger than any before it grows a queue.
- *Sample.* `Ion.Examples.Breakout.Net`: the server simulates balls, blocks and one kinematic paddle per player on the 2D physics module; `Transform2D` is replicated and interpolated, the paddle's `PaddleControl` is predicted from `PaddleInput`, balls are requested with a reliable `LaunchBall` message, a replicated `Scoreboard` singleton carries the score and a `BlockBroken` message the sound. Its tests step a headless dedicated server and a headless autopilot client in one process (loopback clean, loopback with 40 ms latency, jitter, 5 % loss and reordering, and LiteNetLib on 127.0.0.1 with a join secret) for 900 frames and check at every frame that the client holds exactly the server's replicated state at its newest snapshot, within 12 ticks of the server, then that the predicted paddle lands where the server puts it.
- *Not done.* The WebSocket transport and browser client (step 4); `Rollback`/`Resimulate` of the whole fixed schedule; clock-rate nudging; a grid interest policy; NAT punch-through; replication in scene worlds; bandwidth-aware snapshot prioritization.

