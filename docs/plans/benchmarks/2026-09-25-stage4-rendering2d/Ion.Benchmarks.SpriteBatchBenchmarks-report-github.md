```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4


```
| Method                                    | Textures | Mean      | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------------------ |--------- |----------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Legacy_Add10kSprites**                      | **1**        | **124.36 μs** | **1.993 μs** | **1.556 μs** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Legacy_Add10kSprites_WithScissorTransform | 1        | 138.98 μs | 1.038 μs | 0.971 μs |  1.12 |    0.02 |         - |          NA |
| V2_Deferred                               | 1        |  56.59 μs | 1.128 μs | 1.885 μs |  0.46 |    0.02 |         - |          NA |
| V2_Deferred_WithUploadCopy                | 1        |  66.94 μs | 1.256 μs | 1.233 μs |  0.54 |    0.01 |         - |          NA |
| V2_TextureSort                            | 1        | 102.68 μs | 2.039 μs | 1.808 μs |  0.83 |    0.02 |         - |          NA |
| V2_BackToFront                            | 1        | 178.66 μs | 3.176 μs | 4.452 μs |  1.44 |    0.04 |         - |          NA |
|                                           |          |           |          |          |       |         |           |             |
| **Legacy_Add10kSprites**                      | **16**       | **146.05 μs** | **1.479 μs** | **1.817 μs** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Legacy_Add10kSprites_WithScissorTransform | 16       | 161.17 μs | 2.189 μs | 1.940 μs |  1.10 |    0.02 |         - |          NA |
| V2_Deferred                               | 16       |  69.06 μs | 1.227 μs | 1.088 μs |  0.47 |    0.01 |         - |          NA |
| V2_Deferred_WithUploadCopy                | 16       |  80.54 μs | 1.503 μs | 1.543 μs |  0.55 |    0.01 |         - |          NA |
| V2_TextureSort                            | 16       | 101.17 μs | 2.013 μs | 3.578 μs |  0.69 |    0.03 |         - |          NA |
| V2_BackToFront                            | 16       | 189.59 μs | 3.614 μs | 3.204 μs |  1.30 |    0.03 |         - |          NA |
