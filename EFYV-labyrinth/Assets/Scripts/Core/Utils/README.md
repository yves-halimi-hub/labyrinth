# Runtime Utilities

[Up to runtime core](../README.md)

- [`Singleton.cs`](Singleton.cs) supplies lazy lookup, duplicate destruction, and instance cleanup, plus a negative cache: a `FindObjectOfType` miss is remembered so hot paths (per-frame `TryGetInstance`) never re-sweep the scene. The cache is stamped by `SingletonSearchCache` and refreshed automatically whenever any singleton registers in `Awake`; scene-transition seams that bypass `Awake` call `SingletonSearchCache.Invalidate()` explicitly (map switches do).
- [`VectorExtensions.cs`](VectorExtensions.cs) adapts backend normalization, squared distance, and random offsets to Unity vectors.
- [`TransformExtensions.cs`](TransformExtensions.cs) applies backend planar translation while preserving Z.

These helpers are hot-path bridges. Avoid adding allocation, scene lookup, or logging to vector and transform extension methods.
