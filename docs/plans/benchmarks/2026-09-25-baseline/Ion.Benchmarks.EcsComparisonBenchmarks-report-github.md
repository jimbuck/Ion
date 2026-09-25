```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4
  ShortRun : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                     | Mean        | Error        | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|--------------------------- |------------:|-------------:|----------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
| Arch_Iterate_Delegate      |    87.27 μs |    76.551 μs |  4.196 μs |  1.00 |    0.06 |        - |        - |        - |      88 B |        1.00 |
| Friflo_Iterate_Delegate    |    94.53 μs |    58.209 μs |  3.191 μs |  1.08 |    0.06 |        - |        - |        - |      88 B |        1.00 |
| Arch_Iterate_StructFunctor |    77.05 μs |     6.723 μs |  0.369 μs |  0.88 |    0.04 |        - |        - |        - |         - |        0.00 |
| Arch_Iterate_ChunkSpans    |    81.31 μs |    41.309 μs |  2.264 μs |  0.93 |    0.05 |        - |        - |        - |         - |        0.00 |
| Friflo_Iterate_ChunkSpans  |    82.34 μs |    66.727 μs |  3.658 μs |  0.94 |    0.05 |        - |        - |        - |         - |        0.00 |
| Arch_Create10k             |   457.20 μs |   248.259 μs | 13.608 μs |  5.25 |    0.26 |   4.3945 |   2.9297 |        - |  632200 B |    7,184.09 |
| Friflo_Create10k           |   797.19 μs |   640.423 μs | 35.104 μs |  9.15 |    0.52 | 399.4141 | 399.4141 | 399.4141 | 1692017 B |   19,227.47 |
| Arch_AddRemoveTag10k       |   122.24 μs |    57.736 μs |  3.165 μs |  1.40 |    0.07 |        - |        - |        - |         - |        0.00 |
| Friflo_AddRemoveTag10k     | 1,040.78 μs | 1,660.021 μs | 90.991 μs | 11.94 |    1.03 |        - |        - |        - |         - |        0.00 |
