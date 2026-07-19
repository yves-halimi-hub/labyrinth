using System;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Models
{
    // Item #7: one authored runtime-effect descriptor (name + params +
    // trigger tag), owned per animation. IMMUTABLE by design: the constructor
    // is the single validation gate, so an instance is always well-formed and
    // undo/redo commands, clones, and snapshots may freely share references.
    //
    // - EffectType is one of the wire strings ("flash"/"tint"/"particleHook").
    // - Trigger is the runtime seam tag ("OnSpawn"/"OnDamaged" or a custom
    //   tag a future consumer may fire); never empty.
    // - Name is a designer label; REQUIRED (non-empty) for particleHook,
    //   where it identifies the particle system to spawn.
    // - ColorRgba/DurationMs/Strength are the flash/tint parameters; the
    //   particle-hook interpretation of params beyond Name is deferred.
    public sealed class EffectDescriptor
    {
        public string EffectType { get; }
        public string Name { get; }
        public string Trigger { get; }
        public uint ColorRgba { get; }
        public int DurationMs { get; }
        public float Strength { get; }

        public EffectDescriptor(
            string effectType,
            string name,
            string trigger,
            uint colorRgba,
            int durationMs,
            float strength)
        {
            if (!IsKnownEffectType(effectType)) throw new ArgumentException(nameof(effectType));
            if (string.IsNullOrWhiteSpace(trigger)) throw new ArgumentException(nameof(trigger));
            if (trigger.Length > Config.Effect.MaxTriggerLength)
                throw new ArgumentException(nameof(trigger));
            if (name != null && name.Length > Config.Effect.MaxNameLength)
                throw new ArgumentException(nameof(name));
            if (effectType == Config.Effect.TypeParticleHook && string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(nameof(name));
            if (durationMs < Config.Effect.MinDurationMs || durationMs > Config.Effect.MaxDurationMs)
                throw new ArgumentOutOfRangeException(nameof(durationMs));
            if (float.IsNaN(strength) ||
                strength < Config.Effect.MinStrength ||
                strength > Config.Effect.MaxStrength)
                throw new ArgumentOutOfRangeException(nameof(strength));

            EffectType = effectType;
            Name = name ?? Config.Common.EmptyString;
            Trigger = trigger;
            ColorRgba = colorRgba;
            DurationMs = durationMs;
            Strength = strength;
        }

        public static bool IsKnownEffectType(string effectType)
        {
            return effectType == Config.Effect.TypeFlash ||
                effectType == Config.Effect.TypeTint ||
                effectType == Config.Effect.TypeParticleHook;
        }
    }
}
