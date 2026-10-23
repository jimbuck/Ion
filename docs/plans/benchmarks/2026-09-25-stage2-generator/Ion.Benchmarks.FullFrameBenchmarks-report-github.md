```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean      | Error     | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------------------- |----------:|----------:|---------:|------:|--------:|----------:|------------:|
| Step_EventSystemOnly              |  34.27 ns |  3.722 ns | 0.204 ns |  1.00 |    0.01 |         - |          NA |
| Step_8Systems                     | 125.30 ns | 15.329 ns | 0.840 ns |  3.66 |    0.03 |         - |          NA |
| Step_8Systems_GeneratedSchedule   |  45.63 ns | 23.125 ns | 1.268 ns |  1.33 |    0.03 |         - |          NA |
| Step_8Systems_DebugTraceInstalled | 129.83 ns | 39.634 ns | 2.172 ns |  3.79 |    0.06 |         - |          NA |
| Step_8Systems_InsideScene         | 145.50 ns | 79.918 ns | 4.381 ns |  4.25 |    0.11 |         - |          NA |
