# Environment Implementations

[Up to environment entities](../README.md)

Concrete world interactions:

| File | Behavior |
| --- | --- |
| [`BaseMerchantProp.cs`](BaseMerchantProp.cs) | Generates inventory, charges currency, applies or refunds purchases |
| [`ChestProp.cs`](ChestProp.cs) | Clamps grade and requests upgrade rewards |
| [`CoinProp.cs`](CoinProp.cs) | Clamps grade, calculates value, and awards persistent/session coins |
| [`DoorProp.cs`](DoorProp.cs) | Hashes a target map ID and requests a map switch |
| [`SarcophageProp.cs`](SarcophageProp.cs) | Selects teleport, ambush, trap, or curse outcomes |
| [`TreeProp.cs`](TreeProp.cs) | Passive nonblocking animated scenery |
| [`XPGem.cs`](XPGem.cs) | Awards authored experience and returns to the pool |

Interactions assume the corresponding managers exist in the active game scene. Pooled implementations should finish by calling `ReleaseToPool` rather than destroying themselves.
