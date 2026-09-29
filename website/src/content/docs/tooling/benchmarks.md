---
title: Benchmarks
description: Run Ion.Benchmarks to measure the engine's per-frame overhead, and read the recorded results, including the generated schedule against the reflection-bound one.
sidebar:
  order: 8
---

`Ion.Benchmarks` is a [BenchmarkDotNet](https://benchmarkdotnet.org) suite that measures the engine's own per-frame cost
(the "engine tax"), independent of any GPU: schedule dispatch, full headless frames, events, metrics, the 2D and 3D
renderers' CPU work, ECS queries and extraction, UI, physics, networking, the web module and coroutines. Every stage of
the 0.3 roadmap was gated on these numbers, and the recorded results live in
[docs/plans/benchmarks](https://github.com/jimbuck/Ion/tree/main/docs/plans/benchmarks).

## Running

From the repository root:

```bash
# Everything (slow, precise)
dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*'

# A quick pass (3 iterations per benchmark)
dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*' --job short

# One class, or a pattern
dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*PipelineBenchmarks*' --job short
dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*Network*' --job short

# List the benchmarks without running them
dotnet run -c Release --project Ion/Ion.Benchmarks -- --list flat
```

Or with the [ion tool](/Ion/tooling/ion-cli/#ion-bench), which finds the `*.Benchmarks.csproj` for you, always builds
Release, and wraps a filter without `*` as `*filter*`:

```bash
ion bench FullFrame -- --job short
ion bench '*SpriteBatch*'
```

Inside the repository, `npm run bench -- --filter '*Schedule*' --job short` is the same `dotnet run` (see the dev
tasks table in the root `README.md`).

Results land in `BenchmarkDotNet.Artifacts/results/` under the working directory (Markdown, CSV and HTML reports).

:::caution[Always Release, always quiet]
Benchmark in Release (`ion bench` does), on an idle machine, plugged in. Many recorded numbers come from a shared 4-core
VM and a `--job short` run: treat them as orders of magnitude and compare rows measured in the same run, not across
machines.
:::

`SpriteBatchBenchmarks` has an extra mode for noisy machines: `-- --min-of-n sprites` runs its cases interleaved many
times in one process and keeps the best, which is robust for A/B checks while optimizing.

## What is measured

| Class | What it measures |
|---|---|
| `PipelineBenchmarks` | Dispatch cost of a stage with 1, 8 and 32 systems: direct calls, the runtime-bound schedule (leaf steps), a hand-built closure chain (the pre-0.3 middleware shape), and the generated schedule. |
| `PipelineBuildBenchmarks` | Startup cost of building the host, binding systems and building the stage pipelines. |
| `InliningBenchmarks` | Closure chain against a struct-generic (constrained call) chain prototype and direct calls, depth 8. |
| `FullFrameBenchmarks` | One headless `GameLoop.Step`: events only, 8 systems (runtime and generated schedule), with the metrics module, with a frame profiler (stats only, and profiling every step), and inside a scene scope. |
| `EventBenchmarks` | Emit and read cost of the event bus: the runtime `EventBus`, through `IEvents`, the generated bus, the obsolete adapters, and the typed-channel prototype it was designed from. |
| `MetricsBenchmarks` | `MetricsScope` and the generated `Begin`/`End` bracket with profiling off and on, a counter increment, the once-per-frame stats write, the obsolete `ITraceTimer` adapter. |
| `SpriteBatchBenchmarks` | CPU cost of a 10k-sprite frame across 1 and 16 textures: the 2D renderer's `SpriteBatch` (Deferred, with the upload copy, Texture and BackToFront sorts) against the removed Veldrid batcher's per-sprite work. |
| `Renderer3DBenchmarks` | The 3D renderer's CPU cost for 10k mesh renderers: submission, and extract plus queue (culling, sort keys, instancing, the shadow fit), without shadows and with two cameras. |
| `Scene3DExtractionBenchmarks` | ECS 3D extraction of 10k mesh entities against the same submissions from flat arrays. |
| `EcsQueryBenchmarks`, `TransformPropagationBenchmarks`, `SpriteExtractionBenchmarks` | The ECS module's queries, transform propagation and 2D sprite extraction. |
| `ArchQueryBenchmarks` | Arch 2.1 delegate query against an inline struct query and chunk spans over 10k entities. |
| `EcsComparisonBenchmarks` | Arch 2.1 against Friflo.Engine.ECS 3.6: iteration styles, entity creation and structural churn on 10k entities. |
| `UiBenchmarks` | A 500-widget UI frame: building, flex layout, tree publication, and the draw submission. |
| `Physics2DStepBenchmarks`, `Physics3DStepBenchmarks`, `PhysicsSyncBenchmarks` | Physics steps and the ECS synchronization around them. |
| `NetworkSnapshotCaptureBenchmarks`, `NetworkDeltaBenchmarks`, `NetworkMessageBenchmarks` | Snapshot capture, delta encoding and decoding, a loopback round trip, and message dispatch. |
| `WebBenchmarks` | The web module on loopback: a keep-alive `GET` round trip and WebSocket push throughput. |
| `CoroutineBenchmarks` | Stepping 100 coroutines that yield every frame. |

`Ion.Benchmarks` itself is **not** compiled with the schedule generator, so its `Ion_*` rows keep measuring the
runtime-bound schedule. The generated cases come from `Ion.Benchmarks.GeneratedApp`, a small library of applications
compiled with the generator (`GeneratedApps.cs`, `GeneratedEvents.cs`, `GeneratedQueries.cs`).

## Results: generated against runtime-bound schedule

The schedule generator (see [Source generators](/Ion/concepts/source-generators/)) compiles the registrations in
`Program.cs` into direct calls. These are the recorded numbers of the stage that introduced it
([2026-09-25-stage2-generator](https://github.com/jimbuck/Ion/tree/main/docs/plans/benchmarks/2026-09-25-stage2-generator)):
BenchmarkDotNet 0.15.8, .NET 10.0.12, Intel Xeon 2.1 GHz VM (4 cores), `--job short`.

### Dispatch of one stage (`PipelineBenchmarks`)

| Systems | Direct calls | Generated schedule | Runtime schedule (leaf steps) | Closure chain |
|---:|---:|---:|---:|---:|
| 1 | 0.33 ns | 0.38 ns | 0.42 ns | 0.45 ns |
| 8 | 3.50 ns | 4.34 ns | 8.15 ns | 15.11 ns |
| 32 | 13.32 ns | 15.75 ns | 67.42 ns | 92.06 ns |

At 32 systems the generated schedule is 1.18x the cost of a hand-written loop of direct calls, against about 5x for the
runtime-bound schedule. Every row allocates 0 bytes.

For reference, before the 0.3 schedule rewrite the reflection-bound middleware pipeline cost 1.16 ns, 18.80 ns and
110.86 ns for 1, 8 and 32 systems
([2026-09-25-stage2-schedule/before](https://github.com/jimbuck/Ion/tree/main/docs/plans/benchmarks/2026-09-25-stage2-schedule/before)).

### One full headless frame (`FullFrameBenchmarks`)

| Case | Mean | Ratio |
|---|---:|---:|
| `Step_EventSystemOnly` | 34.27 ns | 1.00 |
| `Step_8Systems` (runtime-bound) | 125.30 ns | 3.66 |
| `Step_8Systems_GeneratedSchedule` | 45.63 ns | 1.33 |
| `Step_8Systems_InsideScene` | 145.50 ns | 4.25 |

With the generator, eight systems add about 11 ns to an empty frame instead of about 91 ns.

## Results: metrics overhead

From [2026-09-25-stage3-metrics](https://github.com/jimbuck/Ion/tree/main/docs/plans/benchmarks/2026-09-25-stage3-metrics)
(default job, same VM):

| Case | Mean |
|---|---:|
| `MetricsScope`, profiling off | 0.77 ns |
| `MetricsScope`, profiling on | 76.1 ns |
| Generated `Begin`/`End` bracket, off | 0.61 ns |
| Generated `Begin`/`End` bracket, on | 73.9 ns |
| `MetricsCounter.Increment` | 6.57 ns |
| Once-per-frame stats write | 112.5 ns |
| Legacy `ITraceTimer`, on | 147.7 ns |

A disabled span is essentially free; an enabled one is dominated by two `Stopwatch.GetTimestamp()` reads, which are
about 40 ns each on that VM (15 to 20 ns on bare metal). In the same run, a frame with 8 systems on the generated
schedule cost 62.9 ns, 180.7 ns with frame stats collected, and 3,573 ns with every step profiled. See
[Metrics and tracing](/Ion/tooling/metrics-and-tracing/) for turning profiling on only when you need it.

## Other recorded results

| Area | Result | Source |
|---|---|---|
| 2D sprite batch | 10k sprites in 56.6 us (1 texture) and 69.1 us (16 textures) deferred, 0.46x and 0.47x the old batcher's per-sprite work; 0 B per frame | `2026-09-25-stage4-rendering2d` |
| 3D renderer | 10k mesh renderers, 3 materials: extract plus queue 1.20 ms with shadows, 0.88 ms without, 1.42 ms with two cameras; 0 B per frame | `2026-09-26-stage5-rendering3d` |
| ECS 3D extraction | 10k mesh entities extracted in 82 us against 64 us from flat arrays | `2026-09-26-stage5-ecs3d` |
| Networking | Snapshot capture of 10k entities 156 us; delta encode and decode of 10k values about 43 us each; loopback round trip with 10k entities 300 us (1 % moving); 0 B everywhere | `2026-09-26-stage6b-networking` |
| Publish (NativeAOT) | Breakout ECS on linux-x64: 12.6 MB executable, 45.6 ms median startup to one headless frame | `2026-09-stage7-publish` |

The physics library decision (Box2D v3 against Aether.Physics2D at 10,000 bodies, x64 and arm64) and its replay
determinism checks are in `2026-09-physics2d`.

## Writing a benchmark

Benchmarks are plain BenchmarkDotNet classes in `Ion/Ion.Benchmarks`; `Program.cs` runs every class in the assembly
through `BenchmarkSwitcher`. Follow the existing ones: build the host or world in `[GlobalSetup]`, measure one frame or
one operation per invocation, add `[MemoryDiagnoser]` so allocations show, and mark the baseline row.

```csharp title="Ion/Ion.Benchmarks/MyBenchmarks.cs"
namespace Ion.Benchmarks;

[MemoryDiagnoser]
public class MyBenchmarks
{
	private int[] _values = [];

	[GlobalSetup]
	public void Setup() => _values = Enumerable.Range(0, 10_000).ToArray();

	[Benchmark(Baseline = true)]
	public int Loop()
	{
		var sum = 0;
		foreach (var v in _values) sum += v;
		return sum;
	}
}
```

To measure code compiled with the schedule generator, put the application in `Ion.Benchmarks.GeneratedApp` and call it
from a benchmark. When you record results for a change, save the `*-report-github.md` files under
`docs/plans/benchmarks/<date>-<topic>/` with a `README.md` stating the machine, the job and the command.

## See also

- [Metrics and tracing](/Ion/tooling/metrics-and-tracing/): profiling a real game rather than a micro-benchmark.
- [Source generators](/Ion/concepts/source-generators/): what the generated schedule is.
- [The ion command line](/Ion/tooling/ion-cli/#ion-bench): `ion bench`.
- [NativeAOT](/Ion/platforms/native-aot/) and [Publishing](/Ion/platforms/publishing/): size and startup.
