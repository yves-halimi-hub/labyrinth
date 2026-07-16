# Tests

[Backend README](../README.md) | [Production core](../Core/README.md)

Dependency-free, outside-in verification for every backend source area. The
console project links `../Core/**/*.cs` directly, enables unsafe code, runs a fixed
set of deterministic groups, prints each result, and exits nonzero on any failure.
Nothing in this directory is referenced by the game or editor runtime.

## Files

- [EFYVBackend.Verification.csproj](EFYVBackend.Verification.csproj): .NET 8
  executable and source-link boundary.
- [Program.cs](Program.cs): runner, baseline integration tests, PNG parser/CRC
  oracle, and common assertions.
- [SchemaCollectionTests.cs](SchemaCollectionTests.cs): reflection coverage for
  schema-backed models plus randomized pool, swap-list, grid, and ring-buffer
  state models.
- [MathMemoryTests.cs](MathMemoryTests.cs): independent numeric and pixel reference
  models, pointer guard regions, deterministic random checks, blur, procedural,
  deformation, and physics tests.
- [ImportSaveExportAdversarialTests.cs](ImportSaveExportAdversarialTests.cs):
  malformed and locked files, save-memory canaries, traversal attempts, metadata
  guards, export preservation, atlas fuzzing, and PNG determinism.

## Run

From the repository root:

```powershell
dotnet run --project Tests\EFYVBackend.Verification.csproj -c Debug
dotnet run --project Tests\EFYVBackend.Verification.csproj -c Release
```

Tests use fixed seeds and bounded loops. Temporary files are isolated under the
system temp directory and removed in `finally` blocks. Large assertion counts are
intentional reference-model checks, not runtime instrumentation.
