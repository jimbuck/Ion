```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4


```
| Method                                       | Listeners | Mean           | Error        | StdDev       | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------------- |---------- |---------------:|-------------:|-------------:|-------:|--------:|-------:|----------:|------------:|
| **Ion_Emit100_Step**                             | **1**         |     **2,391.9 ns** |     **47.05 ns** |     **99.24 ns** |   **1.00** |    **0.06** | **0.0267** |    **3600 B** |        **1.00** |
| Ion_Emit100_PollAllListeners_Step            | 1         |   180,010.3 ns |  3,486.11 ns |  5,217.84 ns |  75.38 |    3.73 |      - |    3600 B |        1.00 |
| Ion_Emit100_OnLatestAllListeners_Step        | 1         |     7,165.6 ns |    141.74 ns |    194.02 ns |   3.00 |    0.15 | 0.0229 |    3600 B |        1.00 |
| Prototype_TypedChannels_Emit100_PollAll_Step | 1         |       204.0 ns |      3.83 ns |      3.58 ns |   0.09 |    0.00 |      - |         - |        0.00 |
|                                              |           |                |              |              |        |         |        |           |             |
| **Ion_Emit100_Step**                             | **8**         |     **2,449.4 ns** |     **46.48 ns** |     **51.66 ns** |   **1.00** |    **0.03** | **0.0229** |    **3600 B** |        **1.00** |
| Ion_Emit100_PollAllListeners_Step            | 8         | 1,491,723.1 ns | 29,326.02 ns | 46,514.17 ns | 609.27 |   22.45 |      - |    3600 B |        1.00 |
| Ion_Emit100_OnLatestAllListeners_Step        | 8         |    37,657.3 ns |    752.74 ns |  1,079.56 ns |  15.38 |    0.53 |      - |    3600 B |        1.00 |
| Prototype_TypedChannels_Emit100_PollAll_Step | 8         |       625.9 ns |     10.97 ns |     16.75 ns |   0.26 |    0.01 |      - |         - |        0.00 |
