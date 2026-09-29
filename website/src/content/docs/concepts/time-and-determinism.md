---
title: Time and determinism
description: GameTime, the fixed-step accumulator, interpolation alpha, the IClock implementations, seeds, and what Ion guarantees about repeatable runs.
sidebar:
  order: 6
---

Every step receives a `GameTime`. How it is filled, and which clock drives it, decides whether your game plays the same
at 30 and 300 frames per second, and whether two runs with the same inputs produce the same result.

## GameTime

```csharp
public class GameTime
{
    public uint Frame { get; set; }        // frame index, from 0
    public float Delta { get; set; }       // seconds since the previous frame (the fixed step in FixedUpdate)
    public float Alpha { get; set; }       // interpolation ratio, 0 to 1
    public TimeSpan Elapsed { get; set; }  // total time
    public static implicit operator float(GameTime time) => time.Delta;
}
```

The loop keeps two instances and passes one or the other:

| Stages | Instance | `Delta` | `Elapsed` | `Alpha` |
|---|---|---|---|---|
| `First`, `Update`, `Render`, `Last` (and `Init`, `Destroy`) | `GameLoop.GameTime` | The frame's duration, clamped to `MaxFrameTime` | The frame's start time on the clock | Leftover accumulator divided by the fixed step (set after the fixed steps) |
| `FixedUpdate` | `GameLoop.FixedGameTime` | Always `1 / FixedUpdateRate` | Simulated time: the sum of every fixed step so far | 1 |

Both share the frame index. Because `GameTime` converts to `float`, `position += velocity * dt` uses `dt.Delta`.

:::caution[Do not keep a GameTime]
The loop reuses the same two objects every frame and mutates them. Copy the values you need (`dt.Delta`,
`dt.Elapsed`) instead of storing the `GameTime` reference.
:::

## The fixed step

`FixedUpdate` runs at a fixed rate, independent of the render rate. Each frame:

1. The loop asks the clock for the frame start time and computes the frame duration.
2. It clamps the duration to `Ion:MaxFrameTime` (100 ms by default), so a long hitch does not cause a burst of
   catch-up steps.
3. It adds the duration to an accumulator.
4. It runs `FixedUpdate` once per whole fixed step (`1 / Ion:FixedUpdateRate` seconds, 1/60 s by default) that fits in
   the accumulator, subtracting each one.
5. The remainder carries to the next frame, and `GameTime.Alpha` is set to `remainder / fixedStep` for `Update` and
   `Render`.

| Frame rate (60 Hz fixed step) | Fixed steps per frame |
|---|---|
| 30 fps | 2 |
| 60 fps | 1 |
| 120 fps | alternately 0 and 1 |
| 300 fps | 1 every 5 frames |

```json title="appsettings.json"
{ "Ion": { "FixedUpdateRate": 120, "MaxFrameTime": "00:00:00.25" } }
```

### Smooth rendering with Alpha

At high frame rates many frames run no fixed step, so an object moved only in `FixedUpdate` moves in visible jumps. Keep
the previous and current simulated positions and blend them by `Alpha` when drawing:

```csharp
public sealed class BallSystem(ISpriteBatch sprites)
{
    private Vector2 _previous, _current, _velocity = new(200, 0);

    [FixedUpdate]
    public void Simulate(GameTime dt)
    {
        _previous = _current;
        _current += _velocity * dt.Delta;
    }

    [Render]
    public void Draw(GameTime dt) =>
        sprites.DrawRect(Color.White, Vector2.Lerp(_previous, _current, dt.Alpha), new Vector2(12));
}
```

The networking module does the same for remote entities with `[Interpolated]` components; see
[Interpolation](/Ion/networking/multiplayer/interpolation/).

## Clocks

The loop never reads the system time directly. It asks an `IClock`, a singleton you can replace:

```csharp
public interface IClock
{
    TimeSpan Elapsed { get; }            // time since the clock started; no side effects
    double Seconds => Elapsed.TotalSeconds;
    TimeSpan NextFrame();                // called once at the start of every frame; returns its start time
    void Sleep(TimeSpan duration);       // frame pacing
}
```

| Clock | Registered by | Behavior |
|---|---|---|
| `StopwatchClock` | `CreateBuilder` (the default) | Real time from `Stopwatch`. `Sleep` sleeps the thread for most of the wait and spins the last `SpinThreshold` (1 ms) to absorb scheduler jitter. On Windows the loop raises the timer resolution to 1 ms while it runs (unless `Ion:VSync` is true). |
| `FixedStepClock(step)` | `AddIon` for headless runs with a frame count, and `IonTestHost` | Every frame lasts exactly `step`, however long it really took. `NextFrame` advances by one step; `Sleep` returns immediately. `Advance()` moves one step by hand. |
| `ManualClock(start)` | Tests that drive time by hand | Time moves only with `Advance(duration)`, or on `Sleep` when `AdvanceOnSleep` is true (the default). Records `SleepCount`, `LastSleep` and `TotalSleep`, so tests can check frame pacing without sleeping. |

