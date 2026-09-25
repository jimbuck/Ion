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
| Step_EventSystemOnly              |  34.50 ns |  9.357 ns | 0.513 ns |  1.00 |    0.02 |         - |          NA |
| Step_8Systems                     | 127.44 ns | 57.234 ns | 3.137 ns |  3.69 |    0.09 |         - |          NA |
| Step_8Systems_DebugTraceInstalled | 127.50 ns | 32.822 ns | 1.799 ns |  3.70 |    0.07 |         - |          NA |
| Step_8Systems_InsideScene         | 153.95 ns | 92.532 ns | 5.072 ns |  4.46 |    0.14 |         - |          NA |
