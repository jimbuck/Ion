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
| **DirectCalls_FlatLoop**          | **1**           |   **0.3322 ns** |  **1.5756 ns** | **0.0864 ns** |  **1.04** |    **0.32** |         **-** |          **NA** |
| Ion_Schedule_LeafSteps        | 1           |   0.4150 ns |  1.7499 ns | 0.0959 ns |  1.30 |    0.38 |         - |          NA |
| Ion_GeneratedSchedule         | 1           |   0.3769 ns |  0.7311 ns | 0.0401 ns |  1.18 |    0.27 |         - |          NA |
| Ion_Schedule_LegacyMiddleware | 1           |   1.1191 ns |  1.7548 ns | 0.0962 ns |  3.52 |    0.79 |         - |          NA |
| ManualClosureChain            | 1           |   0.4498 ns |  1.0971 ns | 0.0601 ns |  1.41 |    0.34 |         - |          NA |
|                               |             |             |            |           |       |         |           |             |
| **DirectCalls_FlatLoop**          | **8**           |   **3.5047 ns** |  **2.0334 ns** | **0.1115 ns** |  **1.00** |    **0.04** |         **-** |          **NA** |
| Ion_Schedule_LeafSteps        | 8           |   8.1513 ns |  0.6490 ns | 0.0356 ns |  2.33 |    0.06 |         - |          NA |
| Ion_GeneratedSchedule         | 8           |   4.3443 ns |  3.8519 ns | 0.2111 ns |  1.24 |    0.06 |         - |          NA |
| Ion_Schedule_LegacyMiddleware | 8           |  18.0056 ns |  0.9573 ns | 0.0525 ns |  5.14 |    0.14 |         - |          NA |
| ManualClosureChain            | 8           |  15.1123 ns |  7.0283 ns | 0.3852 ns |  4.31 |    0.15 |         - |          NA |
|                               |             |             |            |           |       |         |           |             |
| **DirectCalls_FlatLoop**          | **32**          |  **13.3240 ns** |  **4.3041 ns** | **0.2359 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Ion_Schedule_LeafSteps        | 32          |  67.4207 ns | 82.6917 ns | 4.5326 ns |  5.06 |    0.30 |         - |          NA |
| Ion_GeneratedSchedule         | 32          |  15.7498 ns |  2.4276 ns | 0.1331 ns |  1.18 |    0.02 |         - |          NA |
| Ion_Schedule_LegacyMiddleware | 32          | 112.2234 ns | 40.5993 ns | 2.2254 ns |  8.42 |    0.19 |         - |          NA |
| ManualClosureChain            | 32          |  92.0648 ns |  6.5043 ns | 0.3565 ns |  6.91 |    0.11 |         - |          NA |
