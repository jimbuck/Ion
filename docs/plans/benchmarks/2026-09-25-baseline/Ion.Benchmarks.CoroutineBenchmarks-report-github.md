```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4
  ShortRun : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method              | Coroutines | Mean     | Error    | StdDev    | Gen0   | Allocated |
|-------------------- |----------- |---------:|---------:|----------:|-------:|----------:|
| Update100Coroutines | 100        | 2.483 μs | 1.912 μs | 0.1048 μs | 0.0153 |   2.34 KB |
