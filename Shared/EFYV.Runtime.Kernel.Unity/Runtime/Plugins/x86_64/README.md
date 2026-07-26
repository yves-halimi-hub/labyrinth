# Windows x64 Runtime Kernel plug-in

[Up: native plug-ins](../README.md) · [Adapter overview](../../../README.md)

`efyv_runtime_kernel.dll` is the Windows x64 shared library built from the
official `EFYV-runtime-kernel` source. Unity loads it for Editor and Windows
player batches issued through `Efyv.RuntimeKernel`; no Labyrinth-specific ABI or
algorithm is implemented here.

Rebuild both managed and native artifacts with
[`Build-UnityAdapter.ps1`](../../../Source~/Build-UnityAdapter.ps1). The script
uses the installed MSVC toolchain when the Windows SDK is complete and otherwise
cross-compiles the same CMake target in Docker with MinGW. A successful native
build replaces
[`efyv_runtime_kernel.provenance.json`](efyv_runtime_kernel.provenance.json) with
the exact source-input, artifact, and observed-toolchain identities. A
`pending-final-native-rebuild` status is deliberately not release provenance.
