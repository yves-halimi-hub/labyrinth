# EFYV Runtime Kernel Unity adapter

[Back to shared Labyrinth packages](../README.md)

This local Unity package exposes the official `Efyv.RuntimeKernel` managed API to the
Labyrinth Unity 6.6 project and carries the matching native runtime plug-in. Gameplay
code consumes `Kernel.SinCosGeometry` and `Kernel.SpatialQueryBatch`; this package
does not declare a second ABI and does not contain managed math or spatial fallbacks.

Unity 6000.6 still compiles game assemblies against .NET Standard 2.1, while the
official binding targets .NET 8. The generated managed plug-in is therefore compiled
from the unchanged
`EFYV-runtime-kernel/bindings/dotnet/Efyv.RuntimeKernel/RuntimeKernel.cs` source plus
[`NetStandardCompatibility.cs`](Source~/NetStandardCompatibility.cs). That compatibility
file only supplies:

- the modern negative-argument guard used by the official source;
- a no-op `SetDllImportResolver`/`TryLoad` surface, because Unity resolves the bundled
  native plug-in by its normal `efyv_runtime_kernel` library name.

No P/Invoke declaration, data layout, algorithm, or gameplay rule is copied here.
[`Build-UnityAdapter.ps1`](Source~/Build-UnityAdapter.ps1) rebuilds the managed plug-in
and the Windows x64 native plug-in from the checked-out shared Runtime Kernel source.
It uses the local MSVC toolchain when available and otherwise reaches a pinned
Docker base image without requiring host CMake. Docker package repositories are
not immutable, so the build is described as traceable rather than bit-reproducible:
each successful native build records the exact Runtime commit, a SHA-256 manifest
of native build inputs, the produced DLL hash, and observed toolchain identities.
The generated DLLs are checked in so a clean Unity checkout imports a complete package.

```powershell
pwsh -File Source~/Build-UnityAdapter.ps1
```

Pass `-NativeToolchain MSVC` or `-NativeToolchain Docker` to require one path
instead of automatic fallback. Pass `-Check` to validate path/tool availability
and the no-host-CMake Auto decision without building. The MinGW build statically
links its compiler runtime, leaving only Windows system-DLL dependencies.

The headless game verification project references the official .NET 8 project directly
when the full `EFYV-system` checkout is present. Its conditional checked-in-DLL fallback
keeps the Labyrinth repository independently buildable.

## Runtime contract

- Geometry and spatial operations are native-only. A missing or ABI-incompatible
  native plug-in fails fast through the official binding.
- All caller buffers remain managed-owned and are pinned only for a batch call.
- Windows x64 is the currently packaged Unity player target. Add a platform plug-in
  built from the same Runtime Kernel revision before enabling another player target.

## Package map

- [`Runtime/`](Runtime/README.md) contains checked-in Unity runtime artifacts.
- [`Runtime/Managed/`](Runtime/Managed/README.md) contains the generated .NET Standard
  2.1 binding assembly.
- [`Runtime/Plugins/`](Runtime/Plugins/README.md) contains platform-native libraries
  and their build provenance.
- [`Source~/`](Source~/README.md) contains the source-only adapter and build tooling;
  Unity deliberately excludes tilde-suffixed directories from asset import.
