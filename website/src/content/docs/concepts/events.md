---
title: Events
description: Emit and read typed, unmanaged events on Ion's frame event bus, understand how long events live and who sees them, and what the source generator adds.
sidebar:
  order: 4
---

Systems communicate through **events**: small unmanaged structs published on typed channels. One system emits, any
number of systems read, and nobody holds a reference to anybody else. Emitting and reading never allocate in steady
state.

```csharp
using Ion;

public record struct BlockHit(int BlockId, int Points);

public sealed class BlockSystem(IEvents events)
{
    [FixedUpdate]
    public void Collide(GameTime dt)
    {
        // ... on a hit:
        events.Emit(new BlockHit(3, 10));
    }
}

public sealed class ScoreSystem(IEvents events)
{
    private EventReader<BlockHit> _hits = events.Reader<BlockHit>();   // created once, in a mutable field

    public int Score { get; private set; }

    [Update]
    public void Tally(GameTime dt)
    {
        foreach (var hit in _hits.Read()) Score += hit.Points;
    }
}
```

## Event types

An event type is any **unmanaged struct**: no reference-type fields, no strings, no arrays. A `record struct` is the
usual choice. A struct that is not unmanaged is a compile error (the `where T : unmanaged` constraint), and the generator
reports `ION104` naming the offending field.

```csharp
public record struct Scored(int Points);          // data
public record struct PlayerDied;                  // no data (a "signal")
public record struct Hit(Entity Target, Vector2 Normal, float Impulse);
```

Refer to things by value or handle (an `Entity`, an index, an id), not by object reference.

## Emitting

Inject `IEvents` and call `Emit`:

```csharp
events.Emit(new Scored(10));
events.Emit<PlayerDied>();           // extension: emits default(T), for events without data
```

`Emit` takes the event by `in` reference and appends it to the type's channel. It does not allocate unless a frame emits
more events of that type than the channel has ever held (it then grows, once, and never shrinks).

## Reading

Create a reader **once** (constructor or field initializer), keep it in a field that is **not** `readonly`, and read
from it in your steps:

```csharp
private EventReader<Scored> _scored = events.Reader<Scored>();
```

| Member | Behavior |
|---|---|
| `Read()` | Every unread event, oldest first, as a `ReadOnlySpan<T>`; advances past them. The span is valid until the end of the frame. |
| `TryRead(out T e)` | Reads the oldest unread event, if any. |
| `TryReadLatest(out T e)` | Reads every unread event and returns the newest (handy for "last value wins" events such as a resize). |
| `Any()` | Whether there is an unread event; does not advance. |
| `Count` | The number of unread visible events. |
| `Skip()` | Marks everything visible as read. |

```csharp
// All of them
foreach (var e in _scored.Read()) total += e.Points;

// One at a time
while (_scored.TryRead(out var e)) Apply(e);

// Only the newest
if (_resized.TryReadLatest(out var size)) Relayout(size.Width, size.Height);
```

Each reader has its own cursor, so **every reader sees every event exactly once**. Two systems reading the same type
each see all of its events.

:::danger[Readers are structs]
`EventReader<T>` is a struct holding the channel and a cursor. Two mistakes follow from that, and the generator reports
both:
- **A reader created inside a step** (`ION103`) starts over every call and re-reads the previous frame's events every
  frame.
- **A reader in a `readonly` field or exposed as a property** (`ION106`) is copied on every call, so its cursor never
  moves and you see the same events forever.
:::

## Lifetime: who sees what, when

The bus keeps two frames of events per type:

- An event emitted in frame N is visible **in frame N, after it is emitted**, and **in frame N+1**. After that it is
  dropped.
- A reader that reads in frame N sees the events emitted earlier in frame N and those of frame N-1 it has not read yet.
- The event frame ends at the very end of `Last` (`EventSystem.StepEvents`, at `StageOrder.Events` = 1000).

So a reader that runs **after** the emitter in the frame order sees the event the same frame; a reader that runs
**before** it sees it the next frame, one frame late. The generator reports that case as `ION105` (info) when a type is
read only in stages earlier than every stage that emits it.

### Fixed steps never miss events

`FixedUpdate` can run zero times in a frame. To make sure fixed-step consumers still see every event, a reader that reads
**during `FixedUpdate`** also sees older events that no fixed step has had the chance to see yet, even if they have left
the two-frame window. This backlog is bounded by `EventBus.MaxBacklogFrames` (1024 frames).

| Reader runs in | Sees |
|---|---|
| `First`, `Update`, `Render`, `Last` | Unread events of the current and previous frame. |
| `FixedUpdate` | The same, plus older events no fixed step has seen yet. |

