using System;
using System.Collections.Generic;
using EFYV.Core.Controllers;
using EFYV.Core.Entities;
using EFYV.Core.Items;
using EFYV.Core.Managers;
using EFYV.Core.Utils;
using EFYV.Core.Weapons;
using EFYV.Core.Weapons.Implementations;
using EFYV.Core.Weapons.Types;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Math;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;
using WeaponData = EFYVBackend.Core.Models.WeaponData;

internal static partial class Program
{
    private static void TestWeaponsCooldownStateMachine()
    {
        // Overshoot is not banked: one giant delta produces exactly one fire and a full reset.
        var weapon = CreateComponent<ProbeWeapon>(invokeAwake: true);
        weapon.Configure(1.5f, 1f);
        weapon.Tick(500f);
        Equal(1, weapon.FireCalls);
        Near(1.5f, weapon.RemainingCooldown, 0f);
        weapon.Tick(1.49f);
        Equal(1, weapon.FireCalls);
        weapon.Tick(0.01f);
        Equal(2, weapon.FireCalls);

        // Zero cooldown fires on every tick, even a zero-length one.
        var rapid = CreateComponent<ProbeWeapon>(invokeAwake: true);
        rapid.Configure(0f, 1f);
        for (int i = 0; i < 32; i++) rapid.Tick(0f);
        Equal(32, rapid.FireCalls);

        // Negative delta raises the cooldown and suppresses firing until it is repaid.
        var delayed = CreateComponent<ProbeWeapon>(invokeAwake: true);
        delayed.Configure(1f, 1f);
        delayed.Tick(-0.5f);
        Equal(0, delayed.FireCalls);
        Near(0.5f, delayed.RemainingCooldown, 0f);
        delayed.Tick(0.25f);
        Equal(0, delayed.FireCalls);
        delayed.Tick(0.25f);
        Equal(1, delayed.FireCalls);

        // Reference model: a naive cooldown simulation compared step-by-step over seeded
        // random delta sequences (including occasional negative deltas).
        var random = new Random(0x57EA9);
        foreach (float cooldown in new[] { 0f, 0.35f, 1.2f })
        {
            var modelled = CreateComponent<ProbeWeapon>(invokeAwake: true);
            modelled.Configure(cooldown, 1f);
            float expectedCooldown = 0f;
            int expectedFires = 0;
            for (int step = 0; step < 400; step++)
            {
                float delta = step % 10 == 9
                    ? (float)(-0.05 * random.NextDouble())
                    : (float)(random.NextDouble() * 0.4);
                expectedCooldown -= delta;
                if (expectedCooldown <= Config.Game.Weapons.CooldownReadyThreshold)
                {
                    expectedFires++;
                    expectedCooldown = cooldown;
                }
                modelled.Tick(delta);
                Equal(expectedFires, modelled.FireCalls);
                Near(expectedCooldown, modelled.RemainingCooldown, 0f);
            }
        }

        // The base upgrade only advances the level; combat stats are untouched.
        float damageBefore = weapon.BaseDamage;
        float cooldownBefore = weapon.CooldownTime;
        weapon.Upgrade();
        Equal(2, weapon.Level);
        Near(damageBefore, weapon.BaseDamage, 0f);
        Near(cooldownBefore, weapon.CooldownTime, 0f);
    }

    private static void TestWeaponsMagicWandTargetingAndUpgrades()
    {
        var wand = CreateComponent<MagicWandWeapon>(invokeAwake: true);
        Near(Config.Game.Weapons.MagicWand.DefaultSpeed, wand.projectileSpeed);
        Equal(Config.Game.Weapons.MagicWand.DefaultPierce, wand.basePierceCount);
        Near(Config.Game.Weapons.MagicWand.DefaultCooldown, wand.CooldownTime);
        Near(Config.Game.Weapons.MagicWand.DefaultDamage, wand.BaseDamage);
        Equal(Config.Game.Weapons.MagicWand.DefaultLevel, wand.Level);

        // Upgrade reference model: pierce on every third level, additive damage, and a
        // cooldown that floors at the configured minimum.
        int expectedLevel = wand.Level;
        float expectedDamage = wand.BaseDamage;
        float expectedCooldown = wand.CooldownTime;
        int expectedPierce = wand.basePierceCount;
        for (int i = 0; i < 40; i++)
        {
            expectedLevel++;
            if (expectedLevel % Config.Game.Weapons.MagicWand.PierceUpgradeInterval ==
                Config.Game.Weapons.PierceUpgradeRemainder)
            {
                expectedPierce += Config.Game.Weapons.MagicWand.PierceUpgradeIncrement;
            }
            expectedDamage += Config.Game.Weapons.MagicWand.UpgradeDamage;
            expectedCooldown = MathF.Max(Config.Game.Weapons.MagicWand.MinCooldown,
                expectedCooldown - Config.Game.Weapons.MagicWand.UpgradeCooldownReduction);
            wand.Upgrade();
            Equal(expectedLevel, wand.Level);
            Equal(expectedPierce, wand.basePierceCount);
            Near(expectedDamage, wand.BaseDamage, 0.0001f);
            Near(expectedCooldown, wand.CooldownTime, 0.0001f);
        }
        Near(Config.Game.Weapons.MagicWand.MinCooldown, wand.CooldownTime, 0.0001f);

        // With no enemies (and no prefab) firing is a safe no-op that needs no pool.
        Equal(0, Enemy.ActiveEnemies.Count);
        wand.Fire();
        Equal(0, Projectile.ActiveProjectiles.Count);

        var pool = CreateComponent<PoolManager>(invokeAwake: true);
        var prefab = CreateComponent<Projectile>();
        var armedWand = CreateComponent<MagicWandWeapon>(invokeAwake: true);
        armedWand.projectilePrefab = prefab;

        var enemies = new List<ProbeEnemy>();
        for (int i = 0; i < 40; i++) enemies.Add(SpawnEnemy(0f, 0f, 1000f));

        // Enemies present but no prefab assigned: still nothing is spawned.
        wand.Fire();
        Equal(0, Projectile.ActiveProjectiles.Count);

        // Nearest-target property test against a naive reference model. The wand's
        // broad-phase X-axis culling must never change the selected target.
        var random = new Random(0x3A9D);
        for (int round = 0; round < 30; round++)
        {
            var origin = new Vector3(
                (float)((random.NextDouble() * 20.0) - 10.0),
                (float)((random.NextDouble() * 20.0) - 10.0),
                0f);
            armedWand.transform.position = origin;
            foreach (ProbeEnemy enemy in enemies)
            {
                enemy.entityTransform.position = new Vector3(
                    (float)((random.NextDouble() * 60.0) - 30.0),
                    (float)((random.NextDouble() * 60.0) - 30.0),
                    0f);
            }

            Enemy expectedNearest = null;
            float bestDistanceSqr = float.MaxValue;
            for (int i = 0; i < Enemy.ActiveEnemies.Count; i++)
            {
                Enemy candidate = Enemy.ActiveEnemies[i];
                float dx = origin.x - candidate.entityTransform.position.x;
                float dy = origin.y - candidate.entityTransform.position.y;
                float distanceSqr = (dx * dx) + (dy * dy);
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    expectedNearest = candidate;
                }
            }

            armedWand.Fire();
            Equal(1, Projectile.ActiveProjectiles.Count);
            Projectile spawned = Projectile.ActiveProjectiles[0];
            Near(origin.x, spawned.entityTransform.position.x, 0f);
            Near(origin.y, spawned.entityTransform.position.y, 0f);
            float expectedX = expectedNearest.entityTransform.position.x - origin.x;
            float expectedY = expectedNearest.entityTransform.position.y - origin.y;
            float magnitude = MathF.Sqrt((expectedX * expectedX) + (expectedY * expectedY));
            Near(expectedX / magnitude, spawned.Direction.x, 0.01f);
            Near(expectedY / magnitude, spawned.Direction.y, 0.01f);
            Near(armedWand.BaseDamage, spawned.Damage, 0f);
            Near(armedWand.projectileSpeed, spawned.Speed, 0f);
            Equal(armedWand.basePierceCount, spawned.PiercingCount);
            pool.Despawn(spawned);
        }
        Equal(0, Projectile.ActiveProjectiles.Count);

