```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                        | SystemCount | Mean        | Error      | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------ |------------ |------------:|-----------:|----------:|------:|--------:|----------:|------------:|
| **DirectCalls_FlatLoop**          | **1**           |   **0.1360 ns** |  **1.0634 ns** | **0.0583 ns** |  **1.18** |    **0.74** |         **-** |          **NA** |
| Ion_Schedule_LeafSteps        | 1           |   0.4677 ns |  1.1144 ns | 0.0611 ns |  4.08 |    2.01 |         - |          NA |
| Ion_Schedule_LegacyMiddleware | 1           |   1.4680 ns |  1.5495 ns | 0.0849 ns | 12.79 |    6.14 |         - |          NA |
| ManualClosureChain            | 1           |   0.5927 ns |  1.0184 ns | 0.0558 ns |  5.16 |    2.51 |         - |          NA |
|                               |             |             |            |           |       |         |           |             |
| **DirectCalls_FlatLoop**          | **8**           |   **3.1696 ns** |  **0.4595 ns** | **0.0252 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Ion_Schedule_LeafSteps        | 8           |   8.9847 ns | 13.0859 ns | 0.7173 ns |  2.83 |    0.20 |         - |          NA |
| Ion_Schedule_LegacyMiddleware | 8           |  20.6379 ns |  6.5180 ns | 0.3573 ns |  6.51 |    0.11 |         - |          NA |
| ManualClosureChain            | 8           |  14.9828 ns |  6.3708 ns | 0.3492 ns |  4.73 |    0.10 |         - |          NA |
|                               |             |             |            |           |       |         |           |             |
| **DirectCalls_FlatLoop**          | **32**          |  **13.2447 ns** |  **2.6084 ns** | **0.1430 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Ion_Schedule_LeafSteps        | 32          |  53.7461 ns | 36.2562 ns | 1.9873 ns |  4.06 |    0.14 |         - |          NA |
| Ion_Schedule_LegacyMiddleware | 32          | 116.7188 ns | 34.9058 ns | 1.9133 ns |  8.81 |    0.15 |         - |          NA |
| ManualClosureChain            | 32          |  93.5192 ns | 37.6139 ns | 2.0617 ns |  7.06 |    0.15 |         - |          NA |
