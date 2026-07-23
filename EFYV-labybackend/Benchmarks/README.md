# LabyBackend Performance Harness

[Back to LabyBackend](../README.md)

This executable performs warmed, repeated RGBA blend, blur, PNG, and complete
LabyMake artifact-export workloads. It reports elapsed time, throughput, and
thread-local managed allocations. `--verify` adds stable invariant and broad
allocation ceilings suitable for regression verification; recorded numbers remain
diagnostic because absolute timing varies by host.

Run `dotnet run --project EFYVBackend.Benchmarks.csproj -c Release -- --verify`.
