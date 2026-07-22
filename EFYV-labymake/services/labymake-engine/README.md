# LabyMake Engine

[Back to LabyMake services](../README.md)

This narrow .NET gRPC service validates a versioned Labyrinth maker document and deterministically
exports the Unity handoff artifacts. `Program.cs` hosts the service, `LabyExport.cs` owns bounded
validation/export logic, `service.efyv` declares the EFYV custom-service contract, and
[`proto/labymake.proto`](proto/README.md) is the transport schema. `Dockerfile` and `build.sh` provide
the reproducible runtime build.

The service may not write arbitrary browser paths or absorb generic Maker storage/history behavior.
Verify it with `dotnet build LabyMakeEngine.csproj -c Release` and the repository-level Labyrinth
contract suites.
