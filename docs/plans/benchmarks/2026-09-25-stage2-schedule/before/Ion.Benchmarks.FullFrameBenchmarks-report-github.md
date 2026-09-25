```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean      | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------------------- |----------:|---------:|---------:|------:|--------:|----------:|------------:|
| Step_EventSystemOnly              |  34.80 ns | 13.45 ns | 0.738 ns |  1.00 |    0.03 |         - |          NA |
| Step_8Systems                     | 123.19 ns | 21.51 ns | 1.179 ns |  3.54 |    0.07 |         - |          NA |
| Step_8Systems_DebugTraceInstalled | 133.22 ns | 67.79 ns | 3.716 ns |  3.83 |    0.12 |         - |          NA |
| Step_8Systems_InsideScene         | 183.31 ns | 65.15 ns | 3.571 ns |  5.27 |    0.13 |         - |          NA |
