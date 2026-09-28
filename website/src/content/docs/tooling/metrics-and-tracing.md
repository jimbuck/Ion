---
title: Metrics and tracing
description: Frame stats, game counters, the frame log, the on-screen overlay, dotnet-counters, Chrome traces for Perfetto and the Tracy bridge.
sidebar:
  order: 6
---

The metrics module (`Ion.Extensions.Metrics`) measures every frame. It collects engine stats (frame time, draw calls,
sprites, entities, events, allocations), lets the game register its own counters, gauges and histograms, records
profiling spans for every stage and step, and exports all of it: a JSON Lines frame log, an overlay, the `Ion` .NET
meter, Chrome traces for [Perfetto](https://ui.perfetto.dev), and live streaming to Tracy.

`AddIon` installs it in every game. Configure it from `Ion:Metrics` or in code:

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon()
	.AddMetrics(metrics =>
	{
		metrics.Overlay = true;
		metrics.OverlayFont = "Fonts/Mono.ttf";
	})
	.AddSystem<BallSystem>();

using var game = builder.Build();
game.UseIon().UseSystem<BallSystem>();   // UseIon adds the metrics systems
game.Run();
```

`builder.AddMetrics(configure)` registers the engine core (which includes the metrics module) and applies `configure`
after `Ion:Metrics` is bound. Without the `Ion` package: `services.AddMetrics(configuration)` and `app.UseMetrics()`.

## Configuration

`MetricsConfig`, bound from `Ion:Metrics`:

| Key | Default | Meaning |
|---|---|---|
| `HistoryFrames` | `300` | Completed frames the profiler keeps (5 s at 60 fps). Traces contain at most this many frames. |
| `SpansPerFrame` | `512` | Spans each frame can hold. `0` disables span recording. |
| `Profiling` | `false` | Whether span recording starts on. When on at shutdown, the kept frames are written to `TraceOutput`. |
| `TraceOutput` | `trace.json` | Where Chrome traces are written (captures and shutdown). Empty disables writing. |
| `FrameLog` | none | A JSON Lines file that receives one line per frame. |
| `FrameLogFlushFrames` | `60` | How often, in frames, the frame log is flushed. `1` flushes every line. |
| `CaptureKey` | `F9` | The key that captures a trace of the next `CaptureFrames` frames. `Unknown` disables it. |
| `CaptureFrames` | `120` | Frames a capture records. |
| `Meter` | `true` | Publish the `Ion` `System.Diagnostics.Metrics.Meter` (for `dotnet-counters`). |
| `Overlay` | `false` | Draw the metrics overlay (needs `OverlayFont`). |
| `OverlayFont` | none | The font asset the overlay is drawn with. |
| `OverlayFontSize` | `16` | The overlay font size. |
| `OverlayRefreshSeconds` | `0.25` | How often the overlay text is rebuilt. |

```json title="appsettings.json"
{
  "Ion": {
    "Metrics": {
      "Profiling": false,
      "FrameLog": "out/frames.jsonl",
      "Overlay": true,
      "OverlayFont": "Fonts/Mono.ttf"
    }
  }
}
```

## Frame stats

The game loop fills a `FrameStats` for every frame when metrics are installed; `IFrameStatsSource`s add engine counters
(the sprite batch adds draw calls, sprites and triangles; the ECS module adds entities). Read the last completed frame
from `IMetrics.LastFrame`.

| Field | Frame log name | Meaning |
|---|---|---|
| `Frame` | `frame` | The frame number. |
| `DeltaMs` | `delta_ms` | The frame's time step from the loop's clock. |
| `FrameMs` | `frame_ms` | Wall-clock time of the frame, including pacing. |
| `IdleMs` | `idle_ms` | Time slept to honour `MaxFPS`. |
| `WorkMs` | `work_ms` | `FrameMs` without `IdleMs`. |
| `Fps` | `fps` | `1000 / FrameMs`. |
| `FixedSteps` | `fixed_steps` | `FixedUpdate` steps the frame ran. |
| `DrawCalls`, `Sprites`, `Triangles` | `draw_calls`, `sprites`, `triangles` | Sprite batch work. |
| `Entities` | `entities` | Live ECS entities. |
| `EventsEmitted` | `events_emitted` | Events emitted across every channel. |
| `Gc0`, `Gc1`, `Gc2` | `gc_gen0`, `gc_gen1`, `gc_gen2` | Collections during the frame. |
| `AllocatedBytes` | `allocated_bytes` | Bytes allocated by the loop thread. |
| `Spans`, `DroppedSpans` | `spans`, `dropped_spans` | Spans recorded, and those that did not fit (raise `SpansPerFrame` when not 0). |

## Game counters

Register an instrument once, keep the handle, and update it in the hot path without any lookup:

```csharp title="Systems.cs"
using Ion.Extensions.Metrics;

public sealed partial class BallSystem(IMetrics metrics)
{
	private readonly MetricsCounter _bounces = metrics.Counter("bounces", description: "Balls bouncing off a window edge.");
	private readonly MetricsGauge _speed = metrics.Gauge("ball_speed", "px/s");
	private readonly MetricsHistogram _contacts = metrics.Histogram("contacts_per_step");

	[FixedUpdate]
	public void Step(GameTime dt)
	{
		// ...
		_bounces.Increment();
		_speed.Set(240);
		_contacts.Record(3);
	}
}
```

| Instrument | Updated with | Reported as |
|---|---|---|
| `MetricsCounter` | `Increment()`, `Add(delta)` (thread safe) | the running total `Value` |
| `MetricsGauge` | `Set(value)` | the last value set |
| `MetricsHistogram` | `Record(value)` (from the loop thread) | per frame `count`, `sum`, `min`, `max` (reset every frame), plus `TotalCount` |

Registering the same name again returns the same instrument; registering it as a different kind throws. Instruments
appear in the frame log, the overlay, the `Ion` meter, the [run summary](/Ion/tooling/ion-cli/#the-run-summary)'s
`counters`, `IonRunResult.Counters` in [tests](/Ion/tooling/testing/), and the remote `metrics.get` method.

## The frame log

Set `Ion:Metrics:FrameLog` to write one JSON object per frame. Names are snake_case; game instruments are under
`counters`, `gauges` and `histograms`. Writing a line does not allocate once every instrument name has been seen.

```bash
dotnet run -- --headless --Ion:Run:Frames=600 --Ion:Metrics:FrameLog=out/frames.jsonl
```

```json title="out/frames.jsonl"
{"frame":42,"delta_ms":16.667,"frame_ms":0.412,"idle_ms":0,"work_ms":0.412,"fps":2427.18,"fixed_steps":1,"draw_calls":103,"sprites":103,"triangles":206,"entities":0,"events_emitted":2,"gc_gen0":0,"gc_gen1":0,"gc_gen2":0,"allocated_bytes":0,"spans":0,"dropped_spans":0,"counters":{"balls":3}}
```

The log is ideal for plotting frame times or checking that a change stopped a per-frame allocation (`allocated_bytes`
should be `0` in a steady state).

## The overlay

With `Overlay` on and an `OverlayFont` that names a font asset, a Render step at `StageOrder.MetricsOverlay` (800) draws a
translucent box in the top-left corner with the sprite batch of any backend:

```text
2427 fps  0.41 ms  (work 0.41 ms)
draws 103  sprites 103  tris 206
entities 0  events 2  fixed 1
alloc 0 B  gc 0/0/0
bounces 37
ball_speed 240
contacts_per_step n=2 mean=3
```

The text is rebuilt at most every `OverlayRefreshSeconds`. To draw it yourself (in your own UI, for example), resolve
`IMetricsOverlaySource` and read its `Lines`; `Version` changes whenever they do.

## Profiling spans and Chrome traces

When profiling is on, the frame profiler records spans:

- the game loop records one span per frame and per stage (`Init`, `First`, `FixedUpdate`, `Update`, `Render`, `Last`,
  `Destroy`);
- the generated schedule and the runtime schedule record one span per step, named after the step;
- engine systems and your code can add their own.

Profiling has two switches. The **runtime toggle** (`Ion:Metrics:Profiling`, `IMetrics.IsProfiling`, or a capture) is
off by default; with it off, a span costs a branch. The **feature switch** (`IonMetricsProfiling` MSBuild property,
default `true`) removes every recording site at compile time when set to `false`: the schedule generator emits no
brackets and a trimmed or NativeAOT publish drops the rest.

```xml title="MyGame.csproj"
<PropertyGroup>
  <!-- Compile span recording out of this game entirely. -->
  <IonMetricsProfiling>false</IonMetricsProfiling>
</PropertyGroup>
```

:::note[The Trace stage order]
`StageOrder.Trace` (-1000) is reserved: it was the order of the 0.2 trace timer scope around every stage. The game loop
now records a span per stage itself, so nothing needs to run there.
:::

### Getting a trace

| How | What you get |
|---|---|
| `ion trace --frames 300 --out out/trace.json` | Runs headless (or `--windowed`) with profiling on and writes the last N frames at shutdown. |
| `Ion:Metrics:Profiling=true` | Profiling from the start; the kept frames (`HistoryFrames`) are written to `TraceOutput` at shutdown. |
| Press `F9` (`CaptureKey`) in a running game | Records the next `CaptureFrames` frames with profiling on, writes them, and restores the toggle. |
| `IMetrics.Capture(frames, path?)` | The same capture from code. |
| `IMetrics.WriteTrace(path?, frames)` | Writes the kept frames now. Returns the path, or `null` without one. |
| Remote `resources.set` of `Ion.Metrics.Profiling` | Turns the runtime toggle on or off in a running game. |

Open the file in [ui.perfetto.dev](https://ui.perfetto.dev) or `chrome://tracing`. Every frame is a complete event (category
`frame`) with its stats as arguments, plus a `stats` counter event so the counters show as tracks; every span is a
complete event (category `span`) on the thread that recorded it. Timestamps are microseconds from the first frame
written. `IMetrics.LastTracePath` is the last file written. `MetricsExporter.WriteChromeTrace(path, frames)` writes any
list of `FrameProfile`s (for example from `FrameProfiler.CopyFrames`).

