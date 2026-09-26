# Ion.Benchmarks

BenchmarkDotNet micro-benchmarks that measure the engine's own per-frame overhead ("engine tax"), independent of any GPU:

| Class | What it measures |
|---|---|
| `PipelineBenchmarks` | Dispatch cost of the reflection-bound middleware chain for 1/8/32 systems vs a hand-built closure chain vs direct calls |
| `PipelineBuildBenchmarks` | Startup cost of building the host, binding systems and building the seven stage pipelines |
| `InliningBenchmarks` | Closure chain vs a struct-generic (constrained call) chain prototype vs direct calls, depth 8 |
| `FullFrameBenchmarks` | One headless `GameLoop.Step` with events only, with 8 systems (runtime and generated schedule), with the metrics module, with a frame profiler (stats only, and profiling every step), and inside a scene scope |
| `EventBenchmarks` | Emit/read cost and allocations of Events v2: the runtime `EventBus` (direct and through `IEvents`), the generated bus of `Ion.Benchmarks.GeneratedApp`, the obsolete `IEventEmitter`/`IEventListener` adapters, and the typed-channel prototype it was designed from |
| `MetricsBenchmarks` | Metrics v2 hot paths: `MetricsScope` and the generated `Begin`/`End` bracket with profiling off and on, a game counter increment, the once-per-frame stats write, and the obsolete `ITraceTimer` adapter |
| `SpriteBatchBenchmarks` | CPU cost of a 10k-sprite frame across 1/16 textures: the 2D renderer's `SpriteBatch` (Deferred, with the upload copy, Texture and BackToFront sorts) against a copy of the removed Veldrid batcher's per-sprite work (with and without its scissor transform); `--min-of-n sprites` runs them interleaved for noisy machines |
| `Renderer3DBenchmarks` | CPU cost of the 3D renderer for 10k `MeshRenderer`s (3 materials, 2 meshes) without a GPU: submission alone, and extract plus queue (world bounds, frustum culling, sort keys and sort, instanced batches and instance data, the directional shadow fit and caster batches), without shadows and with two cameras |
| `Scene3DExtractionBenchmarks` | The ECS 3D extraction of 10k mesh entities (plus a camera and a light) into the CPU-only 3D renderer, against the same submissions from flat arrays; with the renderer's CPU pipeline, and with the transform propagation's unchanged pass |
| `CoroutineBenchmarks` | Stepping 100 coroutines that yield `Wait.For` every frame, as `IEnumerator<Wait>` (unboxed) and as plain `IEnumerator` |
| `ArchQueryBenchmarks` | Arch 2.1 delegate query vs inline struct query vs chunk spans over 10k entities |
| `EcsComparisonBenchmarks` | Arch 2.1 vs Friflo.Engine.ECS 3.6: iteration styles, entity creation and structural churn on 10k entities |

Run everything (slow, precise):

```sh
dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*'
```

Quick pass:

```sh
dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*' --job short
```

Results land in `BenchmarkDotNet.Artifacts/results/` next to the working directory.