`IonRun.FrameTime` (and `IonTestHost.DefaultFrameTime`) is one 60 Hz frame rounded up to a whole tick
(166,667 ticks), which guarantees exactly one fixed step per frame at the default rate.

### When the deterministic clock is used

| Situation | Clock |
|---|---|
| A normal run, windowed or headless | `StopwatchClock` |
| Headless with `Ion:Run:Frames` set (what `ion run --headless --frames N` passes) | `FixedStepClock(IonRun.FrameTime)` |
| `Ion:Run:FixedStep=true` | `FixedStepClock`, whatever the mode |
| `Ion:Run:FixedStep=false` | `StopwatchClock`, even headless with a frame count |
| `IonTestHost` | `FixedStepClock(frameTime)`, 1/60 s by default (`new IonTestHost(TimeSpan.FromSeconds(1.0 / 120))` for another) |

To replace the clock yourself, register your own after the engine's:

```csharp
builder.Services.AddSingleton<IClock>(new FixedStepClock(TimeSpan.FromSeconds(1.0 / 30)));
```

## Seeds

Ion reserves one key for the random seed: `Ion:Seed` (`--Ion:Seed=7`, `ion run --seed 7`). The engine does not seed
anything itself; games read it with `IonRun.Seed`:

```csharp
public sealed record GameSettings(int Seed)
{
    public static GameSettings From(IConfiguration config) => new(IonRun.Seed(config, fallback: 1));
}
```

The run summary written by `ion run --summary` records the seed, so a failing run can be replayed exactly.

Good practice, from the Breakout ECS sample:

- **Give each consumer its own `Random`**, derived from the seed and a fixed stream number, so that adding a random draw
  in one system does not change the numbers another system sees:

  ```csharp
  public Random CreateRandom(int stream) => new(unchecked(Seed * 7919 + stream * 104729));
  ```

- **Do not derive seeds from `HashCode` or `string.GetHashCode()`**: they are randomized per process in .NET.
- **Never use `Random.Shared` or `new Random()` without a seed** in simulation code.

## What is deterministic

With the deterministic clock, a fixed seed and the same inputs, a run of the same build produces the same state frame
after frame. The pieces that make it so:

| Piece | Guarantee |
|---|---|
| Game loop | With `FixedStepClock`, frame durations, the number of fixed steps per frame and every `GameTime` value are exact. |
| Schedule | Steps run in a fixed, printed order (constraints, `Order`, registration, declaration). |
| Events | Emitted and read in schedule order; readers see events in emission order. |
| Input | Scripted input (`NullInputState`) and recorded input (`AddInputRecording`/`AddInputPlayback`) are applied at the start of a given frame. A recording replayed into a test reproduces the same `Pressed`/`Down` sequence. See [Recording and playback](/Ion/interaction/input/recording-and-playback/). |
| Audio | Headless audio mixes on the game thread, driven by the game clock. |
| Physics | Box2D and Bepu steps depend only on components, the fixed delta and entity order; replay tests hash 10,000 steps. See [Physics determinism](/Ion/physics/determinism/). |
| Networking tests | `LoopbackTransport` simulates latency, jitter, loss and reordering from a seed. |

The templates' tests rely on this: `TheSameSeedGivesTheSameWorld` runs the ECS template twice with `Ion:Seed=7` and
compares the worlds as JSON, and the snapshot tests compare a run against a committed file.

### What is not guaranteed

- **Real-time runs.** With `StopwatchClock` the number of fixed steps per frame depends on real frame times, so
  `Update`-based logic and the interleaving of fixed and variable steps vary between runs. Keep simulation in
  `FixedUpdate` to minimize the difference.
- **Your own sources of nondeterminism**: unseeded `Random`, `DateTime.Now`, iterating a `HashSet` of objects, threads,
  or reading files that change.
- **Bit-identical results across CPU architectures.** Floating-point results can differ between x64 and arm64 (and
  between SIMD widths). The physics design notes measure this per engine: Bepu matches at equal `Vector<float>` width,
  and the packaged Box2D natives differ between x64 and arm64. See [Physics determinism](/Ion/physics/determinism/).

## Time in tests

```csharp
using var host = new IonTestHost().UseEntryPoint<Program>();   // FixedStepClock, 1/60 s per frame
host.Step(60);                                                  // exactly one simulated second
Assert.Equal(1, host.LastFrame.FixedSteps);                     // one fixed step in every frame
```

`host.Step(n)` runs `n` timed frames on the fixed clock; `host.Clock` is the `FixedStepClock`. For sub-frame control,
`GameLoop.Step(GameTime)` runs one untimed pass of every per-frame stage with a `GameTime` you provide (one fixed step,
no clock, no pacing), which is the cheapest way to drive systems from benchmarks.

## See also

- [The game loop](/Ion/concepts/game-loop/): frame structure, pacing and exit.
- [Stages](/Ion/concepts/stages/): what runs in `FixedUpdate` versus `Update`.
- [Testing](/Ion/tooling/testing/) and [Snapshots and goldens](/Ion/tooling/snapshots-and-goldens/).
- [Physics determinism](/Ion/physics/determinism/).
