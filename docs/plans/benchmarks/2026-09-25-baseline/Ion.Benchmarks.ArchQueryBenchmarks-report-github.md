```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4
  ShortRun : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method            | Mean     | Error     | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------ |---------:|----------:|---------:|------:|--------:|----------:|------------:|
| DelegateQuery     | 82.47 μs | 40.759 μs | 2.234 μs |  1.00 |    0.03 |      88 B |        1.00 |
| InlineStructQuery | 79.79 μs |  6.219 μs | 0.341 μs |  0.97 |    0.02 |         - |        0.00 |
| ChunkSpans        | 86.28 μs | 30.658 μs | 1.680 μs |  1.05 |    0.03 |         - |        0.00 |
