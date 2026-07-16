# Runtime Items

[Up to runtime core](../README.md)

[`PowerUp.cs`](PowerUp.cs) is a value-type wrapper over backend `PowerUpData`.

It caps level upgrades, consumes a fixed number of uses, degrades grade when exhausted, and resets normal-grade items to their initial level. Because it is a struct, callers must store the mutated value back into their collection.
