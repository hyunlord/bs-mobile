```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 8.0.425
  [Host]   : .NET 8.0.31 (8.0.3126.42015), X64 RyuJIT AVX2
  ShortRun : .NET 8.0.31 (8.0.3126.42015), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                  | Mean     | Error    | StdDev   | Gen0   | Allocated |
|---------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| &#39;S0 synthetic full run (not S2 combat)&#39; | 11.67 μs | 0.352 μs | 0.019 μs | 0.0458 |     912 B |