        UnityEngine.Object.Destroy(pool.gameObject);
    }

    private static void TestWeaponsAuraMeleeBoundaries()
    {
        var enemies = new List<ProbeEnemy>();
        for (int i = 0; i < 24; i++) enemies.Add(SpawnEnemy(1000f + i, 1000f, 1000000f));

        var aura = CreateComponent<ProbeAuraWeapon>(invokeAwake: true);
        aura.transform.position = Vector3.zero;
        Near(Config.Game.Weapons.Aura.DefaultRadius, aura.radius);

        var garlic = CreateComponent<GarlicAura>(invokeAwake: true);
        Near(Config.Game.Weapons.Aura.Garlic.Damage, garlic.BaseDamage);
        Near(Config.Game.Weapons.Aura.Garlic.Radius, garlic.radius);
        Near(Config.Game.Weapons.Aura.Garlic.Cooldown, garlic.CooldownTime);
        var swords = CreateComponent<SpinningSwordsAura>(invokeAwake: true);
        Near(Config.Game.Weapons.Aura.Swords.Damage, swords.BaseDamage);
        Near(Config.Game.Weapons.Aura.Swords.Radius, swords.radius);
        Near(Config.Game.Weapons.Aura.Swords.Cooldown, swords.CooldownTime);

        // Inclusive boundary, exclusion just outside, and z-plane independence.
        aura.radius = 2.5f;
        aura.SetDamage(7f);
        enemies[0].entityTransform.position = new Vector3(2.5f, 0f, 0f);
        enemies[1].entityTransform.position = new Vector3(2.51f, 0f, 0f);
        enemies[2].entityTransform.position = new Vector3(0f, -2.5f, 123f);
        aura.Fire();
        Near(1000000f - 7f, enemies[0].CurrentHealth, 0f);
        Near(1000000f, enemies[1].CurrentHealth, 0f);
        Near(1000000f - 7f, enemies[2].CurrentHealth, 0f);

        // Randomized property rounds against a naive squared-distance damage model.
        var expectedHealth = new float[enemies.Count];
        for (int i = 0; i < enemies.Count; i++) expectedHealth[i] = enemies[i].CurrentHealth;
        var random = new Random(0x41B4);
        for (int round = 0; round < 6; round++)
        {
            float radius = 0.5f + (float)(random.NextDouble() * 5.5);
            float damage = 1f + (float)(random.NextDouble() * 5.0);
            aura.radius = radius;
            aura.SetDamage(damage);
            for (int i = 0; i < enemies.Count; i++)
            {
                enemies[i].entityTransform.position = new Vector3(
                    (float)((random.NextDouble() * 16.0) - 8.0),
                    (float)((random.NextDouble() * 16.0) - 8.0),
                    (float)((random.NextDouble() * 4.0) - 2.0));
            }
            float sqrRadius = radius * radius;
            for (int i = 0; i < enemies.Count; i++)
            {
                float dx = enemies[i].entityTransform.position.x - 0f;
                float dy = enemies[i].entityTransform.position.y - 0f;
                if ((dx * dx) + (dy * dy) <= sqrRadius) expectedHealth[i] -= damage;
            }
            aura.Fire();
            for (int i = 0; i < enemies.Count; i++) Near(expectedHealth[i], enemies[i].CurrentHealth, 0f);
        }

        // One Fire can kill every enemy while the packed list swap-removes under iteration.
        for (int i = 0; i < enemies.Count; i++)
        {
            enemies[i].SetHealth(1f);
            enemies[i].entityTransform.position = new Vector3(i * 0.01f, 0f, 0f);
        }
        aura.radius = 10f;
        aura.SetDamage(2f);
        aura.Fire();
        Equal(0, Enemy.ActiveEnemies.Count);
        foreach (ProbeEnemy enemy in enemies)
        {
            Equal(1, enemy.DeathCalls);
            Equal(Config.Game.EnvironmentData.UnregisteredListIndex, enemy.ActiveListIndex);
            Check(!enemy.IsSpawned);
        }

        // Melee: implementation defaults, inclusive range, knockback displacement model,
        // zero-offset safety, and no knockback for enemies killed by the hit.
        var bat = CreateComponent<AluminumBat>(invokeAwake: true);
        Near(Config.Game.Weapons.Melee.Bat.Damage, bat.BaseDamage);
        Near(Config.Game.Weapons.Melee.Bat.AttackRange, bat.attackRange);
        Near(Config.Game.Weapons.Melee.Bat.Knockback, bat.knockbackForce);
        Near(Config.Game.Weapons.Melee.Bat.Cooldown, bat.CooldownTime);
        var sword = CreateComponent<Longsword>(invokeAwake: true);
        Near(Config.Game.Weapons.Melee.Sword.Damage, sword.BaseDamage);
        Near(Config.Game.Weapons.Melee.Sword.AttackRange, sword.attackRange);
        Near(Config.Game.Weapons.Melee.Sword.Knockback, sword.knockbackForce);
        Near(Config.Game.Weapons.Melee.Sword.Cooldown, sword.CooldownTime);

        var melee = CreateComponent<ProbeMeleeWeapon>(invokeAwake: true);
        Near(Config.Game.Weapons.Melee.DefaultAttackRange, melee.attackRange);
        Near(Config.Game.Weapons.Melee.DefaultKnockback, melee.knockbackForce);
        melee.transform.position = new Vector3(1f, -2f, 0f);
        melee.attackRange = 3f;
        melee.knockbackForce = 12f;
        melee.SetDamage(4f);
        // Knockback distance derives from the DRIVING TICK's deltaTime (#24). The
        // global clock is poisoned to prove Fire no longer reads Time.deltaTime.
        Time.deltaTime = 99f;
        float step = 12f * 0.2f;

        ProbeEnemy insideEnemy = SpawnEnemy(2.5f, -2f, 50f);
        ProbeEnemy boundaryEnemy = SpawnEnemy(1f, 1f, 50f);
        ProbeEnemy farEnemy = SpawnEnemy(10f, 10f, 50f);
        ProbeEnemy colocatedEnemy = SpawnEnemy(1f, -2f, 50f);
        ProbeEnemy fragileEnemy = SpawnEnemy(1.5f, -2f, 3f);
        melee.Tick(0.2f);

        Near(46f, insideEnemy.CurrentHealth, 0f);
        Near(46f, boundaryEnemy.CurrentHealth, 0f);
        Near(50f, farEnemy.CurrentHealth, 0f);
        Near(46f, colocatedEnemy.CurrentHealth, 0f);
        Check(!fragileEnemy.IsSpawned);
        Equal(1, fragileEnemy.DeathCalls);

        Near(2.5f + step, insideEnemy.entityTransform.position.x, 0.0001f);
        Near(-2f, insideEnemy.entityTransform.position.y, 0.0001f);
        Near(1f, boundaryEnemy.entityTransform.position.x, 0.0001f);
        Near(1f + step, boundaryEnemy.entityTransform.position.y, 0.0001f);
        Near(10f, farEnemy.entityTransform.position.x, 0f);
        // Zero offset: the stubbed normalized zero vector leaves the enemy in place (no NaN).
        Near(1f, colocatedEnemy.entityTransform.position.x, 0f);
        Near(-2f, colocatedEnemy.entityTransform.position.y, 0f);
        // Killed enemies are not knocked back.
        Near(1.5f, fragileEnemy.entityTransform.position.x, 0f);
        Near(-2f, fragileEnemy.entityTransform.position.y, 0f);

        // A Fire no tick ever drove (TickDeltaTime defaults to 0) deals damage but
        // moves nothing: melee knockback is strictly tick-scaled (#24).
        var untickedMelee = CreateComponent<ProbeMeleeWeapon>(invokeAwake: true);
        untickedMelee.transform.position = new Vector3(9.5f, 10f, 0f);
        untickedMelee.attackRange = 1f;
        untickedMelee.knockbackForce = 50f;
        untickedMelee.SetDamage(1f);
        untickedMelee.Fire();
        Near(49f, farEnemy.CurrentHealth, 0f);
        Near(10f, farEnemy.entityTransform.position.x, 0f);
        Near(10f, farEnemy.entityTransform.position.y, 0f);
    }

    private static void TestWeaponsOrbitalRotationAndDamage()
    {
        var axe = CreateComponent<SpinningAxe>(invokeAwake: true);
        Near(Config.Game.Weapons.Orbital.Axe.Damage, axe.BaseDamage);
        Near(Config.Game.Weapons.Orbital.Axe.OrbitRadius, axe.orbitRadius);
        Near(Config.Game.Weapons.Orbital.Axe.RotationSpeed, axe.rotationSpeed);
        Equal(Config.Game.Weapons.Orbital.Axe.Count, axe.projectileCount);
        Near(Config.Game.Weapons.Orbital.Axe.DamageRadius, axe.damageRadius);
        var beyblade = CreateComponent<Beyblade>(invokeAwake: true);
        Near(Config.Game.Weapons.Orbital.Beyblade.Damage, beyblade.BaseDamage);
        Near(Config.Game.Weapons.Orbital.Beyblade.OrbitRadius, beyblade.orbitRadius);
        Near(Config.Game.Weapons.Orbital.Beyblade.RotationSpeed, beyblade.rotationSpeed);
        Equal(Config.Game.Weapons.Orbital.Beyblade.Count, beyblade.projectileCount);
        Near(Config.Game.Weapons.Orbital.Beyblade.DamageRadius, beyblade.damageRadius);

        var orbital = CreateComponent<ProbeOrbitalWeapon>(invokeAwake: true);
        Near(Config.Game.Weapons.Orbital.DefaultOrbitRadius, orbital.orbitRadius);
        Near(Config.Game.Weapons.Orbital.DefaultRotationSpeed, orbital.rotationSpeed);
        Equal(Config.Game.Weapons.Orbital.DefaultProjectileCount, orbital.projectileCount);
        Near(Config.Game.Weapons.Orbital.DefaultDamageRadius, orbital.damageRadius);
        WeaponData data = GetField<WeaponData>(orbital, "Data");
        Near(Config.Game.Weapons.Orbital.InitialAngle, data.CurrentAngle, 0f);

        // Rotation accumulates per tick parameter and stays wrapped for sane speeds.
        orbital.transform.position = Vector3.zero;
        orbital.projectileCount = 0;
        orbital.rotationSpeed = 180f;
        float expectedAngle = data.CurrentAngle;
        for (int i = 0; i < 24; i++)
        {
            orbital.Tick(0.25f);
            expectedAngle += 180f * 0.25f;
            if (expectedAngle >= Config.Game.Weapons.Orbital.FullCircleDegrees)
                expectedAngle -= Config.Game.Weapons.Orbital.FullCircleDegrees;
            data = GetField<WeaponData>(orbital, "Data");
            Near(expectedAngle, data.CurrentAngle, 0.001f);
            Check(data.CurrentAngle >= 0f && data.CurrentAngle < Config.Game.Weapons.Orbital.FullCircleDegrees);
        }

        // Documents current behavior: the wrap subtracts a single revolution, so one huge
        // step leaves currentAngle far above 360 (harmless: the Taylor trig wraps radians).
        orbital.rotationSpeed = 100000f;
        orbital.Tick(1f);
        data = GetField<WeaponData>(orbital, "Data");
        Check(data.CurrentAngle > Config.Game.Weapons.Orbital.FullCircleDegrees);

        // Visual sprites follow the orbit; extra slots and null entries are tolerated.
        var placed = CreateComponent<ProbeOrbitalWeapon>(invokeAwake: true);
        placed.transform.position = new Vector3(5f, -3f, 2f);
        placed.orbitRadius = 3f;
        placed.projectileCount = 4;
        placed.rotationSpeed = 90f;
        var sprites = new Transform[3];
        sprites[0] = new GameObject("weapons-orb-0").transform;
        sprites[1] = null;
        sprites[2] = new GameObject("weapons-orb-2").transform;
        placed.visualSprites = sprites;
        Time.deltaTime = 0f;
        placed.Tick(0.5f);
        data = GetField<WeaponData>(placed, "Data");
        float baseAngle = data.CurrentAngle;
        Near(45f, baseAngle, 0.001f);
        foreach (int spriteIndex in new[] { 0, 2 })
        {
            float degrees = baseAngle + (spriteIndex * (360f / 4f));
            float radians = degrees * (MathF.PI / 180f);
            // Taylor approximation tolerance: |error| <= ~0.06 per axis before scaling.
            Near(5f + (MathF.Cos(radians) * 3f), sprites[spriteIndex].position.x, 0.25f);
            Near(-3f + (MathF.Sin(radians) * 3f), sprites[spriteIndex].position.y, 0.25f);
            Near(2f + Config.Game.Weapons.DefaultZOffset, sprites[spriteIndex].position.z, 0.0001f);
        }

        // Overlapping orbital projectiles stack damage once per projectile per Fire.
        // Contact damage scales by the DRIVING TICK's deltaTime (#24); the global
        // clock is poisoned to prove Fire no longer reads Time.deltaTime.
        ProbeEnemy pivot = SpawnEnemy(0f, 0f, 1000f);
        var stack = CreateComponent<ProbeOrbitalWeapon>(invokeAwake: true);
        stack.transform.position = Vector3.zero;
        stack.orbitRadius = 0f;
        stack.damageRadius = 1f;
        stack.projectileCount = 3;
        stack.SetDamage(8f);
        Time.deltaTime = 99f;
        stack.Tick(0.25f);
        Near(1000f - (3f * 8f * 0.25f), pivot.CurrentHealth, 0.0001f);

        // A custom-dt driver stays in sync: damage follows the tick parameter
        // exactly, never the (still poisoned) global clock.
        Time.deltaTime = 0.5f;
        float healthBefore = pivot.CurrentHealth;
        stack.Tick(0.125f);
        Near(healthBefore - (3f * 8f * 0.125f), pivot.CurrentHealth, 0.0001f);

        // A Fire no tick ever drove (TickDeltaTime defaults to 0) is harmless.
        var untickedOrbital = CreateComponent<ProbeOrbitalWeapon>(invokeAwake: true);
        untickedOrbital.transform.position = Vector3.zero;
        untickedOrbital.orbitRadius = 0f;
        untickedOrbital.damageRadius = 1f;
        untickedOrbital.projectileCount = 2;
        untickedOrbital.SetDamage(8f);
        healthBefore = pivot.CurrentHealth;
        untickedOrbital.Fire();
        Near(healthBefore, pivot.CurrentHealth, 0f);

        // Non-positive projectile counts are a complete no-op.
        stack.projectileCount = -2;
        healthBefore = pivot.CurrentHealth;
        stack.Fire();
        Near(healthBefore, pivot.CurrentHealth, 0f);

        // Orbital ticking never touches the cooldown machinery.
        data = GetField<WeaponData>(stack, "Data");
        Near(0f, data.CurrentCooldown, 0f);
    }

    private static void TestWeaponsDropAndSplashSeededPlacement()
    {
        var bomb = CreateComponent<BombDrop>(invokeAwake: true);
        Near(Config.Game.Weapons.Drop.Bomb.Damage, bomb.BaseDamage);
        Near(Config.Game.Weapons.Drop.Bomb.DamageRadius, bomb.damageRadius);
        Equal(Config.Game.Weapons.Drop.Bomb.Count, bomb.dropCount);
        Near(Config.Game.Weapons.Drop.Bomb.Cooldown, bomb.CooldownTime);
        var meteor = CreateComponent<MeteorDrop>(invokeAwake: true);
        Near(Config.Game.Weapons.Drop.Meteor.Damage, meteor.BaseDamage);
        Near(Config.Game.Weapons.Drop.Meteor.DamageRadius, meteor.damageRadius);
        Equal(Config.Game.Weapons.Drop.Meteor.Count, meteor.dropCount);
        Near(Config.Game.Weapons.Drop.Meteor.Cooldown, meteor.CooldownTime);
        var lightning = CreateComponent<LightningSplash>(invokeAwake: true);
        Near(Config.Game.Weapons.Splash.Lightning.Damage, lightning.BaseDamage);
        Near(Config.Game.Weapons.Splash.Lightning.SplashRadius, lightning.splashRadius);
        Near(Config.Game.Weapons.Splash.Lightning.DamageRadius, lightning.damageRadius);
        Equal(Config.Game.Weapons.Splash.Lightning.Count, lightning.splashCount);
        Near(Config.Game.Weapons.Splash.Lightning.Cooldown, lightning.CooldownTime);
        var holyWater = CreateComponent<HolyWaterSplash>(invokeAwake: true);
        Near(Config.Game.Weapons.Splash.HolyWater.Damage, holyWater.BaseDamage);
        Near(Config.Game.Weapons.Splash.HolyWater.SplashRadius, holyWater.splashRadius);
        Near(Config.Game.Weapons.Splash.HolyWater.DamageRadius, holyWater.damageRadius);
        Equal(Config.Game.Weapons.Splash.HolyWater.Count, holyWater.splashCount);
        Near(Config.Game.Weapons.Splash.HolyWater.Cooldown, holyWater.CooldownTime);

        // Without a main camera, firing a drop weapon is a no-op and consumes no randomness.
        Camera.main = null;
        var drop = CreateComponent<WeaponsProbeDropWeapon>(invokeAwake: true);
        Near(Config.Game.Weapons.Drop.DefaultDamageRadius, drop.damageRadius);
        Equal(Config.Game.Weapons.Drop.DefaultCount, drop.dropCount);
        drop.SetDamage(5f);
        FastRandom.SetSeed(0xD201u);
        drop.Fire();
        uint observed = FastRandom.Next();
        FastRandom.SetSeed(0xD201u);
        Equal(FastRandom.Next(), observed);

        // Seeded reference model: replay the RNG to predict every drop point, then verify
        // the exact per-enemy hit count from the naive 2D containment model.
        var cameraObject = new GameObject("weapons-camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographicSize = 4f;
        camera.aspect = 2f;
        cameraObject.transform.position = new Vector3(10f, 20f, -5f);
        Camera.main = camera;

        float fovHeight = 4f;
        float fovWidth = fovHeight * 2f;
        drop.damageRadius = 1.5f;
        drop.dropCount = 5;
        const uint dropSeed = 0xD202u;
        var dropPoints = new List<Vector2>();
        FastRandom.SetSeed(dropSeed);
        for (int d = 0; d < drop.dropCount; d++)
        {
            float x = FastRandom.Range(10f - fovWidth, 10f + fovWidth);
            float y = FastRandom.Range(20f - fovHeight, 20f + fovHeight);
            Check(x >= 10f - fovWidth && x <= 10f + fovWidth, "Drop X escaped the camera FOV.");
            Check(y >= 20f - fovHeight && y <= 20f + fovHeight, "Drop Y escaped the camera FOV.");
            dropPoints.Add(new Vector2(x, y));
        }

        var targets = new List<ProbeEnemy>();
        foreach (Vector2 point in dropPoints) targets.Add(SpawnEnemy(point.x, point.y, 100000f));
        ProbeEnemy sentinel = SpawnEnemy(200f, 20f, 100000f);
        var expectedHealth = new float[targets.Count];
        for (int i = 0; i < targets.Count; i++)
        {
            float health = targets[i].CurrentHealth;
            foreach (Vector2 point in dropPoints)
            {
                float dx = targets[i].entityTransform.position.x - point.x;
                float dy = targets[i].entityTransform.position.y - point.y;
                if ((dx * dx) + (dy * dy) <= drop.damageRadius * drop.damageRadius) health -= 5f;
            }
            expectedHealth[i] = health;
        }
        FastRandom.SetSeed(dropSeed);
        drop.Fire();
        for (int i = 0; i < targets.Count; i++) Near(expectedHealth[i], targets[i].CurrentHealth, 0f);
        Near(100000f, sentinel.CurrentHealth, 0f);
        Check(expectedHealth[0] < 100000f, "An enemy standing on a drop point must be hit.");

        // A non-positive drop count fires nothing and consumes no randomness.
        drop.dropCount = -3;
        FastRandom.SetSeed(0xD203u);
        drop.Fire();
        observed = FastRandom.Next();
        FastRandom.SetSeed(0xD203u);
        Equal(FastRandom.Next(), observed);

        // Vfx path: bombs place pooled visuals exactly on the seeded drop points and the
        // pool reclaims them after the configured lifetime.
        var pool = CreateComponent<PoolManager>(invokeAwake: true);
        var vfxPrefab = new GameObject("weapons-drop-vfx");
        drop.bombVisualPrefab = vfxPrefab;
        drop.dropCount = 2;
        drop.damageRadius = 0.5f;
        const uint vfxSeed = 0xD204u;
        var vfxPoints = new List<Vector2>();
        FastRandom.SetSeed(vfxSeed);
        for (int d = 0; d < drop.dropCount; d++)
        {
            vfxPoints.Add(new Vector2(
                FastRandom.Range(10f - fovWidth, 10f + fovWidth),
                FastRandom.Range(20f - fovHeight, 20f + fovHeight)));
        }
        FastRandom.SetSeed(vfxSeed);
        drop.Fire();
        var vfxClones = new List<GameObject>();
        foreach (GameObject candidate in UnityEngine.Object.FindObjectsOfType<GameObject>())
        {
            if (candidate.name == "weapons-drop-vfx(Clone)" && candidate.activeSelf) vfxClones.Add(candidate);
        }
        Equal(2, vfxClones.Count);
        foreach (Vector2 point in vfxPoints)
        {
            bool found = false;
            foreach (GameObject clone in vfxClones)
            {
                if (MathF.Abs(clone.transform.position.x - point.x) < 0.0001f &&
                    MathF.Abs(clone.transform.position.y - point.y) < 0.0001f &&
                    MathF.Abs(clone.transform.position.z - Config.Game.Weapons.DefaultZOffset) < 0.0001f)
                {
                    found = true;
                }
            }
            Check(found, "Bomb vfx missing from an expected drop point.");
        }
        Time.deltaTime = Config.Game.Weapons.Drop.VfxLifetime + 0.01f;
        Invoke(pool, "Update");
        foreach (GameObject clone in vfxClones) Check(!clone.activeSelf);

        // Splash: seeded offsets stay inside the splash radius and damage follows the
        // exact containment model around each splash point.
        var splash = CreateComponent<WeaponsProbeSplashWeapon>(invokeAwake: true);
        Near(Config.Game.Weapons.Splash.DefaultSplashRadius, splash.splashRadius);
        Near(Config.Game.Weapons.Splash.DefaultDamageRadius, splash.damageRadius);
        Equal(Config.Game.Weapons.Splash.DefaultCount, splash.splashCount);
        splash.transform.position = new Vector3(-40f, 70f, 0f);
        splash.SetDamage(9f);
        splash.splashRadius = 3f;
        splash.damageRadius = 1.25f;
        splash.splashCount = 4;
        const uint splashSeed = 0x5985u;
        var splashPoints = new List<Vector3>();
        FastRandom.SetSeed(splashSeed);
        for (int s = 0; s < splash.splashCount; s++)
        {
            Vector3 point = splash.transform.position +
                VectorExtensions.GetRandomOffset(splash.splashRadius, Config.Game.Weapons.DefaultZOffset);
            float offsetX = point.x - (-40f);
            float offsetY = point.y - 70f;
            Check((offsetX * offsetX) + (offsetY * offsetY) <= (3f * 3f) + 0.001f,
                "Splash point escaped the splash radius.");
            Near(Config.Game.Weapons.DefaultZOffset, point.z, 0f);
            splashPoints.Add(point);
        }
        for (int i = 0; i < targets.Count; i++)
        {
            Vector3 position = i < splashPoints.Count
                ? new Vector3(splashPoints[i].x, splashPoints[i].y, 0f)
                : new Vector3(500f, 500f, 0f);
            targets[i].entityTransform.position = position;
        }
        sentinel.entityTransform.position = new Vector3(-500f, -500f, 0f);
        var expectedSplashHealth = new float[targets.Count];
        for (int i = 0; i < targets.Count; i++)
        {
            float health = targets[i].CurrentHealth;
            foreach (Vector3 point in splashPoints)
            {
                float dx = targets[i].entityTransform.position.x - point.x;
                float dy = targets[i].entityTransform.position.y - point.y;
                if ((dx * dx) + (dy * dy) <= splash.damageRadius * splash.damageRadius) health -= 9f;
            }
            expectedSplashHealth[i] = health;
        }
        float sentinelHealth = sentinel.CurrentHealth;
        FastRandom.SetSeed(splashSeed);
        splash.Fire();
        for (int i = 0; i < targets.Count; i++) Near(expectedSplashHealth[i], targets[i].CurrentHealth, 0f);
        Near(sentinelHealth, sentinel.CurrentHealth, 0f);
        Check(expectedSplashHealth[0] < 100000f, "An enemy standing on a splash point must be hit.");

        // A zero splash count consumes no randomness.
        splash.splashCount = 0;
        FastRandom.SetSeed(0x5986u);
        splash.Fire();
        observed = FastRandom.Next();
        FastRandom.SetSeed(0x5986u);
        Equal(FastRandom.Next(), observed);

        // A negative splash radius surfaces the backend's argument guard.
        splash.splashCount = 1;
        splash.splashRadius = -0.5f;
        Throws<ArgumentOutOfRangeException>(() => splash.Fire());

        Camera.main = null;
        UnityEngine.Object.Destroy(pool.gameObject);
        FastRandom.SetSeed(Config.Backend.Random.DefaultSeed);
    }

    private static void TestWeaponsProjectileWeaponPoolContracts()
    {
        var gun = CreateComponent<GunWeapon>(invokeAwake: true);
        Near(Config.Game.Weapons.Projectile.Gun.Damage, gun.BaseDamage);
        Near(Config.Game.Weapons.Projectile.Gun.Speed, gun.projectileSpeed);
        Equal(Config.Game.Weapons.Projectile.Gun.Pierce, gun.basePierceCount);
        Near(Config.Game.Weapons.Projectile.Gun.Cooldown, gun.CooldownTime);
        var bow = CreateComponent<BowWeapon>(invokeAwake: true);
        Near(Config.Game.Weapons.Projectile.Bow.Damage, bow.BaseDamage);
        Near(Config.Game.Weapons.Projectile.Bow.Speed, bow.projectileSpeed);
        Equal(Config.Game.Weapons.Projectile.Bow.Pierce, bow.basePierceCount);
        Near(Config.Game.Weapons.Projectile.Bow.Cooldown, bow.CooldownTime);

        // No enemies: firing returns before touching the pool (works with no PoolManager).
        Equal(0, Enemy.ActiveEnemies.Count);
        gun.Fire();
        Equal(0, Projectile.ActiveProjectiles.Count);

        var pool = CreateComponent<PoolManager>(invokeAwake: true);

        // Nearest-target selection (#18): the far FIRST registration is ignored in
        // favor of the closest enemy, matching the MagicWandWeapon reference model.
        ProbeEnemy firstRegistered = SpawnEnemy(100f, 0f, 500f);
        ProbeEnemy nearSecond = SpawnEnemy(-1f, 0f, 500f);
        var prefab = CreateComponent<Projectile>();
        pool.Prewarm(prefab, 1);
        gun.projectilePrefabKey = PoolManager.GetPoolKey(prefab.gameObject);
        gun.transform.position = Vector3.zero;
        gun.Fire();
        Equal(1, Projectile.ActiveProjectiles.Count);
        Projectile spawned = Projectile.ActiveProjectiles[0];
        Near(-1f, spawned.Direction.x, 0.005f);
        Near(0f, spawned.Direction.y, 0.005f);
        Near(0f, spawned.entityTransform.position.x, 0f);
        Near(gun.BaseDamage, spawned.Damage, 0f);
        Near(gun.projectileSpeed, spawned.Speed, 0f);
        Equal(gun.basePierceCount, spawned.PiercingCount);
        Equal(Faction.Player, spawned.OwnerFaction);
        Near(500f, firstRegistered.CurrentHealth, 0f);
        Near(500f, nearSecond.CurrentHealth, 0f);
        pool.Despawn(spawned);

        // An unregistered prefab key rents nothing and must not throw.
        gun.projectilePrefabKey = int.MinValue;
        gun.Fire();
        Equal(0, Projectile.ActiveProjectiles.Count);

        // Type-check BEFORE activation (#18): a mis-keyed pool entry is returned to
        // its own pool unharmed instead of being spawned uninitialized and leaked.
        var entityPrefab = CreateComponent<ProbeEntity>(addRenderer: true);
        entityPrefab.Initialize();
        pool.Prewarm(entityPrefab, 1);
        gun.projectilePrefabKey = PoolManager.GetPoolKey(entityPrefab.gameObject);
        gun.Fire();
        Equal(0, Projectile.ActiveProjectiles.Count);
        foreach (ProbeEntity candidate in UnityEngine.Object.FindObjectsOfType<ProbeEntity>())
        {
            Check(!candidate.IsSpawned, "A mis-keyed pool entry must never be left spawned.");
            Equal(0, candidate.SpawnCalls);
            Equal(0, candidate.DespawnCalls);
        }
        // The returned entry is still rentable through the normal pool contract.
        var recovered = (ProbeEntity)pool.SpawnByKey(
            PoolManager.GetPoolKey(entityPrefab.gameObject), Vector3.zero, Quaternion.identity);
        Check(recovered != null, "The mis-keyed entry must remain available to its own pool.");
        Check(recovered.IsSpawned);
        Equal(1, recovered.SpawnCalls);
        pool.Despawn(recovered);

        // Preferred wiring (#18): a typed Projectile prefab reference spawns through
        // PoolManager.Spawn (the MagicWandWeapon pattern) with no key bookkeeping,
        // and stamps the firing weapon's faction on the projectile.
        var armedGun = CreateComponent<GunWeapon>(invokeAwake: true);
        var typedPrefab = CreateComponent<Projectile>();
        armedGun.projectilePrefab = typedPrefab;
        armedGun.transform.position = Vector3.zero;
        armedGun.Fire();
        Equal(1, Projectile.ActiveProjectiles.Count);
        Projectile typedSpawned = Projectile.ActiveProjectiles[0];
        Near(-1f, typedSpawned.Direction.x, 0.005f);
        Near(0f, typedSpawned.Direction.y, 0.005f);
        Near(armedGun.BaseDamage, typedSpawned.Damage, 0f);
        Near(armedGun.projectileSpeed, typedSpawned.Speed, 0f);
        Equal(armedGun.basePierceCount, typedSpawned.PiercingCount);
        Equal(Faction.Player, typedSpawned.OwnerFaction);
        pool.Despawn(typedSpawned);

        UnityEngine.Object.Destroy(pool.gameObject);
    }

    private static void TestWeaponsControllerEvolutionMatrix()
    {
        int maxLevel = Config.Game.Weapons.Inventory.MaxLevel;
        int fullUses = Config.Game.Weapons.Inventory.PowerUpUses;

        // An uninitialized (or negatively initialized) controller accepts no weapons.
        var controller = CreateComponent<WeaponController>();
        Check(!controller.TryAddWeapon(CreateComponent<ProbeWeapon>(invokeAwake: true)));
        controller.Initialize(-1);
        Check(!controller.TryAddWeapon(CreateComponent<ProbeWeapon>(invokeAwake: true)));
        controller.TickWeapons(1f);

        controller.Initialize(3);
        var first = CreateComponent<ProbeWeapon>(invokeAwake: true);
        var middle = CreateComponent<ProbeWeapon>(invokeAwake: true);
        var last = CreateComponent<ProbeWeapon>(invokeAwake: true);
        Check(controller.TryAddWeapon(first));
        Check(controller.TryAddWeapon(middle));
        Check(controller.TryAddWeapon(last));
        Check(!controller.TryAddWeapon(CreateComponent<ProbeWeapon>(invokeAwake: true)));

        // Failure matrix. None of these may consume the held powerup.
        first.Configure(10f, 1f, maxLevel - 1);
        first.AvailableEvolutions.Add(new WeaponEvolution("stone",
            CreateComponent<WeaponsEvolvedAlphaWeapon>(invokeAwake: true)));
        var stonePower = new PowerUp(FastMath.FastHash("stone"), PowerUpGrade.Rare);
        for (int i = stonePower.Level; i < maxLevel; i++) stonePower.UpgradeLevel();
        controller.AddPowerUp(stonePower);
        Check(!controller.TryEvolveWeapon(first), "Below max level must not evolve.");

        middle.Configure(10f, 1f, maxLevel);
        Check(!controller.TryEvolveWeapon(middle), "No evolutions listed must not evolve.");

        middle.AvailableEvolutions.Add(new WeaponEvolution("Stone",
            CreateComponent<WeaponsEvolvedAlphaWeapon>(invokeAwake: true)));
        Check(!controller.TryEvolveWeapon(middle), "PowerUp id hashing is case-sensitive.");

        middle.AvailableEvolutions.Clear();
        middle.AvailableEvolutions.Add(new WeaponEvolution("ember",
            CreateComponent<WeaponsEvolvedAlphaWeapon>(invokeAwake: true)));
        var lowEmber = new PowerUp(FastMath.FastHash("ember"), PowerUpGrade.Epic);
        controller.AddPowerUp(lowEmber);
        Check(!controller.TryEvolveWeapon(middle), "A below-max powerup must not satisfy an evolution.");

        middle.AvailableEvolutions.Clear();
        middle.AvailableEvolutions.Add(new WeaponEvolution("stone", null));
        Check(!controller.TryEvolveWeapon(middle), "A null evolved prefab must abort the evolution.");
        Equal(fullUses, controller.activePowerUps[0].UsesRemaining);
        Same(middle, controller.activeWeapons[1]);

        var foreign = CreateComponent<ProbeWeapon>(invokeAwake: true);
        foreign.Configure(10f, 1f, maxLevel);
        foreign.AvailableEvolutions.Add(new WeaponEvolution("stone",
            CreateComponent<WeaponsEvolvedAlphaWeapon>(invokeAwake: true)));
        Check(!controller.TryEvolveWeapon(foreign), "A weapon this controller does not own must not evolve.");
        Equal(fullUses, controller.activePowerUps[0].UsesRemaining);

        // Seeded two-way selection: predict the xorshift pick, then verify the chosen
        // evolution's powerup (and only it) is consumed and the slot is replaced in place.
        var alphaPrefab = CreateComponent<WeaponsEvolvedAlphaWeapon>(invokeAwake: true);
        var betaPrefab = CreateComponent<WeaponsEvolvedBetaWeapon>(invokeAwake: true);
        middle.AvailableEvolutions.Clear();
        middle.AvailableEvolutions.Add(new WeaponEvolution("stone", alphaPrefab));
        middle.AvailableEvolutions.Add(new WeaponEvolution("gale", betaPrefab));
        var galePower = new PowerUp(FastMath.FastHash("gale"), PowerUpGrade.Legendary);
        for (int i = galePower.Level; i < maxLevel; i++) galePower.UpgradeLevel();
        controller.AddPowerUp(galePower);

        const uint evolveSeed = 0xEE01u;
        FastRandom.SetSeed(evolveSeed);
        int expectedChoice = (int)(FastRandom.Next() % 2u);
        FastRandom.SetSeed(evolveSeed);
        Check(controller.TryEvolveWeapon(middle));
        Equal(3, controller.activeWeapons.Count);
        Same(first, controller.activeWeapons[0]);
        Same(last, controller.activeWeapons[2]);
        NotSame(middle, controller.activeWeapons[1]);
        Weapon evolved = controller.activeWeapons[1];
        Equal(expectedChoice == 0 ? typeof(WeaponsEvolvedAlphaWeapon) : typeof(WeaponsEvolvedBetaWeapon),
            evolved.GetType());
        // (Parenting under the controller is not observable here: the stub CloneObject
        // copies the gameObject backing field, so the clone component keeps the source
        // prefab's GameObject and SetParent lands on a detached clone object.)
        Near(0f, evolved.transform.localPosition.x, 0f);
        Near(0f, evolved.transform.localPosition.y, 0f);
        // The clone re-runs Awake, so an evolved weapon restarts at the initial level.
        Equal(Config.Game.Weapons.Inventory.InitialLevel, evolved.Level);
        int stoneIndex = 0;
        int galeIndex = 2;
        int consumedIndex = expectedChoice == 0 ? stoneIndex : galeIndex;
        int untouchedIndex = expectedChoice == 0 ? galeIndex : stoneIndex;
        Equal(fullUses - 1, controller.activePowerUps[consumedIndex].UsesRemaining);
        Equal(fullUses, controller.activePowerUps[untouchedIndex].UsesRemaining);
        Equal(fullUses, controller.activePowerUps[1].UsesRemaining);
        Check(!middle.gameObject.activeSelf, "The pre-evolution weapon must be destroyed.");
        Check(!controller.TryEvolveWeapon(middle), "A replaced weapon may not evolve again.");

        // Duplicate maxed powerups: FindIndex consumes the first match only.
        var duplicateController = CreateComponent<WeaponController>();
        duplicateController.Initialize(1);
        var duplicateWeapon = CreateComponent<ProbeWeapon>(invokeAwake: true);
        duplicateWeapon.Configure(10f, 1f, maxLevel);
        duplicateWeapon.AvailableEvolutions.Add(new WeaponEvolution("twin",
            CreateComponent<WeaponsEvolvedAlphaWeapon>(invokeAwake: true)));
        Check(duplicateController.TryAddWeapon(duplicateWeapon));
        var twinA = new PowerUp(FastMath.FastHash("twin"), PowerUpGrade.Normal);
        for (int i = twinA.Level; i < maxLevel; i++) twinA.UpgradeLevel();
        var twinB = new PowerUp(FastMath.FastHash("twin"), PowerUpGrade.Normal);
        for (int i = twinB.Level; i < maxLevel; i++) twinB.UpgradeLevel();
        duplicateController.AddPowerUp(twinA);
        duplicateController.AddPowerUp(twinB);
        FastRandom.SetSeed(0xEE02u);
        Check(duplicateController.TryEvolveWeapon(duplicateWeapon));
        Equal(fullUses - 1, duplicateController.activePowerUps[0].UsesRemaining);
        Equal(fullUses, duplicateController.activePowerUps[1].UsesRemaining);

        // A weapon may evolve itself from inside TickWeapons without corrupting iteration.
        var tickController = CreateComponent<WeaponController>();
        tickController.Initialize(2);
        var selfEvolving = CreateComponent<WeaponsSelfEvolvingWeapon>(invokeAwake: true);
        selfEvolving.Controller = tickController;
        selfEvolving.SetLevel(maxLevel);
        selfEvolving.AvailableEvolutions.Add(new WeaponEvolution("surge",
            CreateComponent<WeaponsEvolvedBetaWeapon>(invokeAwake: true)));
        var surgePower = new PowerUp(FastMath.FastHash("surge"), PowerUpGrade.Normal);
        for (int i = surgePower.Level; i < maxLevel; i++) surgePower.UpgradeLevel();
        tickController.AddPowerUp(surgePower);
        var companion = CreateComponent<ProbeWeapon>(invokeAwake: true);
        companion.Configure(5f, 1f);
        Check(tickController.TryAddWeapon(selfEvolving));
        Check(tickController.TryAddWeapon(companion));
        FastRandom.SetSeed(0xEE03u);
        tickController.TickWeapons(0.1f);
        Check(selfEvolving.EvolveResult, "The in-tick evolution must succeed.");
        Equal(2, tickController.activeWeapons.Count);
        Equal(typeof(WeaponsEvolvedBetaWeapon), tickController.activeWeapons[0].GetType());
        Same(companion, tickController.activeWeapons[1]);
        Equal(1, companion.FireCalls);
        Check(!selfEvolving.gameObject.activeSelf);

        FastRandom.SetSeed(Config.Backend.Random.DefaultSeed);
    }

    private static void TestWeaponsPowerUpLifecycleModel()
    {
        int initialLevel = Config.Game.Weapons.Inventory.InitialLevel;
        int maxLevel = Config.Game.Weapons.Inventory.MaxLevel;
        int fullUses = Config.Game.Weapons.Inventory.PowerUpUses;

        // The level survives a grade degrade; only a Normal-grade exhaustion resets it.
        var keeper = new PowerUp(FastMath.FastHash("keeper"), PowerUpGrade.Legendary);
        for (int i = keeper.Level; i < maxLevel; i++) keeper.UpgradeLevel();
        Equal(maxLevel, keeper.Level);
        for (int use = 1; use < fullUses; use++)
        {
            keeper.ConsumeUse();
            Equal(fullUses - use, keeper.UsesRemaining);
            Equal(PowerUpGrade.Legendary, keeper.Grade);
        }
        keeper.ConsumeUse();
        Equal(PowerUpGrade.Epic, keeper.Grade);
        Equal(maxLevel, keeper.Level);
        Equal(fullUses, keeper.UsesRemaining);
        keeper.UpgradeLevel();
        Equal(maxLevel, keeper.Level);

        var normal = new PowerUp(FastMath.FastHash("normal"), PowerUpGrade.Normal);
        for (int i = normal.Level; i < maxLevel; i++) normal.UpgradeLevel();
        for (int use = 0; use < fullUses; use++) normal.ConsumeUse();
        Equal(PowerUpGrade.Normal, normal.Grade);
        Equal(initialLevel, normal.Level);
        Equal(fullUses, normal.UsesRemaining);

        // Struct value semantics: consuming a copy leaves the original untouched.
        var original = new PowerUp(FastMath.FastHash("copy"), PowerUpGrade.Epic);
        PowerUp copy = original;
        copy.ConsumeUse();
        Equal(fullUses, original.UsesRemaining);
        Equal(fullUses - 1, copy.UsesRemaining);
        Equal(original.PowerUpIdHash, copy.PowerUpIdHash);

        // Randomized op-sequence reference model, including an out-of-range starting grade.
        var random = new Random(0x90EE);
        foreach (int startGrade in new[] { 0, 1, 2, 3, 7 })
        {
            var subject = new PowerUp(FastMath.FastHash("model-" + startGrade), (PowerUpGrade)startGrade);
            int modelLevel = initialLevel;
            int modelGrade = startGrade;
            int modelUses = fullUses;
            for (int op = 0; op < 400; op++)
            {
                if (random.Next(3) == 0)
                {
                    subject.UpgradeLevel();
                    if (modelLevel < maxLevel) modelLevel++;
                }
                else
                {
                    subject.ConsumeUse();
                    modelUses--;
                    if (modelUses <= 0)
                    {
                        if (modelGrade > (int)PowerUpGrade.Normal) modelGrade--;
                        else modelLevel = initialLevel;
                        modelUses = fullUses;
                    }
                }
                Equal(modelLevel, subject.Level);
                Equal(modelGrade, (int)subject.Grade);
                Equal(modelUses, subject.UsesRemaining);
            }
        }
    }

    private static void TestWeaponsFactionOwnershipAndGameOver()
    {
        // A WeaponController stamps its owner's combat side on Initialize and on
        // every added weapon (#21): enemy hosts fight the player, all other hosts
        // fight enemies.
        var playerObject = new GameObject("weapons-faction-player");
        playerObject.AddComponent<SpriteRenderer>();
        var playerWeapons = playerObject.AddComponent<WeaponController>();
        var player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);
        Same(player, PlayerController.Instance);
        Equal(Faction.Player, playerWeapons.OwnerFaction);
        var playerBlade = CreateComponent<ProbeMeleeWeapon>(invokeAwake: true);
        playerBlade.attackRange = 0f;
        Check(playerWeapons.TryAddWeapon(playerBlade));
        Equal(Faction.Player, playerBlade.OwnerFaction);

        var enemyObject = new GameObject("weapons-faction-enemy");
        enemyObject.AddComponent<SpriteRenderer>();
        var enemyWeapons = enemyObject.AddComponent<WeaponController>();
        var armedEnemy = (ProbeEnemy)enemyObject.AddComponent(typeof(ProbeEnemy), true);
        armedEnemy.Apply(50f, 1f, 25f, 0f);
        enemyWeapons.Initialize(4);
        Equal(Faction.Enemy, enemyWeapons.OwnerFaction);

        // Enemy-owned aimed weapon: it targets the PLAYER (ignoring a closer enemy)
        // and the spawned projectile carries the enemy faction.
        var pool = CreateComponent<PoolManager>(invokeAwake: true);
        var prefab = CreateComponent<Projectile>();
        var enemyWand = CreateComponent<MagicWandWeapon>(invokeAwake: true);
        enemyWand.projectilePrefab = prefab;
        Check(enemyWeapons.TryAddWeapon(enemyWand));
        Equal(Faction.Enemy, enemyWand.OwnerFaction);
        player.entityTransform.position = Vector3.zero;
        enemyWand.transform.position = new Vector3(3f, 4f, 0f);
        ProbeEnemy decoy = SpawnEnemy(3f, 3.5f, 50f);
        enemyWand.Fire();
        Equal(1, Projectile.ActiveProjectiles.Count);
        Projectile shot = Projectile.ActiveProjectiles[0];
        Equal(Faction.Enemy, shot.OwnerFaction);
        Near(-0.6f, shot.Direction.x, 0.005f);
        Near(-0.8f, shot.Direction.y, 0.005f);
        Near(50f, decoy.CurrentHealth, 0f);
        pool.Despawn(shot);

        float playerFullHealth = player.CurrentHealth;

        // Enemy-owned aura: only the player is damaged, enemies are never
        // friendly-fired, and range still gates the hit.
        var enemyAura = CreateComponent<ProbeAuraWeapon>(invokeAwake: true);
        enemyAura.OwnerFaction = Faction.Enemy;
        enemyAura.transform.position = player.entityTransform.position;
        enemyAura.radius = 2f;
        enemyAura.SetDamage(6f);
        enemyAura.Fire();
        Near(playerFullHealth - 6f, player.CurrentHealth, 0f);
        Near(50f, decoy.CurrentHealth, 0f);
        WeaponsClearPlayerIFrames(player);
        enemyAura.transform.position = new Vector3(50f, 50f, 0f);
        enemyAura.Fire();
        Near(playerFullHealth - 6f, player.CurrentHealth, 0f);

        // Enemy-owned melee (#21 + #24): swings at the player only, with knockback
        // scaled by the driving tick's deltaTime (the global clock is poisoned).
        var enemyMelee = CreateComponent<ProbeMeleeWeapon>(invokeAwake: true);
        enemyMelee.OwnerFaction = Faction.Enemy;
        enemyMelee.transform.position = new Vector3(-1f, 0f, 0f);
        enemyMelee.attackRange = 3f;
        enemyMelee.knockbackForce = 10f;
        enemyMelee.SetDamage(4f);
        WeaponsClearPlayerIFrames(player);
        player.entityTransform.position = Vector3.zero;
        Time.deltaTime = 99f;
        enemyMelee.Tick(0.5f);
        Near(playerFullHealth - 10f, player.CurrentHealth, 0f);
        Near(5f, player.entityTransform.position.x, 0.0001f);
        Near(0f, player.entityTransform.position.y, 0.0001f);
        Near(50f, decoy.CurrentHealth, 0f);

        // Enemy-owned orbital: tick-scaled contact damage lands on the player only.
        var enemyOrbital = CreateComponent<ProbeOrbitalWeapon>(invokeAwake: true);
        enemyOrbital.OwnerFaction = Faction.Enemy;
        enemyOrbital.transform.position = player.entityTransform.position;
        enemyOrbital.orbitRadius = 0f;
        enemyOrbital.damageRadius = 1f;
        enemyOrbital.projectileCount = 1;
        enemyOrbital.SetDamage(8f);
        WeaponsClearPlayerIFrames(player);
        enemyOrbital.Tick(0.25f);
        Near(playerFullHealth - 12f, player.CurrentHealth, 0f);
        Near(50f, decoy.CurrentHealth, 0f);

        // A live player still takes enemy contact damage through the trigger path.
        var playerCollider = playerObject.AddComponent<Collider2D>();
        WeaponsClearPlayerIFrames(player);
        Invoke(armedEnemy, "OnTriggerStay2D", playerCollider);
        Near(playerFullHealth - 37f, player.CurrentHealth, 0f);

        // Game over (#25): a lethal hit latches IsDead, raises the static
        // OnPlayerDied broadcast exactly once, and the entire enemy side stops
        // attacking the corpse.
        int gameOvers = 0;
        Action onDied = () => gameOvers++;
        PlayerController.OnPlayerDied += onDied;
        try
        {
            WeaponsClearPlayerIFrames(player);
            player.TakeDamage(1000000f);
            Check(player.IsDead);
            Equal(1, gameOvers);

            // Aimed weapons no longer find a target: nothing is spawned.
            enemyWand.Fire();
            Equal(0, Projectile.ActiveProjectiles.Count);

            // Radius and melee weapons skip the dead player entirely.
            float deadHealth = player.CurrentHealth;
            WeaponsClearPlayerIFrames(player);
            enemyAura.transform.position = player.entityTransform.position;
            enemyAura.Fire();
            enemyMelee.transform.position = player.entityTransform.position;
            enemyMelee.Tick(0.5f);
            enemyOrbital.transform.position = player.entityTransform.position;
            enemyOrbital.Tick(0.25f);
            Near(deadHealth, player.CurrentHealth, 0f);

            // Enemies stop chasing the corpse; custom (non-player) targets still work.
            armedEnemy.SetTarget(player.entityTransform);
            armedEnemy.Tick(0.1f);
            Equal(0, armedEnemy.TickMoves);
            var rally = new GameObject("weapons-faction-rally").transform;
            armedEnemy.SetTarget(rally);
            armedEnemy.Tick(0.1f);
            Equal(1, armedEnemy.TickMoves);

            // Contact damage also ignores the dead player.
            WeaponsClearPlayerIFrames(player);
            Invoke(armedEnemy, "OnTriggerStay2D", playerCollider);
            Near(deadHealth, player.CurrentHealth, 0f);
        }
        finally
        {
            PlayerController.OnPlayerDied -= onDied;
        }

        UnityEngine.Object.Destroy(pool.gameObject);
    }

    // Clears the player's invincibility window between scripted hits. PlayerData is
    // a struct, so the reflected copy must be written back after mutation.
    private static void WeaponsClearPlayerIFrames(PlayerController player)
    {
        var data = GetField<EFYVBackend.Core.Models.PlayerData>(player, "playerData");
        data.IFrameTimer = 0f;
        SetField(player, "playerData", data);
    }

    private sealed class WeaponsProbeDropWeapon : DropWeapon
    {
        public void SetDamage(float value) { BaseDamage = value; }
    }

    private sealed class WeaponsProbeSplashWeapon : SplashWeapon
    {
        public void SetDamage(float value) { BaseDamage = value; }
    }

    private sealed class WeaponsEvolvedAlphaWeapon : Weapon
    {
        public override void Fire() { }
    }

    private sealed class WeaponsEvolvedBetaWeapon : Weapon
    {
        public override void Fire() { }
    }

    private sealed class WeaponsSelfEvolvingWeapon : Weapon
    {
        public WeaponController Controller;
        public bool EvolveResult;
        public void SetLevel(int level) { Level = level; }
        public override void Fire()
        {
            EvolveResult = Controller != null && Controller.TryEvolveWeapon(this);
        }
    }
}