Input has the same guarantee: see [Stages](/Ion/concepts/stages/#input-and-events-across-stages).

## Built-in events

| Event | Emitted by | Meaning |
|---|---|---|
| `ExitGameEvent` | You, or the window system | Asks the loop to exit after the current frame. |
| `WindowResizeEvent(uint Width, uint Height)` | The window (and the null window at Init) | The window's new size. |
| `WindowClosedEvent` | The window | The user closed the window; the window system turns it into `ExitGameEvent`. |
| `WindowFocusGainedEvent`, `WindowFocusLostEvent` | The window | Focus changes. |
| `AssetReloadedEvent` | Asset hot reload | An asset was reloaded; see [Assets](/Ion/rendering/assets/). |
| `ChangeSceneEvent(int NextSceneId)` | `EmitChangeScene(...)` | Switches the active scene; see [Scenes](/Ion/ecs/scenes/). |
| `Collision2D`, `Trigger2D`, `Collision3D`, `Trigger3D` | The physics modules | Contact begin and end; see [Queries and events](/Ion/physics/queries-and-events/). |

```csharp
public sealed class QuitOnEscape(IInputState input, IEvents events)
{
    [Update]
    public void Check(GameTime dt)
    {
        if (input.Pressed(Key.Escape)) events.Emit<ExitGameEvent>();
    }
}
```

## Reading types chosen at the call site

Code that reads event types it does not know ahead of time (a coroutine waiting for "any event of T", a test collector)
can use `EventReaderSet`, which keeps one reader per type, created on first use:

```csharp
var readers = new EventReaderSet(events);
if (readers.TryRead<Scored>(out var s)) { /* ... */ }
ref var reader = ref readers.Reader<PlayerDied>();   // by reference, so reads advance it
```

It allocates once per type. Systems that know their event types should hold `EventReader<T>` fields instead.

## The generated bus

Without the generator, `IEvents` is the runtime `EventBus`: one channel per type, created on first use and found through
a per-type index. With the Ion source generator (on by default when you reference the `Ion` package), the application's
`IonApplication.CreateBuilder` call installs a **generated bus** instead:

- one typed channel field per event type the game (or an Ion assembly it references) uses;
- a compile-time integer id per type, `EventId<T>.Value`, used in logs and traces (types it did not see get ids from
  `EventIds.FirstRuntimeId`, 65536, upwards);
- an initial capacity per channel chosen from how the type is emitted: larger when emitted in `FixedUpdate` or `Update`,
  larger still when emitted in a loop there (the runtime default is 16, `EventBus.DefaultCapacity`);
- interceptors that route the game's own `Emit` and `Reader` calls straight to the typed fields.

Types the generator cannot see (a plugin's events, calls through generic helpers) still get a channel at run time on the
same bus, so both paths coexist. When a channel has to grow past its capacity, the event system logs it at `Debug`
level with the event id.

### Event diagnostics

| Id | Severity | Reported when |
|---|---|---|
| `ION101` | Warning | An event type is emitted but nothing in the application or the Ion assemblies it references reads it. |
| `ION102` | Warning | An event type is read but nothing emits it. |
| `ION103` | Warning | A reader is created inside a per-frame stage method. |
| `ION104` | Error | An event payload is not an unmanaged struct (the message names the field). |
| `ION105` | Info | A reader reads in an earlier stage than the only stages that emit the type, so it sees each event a frame late. |
| `ION106` | Warning | A reader is stored in a `readonly` field or exposed as a property. |

Library methods that emit or read for their caller are marked `[EmitsEvent]` or `[ReadsEvent]` (optionally with the
event type) so the generator counts their call sites. Libraries compiled with the generator publish an
`[assembly: EventUsage(...)]` summary so an application that references them does not report their events as never
read or never emitted.

```csharp
public static class ScoreEvents
{
    [EmitsEvent(typeof(Scored))]
    public static void EmitScore(this IEvents events, int points) => events.Emit(new Scored(points));
}
```

## Cost

One array per event type, reused frame after frame; a reader is a struct with a reference and a cursor. The README's
benchmark: 100 events of 4 types per frame, each read by 8 readers, costs about 650 ns per frame, with no allocation.

## Events in tests

`IonTestHost.Collect<T>()` records every event of a type the game emits, with the frame number:

```csharp
using var host = new IonTestHost().UseEntryPoint<Program>();
var scores = host.Collect<Scored>();
Assert.True(host.RunUntil(() => scores.Count > 0, maxFrames: 600));
Assert.Equal(10, scores[0].Points);
```

## Legacy API

`IEventEmitter`, `IEventListener`, `IEventListenerFactory`, `EventEmitter` and `EventListener` are obsolete adapters over
`IEvents`, kept for one release. Move to `IEvents.Emit` and `EventReader<T>`.

## See also

- [Stages](/Ion/concepts/stages/): which stage runs when, and why a reader might see an event a frame late.
- [Source generators](/Ion/concepts/source-generators/): the other generated pieces.
- [Multiplayer messages](/Ion/networking/multiplayer/messages/): `INetworkMessages` and `NetworkReader<T>` mirror
  `IEvents` and `EventReader<T>` across the network.
- [Diagnostics](/Ion/reference/diagnostics/).
