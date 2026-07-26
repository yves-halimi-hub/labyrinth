# Unity runtime artifacts

[Up: Runtime Kernel Unity adapter](../README.md)

This directory contains only artifacts imported by Unity at game/editor runtime.
Adapter source and rebuild tooling stay under [`Source~/`](../Source~/README.md).

- [`Managed/`](Managed/README.md) carries the .NET Standard 2.1 assembly generated
  from the unchanged official binding source plus the compatibility surface.
- [`Plugins/`](Plugins/README.md) carries platform-native Runtime Kernel libraries
  and per-artifact provenance.

The managed and native artifacts must be rebuilt as one package whenever their
binding ABI or Runtime build inputs change.
