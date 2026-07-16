using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Entities.Environment;
using EFYV.Core.Interfaces;
using EFYV.Core.Weapons;
using EFYV.Core.Weapons.Types;
using EFYVBackend.Core.Collections;
using EFYVBackend.Core.Data;
using UnityEditor;
using UnityEngine;

internal static partial class Program
{
    private static long assertions;

    private static void Main()
    {
        var tests = new (string Name, Action Body)[]
        {
            ("schema block isolation and asset names", TestSchemaBlockIsolationAndAssetNames),
            ("designable types and registration contracts", TestDesignableTypesAndRegistrationContracts),
            ("atlas and directional import data", TestAtlasAndDirectionalImportData),
            ("importer validation and sprite slicing", TestImporterValidationAndSpriteSlicing),
            ("importer conversion and outside-in import", TestImporterConversionAndOutsideInImport),
            ("hitbox calculations and live refresh", TestHitboxCalculationsAndLiveRefresh),
            ("vector, transform, and viewport calculations", TestVectorTransformAndViewportCalculations),
            ("entity health, sprites, and lifecycle", TestEntityHealthSpritesAndLifecycle),
            ("enemy, boss, and packed-list mutation", TestEnemyBossAndPackedListMutation),
            ("projectile lifecycle and central iteration", TestProjectileLifecycleAndCentralIteration),
            ("power-ups, weapons, and inventory", TestPowerUpsWeaponsAndInventory),
            ("entity and GameObject pooling", TestEntityAndGameObjectPooling),
            ("spawning, AI scaling, and fallback maps", TestSpawningAiAndFallbackMaps),
            ("save progression and saturating arithmetic", TestSaveProgressionAndSaturatingArithmetic),
            ("props, purchases, and achievements", TestPropsPurchasesAndAchievements),
            ("camera and manager state machines", TestCameraAndManagerStateMachines)
        };

        int failures = 0;
        for (int i = 0; i < tests.Length; i++)
        {
            try
            {
                ResetState();
                tests[i].Body();
                Console.WriteLine("PASS " + tests[i].Name);
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine("FAIL " + tests[i].Name + ": " + exception);
            }
            finally
            {
                ResetState();
            }
        }

        Console.WriteLine(assertions.ToString("N0") + " assertions across " + tests.Length + " game/editor groups.");
        if (failures != 0) Environment.Exit(1);
    }

    private static void ResetState()
    {
        TestRuntime.Reset();
        FastPoolRegistry<GameEntity>.Clear();
        FastPoolRegistry<GameObject>.Clear();
        Enemy.ActiveEnemies.Clear();
        Projectile.ActiveProjectiles.Clear();
        PropEntity.ActiveAnimatedProps.Clear();
        EditorUtility.DirtyObjects.Clear();
        Handles.Labels.Clear();
        RenderTexture.active = null;
        Application.persistentDataPath = Path.GetTempPath();

        Type bridge = typeof(EFYV.Editor.EFYVPixelArtImporter).Assembly.GetType("EFYV.Editor.EFYVLiveDebugBridge", true);
        object pending = GetField(bridge, "PendingAssets", null);
        pending.GetType().GetMethod("Clear").Invoke(pending, null);
        SetField(bridge, "applyScheduled", null, false);
    }

