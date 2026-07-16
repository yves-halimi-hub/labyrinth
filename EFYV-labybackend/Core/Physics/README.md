# Physics

[Core README](../README.md) | [Math helpers](../Math/README.md)

Minimal engine-neutral integration math for runtime movement.

- [FastPhysics.cs](FastPhysics.cs) exposes `CalculateTranslation`, which adds
  `direction * speed * deltaTime` to caller-owned X/Y positions.
- The method is allocation-free, aggressively inlined, and does not normalize the
  direction, clamp time, detect collisions, or reject non-finite inputs. The
  caller owns those gameplay and physics policies.
- Updating both coordinates by reference keeps this layer independent of Unity
  vector types and makes it directly testable in the backend harness.

