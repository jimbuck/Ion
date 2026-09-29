---
title: The game loop
description: How Ion's GameLoop runs Init, frames and Destroy, how frames are paced, how headless and fixed-length runs work, and how a game exits.
sidebar:
  order: 7
---

`game.Run()` builds a `GameLoop` (namespace `Ion.Core`) from the application's schedule and runs it on the calling
thread. The loop owns the timing; the schedule owns what runs. This page describes the loop; [Stages](/Ion/concepts/stages/)
and [Systems](/Ion/concepts/systems/) describe the schedule.

## Structure

```text
Run()
 ├─ build and validate the schedule (IonScheduleException on errors)
 ├─ Initialize()        Init stage, then restart frame timing (loading does not count as frame 0)
 ├─ loop until exit:
 │   └─ Step()          one timed frame
 │       ├─ clock.NextFrame(), clamp to MaxFrameTime, add to the accumulator
 │       ├─ First
 │       ├─ FixedUpdate × n (while the accumulator holds a whole fixed step)
 │       ├─ Update
 │       ├─ Render
 │       ├─ exit check (ExitGameEvent)
 │       ├─ Last
 │       ├─ pacing: sleep what is left of 1 / MaxFPS
 │       └─ frame stats, frame index + 1, hot reload rebuild if requested
 └─ Shutdown()          Destroy stage
```

The loop is single-threaded: every stage runs on the thread that called `Run`. Engine modules that use other threads
(audio mixing, the web server, network transports, the remote protocol) hand their work to the game thread at fixed
points in the schedule, so your steps never need locks for engine state.

Before each stage the loop sets `ILoopContext.Stage` (and increments `FixedStepCount` before each fixed step). Engine
services use it to give fixed-step and per-frame readers the right view of input and events. You can inject
`ILoopContext` to know where you are:

```csharp
public sealed class DebugService(ILoopContext loop, ILogger<DebugService> logger)
{
    public void Log(string message) =>
        logger.LogInformation("[{Stage} frame {Frame}] {Message}", loop.Stage, loop.Frame, message);
}
```

## Frame pacing

After `Last`, the loop sleeps for what is left of the frame budget, `1 / Ion:MaxFPS`. The sleep goes through the clock,
so a `FixedStepClock` never blocks.

| Setting | Effect |
|---|---|
| `Ion:MaxFPS` (default `300`) | The render rate cap. |
| `Ion:MaxFPS` below 1 | Uncapped: no sleeping. |
| `Ion:VSync = true` | The loop does no pacing and relies on presentation to block. Set `Ion:Graphics:VSync = true` too, so the swapchain actually waits. |

Pacing limits rendering only. The simulation rate is `Ion:FixedUpdateRate` (60 Hz by default) whatever the frame rate;
see [Time and determinism](/Ion/concepts/time-and-determinism/).

```json title="appsettings.json"
{ "Ion": { "MaxFPS": 0, "VSync": true, "Graphics": { "VSync": true } } }
```

With profiling on, the time spent sleeping is recorded as the frame's idle time (`FrameStats.IdleMs`, `idle_ms` in the
frame log).

## Running

| Call | What it does |
|---|---|
| `game.Run()` | Runs until the game exits. Honours `Ion:Run:Frames`. |
| `game.Run(cancellationToken)` | Also stops after the current frame when the token is cancelled. |
| `game.RunFrames(n)` | Runs `Init`, at most `n` frames, then `Destroy`. |
| `game.Build()` | Builds and returns the `GameLoop` without running it (the schedule is validated and bound). |

`GameLoop` itself exposes the same pieces for hosts that drive it by hand, as `Ion.Testing` does:

| Member | Use |
|---|---|
| `Run(ct)`, `RunFrames(n)` | Full runs, as above. Throws if the loop is already running. |
| `Initialize()` | Runs `Init` once; call before the first `Step()` when driving by hand. |
| `Step()` | One complete timed frame. Ignores exit requests (the caller decides). |
| `Step(GameTime time)` | One untimed pass of every per-frame stage with the given time: exactly one `FixedUpdate`, no clock, accumulator or pacing. `Render` and `Last` are skipped once exit is requested. |
| `Shutdown()` | Runs `Destroy` once, after the last frame. |
| `Stop()` | Asks the loop to exit after the current frame. |
| `IsRunning`, `IsExitRequested` | State. |
| `GameTime`, `FixedGameTime`, `Clock`, `Context`, `Profiler`, `Schedule` | The loop's time objects, clock, context, profiler and running schedule. |

```csharp
// Driving the loop yourself (for example from a platform's own frame callback).
using var game = builder.Build();
game.UseIon().UseSystem<MySystem>();

var loop = game.Build();
loop.Initialize();
while (!loop.IsExitRequested) loop.Step();
loop.Shutdown();
```