    private static void Check(bool condition, string message = null)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message ?? "Assertion failed.");
    }

    private static void Equal<T>(T expected, T actual, string message = null)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message ??
                "Expected <" + expected + "> but got <" + actual + ">.");
        }
    }

    private static void Same(object expected, object actual, string message = null)
    {
        assertions++;
        if (!ReferenceEquals(expected, actual))
            throw new InvalidOperationException(message ?? "References were not identical.");
    }

    private static void NotSame(object left, object right, string message = null)
    {
        assertions++;
        if (ReferenceEquals(left, right))
            throw new InvalidOperationException(message ?? "References unexpectedly aliased.");
    }

    private static void Near(float expected, float actual, float tolerance = 0.0001f, string message = null)
    {
        assertions++;
        if (float.IsNaN(actual) || MathF.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(message ??
                "Expected approximately <" + expected + "> but got <" + actual + ">.");
        }
    }

    private static TException Throws<TException>(Action action) where TException : Exception
    {
        assertions++;
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Expected " + typeof(TException).Name + " but got " + exception.GetType().Name + ".", exception);
        }

        throw new InvalidOperationException("Expected " + typeof(TException).Name + " but no exception was thrown.");
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static object GetField(Type type, string name, object target)
    {
        return FindField(type, name).GetValue(target);
    }

    private static T GetField<T>(object target, string name)
    {
        return (T)GetField(target.GetType(), name, target);
    }

    private static void SetField(Type type, string name, object target, object value)
    {
        FindField(type, name).SetValue(target, value);
    }

    private static void SetField(object target, string name, object value)
    {
        SetField(target.GetType(), name, target, value);
    }

    private static MethodInfo FindMethod(Type type, string name, bool isStatic, int parameterCount = -1)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
            (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        for (Type current = type; current != null; current = current.BaseType)
        {
            foreach (MethodInfo method in current.GetMethods(flags | BindingFlags.DeclaredOnly))
            {
                if (method.Name == name && (parameterCount < 0 || method.GetParameters().Length == parameterCount))
                    return method;
            }
        }
        throw new MissingMethodException(type.FullName, name);
    }

    private static object Invoke(object target, string name, params object[] arguments)
    {
        return InvokeMethod(FindMethod(target.GetType(), name, false, arguments.Length), target, arguments);
    }

    private static object InvokeStatic(Type type, string name, params object[] arguments)
    {
        return InvokeMethod(FindMethod(type, name, true, arguments.Length), null, arguments);
    }

    private static object InvokeMethod(MethodInfo method, object target, object[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static JsonElement JsonValue(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static T CreateComponent<T>(bool addRenderer = false, bool invokeAwake = false)
        where T : Component
    {
        var gameObject = new GameObject(typeof(T).Name);
        if (addRenderer) gameObject.AddComponent<SpriteRenderer>();
        return (T)gameObject.AddComponent(typeof(T), invokeAwake);
    }

    private sealed class ProbeEntity : GameEntity
    {
        public int SpawnCalls;
        public int DespawnCalls;
        public override void OnSpawn() { SpawnCalls++; base.OnSpawn(); }
        public override void OnDespawn() { DespawnCalls++; base.OnDespawn(); }
    }

    private class ProbeLiving : LivingEntity
    {
        public int MoveCalls;
        public int RefreshCalls;
        public void Apply(float maxHealth, float speed, bool preserve = false)
        {
            FastSchemaBlock block = default;
            block.SetFloat((int)AssetSchema.MaxHealth, maxHealth);
            block.SetFloat((int)AssetSchema.BaseSpeed, speed);
            ApplySchemaBlock(block, preserve);
        }
        public void SetHealth(float value) { CurrentHealth = value; }
        protected override void Move(float deltaTime) { MoveCalls++; }
        public override void RefreshDataFromAsset() { RefreshCalls++; base.RefreshDataFromAsset(); }
    }

    private class ProbeEnemy : Enemy
    {
        public int TickMoves;
        public int DeathCalls;
        public void SetTarget(Transform value) { TargetPlayer = value; }
        public void Apply(float maxHealth, float speed, float damage, float experience)
        {
            FastSchemaBlock block = default;
            block.SetFloat((int)AssetSchema.MaxHealth, maxHealth);
            block.SetFloat((int)AssetSchema.BaseSpeed, speed);
            block.SetFloat((int)AssetSchema.DamageToPlayer, damage);
            block.SetFloat((int)AssetSchema.ExperienceValue, experience);
            ApplySchemaBlock(block, false);
        }
        public void SetHealth(float value) { CurrentHealth = value; }
        protected override void Move(float deltaTime) { TickMoves++; }
        public override void Die() { DeathCalls++; base.Die(); }
    }

    private sealed class ProbeBoss : BossEnemy
    {
        public void Apply(float maxHealth, float speed, float threshold)
        {
            FastSchemaBlock block = default;
            block.SetFloat((int)AssetSchema.MaxHealth, maxHealth);
            block.SetFloat((int)AssetSchema.BaseSpeed, speed);
            block.SetFloat((int)AssetSchema.Phase2HealthThreshold, threshold);
            ApplySchemaBlock(block, false);
        }
    }

    private sealed class ProbeProjectile : Projectile
    {
        public int DespawnCalls;
        public override void OnDespawn() { DespawnCalls++; base.OnDespawn(); }
    }

    private class ProbeWeapon : Weapon
    {
        public int FireCalls;
        public void Configure(float cooldown, float damage, int level = 1)
        {
            CooldownTime = cooldown;
            BaseDamage = damage;
            Level = level;
            currentCooldown = 0f;
        }
        public float RemainingCooldown => currentCooldown;
        public override void Fire() { FireCalls++; }
    }

    private sealed class ProbeAuraWeapon : AuraWeapon
    {
        public void SetDamage(float value) { BaseDamage = value; }
    }

    private sealed class ProbeMeleeWeapon : MeleeWeapon
    {
        public void SetDamage(float value) { BaseDamage = value; }
    }

    private sealed class ProbeOrbitalWeapon : OrbitalWeapon
    {
        public void SetDamage(float value) { BaseDamage = value; }
    }

    private sealed class ProbeProp : PropEntity
    {
        public int Frame => currentFrame;
        public float Timer => animTimer;
        public int RefreshCalls;
        public new void RefreshDataFromAsset() { RefreshCalls++; base.RefreshDataFromAsset(); }
    }

    private sealed class ProbeDamageable : IDamageable
    {
        public float TotalDamage;
        public int DieCalls;
        public void TakeDamage(float amount) { TotalDamage += amount; }
        public void Die() { DieCalls++; }
    }
}
