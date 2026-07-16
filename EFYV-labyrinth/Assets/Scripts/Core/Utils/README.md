# Runtime Utilities

[Up to runtime core](../README.md)

- [`Singleton.cs`](Singleton.cs) supplies lazy lookup, duplicate destruction, and instance cleanup.
- [`VectorExtensions.cs`](VectorExtensions.cs) adapts backend normalization, squared distance, and random offsets to Unity vectors.
- [`TransformExtensions.cs`](TransformExtensions.cs) applies backend planar translation while preserving Z.

These helpers are hot-path bridges. Avoid adding allocation, scene lookup, or logging to vector and transform extension methods.
