# Verification Harness

[Repository](../README.md) | [Core](../Core/README.md) | [Shared backend](../../EFYV-labybackend/)

`EFYVLabyMake.Verification.csproj` is a dependency-free .NET 8 executable, not an xUnit/NUnit project. It links every `Core/**/*.cs` file from this repository and every backend `Core/**/*.cs` file from the sibling repository, enables unsafe code, and runs deterministic assertions from a partial `Program` class.

## Run

From the LabyMake repository root:

```powershell
dotnet build Tests/EFYVLabyMake.Verification.csproj --configuration Debug
dotnet run --project Tests/EFYVLabyMake.Verification.csproj --configuration Debug
```

The runner prints the total assertion and group counts and exits nonzero on the first failed invariant.

## Test files

- [`Program.cs`](Program.cs): runner, shared fixtures/helpers, baseline schema, compositing, export, preview, persistence, autosave, and live-debug checks.
- [`ModelsAndToolsTests.cs`](ModelsAndToolsTests.cs): clone isolation, randomized compositing, exact drawing reference models, animation generation, snapshots, and viewport boundaries.
- [`SchemaValidatorCommandTests.cs`](SchemaValidatorCommandTests.cs): exhaustive schema/toolbar matrices, validator issue coverage, and randomized bounded-history modeling.
- [`SessionMapPreviewTests.cs`](SessionMapPreviewTests.cs): session CRUD and gesture rollback, extreme preview timing, and deterministic map modes.
- [`StorageAndExportTests.cs`](StorageAndExportTests.cs): path attack corpus, asset-bank corruption, malformed project documents, atomic cancellation, exact decoded PNG pixels, and publication rollback.
- [`AsyncLifecycleTests.cs`](AsyncLifecycleTests.cs): adversarial live-debug/autosave state machines and session save/reload lifecycle.

## Adding coverage

Add methods to an external `internal static partial class Program` file, register each group in `Program.Main`, and reuse the bounded fixtures and `ManualScheduler`. Tests should use fixed seeds, temporary directories with `finally` cleanup, explicit canaries/reference models for unsafe code, and finite workloads. Avoid external packages so the harness continues to verify the exact source shared with Unity.

