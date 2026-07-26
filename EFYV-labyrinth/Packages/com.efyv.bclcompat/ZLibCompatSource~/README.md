# ZLib compatibility source

[Back to the BCL compatibility package](../README.md)

This Unity-hidden source project builds `EFYV.ZLibCompat.dll`, the narrow
`System.IO.Compression.ZLibStream` surface needed by the backend PNG encoder on Unity's
.NET Standard 2.1 profile. It targets .NET Standard 2.0 and wraps `DeflateStream` with the RFC 1950
header plus a big-endian Adler-32 trailer; decompression validates the header while PNG chunk CRCs
remain the outer integrity check.

Rebuild from this directory with:

```powershell
dotnet build -c Release
```

Copy the resulting release DLL to the parent package only after cross-checking shim-compressed data
with the framework `ZLibStream`, framework-compressed data with the shim, and malformed-header
rejection. Unity excludes the tilde-suffixed directory from asset import, so source and build files
do not become runtime assets.
