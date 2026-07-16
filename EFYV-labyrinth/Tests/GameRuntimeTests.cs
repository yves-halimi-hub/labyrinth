using System;
using System.Collections.Generic;
using EFYV.Core.Controllers;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Items;
using EFYV.Core.Managers;
using EFYV.Core.Utils;
using EFYV.Core.Weapons;
using EFYVBackend.Core.Collections;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Math;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;

internal static partial class Program
{
    private static void TestVectorTransformAndViewportCalculations()
    {
        Vector2 zero2 = VectorExtensions.FastNormalized(Vector2.zero);
        Near(0f, zero2.x);
        Near(0f, zero2.y);
        Vector3 zero3 = VectorExtensions.FastNormalized(new Vector3(0, 0, 99));
        Near(0f, zero3.x);
        Near(0f, zero3.y);
        Near(99f, zero3.z);

        var random = new Random(0xC0111D3);
        for (int i = 0; i < 20000; i++)
        {
            float x = (float)((random.NextDouble() * 2000.0) - 1000.0);
            float y = (float)((random.NextDouble() * 2000.0) - 1000.0);
            if (MathF.Abs(x) + MathF.Abs(y) < 0.001f) x = 1f;
            Vector2 normalized = new Vector2(x, y).FastNormalized();
            Near(1f, MathF.Sqrt((normalized.x * normalized.x) + (normalized.y * normalized.y)), 0.002f);

            float z = (float)((random.NextDouble() * 100.0) - 50.0);
            Vector3 normalized3 = new Vector3(x, y, z).FastNormalized();
            Near(normalized.x, normalized3.x, 0.00001f);
            Near(normalized.y, normalized3.y, 0.00001f);
            Near(z, normalized3.z, 0f);

            Vector2 other = new Vector2(y * 0.5f, x * -0.25f);
            float expectedDistance = ((x - other.x) * (x - other.x)) + ((y - other.y) * (y - other.y));
            Near(expectedDistance, new Vector2(x, y).FastSqrDistance(other),
                MathF.Max(0.01f, MathF.Abs(expectedDistance) * 0.000001f));
        }

        for (int i = 0; i < 20000; i++)
        {
            Vector2 offset = VectorExtensions.GetRandomOffset2D(17.5f);
            Check((offset.x * offset.x) + (offset.y * offset.y) <= (17.5f * 17.5f) + 0.001f);
            Vector3 offset3 = VectorExtensions.GetRandomOffset(3.25f, -7f);
            Check((offset3.x * offset3.x) + (offset3.y * offset3.y) <= (3.25f * 3.25f) + 0.001f);
            Near(-7f, offset3.z, 0f);
        }
        Throws<ArgumentOutOfRangeException>(() => VectorExtensions.GetRandomOffset2D(-1f));
        Throws<ArgumentOutOfRangeException>(() => VectorExtensions.GetRandomOffset2D(float.NaN));
        Throws<ArgumentOutOfRangeException>(() => VectorExtensions.GetRandomOffset(float.PositiveInfinity));

        var transform = new GameObject("translation").transform;
        transform.position = new Vector3(10f, -4f, 77f);
        transform.ApplyFastTranslation(0.6f, -0.8f, 5f, 2f);
        Near(16f, transform.position.x);
        Near(-12f, transform.position.y);
        Near(77f, transform.position.z, 0f);

        for (int i = 1; i <= 10000; i++)
        {
            float fov = (i % 101) + 0.125f;
            float tile = ((i % 17) + 1) * 0.25f;
            int padding = i % 9;
            int expected = (int)MathF.Ceiling(fov / tile) + (padding * 2) +
                Config.Game.Map.InclusiveBoundsCellCount;
            Equal(expected, MapViewportController.CalculateVisualGridDimension(fov, tile, padding));
        }
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() =>
                MapViewportController.CalculateVisualGridDimension(bad, 1f, 0));
            Throws<ArgumentOutOfRangeException>(() =>
                MapViewportController.CalculateVisualGridDimension(1f, bad, 0));
        }
        Throws<ArgumentOutOfRangeException>(() =>
            MapViewportController.CalculateVisualGridDimension(1f, 1f, -1));
    }

    private static void TestEntityHealthSpritesAndLifecycle()
    {
        var entity = CreateComponent<ProbeEntity>(addRenderer: true);
        entity.Initialize();
        Same(entity.transform, entity.entityTransform);
        Same(entity.GetComponent<SpriteRenderer>(), entity.spriteRenderer);
        Check(!entity.IsSpawned);

        entity.OnSpawn();
        Check(entity.IsSpawned);
        Check(entity.gameObject.activeSelf);
        Equal(1, entity.SpawnCalls);
        entity.OnDespawn();
        Check(!entity.IsSpawned);
        Check(!entity.gameObject.activeSelf);
        Equal(1, entity.DespawnCalls);

        entity.gameObject.SetActive(true);
        entity.ReleaseToPool();
        Equal(2, entity.DespawnCalls);
        entity.ReleaseToPool();
        Equal(2, entity.DespawnCalls);

        var data = ScriptableObject.CreateInstance<LivingEntityData>();
        var fallback = new Sprite { name = "fallback" };
        var up = new Sprite { name = "up" };
        var down = new Sprite { name = "down" };
        var left = new Sprite { name = "left" };
        var right = new Sprite { name = "right" };
        data.spriteSheet = fallback;
        data.spriteSheetUp = up;
        data.spriteSheetDown = down;
        data.spriteSheetLeft = left;
        data.spriteSheetRight = right;
        FastSchemaBlock block = default;
        block.SetFloat((int)AssetSchema.MaxHealth, 100f);
        block.SetFloat((int)AssetSchema.BaseSpeed, 4f);
        data.SetSchemaBlock(block);

        var living = CreateComponent<ProbeLiving>(addRenderer: true);
        living.Initialize();
        var observed = new List<float>();
        living.OnHealthChanged += observed.Add;
        living.LoadData(data);
        Near(100f, living.MaxHealth);
        Near(100f, living.CurrentHealth);
        Near(4f, living.BaseSpeed);
        Same(down, living.spriteRenderer.sprite);
        Equal(1, observed.Count);

        living.OnSpawn();
        living.TakeDamage(25f);
        Near(75f, living.CurrentHealth);
        Equal(2, observed.Count);
        living.Heal(-1f);
        living.Heal(0f);
        Near(75f, living.CurrentHealth);
        Equal(2, observed.Count);
        living.Heal(1000f);
        Near(100f, living.CurrentHealth);
        Equal(3, observed.Count);

        living.TakeDamage(25f);
        block.SetFloat((int)AssetSchema.MaxHealth, 200f);
        block.SetFloat((int)AssetSchema.BaseSpeed, 9f);
        data.SetSchemaBlock(block);
        living.RefreshDataFromAsset();
        Near(200f, living.MaxHealth);
        Near(150f, living.CurrentHealth);
        Near(9f, living.BaseSpeed);

        living.UpdateDirectionalSprite(0f, 1f);
        Same(up, living.spriteRenderer.sprite);
        Equal(FastMath.FacingDirection.Up, living.HitboxPreviewFacing);
        living.UpdateDirectionalSprite(-1f, 0f);
        Same(left, living.spriteRenderer.sprite);
        living.UpdateDirectionalSprite(1f, 0f);
        Same(right, living.spriteRenderer.sprite);
        living.UpdateDirectionalSprite(0f, -1f);
        Same(down, living.spriteRenderer.sprite);
        living.UpdateDirectionalSprite(0f, 0f);
        Same(down, living.spriteRenderer.sprite);
        data.spriteSheetUp = null;
        living.UpdateDirectionalSprite(0f, 1f);
        Same(fallback, living.spriteRenderer.sprite);

        living.TakeDamage(float.MaxValue);
        Check(!living.IsSpawned);
        Check(!living.gameObject.activeSelf);
    }

    private static ProbeEnemy SpawnEnemy(float x, float y, float health = 100f)
    {
        var enemy = CreateComponent<ProbeEnemy>(addRenderer: true);
        enemy.Initialize();
        enemy.Apply(health, 2f, 7f, 3f);
        enemy.entityTransform.position = new Vector3(x, y, 0f);
        enemy.OnSpawn();
        return enemy;
    }

    private static void TestEnemyBossAndPackedListMutation()
    {
        var random = new Random(0xB055);
        for (int i = 0; i < 50000; i++)
        {
            float sourceHealth = (float)((random.NextDouble() * 2000.0) - 1000.0);
            float sourceSpeed = (float)((random.NextDouble() * 200.0) - 100.0);
            float healthMultiplier = (float)((random.NextDouble() * 10.0) - 5.0);
            float speedMultiplier = (float)((random.NextDouble() * 10.0) - 5.0);
            Enemy.CalculateSpawnStats(sourceHealth, sourceSpeed, healthMultiplier, speedMultiplier,
                out float health, out float speed);
            Near(sourceHealth * healthMultiplier, health,
                MathF.Max(0.0001f, MathF.Abs(sourceHealth * healthMultiplier) * 0.000001f));
            Near(sourceSpeed * speedMultiplier, speed,
                MathF.Max(0.0001f, MathF.Abs(sourceSpeed * speedMultiplier) * 0.000001f));
        }

        ProbeEnemy first = SpawnEnemy(0f, 0f, 10f);
        ProbeEnemy second = SpawnEnemy(1f, 0f, 10f);
        ProbeEnemy third = SpawnEnemy(2f, 0f, 10f);
        ProbeEnemy outside = SpawnEnemy(20f, 0f, 10f);
        Equal(4, Enemy.ActiveEnemies.Count);
        Equal(0, first.ActiveListIndex);
        Equal(3, outside.ActiveListIndex);
        Throws<InvalidOperationException>(() => first.OnSpawn());

        Enemy.ApplyDamageInRadius(Vector3.zero, 4.01f, 100f);
        Equal(1, Enemy.ActiveEnemies.Count);
        Same(outside, Enemy.ActiveEnemies[0]);
        Equal(0, outside.ActiveListIndex);
        Equal(-1, first.ActiveListIndex);
        Equal(-1, second.ActiveListIndex);
        Equal(-1, third.ActiveListIndex);
        Check(first.DeathCalls == 1 && second.DeathCalls == 1 && third.DeathCalls == 1);

        float previous = outside.CurrentHealth;
        Enemy.ApplyDamageInRadius(Vector3.zero, -1f, 50f);
        Near(previous, outside.CurrentHealth);
        outside.SetTarget(new GameObject("target").transform);
        outside.Tick(0.25f);
        Equal(1, outside.TickMoves);
        var victim = new ProbeDamageable();
        outside.Attack(victim);
        Near(7f, victim.TotalDamage);

        var boss = CreateComponent<ProbeBoss>(addRenderer: true);
        boss.Initialize();
        boss.Apply(100f, 1f, 40f);
        int transitions = 0;
        boss.PhaseTwoStarted += () => transitions++;
        boss.OnSpawn();
        Equal(Config.Game.Boss.PhaseOne, boss.CurrentPhase);
        Check(!boss.IsEnraged);
        Near(40f, boss.Phase2HealthThreshold);
        boss.TakeDamage(59f);
        Equal(Config.Game.Boss.PhaseOne, boss.CurrentPhase);
        boss.TakeDamage(1f);
        Equal(Config.Game.Boss.PhaseTwo, boss.CurrentPhase);
        Check(boss.IsEnraged);
        Equal(1, transitions);
        boss.TakeDamage(1f);
        Equal(1, transitions);
        boss.OnDespawn();
        boss.OnSpawn();
        Equal(Config.Game.Boss.PhaseOne, boss.CurrentPhase);
        Check(!boss.IsEnraged);

        foreach (float threshold in new[] { -100f, 0f, 1f, 123.5f, float.MaxValue })
        {
            foreach (float multiplier in new[] { -2f, 0f, 0.5f, 1f, 99f })
                Near(threshold * multiplier, BossEnemy.ScalePhaseThreshold(threshold, multiplier),
                    MathF.Max(0.001f, MathF.Abs(threshold * multiplier) * 0.000001f));
        }
    }

    private static void TestProjectileLifecycleAndCentralIteration()
    {
        var projectile = CreateComponent<ProbeProjectile>(addRenderer: true);
        projectile.Initialize();
        Equal(Config.Game.EnvironmentData.UnregisteredListIndex, projectile.ActiveListIndex);
        projectile.Initialize(new Vector2(3f, 4f), 12.5f, 8f, 2);
        Near(12.5f, projectile.Damage);
        Near(8f, projectile.Speed);
        Equal(2, projectile.PiercingCount);
        Near(0.6f, projectile.Direction.x, 0.002f);
        Near(0.8f, projectile.Direction.y, 0.002f);

        projectile.entityTransform.position = new Vector3(1f, 2f, 9f);
        projectile.OnSpawn();
        Equal(1, Projectile.ActiveProjectiles.Count);
        projectile.Tick(0.5f);
        Near(3.4f, projectile.entityTransform.position.x, 0.01f);
        Near(5.2f, projectile.entityTransform.position.y, 0.01f);
        Near(9f, projectile.entityTransform.position.z, 0f);

        var target = new ProbeDamageable();
        projectile.OnHit(null);
        Near(0f, target.TotalDamage);
        projectile.OnHit(target);
        Near(12.5f, target.TotalDamage);
        Check(projectile.IsSpawned);
        projectile.OnHit(target);
        Near(25f, target.TotalDamage);
        Check(!projectile.IsSpawned);
        Equal(1, projectile.DespawnCalls);
        Equal(0, Projectile.ActiveProjectiles.Count);
        projectile.OnHit(target);
        Near(25f, target.TotalDamage);

        var lifetime = CreateComponent<ProbeProjectile>(addRenderer: true);
        lifetime.Initialize();
        lifetime.Initialize(new Vector2(1, 0), 1, 1, 99);
        lifetime.OnSpawn();
        lifetime.Tick(Config.Game.Weapons.Projectile.DefaultLifetime - 0.01f);
        Check(lifetime.IsSpawned);
        lifetime.Tick(0.02f);
        Check(!lifetime.IsSpawned);

        var projectiles = new List<ProbeProjectile>();
        for (int i = 0; i < 128; i++)
        {
            var item = CreateComponent<ProbeProjectile>(addRenderer: true);
            item.Initialize();
            item.Initialize(new Vector2(1, i), i, 1, 1);
            item.OnSpawn();
            projectiles.Add(item);
        }
        Equal(128, Projectile.ActiveProjectiles.Count);
        PlayerController.TickActiveProjectiles(Config.Game.Weapons.Projectile.DefaultLifetime + 1f);
        Equal(0, Projectile.ActiveProjectiles.Count);
        foreach (ProbeProjectile item in projectiles)
        {
            Check(!item.IsSpawned);
            Equal(-1, item.ActiveListIndex);
            Equal(1, item.DespawnCalls);
        }
    }

    private static void TestPowerUpsWeaponsAndInventory()
    {
        int idHash = FastMath.FastHash("power");
        var power = new PowerUp(idHash, PowerUpGrade.Legendary);
        Equal(idHash, power.PowerUpIdHash);
        Equal(Config.Game.Weapons.Inventory.InitialLevel, power.Level);
        Equal(PowerUpGrade.Legendary, power.Grade);
        Equal(Config.Game.Weapons.Inventory.PowerUpUses, power.UsesRemaining);
        for (int i = 0; i < 100; i++) power.UpgradeLevel();
        Equal(Config.Game.Weapons.Inventory.MaxLevel, power.Level);

        for (int grade = (int)PowerUpGrade.Legendary; grade >= (int)PowerUpGrade.Normal; grade--)
        {
            Equal((PowerUpGrade)grade, power.Grade);
            for (int use = 0; use < Config.Game.Weapons.Inventory.PowerUpUses; use++) power.ConsumeUse();
            Equal(Config.Game.Weapons.Inventory.PowerUpUses, power.UsesRemaining);
            if (grade > (int)PowerUpGrade.Normal) Equal((PowerUpGrade)(grade - 1), power.Grade);
        }
        Equal(Config.Game.Weapons.Inventory.InitialLevel, power.Level);

        var weapon = CreateComponent<ProbeWeapon>(invokeAwake: true);
        Equal(Config.Game.Weapons.Inventory.InitialLevel, weapon.Level);
        weapon.Configure(1f, 5f);
        weapon.Tick(0.01f);
        Equal(1, weapon.FireCalls);
        Near(1f, weapon.RemainingCooldown);
        weapon.Tick(0.25f);
        Equal(1, weapon.FireCalls);
        weapon.Tick(0.75f);
        Equal(2, weapon.FireCalls);
        weapon.Upgrade();
        Equal(2, weapon.Level);

        var controller = CreateComponent<WeaponController>();
        controller.Initialize(2);
        Check(controller.TryAddWeapon(weapon));
        var second = CreateComponent<ProbeWeapon>(invokeAwake: true);
        Check(controller.TryAddWeapon(second));
        Check(!controller.TryAddWeapon(CreateComponent<ProbeWeapon>(invokeAwake: true)));
        controller.TickWeapons(0.1f);
        Check(weapon.FireCalls >= 2);
        Check(second.FireCalls >= 1);

        var evolvingController = CreateComponent<WeaponController>();
        evolvingController.Initialize(1);
        var oldWeapon = CreateComponent<ProbeWeapon>(invokeAwake: true);
        oldWeapon.Configure(1f, 2f, Config.Game.Weapons.Inventory.MaxLevel);
        var evolvedPrefab = CreateComponent<ProbeWeapon>(invokeAwake: true);
        oldWeapon.AvailableEvolutions.Add(new WeaponEvolution("power", evolvedPrefab));
        Check(evolvingController.TryAddWeapon(oldWeapon));
        var maxPower = new PowerUp(idHash, PowerUpGrade.Epic);
        for (int i = maxPower.Level; i < Config.Game.Weapons.Inventory.MaxLevel; i++) maxPower.UpgradeLevel();
        evolvingController.AddPowerUp(maxPower);
        Check(evolvingController.TryEvolveWeapon(oldWeapon));
        Equal(1, evolvingController.activeWeapons.Count);
        NotSame(oldWeapon, evolvingController.activeWeapons[0]);
        Equal(Config.Game.Weapons.Inventory.PowerUpUses - 1,
            evolvingController.activePowerUps[0].UsesRemaining);
        Check(!oldWeapon.gameObject.activeSelf);
        Check(!evolvingController.TryEvolveWeapon(oldWeapon));

        Enemy.ActiveEnemies.Clear();
        ProbeEnemy near = SpawnEnemy(1f, 0f, 20f);
        ProbeEnemy far = SpawnEnemy(10f, 0f, 20f);
        var aura = CreateComponent<ProbeAuraWeapon>(invokeAwake: true);
        aura.transform.position = Vector3.zero;
        aura.radius = 2f;
        aura.SetDamage(5f);
        aura.Fire();
        Near(15f, near.CurrentHealth);
        Near(20f, far.CurrentHealth);

        var melee = CreateComponent<ProbeMeleeWeapon>(invokeAwake: true);
        melee.transform.position = Vector3.zero;
        melee.attackRange = 2f;
        melee.knockbackForce = 10f;
        melee.SetDamage(5f);
        Time.deltaTime = 0.5f;
        float oldX = near.entityTransform.position.x;
        melee.Fire();
        Near(10f, near.CurrentHealth);
        Check(near.entityTransform.position.x > oldX);
        Near(20f, far.CurrentHealth);

        var orbital = CreateComponent<ProbeOrbitalWeapon>(invokeAwake: true);
        orbital.transform.position = Vector3.zero;
        orbital.orbitRadius = 0f;
        orbital.damageRadius = 2f;
        orbital.projectileCount = 0;
        orbital.SetDamage(10f);
        float before = near.CurrentHealth;
        orbital.Fire();
        Near(before, near.CurrentHealth);
        orbital.projectileCount = 1;
        near.entityTransform.position = Vector3.zero;
        Time.deltaTime = 0.25f;
        orbital.Fire();
        Near(before - 2.5f, near.CurrentHealth);
    }

    private static void TestEntityAndGameObjectPooling()
    {
        var manager = CreateComponent<PoolManager>(invokeAwake: true);
        Same(manager, PoolManager.Instance);

        var prefab = CreateComponent<ProbeEntity>(addRenderer: true);
        prefab.Initialize();
        manager.Prewarm(prefab, 3);
        int key = prefab.gameObject.GetInstanceID();
        var spawned = (ProbeEntity)manager.Spawn(prefab, new Vector3(2, 3, 4), Quaternion.identity);
        Check(spawned != null);
        Check(spawned.IsSpawned);
        Equal(key, spawned.prefabPoolKey);
        Near(2f, spawned.transform.position.x);
        Near(3f, spawned.transform.position.y);
        Near(4f, spawned.transform.position.z);
        manager.Despawn(spawned);
        Check(!spawned.IsSpawned);
        Check(!spawned.gameObject.activeSelf);
        var reused = manager.SpawnByKey(key, new Vector3(-1, -2, -3), Quaternion.identity);
        Same(spawned, reused);
        Check(reused.IsSpawned);
        manager.Despawn(reused);
        manager.Despawn(reused);
        Check(!reused.IsSpawned);
        Equal(null, manager.SpawnByKey(int.MinValue, Vector3.zero, Quaternion.identity));
        manager.Prewarm(null, 10);

        var rented = new List<GameEntity>();
        for (int i = 0; i < Config.Game.Pool.DefaultPoolCapacity; i++)
        {
            GameEntity item = manager.SpawnByKey(key, Vector3.zero, Quaternion.identity);
            Check(item != null);
            rented.Add(item);
        }
        Equal(null, manager.SpawnByKey(key, Vector3.zero, Quaternion.identity));
        foreach (GameEntity item in rented) manager.Despawn(item);

        var objectPrefab = new GameObject("VfxPrefab");
        manager.PrewarmGameObject(objectPrefab, 2);
        int objectKey = objectPrefab.GetInstanceID();
        GameObject vfx = manager.SpawnGameObject(objectPrefab, new Vector3(8, 9, 10), Quaternion.identity);
        Check(vfx != null && vfx.activeSelf);
        Near(8f, vfx.transform.position.x);
        manager.DespawnGameObject(vfx, objectKey, 1f);
        Check(vfx.activeSelf);
        Time.deltaTime = 0.4f;
        Invoke(manager, "Update");
        Check(vfx.activeSelf);
        Time.deltaTime = 0.7f;
        Invoke(manager, "Update");
        Check(!vfx.activeSelf);
        Same(vfx, manager.SpawnGameObject(objectPrefab, Vector3.zero, Quaternion.identity));

        manager.DespawnGameObject(vfx, objectKey, 5f);
        manager.DespawnGameObject(vfx, objectKey, 0f);
        Check(!vfx.activeSelf);
        Same(vfx, manager.SpawnGameObject(objectPrefab, Vector3.zero, Quaternion.identity));
        manager.DespawnGameObject(null, objectKey);
        manager.PrewarmGameObject(null, 100);

        UnityEngine.Object.Destroy(manager.gameObject);
        Equal(null, PoolManager.Instance);
        Equal(null, FastPoolRegistry<GameEntity>.Rent(key));
        Equal(null, FastPoolRegistry<GameObject>.Rent(objectKey));
    }
}
