```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4
  ShortRun : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                      | SystemCount | Mean        | Error      | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------------- |------------ |------------:|-----------:|----------:|------:|--------:|----------:|------------:|
| **DirectCalls_FlatLoop**        | **1**           |   **0.3996 ns** |  **1.0045 ns** | **0.0551 ns** |  **1.01** |    **0.17** |         **-** |          **NA** |
| Ion_ReflectionBoundPipeline | 1           |   1.0463 ns |  0.9862 ns | 0.0541 ns |  2.65 |    0.33 |         - |          NA |
| ManualClosureChain          | 1           |   0.5635 ns |  0.6563 ns | 0.0360 ns |  1.43 |    0.18 |         - |          NA |
|                             |             |             |            |           |       |         |           |             |
| **DirectCalls_FlatLoop**        | **8**           |   **3.5344 ns** |  **3.2578 ns** | **0.1786 ns** |  **1.00** |    **0.06** |         **-** |          **NA** |
| Ion_ReflectionBoundPipeline | 8           |  19.9103 ns |  8.3805 ns | 0.4594 ns |  5.64 |    0.27 |         - |          NA |
| ManualClosureChain          | 8           |  15.3511 ns |  4.6046 ns | 0.2524 ns |  4.35 |    0.20 |         - |          NA |
|                             |             |             |            |           |       |         |           |             |
| **DirectCalls_FlatLoop**        | **32**          |  **14.4675 ns** |  **7.7024 ns** | **0.4222 ns** |  **1.00** |    **0.04** |         **-** |          **NA** |
| Ion_ReflectionBoundPipeline | 32          | 112.5980 ns | 52.0393 ns | 2.8525 ns |  7.79 |    0.26 |         - |          NA |
| ManualClosureChain          | 32          |  93.0860 ns |  3.1642 ns | 0.1734 ns |  6.44 |    0.16 |         - |          NA |