:::tip
A capture needs a history: set `HistoryFrames` at least as large as the frames you want. `ion trace` does this for you.
:::

### Custom spans

Register a span name once (a `SpanId`), then open a scope in the hot path. `MetricsScope` is a `ref struct`: it cannot be
boxed and never allocates, and the default value does nothing.

```csharp
public sealed class PathfindingSystem(IMetrics metrics)
{
	private static readonly SpanId Solve = MetricsIds.Register("Pathfinding.Solve");

	[Update]
	public void Update(GameTime dt)
	{
		using var _ = metrics.Scope(Solve);
		// ... the work to measure
	}
}
```

Spans may be recorded from any thread; they land in the frame that is current on the loop thread. `IMetrics.Span(name)`
is the same as `MetricsIds.Register(name)`.

### The 0.2 trace timers

`ITraceManager`, `ITraceTimer` and `ITraceTimer<T>` (namespace `Ion.Extensions.Debug`, shipped in
`Ion.Extensions.Metrics.Abstractions`) still work as obsolete adapters over the
frame profiler and will be removed in 0.4. They intern a name on every `Start` and box an instance per recording; move to
`MetricsScope` with a `SpanId` registered once.

## dotnet-counters

With `Meter` on, the module publishes a `System.Diagnostics.Metrics.Meter` named `Ion`:

