# Native Runtime Kernel plug-ins

[Up: Unity runtime artifacts](../README.md)

Each platform directory contains the native library loaded by the official .NET
binding and a provenance record generated in the same successful build.

- [`x86_64/`](x86_64/README.md) is the Windows x64 Unity Editor/player target.

Do not enable another Unity player platform until its matching library is built
from the same Runtime inputs and has equivalent native-backed adapter tests.
