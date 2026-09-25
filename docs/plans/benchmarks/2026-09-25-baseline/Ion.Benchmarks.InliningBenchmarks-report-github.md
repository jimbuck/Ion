```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4
  ShortRun : .NET 8.0.31 (8.0.31, 8.0.3126.42015), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                               | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| DirectCalls_8                        |  3.998 ns |  4.464 ns | 0.2447 ns |  1.00 |    0.07 |         - |          NA |
| ClosureChain_8                       | 15.512 ns | 11.816 ns | 0.6477 ns |  3.89 |    0.24 |         - |          NA |
| StructGenericChain_8                 | 12.529 ns |  3.244 ns | 0.1778 ns |  3.14 |    0.17 |         - |          NA |
| StructGenericChain_ClassConstraint_8 | 83.108 ns | 67.274 ns | 3.6875 ns | 20.84 |    1.33 |         - |          NA |
