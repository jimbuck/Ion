```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4
  ShortRun : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------------- |----------:|----------:|---------:|------:|--------:|-------:|----------:|------------:|
| Step_EventSystemOnly              |  24.35 ns |  6.651 ns | 0.365 ns |  1.00 |    0.02 | 0.0001 |      24 B |        1.00 |
| Step_8Systems                     | 124.89 ns |  6.957 ns | 0.381 ns |  5.13 |    0.07 |      - |      24 B |        1.00 |
| Step_8Systems_DebugTraceInstalled | 119.11 ns | 17.749 ns | 0.973 ns |  4.89 |    0.07 |      - |         - |        0.00 |
| Step_8Systems_InsideScene         | 197.16 ns | 82.583 ns | 4.527 ns |  8.10 |    0.19 | 0.0010 |     144 B |        6.00 |
