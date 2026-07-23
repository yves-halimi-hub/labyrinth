# LabyMake Engine Behavioral Verification

[Back to repository verification](../README.md)

This executable suite exercises the app-owned engine through its shared artifact
layer. It locks deterministic ZIP order/content, canonical backend metadata,
malformed and unsafe input handling, cancellation, bounded admission behavior, and the exact
CI-owned contract-mode startup switch.

Run with `dotnet run --project LabyMakeEngine.Tests.csproj -c Release`.