:::note
Most games never touch `GameLoop`. The mobile heads, tools and the test host use it; a game uses `game.Run()`.
:::

## Exiting

A game exits after the frame in which any of these happens:

| Trigger | How |
|---|---|
| `ExitGameEvent` | `events.Emit<ExitGameEvent>()` from any step. |
| The window is closed | The window system turns `WindowClosedEvent` into `ExitGameEvent` (at `StageOrder.WindowClose` in `Render`). |
| `GameLoop.Stop()` | From code that holds the loop (`GameLoopContext.Loop`). |
| Cancellation | The token passed to `Run(cancellationToken)`. |
| Frame count | `RunFrames(n)`, or `Ion:Run:Frames` with `Run()`. |

The loop checks for `ExitGameEvent` **after `Render` and before `Last`**, so `Last` still runs for the frame that asked
to exit, then `Destroy` runs once. An event emitted in `Last` is seen at the next frame's check.

```csharp
public sealed class QuitSystem(IInputState input, IEvents events)
{
    [Update]
    public void Check(GameTime dt)
    {
        if (input.Pressed(Key.Escape)) events.Emit<ExitGameEvent>();
    }
}
```

`Destroy` runs once after a normal exit. If a step throws, the exception propagates out of `Run()` (with a stack trace
that shows your frames, not the engine's dispatch): scope ends run in their `finally` blocks on the way out, but the
`Destroy` stage does not run. Disposing the application (`using var game`) still disposes every service. With
`Ion:Run:Summary` set, an exception that ends the process unhandled is written into the summary.

## Headless runs

Headless is a configuration, not a separate build. With `--headless` (`Ion:Headless=true`), `AddIon` registers:

| Service | Headless implementation |
|---|---|
| `IWindow` | `NullWindow`, sized from `Ion:Window` (960 by 540 by default); `Close()` ends the game. |
| `IInputState` | `NullInputState`: scripted keys, mouse, text, gamepads and touch, applied at the start of the next frame. |
| `ISpriteBatch` | `NullSpriteBatch`: draws nothing, counts draw calls and sprites. |
| `IAudioManager` | `NullAudioManager`: the real mixer on a null output, recording every play. |
| Assets | Texture loaders that read sizes from image headers; fonts that measure with a fixed glyph width. |

Add `--headless-render` (`Ion:Headless:Render=true`) to render for real into an offscreen target (Vulkan on Mesa
lavapipe, or OpenGL ES through EGL) and capture screenshots.

### Fixed-length and agent runs

These keys work in every game built on `AddIon`/`UseIon`, without code:

| Key | Effect |
|---|---|
| `Ion:Run:Frames` | `Run()` stops after this many frames. |
| `Ion:Run:FixedStep` | Use the deterministic `FixedStepClock`. Defaults to on when headless and `Ion:Run:Frames` is set. |
| `Ion:Run:Screenshot` | Write the last rendered frame to this PNG at the end (needs headless rendering or a window). |
| `Ion:Run:Summary` | Write a JSON run summary at the end, or when an unhandled exception ends the run. |
| `Ion:Seed` | The seed, recorded in the summary. |

```bash
dotnet run -- --headless --Ion:Run:Frames=600 --Ion:Seed=1 --Ion:Run:Summary=out/run.json
# the same, with the ion tool:
ion run --headless --frames 600 --seed 1 --summary out/run.json
```

The summary (`version`, `status`, `title`, `frames`, `requestedFrames`, `seed`, `headless`, `fixedStep`, `wallMs`,
`frameStats`, `lastFrame`, `counters`, `warnings`, `errors`, `exception`, `screenshot`, `schedule`) is written by
`RunReportSystem`, which `UseIon` adds when a screenshot or summary is requested. See
[The ion CLI](/Ion/tooling/ion-cli/).

## Hot reload

When you run a Debug build of the engine under `dotnet watch`, a code change applied by .NET hot reload sets
`GameLoop.Rebuild`, and the loop rebuilds the schedule from the application's registrations at the end of the frame, so
changed or new step methods take effect without restarting. Asset hot reload is separate and works in any build; see
[Assets](/Ion/rendering/assets/).

## Frame statistics

Every frame the loop writes a `FrameStats` (frame and idle time, fixed steps, draw calls, sprites, triangles, entities,
events emitted, GC counts, bytes allocated) into the metrics history, and with profiling on a span per stage and per
step. `IonTestHost.LastFrame` exposes the last one in tests. See [Metrics and tracing](/Ion/tooling/metrics-and-tracing/).

## See also

- [Stages](/Ion/concepts/stages/): what each stage is for.
- [Time and determinism](/Ion/concepts/time-and-determinism/): `GameTime`, the accumulator and clocks.
- [The application](/Ion/concepts/application/): `Run`, `RunFrames` and headless registration.
- [Testing](/Ion/tooling/testing/): driving the loop frame by frame.
