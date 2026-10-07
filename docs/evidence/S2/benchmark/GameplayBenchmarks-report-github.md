```

BenchmarkDotNet v0.14.0, macOS 26.4.1 (25E253) [Darwin 25.4.0]
Apple M4 Max, 1 CPU, 14 logical and 14 physical cores
.NET SDK 8.0.425
  [Host]   : .NET 8.0.31 (8.0.3126.42015), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 8.0.31 (8.0.3126.42015), Arm64 RyuJIT AdvSIMD

Job=ShortRun  InvocationCount=1  IterationCount=3  
LaunchCount=1  UnrollFactor=1  WarmupCount=3  

```
| Method                                                             | Mean     | Error    | StdDev   | Allocated |
|------------------------------------------------------------------- |---------:|---------:|---------:|----------:|
| &#39;S2 actual load tick; entity refill and count assertions included&#39; | 848.3 μs | 300.7 μs | 16.48 μs | 486.29 KB |
