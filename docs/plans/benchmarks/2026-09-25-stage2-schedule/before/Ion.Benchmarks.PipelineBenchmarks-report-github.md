```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                      | SystemCount | Mean        | Error      | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------------- |------------ |------------:|-----------:|----------:|------:|--------:|----------:|------------:|
| **DirectCalls_FlatLoop**        | **1**           |   **0.0000 ns** |  **0.0000 ns** | **0.0000 ns** |     **?** |       **?** |         **-** |           **?** |
| Ion_ReflectionBoundPipeline | 1           |   1.1617 ns |  2.1966 ns | 0.1204 ns |     ? |       ? |         - |           ? |
| ManualClosureChain          | 1           |   0.5421 ns |  0.5663 ns | 0.0310 ns |     ? |       ? |         - |           ? |
|                             |             |             |            |           |       |         |           |             |
| **DirectCalls_FlatLoop**        | **8**           |   **3.3137 ns** |  **2.4955 ns** | **0.1368 ns** |  **1.00** |    **0.05** |         **-** |          **NA** |
| Ion_ReflectionBoundPipeline | 8           |  18.8009 ns |  1.9707 ns | 0.1080 ns |  5.68 |    0.20 |         - |          NA |
| ManualClosureChain          | 8           |  15.1610 ns | 12.4811 ns | 0.6841 ns |  4.58 |    0.24 |         - |          NA |
|                             |             |             |            |           |       |         |           |             |
| **DirectCalls_FlatLoop**        | **32**          |  **13.8939 ns** |  **1.4925 ns** | **0.0818 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Ion_ReflectionBoundPipeline | 32          | 110.8605 ns | 23.7018 ns | 1.2992 ns |  7.98 |    0.09 |         - |          NA |
| ManualClosureChain          | 32          |  92.6765 ns | 33.9052 ns | 1.8585 ns |  6.67 |    0.12 |         - |          NA |
