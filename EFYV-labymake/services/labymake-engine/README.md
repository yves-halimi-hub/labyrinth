# LabyMake Engine

[Back to LabyMake services](../README.md) | [Behavioral tests](../../../Verification/LabyMakeEngine/README.md)

This stateless .NET gRPC service validates a versioned LabyMake snapshot and deterministically
exports the Unity handoff. `EngineOperations` invokes the shared
[`EFYV.Labyrinth.Artifacts`](../../../Shared/EFYV.Labyrinth.Artifacts/) package, so validation and
export parse a request once and use the same backend PNG, metadata, path, effect, attachment, and
tileset behavior as the game.

`WorkAdmission` permits two active parse/export operations and eight waiters. Cancellation is
checked during parsing, layer composition, metadata/PNG production, ZIP writing, and streaming.
Unary `Validate`/`Export` remain compatible with the current app. The v2
`LabyMakeArtifactTransfer.GetLimits` and bidirectional `Export` use 64 KiB chunks and return both
the bundle SHA-256 and `sha256:` artifact reference. The [v1 protocol contract](proto/README.md)
remains the Platform-bound compatibility surface.

`EFYV_CONTRACT_TEST_MODE=1` enables the deterministic, side-effect-free responses required by the
EFYV-CI custom-service runtime gate. The switch is exact and opt-in: every other value follows the
normal production path. Set `EFYV_RUNTIME_KERNEL_NATIVE=1` to request the optional canonical
`efyv_runtime_kernel`; a missing, mismatched, or failing native library falls back to bit-compatible
managed behavior.

Verification:

```powershell
dotnet build LabyMakeEngine.csproj -c Release
dotnet run --project ..\..\..\Verification\LabyMakeEngine\LabyMakeEngine.Tests.csproj -c Release
docker build -f EFYV-labymake/services/labymake-engine/Dockerfile -t efyv-labymake-engine:verify .
```

The Docker command runs from the Labyrinth repository root because the image consumes the shared
packages and backend project. Its SDK/runtime tags and NuGet graph are pinned. The schema-v2
`service.efyv` declares a fail-closed repository context whose four source directories are validated,
digested, and copied into an isolated build snapshot by EFYV Platform and EFYV-CI.
