# Unity Packages

[Back to the Unity project](../README.md)

`manifest.json` and `packages-lock.json` pin Unity dependencies plus the local generic media and backend Core packages.
[`com.efyv.bclcompat`](com.efyv.bclcompat/README.md) supplies the exact managed assemblies Unity's
scripting profile lacks. Package changes must preserve the local backend path and pass static asset,
headless game, and real Unity editor verification.
