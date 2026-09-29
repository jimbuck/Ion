```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4


```
| Method                         | Mean        | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------- |------------:|----------:|----------:|------:|--------:|----------:|------------:|
| Extract_Submit10k              |    61.01 μs |  1.194 μs |  1.552 μs |  0.05 |    0.00 |         - |          NA |
| ExtractAndQueue_10k            | 1,195.28 μs | 18.459 μs | 16.363 μs |  1.00 |    0.02 |         - |          NA |
| ExtractAndQueue_10k_NoShadows  |   880.68 μs | 16.873 μs | 15.783 μs |  0.74 |    0.02 |         - |          NA |
| ExtractAndQueue_10k_TwoCameras | 1,417.56 μs | 16.012 μs | 13.371 μs |  1.19 |    0.02 |         - |          NA |
