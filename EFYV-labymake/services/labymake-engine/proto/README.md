# LabyMake Engine Protocol

[Back to the engine service](../README.md)

`labymake.proto` defines the descriptor-bound request, validation result, and deterministic export
response shared by LabyMake and its engine. Additive protocol changes update the EFYV service
declaration, generated bindings, and contract tests together; app code never calls undeclared RPCs.
