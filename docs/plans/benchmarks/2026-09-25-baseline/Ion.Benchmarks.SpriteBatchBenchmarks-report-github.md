```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4
  ShortRun : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                             | Textures | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------------------------- |--------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Add10kSprites**                      | **1**        | **269.1 μs** | **199.7 μs** | **10.95 μs** |  **1.00** |    **0.05** |         **-** |          **NA** |
| Add10kSprites_WithScissorTransform | 1        | 280.5 μs | 104.6 μs |  5.73 μs |  1.04 |    0.04 |         - |          NA |
|                                    |          |          |          |          |       |         |           |             |
| **Add10kSprites**                      | **16**       | **267.2 μs** | **138.7 μs** |  **7.61 μs** |  **1.00** |    **0.03** |         **-** |          **NA** |
| Add10kSprites_WithScissorTransform | 16       | 295.3 μs | 316.8 μs | 17.36 μs |  1.11 |    0.06 |         - |          NA |
