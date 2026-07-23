# LabyMake Engine Protocol

[Back to the engine service](../README.md)

`labymake.proto` defines the descriptor-bound request, validation result, and deterministic export
response shared by LabyMake and its engine. Additive protocol changes update the EFYV service
declaration, generated bindings, and contract tests together; app code never calls undeclared RPCs.

`Validate` and `Export` are the unary compatibility surface consumed by Platform. The additive
[v2 transfer contract](../../../../Shared/EFYV.Labyrinth.Artifacts/Protocol/README.md) is linked into
the executable separately so the current unary-only Platform descriptor remains valid.
