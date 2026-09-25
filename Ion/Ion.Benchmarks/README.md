# Ion.Benchmarks

BenchmarkDotNet micro-benchmarks that measure the engine's own per-frame overhead ("engine tax"), independent of any GPU:

| Class | What it measures |
|---|---|
| `PipelineBenchmarks` | Dispatch cost of the reflection-bound middleware chain for 1/8/32 systems vs a hand-built closure chain vs direct calls |
| `PipelineBuildBenchmarks` | Startup cost of building the host, binding systems and building the seven stage pipelines |
| `InliningBenchmarks` | Closure chain vs a struct-generic (constrained call) chain prototype vs direct calls, depth 8 |
| `FullFrameBenchmarks` | One headless `GameLoop.Step` with events only, with 8 systems, with the Debug trace package, and inside a scene scope |
| `EventBenchmarks` | Emit/poll cost and allocations of the boxed `IEvent` ring buffer vs an unboxed typed-channel prototype |
| `TraceBenchmarks` | `trace.Start()/Stop()` cost for the Core null timer and the Debug package timer |
| `SpriteBatchBenchmarks` | CPU cost of batching 10k sprites across 1/16 textures, with and without the per-sprite scissor transform |
| `CoroutineBenchmarks` | Stepping 100 coroutines that yield `Wait.For` every frame |
| `ArchQueryBenchmarks` | Arch 1.2.8 delegate query vs inline struct query vs chunk spans over 10k entities |

Run everything (slow, precise):

```sh
dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*'
```

Quick pass:

```sh
dotnet run -c Release --project Ion/Ion.Benchmarks -- --filter '*' --job short
```

Results land in `BenchmarkDotNet.Artifacts/results/` next to the working directory.
