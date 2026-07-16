# Entity Implementations

[Up to entities](../README.md)

Thin concrete types configure capabilities while the base entity hierarchy owns shared behavior.

- [`Monster.cs`](Monster.cs), [`MiniBoss.cs`](MiniBoss.cs), and [`Boss.cs`](Boss.cs) provide weapon-capacity tiers.
- [`EvilEye.cs`](EvilEye.cs) uses base enemy behavior directly.
- [`EyeBearer.cs`](EyeBearer.cs) spawns pooled Evil Eyes before normal death handling.
- [Egypt](Egypt/README.md) declares the designer-visible theme asset types.

Keep concrete classes small unless behavior is unique; shared movement, scaling, death, and list management belong in the base classes.
