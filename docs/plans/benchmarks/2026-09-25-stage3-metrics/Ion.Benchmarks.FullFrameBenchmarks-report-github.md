```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4


```
| Method                                     | Mean        | Error     | StdDev     | Median      | Ratio  | RatioSD | Allocated | Alloc Ratio |
|------------------------------------------- |------------:|----------:|-----------:|------------:|-------:|--------:|----------:|------------:|
| Step_EventSystemOnly                       |    15.07 ns |  0.343 ns |   0.738 ns |    15.08 ns |   1.00 |    0.07 |         - |          NA |
| Step_8Systems                              |    99.63 ns |  2.042 ns |   3.629 ns |   100.02 ns |   6.63 |    0.41 |         - |          NA |
| Step_8Systems_GeneratedSchedule            |    62.90 ns |  1.314 ns |   2.742 ns |    63.10 ns |   4.18 |    0.28 |         - |          NA |
| Step_8Systems_MetricsInstalled             |   253.81 ns |  4.884 ns |  10.513 ns |   250.02 ns |  16.88 |    1.09 |         - |          NA |
| Step_8Systems_GeneratedSchedule_FrameStats |   180.67 ns |  3.648 ns |   7.696 ns |   180.72 ns |  12.02 |    0.78 |         - |          NA |
| Step_8Systems_GeneratedSchedule_Profiling  | 3,572.96 ns | 70.021 ns | 124.462 ns | 3,570.98 ns | 237.61 |   14.37 |         - |          NA |
| Step_8Systems_InsideScene                  |   123.08 ns |  2.501 ns |   5.051 ns |   122.41 ns |   8.19 |    0.53 |         - |          NA |
