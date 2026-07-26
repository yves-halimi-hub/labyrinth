# Unity Packages

[Back to the Unity project](../README.md)

`manifest.json` and `packages-lock.json` pin Unity dependencies plus the local
[Runtime Kernel adapter](../../Shared/EFYV.Runtime.Kernel.Unity/README.md),
[generic media](../../Shared/EFYV.Runtime.Media/README.md), and
[backend Core](../../EFYV-labybackend/Core/README.md) packages. The Runtime package carries the
official managed binding and matching native plug-in used by grouped gameplay compute.
[`com.efyv.bclcompat`](com.efyv.bclcompat/README.md) supplies the exact managed assemblies Unity's
scripting profile lacks. Package changes must preserve all three local package paths, keep Runtime
managed/native provenance matched, and pass static asset, headless game, and real Unity editor
verification.