| Instrument | Kind |
|---|---|
| `ion.frame.duration` | histogram (ms) |
| `ion.fps` | observable gauge |
| `ion.frames` | observable counter |
| `ion.frame.fixed_steps`, `ion.frame.draw_calls`, `ion.frame.sprites`, `ion.frame.triangles`, `ion.frame.entities`, `ion.frame.events_emitted`, `ion.frame.allocated_bytes`, `ion.frame.gc_gen0`, `ion.frame.gc_gen1`, `ion.frame.gc_gen2` | observable gauges of the last frame |
| every game counter, gauge and histogram | under its registered name |

```bash
dotnet-counters monitor -n MyGame --counters Ion
```

Any OpenTelemetry metrics exporter that listens to the `Ion` meter sees the same values.

## Tracy

`Ion.Extensions.Metrics.Tracy` streams the profiler to a [Tracy](https://github.com/wolfpld/tracy) server: every span
as a zone on its thread, a frame mark per frame, and `frame_ms`, `draw_calls`, `sprites`, `events_emitted` and
`allocated_bytes` as plots. It is a separate project because it has native dependencies (Tracy-CSharp ships TracyClient for
win-x64 and linux-x64). Its bindings are source-generated, so it works under NativeAOT.

```csharp title="Program.cs"
using var game = builder.Build();
game.UseIon().UseSystem<BallSystem>();

#if TRACY
// Built with a reference to Ion.Extensions.Metrics.Tracy and the TRACY symbol defined.
Ion.Extensions.Metrics.Tracy.TracyExtensions.UseMetricsTracy(game);
#endif

game.Run();
```

`UseMetricsTracy` attaches a `TracySpanSink` to the frame profiler and turns profiling on. Call it after the metrics are
registered. The Breakout ECS sample wires it behind `dotnet run -p:IonTracy=true`.

## Metrics in tests and tools

- `IonTestHost.Metrics`, `IonTestHost.LastFrame` and `IonRunResult.Counters` expose everything above in
  [tests](/Ion/tooling/testing/).
- `ion run --summary` records `frameStats`, `lastFrame` and `counters` in the run summary.
- The remote protocol's `metrics.get` (watchable) returns the last frame, counters, gauges, histograms and the profiling
  toggle; the MCP server's `ion_metrics` tool calls it.
- `Ion.Benchmarks` measures the cost of the metrics hot paths themselves (see [Benchmarks](/Ion/tooling/benchmarks/)).

## See also

- [The ion command line](/Ion/tooling/ion-cli/#ion-trace): `ion trace`.
- [Game loop](/Ion/concepts/game-loop/) and [Stages](/Ion/concepts/stages/): what the stage spans cover.
- [Stage order](/Ion/reference/stage-order/): `StageOrder.Metrics`, `MetricsOverlay` and the reserved `Trace`.
- [Benchmarks](/Ion/tooling/benchmarks/).
