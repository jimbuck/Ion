```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4
  ShortRun : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                             | Mean        | Error       | StdDev     | Median      | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------------- |------------:|------------:|-----------:|------------:|-------:|--------:|-------:|----------:|------------:|
| Core_NullTraceTimer_StartStop      |   7.4253 ns |   2.5506 ns |  0.1398 ns |   7.4714 ns |  1.000 |    0.02 | 0.0002 |      24 B |        1.00 |
| Debug_TraceTimer_StartStop         |   0.0220 ns |   0.6950 ns |  0.0381 ns |   0.0000 ns |  0.003 |    0.00 |      - |         - |        0.00 |
| Core_NullTraceTimer_StartStop_x100 | 685.1811 ns | 310.4852 ns | 17.0187 ns | 688.5367 ns | 92.299 |    2.50 | 0.0172 |    2400 B |      100.00 |
