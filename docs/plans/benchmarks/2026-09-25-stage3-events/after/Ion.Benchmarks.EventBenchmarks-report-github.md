```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4


```
| Method                                       | Listeners | Mean        | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------------------------------------------- |---------- |------------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Ion_Emit100_Step**                             | **1**         |    **199.8 ns** |   **3.20 ns** |   **3.00 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Ion_Emit100_ReadAll_Step                     | 1         |    258.0 ns |   5.14 ns |   5.28 ns |  1.29 |    0.03 |         - |          NA |
| Ion_Emit100_TryReadAll_Step                  | 1         |    383.3 ns |   7.61 ns |  13.91 ns |  1.92 |    0.07 |         - |          NA |
| Ion_Emit100_ReadLatest_Step                  | 1         |    208.5 ns |   4.20 ns |   6.53 ns |  1.04 |    0.04 |         - |          NA |
| Ion_IEvents_Emit100_ReadAll_Step             | 1         |    807.2 ns |  16.06 ns |  34.23 ns |  4.04 |    0.18 |         - |          NA |
| Ion_GeneratedBus_Emit100_ReadAll_Step        | 1         |    328.8 ns |   6.50 ns |   8.22 ns |  1.65 |    0.05 |         - |          NA |
| Legacy_Adapters_Emit100_PollAll_Step         | 1         |  2,746.8 ns |  54.39 ns |  48.22 ns | 13.75 |    0.31 |         - |          NA |
| Prototype_TypedChannels_Emit100_PollAll_Step | 1         |    203.7 ns |   4.13 ns |   5.38 ns |  1.02 |    0.03 |         - |          NA |
|                                              |           |             |           |           |       |         |           |             |
| **Ion_Emit100_Step**                             | **8**         |    **201.0 ns** |   **2.53 ns** |   **2.11 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Ion_Emit100_ReadAll_Step                     | 8         |    649.6 ns |  13.01 ns |  22.08 ns |  3.23 |    0.11 |         - |          NA |
| Ion_Emit100_TryReadAll_Step                  | 8         |  1,828.9 ns |  28.75 ns |  25.48 ns |  9.10 |    0.15 |         - |          NA |
| Ion_Emit100_ReadLatest_Step                  | 8         |    235.4 ns |   4.41 ns |   5.88 ns |  1.17 |    0.03 |         - |          NA |
| Ion_IEvents_Emit100_ReadAll_Step             | 8         |  1,227.4 ns |  14.93 ns |  13.23 ns |  6.11 |    0.09 |         - |          NA |
| Ion_GeneratedBus_Emit100_ReadAll_Step        | 8         |    757.7 ns |  11.30 ns |  13.46 ns |  3.77 |    0.08 |         - |          NA |
| Legacy_Adapters_Emit100_PollAll_Step         | 8         | 13,831.1 ns | 272.17 ns | 447.19 ns | 68.83 |    2.30 |         - |          NA |
| Prototype_TypedChannels_Emit100_PollAll_Step | 8         |    605.4 ns |   8.45 ns |   7.49 ns |  3.01 |    0.05 |         - |          NA |
