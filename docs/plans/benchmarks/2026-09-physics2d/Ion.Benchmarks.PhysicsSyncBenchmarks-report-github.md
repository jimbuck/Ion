```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                 | Mean       | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------------- |-----------:|---------:|---------:|------:|--------:|----------:|------------:|
| Box2D_SimulateOnly_10k |   709.9 μs | 410.5 μs | 22.50 μs |  1.00 |    0.04 |         - |          NA |
| Box2D_StepWithSync_10k | 1,388.1 μs | 550.7 μs | 30.19 μs |  1.96 |    0.06 |         - |          NA |
| Bepu_SimulateOnly_10k  |   968.4 μs | 495.8 μs | 27.17 μs |  1.37 |    0.05 |         - |          NA |
| Bepu_StepWithSync_10k  | 1,368.9 μs | 437.7 μs | 23.99 μs |  1.93 |    0.06 |         - |          NA |
