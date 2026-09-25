```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4
  ShortRun : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method            | Mean      | Error     | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------ |----------:|----------:|---------:|------:|--------:|----------:|------------:|
| DelegateQuery     | 215.08 μs |  25.16 μs | 1.379 μs |  1.00 |    0.01 |      88 B |        1.00 |
| InlineStructQuery | 206.26 μs |  64.04 μs | 3.510 μs |  0.96 |    0.02 |         - |        0.00 |
| ChunkSpans        |  84.09 μs | 130.41 μs | 7.148 μs |  0.39 |    0.03 |         - |        0.00 |
