# EFYV BCL Compatibility

Embedded Unity package carrying the NuGet assemblies `EFYVBackend.Core` needs but
Unity's Mono/netstandard2.1 scripting profile does not ship:

| Assembly | Origin | Target |
| --- | --- | --- |
| `System.Text.Json.dll` | NuGet 8.0.5 | netstandard2.0 |
| `System.Text.Encodings.Web.dll` | NuGet 8.0.0 | netstandard2.0 |
| `System.Runtime.CompilerServices.Unsafe.dll` | NuGet 6.0.0 | netstandard2.0 |
| `Microsoft.Bcl.AsyncInterfaces.dll` | NuGet 8.0.0 | netstandard2.1 |
| `EFYV.ZLibCompat.dll` | built from [`ZLibCompatSource~`](ZLibCompatSource~/ZLibStream.cs) | netstandard2.0 |

`EFYV.ZLibCompat.dll` provides `System.IO.Compression.ZLibStream` (a .NET 6+
API the backend PNG encoder/decoder uses) as a thin zlib wrapper over
`DeflateStream` with a real Adler-32 trailer. Its source lives in the
Unity-hidden `ZLibCompatSource~` folder; rebuild with
`dotnet build -c Release ZLibCompatSource~/EFYV.ZLibCompat.csproj` and copy the
DLL here. It was cross-validated against the real .NET `ZLibStream`
(shim-compress -> real-inflate, real-compress -> shim-inflate, malformed-header
rejection) before being checked in.

`System.Buffers`, `System.Memory`, and `System.Threading.Tasks.Extensions` are
deliberately NOT included: Unity ships those facades in its netstandard compat
profile, and duplicating them would produce ambiguous-type compile errors.

The headless verification projects do not use this package; they target
net8.0/net10.0 where `System.Text.Json` is part of the framework.
