```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4
  ShortRun : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                       | SystemCount | Mean     | Error     | StdDev    | Allocated |
|----------------------------- |------------ |---------:|----------:|----------:|----------:|
| **BuildApplicationAndPipelines** | **8**           | **1.284 ms** | **0.6040 ms** | **0.0331 ms** | **295.78 KB** |
| **BuildApplicationAndPipelines** | **32**          | **2.008 ms** | **1.4339 ms** | **0.0786 ms** | **450.89 KB** |
