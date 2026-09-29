```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method | Bodies | Mean        | Error       | StdDev    | Allocated |
|------- |------- |------------:|------------:|----------:|----------:|
| **Step**   | **1000**   |    **773.0 μs** |    **392.1 μs** |  **21.49 μs** |         **-** |
| **Step**   | **10000**  | **14,269.9 μs** | **16,640.4 μs** | **912.12 μs** |         **-** |
