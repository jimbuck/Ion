```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4


```
| Method                             | Mean        | Error     | StdDev    | Ratio  | RatioSD | Allocated | Alloc Ratio |
|----------------------------------- |------------:|----------:|----------:|-------:|--------:|----------:|------------:|
| Scope_Disabled                     |   0.7683 ns | 0.0140 ns | 0.0226 ns |   1.00 |    0.04 |         - |          NA |
| Scope_Enabled                      |  76.1243 ns | 1.5101 ns | 1.7391 ns |  99.16 |    3.58 |         - |          NA |
| BeginEnd_GeneratedBracket_Disabled |   0.6103 ns | 0.0121 ns | 0.0153 ns |   0.79 |    0.03 |         - |          NA |
| BeginEnd_GeneratedBracket_Enabled  |  73.9423 ns | 0.9277 ns | 0.8224 ns |  96.32 |    2.93 |         - |          NA |
| Counter_Increment                  |   6.5693 ns | 0.1298 ns | 0.1214 ns |   8.56 |    0.29 |         - |          NA |
| FrameStats_Write                   | 112.5105 ns | 2.2999 ns | 3.8426 ns | 146.56 |    6.46 |         - |          NA |
| LegacyTraceTimer_Disabled          |   0.8820 ns | 0.0167 ns | 0.0326 ns |   1.15 |    0.05 |         - |          NA |
| LegacyTraceTimer_Enabled           | 147.7396 ns | 2.9424 ns | 3.9280 ns | 192.45 |    7.42 |         - |          NA |
