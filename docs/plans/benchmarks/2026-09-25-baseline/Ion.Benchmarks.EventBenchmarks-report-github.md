```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4
  ShortRun : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                       | Listeners | Mean           | Error        | StdDev       | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------------- |---------- |---------------:|-------------:|-------------:|-------:|--------:|-------:|----------:|------------:|
| **Ion_Emit100_Step**                             | **1**         |     **2,408.1 ns** |     **895.0 ns** |     **49.06 ns** |   **1.00** |    **0.02** | **0.0229** |    **3600 B** |        **1.00** |
| Ion_Emit100_PollAllListeners_Step            | 1         |   190,963.2 ns | 131,437.5 ns |  7,204.53 ns |  79.32 |    2.94 |      - |    3600 B |        1.00 |
| Ion_Emit100_OnLatestAllListeners_Step        | 1         |     6,984.1 ns |   2,084.7 ns |    114.27 ns |   2.90 |    0.07 | 0.0153 |    3600 B |        1.00 |
| Prototype_TypedChannels_Emit100_PollAll_Step | 1         |       230.6 ns |     128.0 ns |      7.02 ns |   0.10 |    0.00 |      - |         - |        0.00 |
|                                              |           |                |              |              |        |         |        |           |             |
| **Ion_Emit100_Step**                             | **8**         |     **2,373.6 ns** |     **127.4 ns** |      **6.98 ns** |   **1.00** |    **0.00** | **0.0229** |    **3600 B** |        **1.00** |
| Ion_Emit100_PollAllListeners_Step            | 8         | 1,322,247.2 ns | 690,579.1 ns | 37,852.97 ns | 557.06 |   13.88 |      - |    3600 B |        1.00 |
| Ion_Emit100_OnLatestAllListeners_Step        | 8         |    39,959.8 ns |  16,281.1 ns |    892.42 ns |  16.84 |    0.33 |      - |    3600 B |        1.00 |
| Prototype_TypedChannels_Emit100_PollAll_Step | 8         |       582.3 ns |     218.9 ns |     12.00 ns |   0.25 |    0.00 |      - |         - |        0.00 |
