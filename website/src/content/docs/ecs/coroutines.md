---
title: Coroutines
description: Write sequences that span frames with Ion.Extensions.Coroutines, waiting on game time, predicates, events, nested routines or custom conditions, without allocating per frame.
sidebar:
  order: 8
---

A coroutine is a C# iterator that the engine resumes once per frame. It lets you write "do this, wait a second, then do
that, then wait until the player presses a key" as straight-line code instead of a state machine. Ion's coroutines live
in `Ion.Extensions.Coroutines`, which is part of the engine core: `AddIon()` registers the runner and `UseIon()` steps it.

```csharp
using System.Collections.Generic;
using Ion;

public sealed class IntroSystem(ICoroutineRunner coroutines)
{
    public bool Finished { get; private set; }

    [Init]
    public void Init(GameTime dt) => coroutines.Start(Intro());

    private IEnumerator<Wait> Intro()
    {
        Console.WriteLine("Get ready...");
        yield return 1.5f;                              // wait 1.5 seconds of game time
        Console.WriteLine("Go!");
        yield return Wait.For<RoundOverEvent>();        // wait for an event
        yield return Wait.For(TimeSpan.FromSeconds(3)); // then three more seconds
        Finished = true;
    }
}

public record struct RoundOverEvent(int Winner);
```

## Registration

| Call | Does |
|---|---|
| `builder.AddIon()` | Registers `ICoroutineRunner` (a singleton `CoroutineRunner`) and `CoroutineSystem`. |
| `game.UseIon()` | Adds `CoroutineSystem`, which steps the runner once per frame. |
| `services.AddCoroutines()` / `game.UseCoroutines()` | The same, for games that compose the engine from parts (the Scenes sample does). |
| `builder.AddCoroutines()` | An alias of `AddIon()`. |

`ICoroutineRunner` and `Wait` are in the `Ion` namespace, so a system needs no extra `using` to start coroutines.

## Starting and stopping

| `ICoroutineRunner` member | Does |
|---|---|
| `Start(IEnumerator routine)` | Adds a coroutine. Its code first runs at the runner's next step. |
| `Stop(IEnumerator routine)` | Removes that coroutine (by reference). Stopping one that is not running is a no-op. |
| `StopAll()` | Removes every coroutine. |
| `IsActive(IEnumerator routine)` | Whether it is still running. |
| `Count` | The number of running coroutines. |
| `Update(GameTime dt)` | Steps every coroutine. Called for you by `CoroutineSystem`. |

To stop a coroutine later, keep the enumerator you started:

```csharp
public sealed class BlinkSystem(ICoroutineRunner coroutines, IInputState input)
{
    private IEnumerator<Wait>? _blink;
    public bool Lit { get; private set; }

    [Update]
    public void Update(GameTime dt)
    {
        if (input.Pressed(Key.B) && _blink is null)
        {
            _blink = Blink();
            coroutines.Start(_blink);
        }
        else if (input.Pressed(Key.Escape) && _blink is not null)
        {
            coroutines.Stop(_blink);   // cancellation
            _blink = null;
            Lit = false;
        }
    }

    private IEnumerator<Wait> Blink()
    {
        while (true)
        {
            Lit = !Lit;
            yield return 0.25f;
        }
    }
}
```

A coroutine also ends by returning (falling off the end or `yield break`). There is no `CancellationToken` support: stop
by reference, or check a flag and `yield break`.

## What you can yield

| Yield | Resumes |
|---|---|
| `null` or `Wait.None` | Next frame |
| a `float` (`yield return 0.5f;`) or `Wait.For(float seconds)` | After that many seconds of game time |
| a `TimeSpan` or `Wait.For(TimeSpan)` | After that duration of game time |
| `Wait.Until(Func<bool> predicate)` | On the first frame the predicate returns true (checked once per frame) |
| `Wait.While(Func<bool> predicate)` | On the first frame the predicate returns false |
| `Wait.For<TEvent>()` | After an event of type `TEvent` (an unmanaged struct) is emitted |
| `Wait.For(IEnumerator routine)` or the routine itself | When the nested coroutine finishes |
| `Wait.For(IWait wait)` or the `IWait` itself | When the custom wait reports `IsReady` |

Anything else yielded from a non-generic `IEnumerator` counts as `Wait.None`.

Time waits count down by each frame's `GameTime.Delta`, so they follow the game clock (a fixed-step test clock makes them
deterministic) rather than wall time.

### Zero allocation with `IEnumerator<Wait>`

Coroutines can be written two ways:

- `IEnumerator` (non-generic): yield anything from the table. Yielding a struct (`float`, `Wait`) boxes it through
  `IEnumerator.Current`.
- `IEnumerator<Wait>`: yield `Wait` values. The runner reads them without boxing and stores the current wait inline in
  the coroutine's handle, so stepping allocates nothing. A `float` or `TimeSpan` converts implicitly
  (`yield return 0.5f;`), `yield return Wait.None;` resumes next frame, and a nested routine is
  `yield return Wait.For(Inner());`.

Prefer `IEnumerator<Wait>` for coroutines that run for a long time or in numbers. The predicate of `Wait.Until` and
`Wait.While` is a delegate: a lambda that captures variables allocates once where it is created, not per frame.

```csharp
private IEnumerator<Wait> Patrol(Entity guard)
{
    while (world.IsAlive(guard))
    {
        world.Get<Transform2D>(guard).Position += new Vector2(32, 0);
        yield return 1f;
        world.Get<Transform2D>(guard).Position -= new Vector2(32, 0);
        yield return 1f;
    }
}
```

### Nested coroutines

A coroutine that yields another runs it to completion first:

```csharp
private IEnumerator<Wait> Cutscene()
{
    yield return Wait.For(Say("Welcome."));
    yield return Wait.For(Say("Press Enter to start."));
    yield return Wait.Until(() => input.Pressed(Key.Enter));
}

private IEnumerator<Wait> Say(string line)
{
    Console.WriteLine(line);
    yield return 2f;
}
```

### Waiting on events

`Wait.For<TEvent>()` reads events through a reader that belongs to that coroutine (every coroutine has its own
`EventReaderSet` over the application's `IEvents`). So each waiting coroutine is resumed once by an event, and a later
`Wait.For<TEvent>()` in the same coroutine does not see the same event again. An event emitted in a FixedUpdate step is
seen once even when the frame runs several fixed steps or none.

`TEvent` must be unmanaged, like every event (see [Events](/Ion/concepts/events/)). `Wait.For<TEvent>()` is marked
`[ReadsEvent]`, so the event analyzer counts it as a reader for ION101/ION102. To read the event's payload, read it
yourself after resuming with a reader created outside the coroutine.

### Custom waits

For a condition the built-in waits do not cover, implement `IWait`:

```csharp
/// <summary>Resumes after a number of rendered frames (not seconds).</summary>
public sealed class FramesWait(int frames) : IWait
{
    private int _left = frames;

    public bool IsReady => _left <= 0;

    public void Update(GameTime dt, EventReaderSet events) => _left--;
}

// yield return Wait.For(new FramesWait(3));
```

The runner calls `Update` once per frame, then reads `IsReady`. The `EventReaderSet` is the coroutine's own, for
conditions on events (`events.TryRead<T>(out var e)`, `events.Any<T>()`). Implement custom waits as classes: a struct
would be boxed on every yield.

## When coroutines run

`CoroutineSystem` steps the runner in **Update** at `StageOrder.Coroutines` (-600): after the engine's setup steps and
before the UI frame, the active scene (-500) and your own Update steps (0). Consequences:

- A coroutine started during a frame's Update (order 0) first runs in the next frame's Update. One started in Init or
  First runs in the same frame's Update.
- Coroutine code runs in Update, so structural ECS changes it makes are ordinary (not inside a query) and it may use
  `World` directly; changes recorded with `Commands` are applied at the end of Update.
- Calling `runner.Update(dt)` yourself as well is harmless: the runner steps at most once per frame between the manual
  call and the system (repeated manual calls in one frame still step each time).

## Coroutines and scenes

The runner is a singleton shared by the whole application, including scene-scoped systems. Coroutines are **not**
stopped when a scene unloads. A coroutine started by a scene system that keeps touching the scene's `World` after the
scene is gone would use a released world. Stop the scene's coroutines in its Destroy stage:

```csharp
public sealed class WaveSystem(ICoroutineRunner coroutines, World world) : IDisposable
{
    private readonly List<IEnumerator<Wait>> _running = [];

    [Init]
    public void Init(GameTime dt) => Run(Waves());

    [Destroy]
    public void Destroy(GameTime dt) => Dispose();

    public void Dispose()
    {
        foreach (var routine in _running) coroutines.Stop(routine);
        _running.Clear();
    }

    private void Run(IEnumerator<Wait> routine)
    {
        _running.Add(routine);
        coroutines.Start(routine);
    }

    private IEnumerator<Wait> Waves()
    {
        for (var wave = 1; wave <= 5; wave++)
        {
            for (var i = 0; i < wave * 3; i++) world.Create(new Transform2D(new Vector2(100 * i, 0)), new Enemy());
            yield return 10f;
        }
    }
}

public record struct Enemy;
```

Registered scoped (`builder.Services.AddScoped<WaveSystem>()`) and added with `scene.UseSystem<WaveSystem>()`, it is
disposed with the scene either way. See [Scenes](/Ion/ecs/scenes/).

## Coroutines or systems?

| Use a coroutine for | Use a system step for |
|---|---|
| One-off sequences: intros, cutscenes, tutorials, timed waves, delayed effects | Work every entity needs every frame |
| Logic that reads as "then wait, then..." | Continuous simulation (movement, physics reactions) |
| A few long-lived scripted behaviours | Many similar entities: a `[Query]` over components is faster and serializable |

Coroutine state lives in the iterator, so it is not part of world snapshots or remote inspection. For state you want to
save or inspect, keep it in components and drive it from a query.

## The coroutine generator

`Ion.Extensions.Coroutines.Generators` only emits a `[Coroutine(methodName)]` marker attribute; nothing consumes it and no
project references the generator by default. It is reserved for future use: you do not need it for anything on this page.

## Testing coroutines

Coroutines run on the game clock, so the headless test host steps them deterministically:

```csharp
using var host = new IonTestHost()
    .Configure(services => services.AddSingleton<IntroSystem>())
    .ConfigureApp(app => app.UseSystem<IntroSystem>());

host.Step(120);                               // two seconds at the host's fixed 60 Hz clock
Assert.False(host.Get<IntroSystem>().Finished);

host.Events.Emit(new RoundOverEvent(1));
host.Step(200);                               // the event, then three seconds
Assert.True(host.Get<IntroSystem>().Finished);
Assert.Equal(0, host.Get<ICoroutineRunner>().Count);
```

See [Testing](/Ion/tooling/testing/) and [Time and determinism](/Ion/concepts/time-and-determinism/).

## See also

- [Scenes](/Ion/ecs/scenes/)
- [Events](/Ion/concepts/events/)
- [Stages](/Ion/concepts/stages/)
- [Scenes example](/Ion/examples/scenes/) (starts a countdown coroutine with Enter)
