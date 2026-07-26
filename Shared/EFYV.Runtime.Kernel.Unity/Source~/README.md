# Adapter source and build tooling

[Up: Runtime Kernel Unity adapter](../README.md)

Unity ignores this tilde-suffixed directory. It contains the auditable inputs
used to generate the checked-in runtime artifacts:

- [`Efyv.RuntimeKernel.Unity.csproj`](Efyv.RuntimeKernel.Unity.csproj) compiles the
  unchanged official binding source for .NET Standard 2.1 and pins package,
  assembly, file, and informational versions to `0.2.0`.
- [`NetStandardCompatibility.cs`](NetStandardCompatibility.cs) supplies only the
  APIs absent from Unity's .NET Standard surface.
- [`Build-UnityAdapter.ps1`](Build-UnityAdapter.ps1) builds the managed assembly,
  selects MSVC or Docker/MinGW, and writes native artifact provenance.

## Provenance

After the native DLL is copied, the build script hashes every C/C++ Runtime
build input under `include/` and `src/` plus `CMakeLists.txt`. It records that
manifest hash, Runtime `HEAD`, dirty-state flag, final DLL SHA-256, official
binding-source SHA-256, managed assembly version, CMake/compiler identity, and
the exact pinned container image when Docker is used.

This proves which source inputs and observed toolchain produced an artifact. It
does not claim bit-for-bit reproducibility across changing Debian package
repositories; compare the recorded output hash when reproducibility is required.
