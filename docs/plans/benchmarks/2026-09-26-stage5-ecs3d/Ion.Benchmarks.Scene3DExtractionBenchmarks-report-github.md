```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4


```
| Method                          | Mean        | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------------- |------------:|----------:|----------:|------:|--------:|----------:|------------:|
| Submit10k_Arrays                |    63.86 μs |  0.824 μs |  0.731 μs |  1.00 |    0.02 |         - |          NA |
| Extract10k                      |    81.70 μs |  1.554 μs |  1.378 μs |  1.28 |    0.03 |         - |          NA |
| ExtractAndQueue10k              | 1,224.05 μs | 19.658 μs | 16.416 μs | 19.17 |    0.33 |         - |          NA |
| PropagateUnchangedAndExtract10k |   126.70 μs |  2.346 μs |  2.702 μs |  1.98 |    0.05 |         - |          NA |
