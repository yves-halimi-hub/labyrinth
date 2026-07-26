# Managed Unity binding

[Up: Unity runtime artifacts](../README.md)

`Efyv.RuntimeKernel.dll` is version `0.2.0.0` and targets .NET Standard 2.1 for
Unity 6000.6. It is generated from the unchanged official
`Efyv.RuntimeKernel/RuntimeKernel.cs` source and the package-local compatibility
surface documented in [`Source~/`](../../Source~/README.md).

- `Efyv.RuntimeKernel.dll` is the imported assembly.
- `Efyv.RuntimeKernel.pdb` preserves managed diagnostics.
- `Efyv.RuntimeKernel.deps.json` records the generated assembly identity.

These are generated artifacts. Change the source project or official binding,
then use [`Build-UnityAdapter.ps1`](../../Source~/Build-UnityAdapter.ps1); do not
patch the binaries independently.
