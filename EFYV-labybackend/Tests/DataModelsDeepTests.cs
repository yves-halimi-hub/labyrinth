using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Math;
using EFYVBackend.Core.Models;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;
using LabyMakeConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using SharedConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared;

namespace EFYVBackend.Verification
{
    // Deep coverage for the data-model layer:
    //   Core/Data/EFYV-LabyrinthConfig.cs, Core/Data/FastSchemas.cs,
    //   Core/Models/GameDataStructs.cs, Core/Models/SharedData.cs
    internal static partial class Program
    {
        private static void TestDataModelsSchemaBlockReferenceModel()
        {
            // Bit-exact randomized reference model of every FastSchemaBlock mutator,
            // guarded by canaries on both sides of the block.
            GuardedSchema guarded = new GuardedSchema
            {
                Before = BeforeCanary,
                After = AfterCanary
            };
            int[] reference = new int[FastSchemaBlock.MaxSize];
            Random random = new Random(0xDA7A0);
            float[] specials =
            {
                float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0f, 0f,
                float.Epsilon, -float.Epsilon, float.MaxValue, -float.MaxValue,
                1.401298464e-42f, 123.456f, -1f, 0.5f, 3f
            };
            for (int operation = 0; operation < 20000; operation++)
            {
                int slot = random.Next(FastSchemaBlock.MaxSize);
                int op = random.Next(6);
                float operand = random.Next(3) == 0
                    ? specials[random.Next(specials.Length)]
                    : BitConverter.Int32BitsToSingle(unchecked((int)NextUInt(random)));
                switch (op)
                {
                    case 0:
                        int intValue = unchecked((int)NextUInt(random));
                        guarded.Block.SetInt(slot, intValue);
                        reference[slot] = intValue;
                        break;
                    case 1:
                        guarded.Block.SetFloat(slot, operand);
                        reference[slot] = BitConverter.SingleToInt32Bits(operand);
                        break;
                    case 2:
                        guarded.Block.ApplyAdditiveFloat(slot, operand);
                        float expectedSum = BitConverter.Int32BitsToSingle(reference[slot]) + operand;
                        reference[slot] = BitConverter.SingleToInt32Bits(expectedSum);
                        if (float.IsNaN(expectedSum))
                        {
                            // NaN payload propagation is implementation-defined between the
                            // two compiled add sites: require a NaN, then resync the model.
                            Assert(float.IsNaN(guarded.Block.GetFloat(slot)));
                            reference[slot] = guarded.Block.GetInt(slot);
                        }
                        break;
                    case 3:
                        guarded.Block.ApplyMultiplicativeFloat(slot, operand);
                        float expectedProduct = BitConverter.Int32BitsToSingle(reference[slot]) * operand;
                        reference[slot] = BitConverter.SingleToInt32Bits(expectedProduct);
                        if (float.IsNaN(expectedProduct))
                        {
                            Assert(float.IsNaN(guarded.Block.GetFloat(slot)));
                            reference[slot] = guarded.Block.GetInt(slot);
                        }
                        break;
                    case 4:
                        AssertEqual(reference[slot], guarded.Block.GetInt(slot));
                        break;
                    default:
                        AssertEqual(reference[slot], BitConverter.SingleToInt32Bits(guarded.Block.GetFloat(slot)));
                        break;
                }
                AssertEqual(reference[slot], guarded.Block.GetInt(slot));
                if (operation % 1000 == 999)
                {
                    for (int i = 0; i < FastSchemaBlock.MaxSize; i++)
                        AssertEqual(reference[i], guarded.Block.GetInt(i));
                }
            }
            AssertEqual(BeforeCanary, guarded.Before);
            AssertEqual(AfterCanary, guarded.After);

            // Bit preservation of every special value through SetFloat/GetFloat/GetInt.
            FastSchemaBlock block = default;
            for (int i = 0; i < specials.Length; i++)
            {
                block.SetFloat(7, specials[i]);
                AssertEqual(BitConverter.SingleToInt32Bits(specials[i]), block.GetInt(7));
                AssertEqual(
                    BitConverter.SingleToInt32Bits(specials[i]),
                    BitConverter.SingleToInt32Bits(block.GetFloat(7)));
            }
            // NaN payload bits written as int survive a GetFloat reinterpretation round trip.
            int payloadNaN = unchecked((int)0xFFC00042u);
            block.SetInt(3, payloadNaN);
            AssertEqual(payloadNaN, BitConverter.SingleToInt32Bits(block.GetFloat(3)));

            // Pinned IEEE semantics of the read-modify-write helpers.
            FastSchemaBlock arithmetic = default;
            arithmetic.SetFloat(0, 1f);
            arithmetic.ApplyAdditiveFloat(0, float.NaN);
            Assert(float.IsNaN(arithmetic.GetFloat(0)));
            arithmetic.SetFloat(0, float.PositiveInfinity);
            arithmetic.ApplyAdditiveFloat(0, float.NegativeInfinity);
            Assert(float.IsNaN(arithmetic.GetFloat(0)));
            arithmetic.SetFloat(0, float.MaxValue);
            arithmetic.ApplyAdditiveFloat(0, float.MaxValue);
            Assert(float.IsPositiveInfinity(arithmetic.GetFloat(0)));
            arithmetic.SetFloat(0, 0f);
            arithmetic.ApplyMultiplicativeFloat(0, float.PositiveInfinity);
            Assert(float.IsNaN(arithmetic.GetFloat(0)));
            arithmetic.SetFloat(0, -0f);
            AssertEqual(int.MinValue, arithmetic.GetInt(0));
            arithmetic.ApplyMultiplicativeFloat(0, -1f);
            AssertEqual(0, arithmetic.GetInt(0));
            arithmetic.SetFloat(0, float.Epsilon);
            arithmetic.ApplyMultiplicativeFloat(0, 0.5f);
            AssertEqual(0, arithmetic.GetInt(0));
        }

        private static unsafe void TestDataModelsSchemaBlockByteLayout()
        {
            // Slot i of a FastSchemaBlock occupies bytes [4*i, 4*i + 4) in native byte
            // order. This is the exact contract FastSaveEngine persists to disk.
            AssertEqual(sizeof(int), BackendConfig.Schema.BytesPerSlot);
            AssertEqual(
                BackendConfig.Schema.BlockSlotCount * BackendConfig.Schema.BytesPerSlot,
                BackendConfig.Schema.BlockSizeBytes);

            FastSchemaBlock block = default;
            Random random = new Random(0x1A70);
            int[] values = new int[FastSchemaBlock.MaxSize];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = unchecked((int)NextUInt(random));
                block.SetInt(i, values[i]);
            }
            byte[] bytes = DataModelsStructBytes(&block, sizeof(FastSchemaBlock));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, bytes.Length);
            for (int i = 0; i < values.Length; i++)
                AssertEqual(values[i], BitConverter.ToInt32(bytes, i * BackendConfig.Schema.BytesPerSlot));

            // Reverse direction: raw bytes copied over the struct read back through accessors.
            byte[] pattern = new byte[BackendConfig.Schema.BlockSizeBytes];
            random.NextBytes(pattern);
            fixed (byte* source = pattern)
            {
                Buffer.MemoryCopy(source, &block, sizeof(FastSchemaBlock), pattern.Length);
            }
            for (int i = 0; i < FastSchemaBlock.MaxSize; i++)
            {
                int expected = BitConverter.ToInt32(pattern, i * BackendConfig.Schema.BytesPerSlot);
                AssertEqual(expected, block.GetInt(i));
                AssertEqual(expected, BitConverter.SingleToInt32Bits(block.GetFloat(i)));
            }

            // PlayerMetaSchema field offsets: the on-disk save layout.
            int blockBytes = BackendConfig.Schema.BlockSizeBytes;
            int statsOffset = sizeof(int);
            int achievementsOffset = statsOffset + blockBytes;
            int toonsOffset = achievementsOffset + blockBytes;
            int expectedSize = toonsOffset + PlayerMetaSchema.MaxToons * blockBytes;
            AssertEqual(expectedSize, sizeof(PlayerMetaSchema));
            AssertEqual(16900, sizeof(PlayerMetaSchema));

            const int probeValue = 0x5A6B7C1D;
            PlayerMetaSchema probe = default;
            probe.TotalCoinsCollected = probeValue;
            DataModelsAssertOnlyInt(SaveStructBytes(probe), 0, probeValue);

            probe = default;
            probe.LegacyStats.SetInt(5, probeValue);
            DataModelsAssertOnlyInt(SaveStructBytes(probe), statsOffset + 5 * sizeof(int), probeValue);

            probe = default;
            probe.LegacyStats.SetInt(FastSchemaBlock.MaxSize - 1, probeValue);
            DataModelsAssertOnlyInt(
                SaveStructBytes(probe),
                statsOffset + (FastSchemaBlock.MaxSize - 1) * sizeof(int),
                probeValue);

            probe = default;
            probe.LegacyAchievements.SetInt(0, probeValue);
            DataModelsAssertOnlyInt(SaveStructBytes(probe), achievementsOffset, probeValue);

            probe = default;
            probe.LegacyAchievements.SetInt(FastSchemaBlock.MaxSize - 1, probeValue);
            DataModelsAssertOnlyInt(
                SaveStructBytes(probe),
                achievementsOffset + (FastSchemaBlock.MaxSize - 1) * sizeof(int),
                probeValue);

            int[][] toonProbes =
            {
                new[] { 0, 0 },
                new[] { 0, FastSchemaBlock.MaxSize - 1 },
                new[] { 1, 0 },
                new[] { 17, 29 },
                new[] { PlayerMetaSchema.MaxToons - 1, FastSchemaBlock.MaxSize - 1 }
            };
            for (int i = 0; i < toonProbes.Length; i++)
            {
                int toonIndex = toonProbes[i][0];
                int slot = toonProbes[i][1];
                probe = default;
                FastSchemaBlock toon = default;
                toon.SetInt(slot, probeValue);
                Assert(probe.TrySetToonBlock(toonIndex, in toon));
                DataModelsAssertOnlyInt(
                    SaveStructBytes(probe),
                    toonsOffset + toonIndex * blockBytes + slot * sizeof(int),
                    probeValue);
            }

            // TryGetToonBlock reads exactly the bytes TrySetToonBlock wrote.
            PlayerMetaSchema patterned = CreatePatternedProfile();
            byte[] patternedBytes = SaveStructBytes(patterned);
            for (int toonIndex = 0; toonIndex < PlayerMetaSchema.MaxToons; toonIndex += 9)
            {
                Assert(patterned.TryGetToonBlock(toonIndex, out FastSchemaBlock toon));
                for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
                {
                    AssertEqual(
                        BitConverter.ToInt32(patternedBytes, toonsOffset + toonIndex * blockBytes + slot * sizeof(int)),
                        toon.GetInt(slot));
                }
            }

            // Default() byte content: zero coins, DefaultStats() block, everything else zero.
            AssertEqual(0, BackendConfig.Schema.InitialTotalCoins);
            byte[] expectedDefault = new byte[expectedSize];
            FastSchemaBlock defaultStats = FastSchemaBlock.DefaultStats();
            for (int i = 0; i < FastSchemaBlock.MaxSize; i++)
            {
                byte[] slotBytes = BitConverter.GetBytes(defaultStats.GetInt(i));
                Buffer.BlockCopy(slotBytes, 0, expectedDefault, statsOffset + i * sizeof(int), sizeof(int));
            }
            AssertSequenceEqual(expectedDefault, SaveStructBytes(PlayerMetaSchema.Default()));
        }

        private static unsafe void TestDataModelsModelSlotMapping()
        {
            // Every unmanaged model wrapper is exactly one schema block, with the block at
            // offset zero, so pointer-based systems can reinterpret wrappers as blocks.
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(DropManagerData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(InventoryData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(SpawnManagerData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(MapManagerData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(UpgradeManagerData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(LayerData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(FrameData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(BrushToolData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(MovingToolData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(SystemData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(ViewportData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(WeaponData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(EntityData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(ProjectileData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(PowerUpData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(LegacyAchievementDefinitionData));
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(HitboxData));

            // Property -> schema-slot mapping. The sibling reflection test only proves each
            // property touches exactly one slot; these prove it is the RIGHT slot.
            WeaponData weapon = default;
            weapon.Level = 11;
            weapon.BaseDamage = 12.5f;
            weapon.CooldownTime = 13.5f;
            weapon.OrbitRadius = 14.5f;
            weapon.RotationSpeed = 15.5f;
            weapon.ProjectileCount = 16;
            weapon.DamageRadius = 17.5f;
            weapon.AuraRadius = 18.5f;
            weapon.DropCount = 19;
            weapon.AttackRange = 20.5f;
            weapon.KnockbackForce = 21.5f;
            weapon.ProjectileSpeed = 22.5f;
            weapon.PierceCount = 23;
            weapon.ProjectilePrefabKey = 24;
            weapon.SplashRadius = 25.5f;
            weapon.SplashCount = 26;
            weapon.CurrentCooldown = 27.5f;
            weapon.CurrentAngle = 28.5f;
            AssertEqual(11, weapon.Block.GetInt((int)WeaponSchema.Level));
            AssertEqual(12.5f, weapon.Block.GetFloat((int)WeaponSchema.BaseDamage));
            AssertEqual(13.5f, weapon.Block.GetFloat((int)WeaponSchema.CooldownTime));
            AssertEqual(14.5f, weapon.Block.GetFloat((int)WeaponSchema.OrbitRadius));
            AssertEqual(15.5f, weapon.Block.GetFloat((int)WeaponSchema.RotationSpeed));
            AssertEqual(16, weapon.Block.GetInt((int)WeaponSchema.ProjectileCount));
            AssertEqual(17.5f, weapon.Block.GetFloat((int)WeaponSchema.DamageRadius));
            AssertEqual(18.5f, weapon.Block.GetFloat((int)WeaponSchema.AuraRadius));
            AssertEqual(19, weapon.Block.GetInt((int)WeaponSchema.DropCount));
            AssertEqual(20.5f, weapon.Block.GetFloat((int)WeaponSchema.AttackRange));
            AssertEqual(21.5f, weapon.Block.GetFloat((int)WeaponSchema.KnockbackForce));
            AssertEqual(22.5f, weapon.Block.GetFloat((int)WeaponSchema.ProjectileSpeed));
            AssertEqual(23, weapon.Block.GetInt((int)WeaponSchema.PierceCount));
            AssertEqual(24, weapon.Block.GetInt((int)WeaponSchema.ProjectilePrefabKey));
            AssertEqual(25.5f, weapon.Block.GetFloat((int)WeaponSchema.SplashRadius));
            AssertEqual(26, weapon.Block.GetInt((int)WeaponSchema.SplashCount));
            AssertEqual(27.5f, weapon.Block.GetFloat((int)WeaponSchema.CurrentCooldown));
            AssertEqual(28.5f, weapon.Block.GetFloat((int)WeaponSchema.CurrentAngle));
            AssertEqual(0, weapon.Block.GetInt((int)WeaponSchema.WeaponIdHash));
            for (int slot = (int)WeaponSchema.SIZE; slot < FastSchemaBlock.MaxSize; slot++)
                AssertEqual(0, weapon.Block.GetInt(slot));
            FastSchemaBlock* weaponBlock = (FastSchemaBlock*)&weapon;
            AssertEqual(26, weaponBlock->GetInt((int)WeaponSchema.SplashCount));
            AssertEqual(11, weaponBlock->GetInt((int)WeaponSchema.Level));

            EntityData entity = default;
            entity.MaxHealth = 31.5f;
            entity.CurrentHealth = 32.5f;
            entity.BaseSpeed = 33.5f;
            entity.DamageToPlayer = 34.5f;
            entity.ExperienceValue = 35.5f;
            entity.ActiveListIndex = 36;
            entity.PrefabPoolKey = 37;
            entity.CurrentPhase = 38;
            entity.IsEnraged = true;
            entity.Phase2HealthThreshold = 39.5f;
            AssertEqual(31.5f, entity.Block.GetFloat((int)EntitySchema.MaxHealth));
            AssertEqual(32.5f, entity.Block.GetFloat((int)EntitySchema.CurrentHealth));
            AssertEqual(33.5f, entity.Block.GetFloat((int)EntitySchema.BaseSpeed));
            AssertEqual(34.5f, entity.Block.GetFloat((int)EntitySchema.DamageToPlayer));
            AssertEqual(35.5f, entity.Block.GetFloat((int)EntitySchema.ExperienceValue));
            AssertEqual(36, entity.Block.GetInt((int)EntitySchema.ActiveListIndex));
            AssertEqual(37, entity.Block.GetInt((int)EntitySchema.PrefabPoolKey));
            AssertEqual(38, entity.Block.GetInt((int)EntitySchema.CurrentPhase));
            AssertEqual(BackendConfig.Serialization.TrueValue, entity.Block.GetInt((int)EntitySchema.IsEnraged));
            AssertEqual(39.5f, entity.Block.GetFloat((int)EntitySchema.Phase2HealthThreshold));
            // Slots without wrapper properties stay untouched.
            AssertEqual(0, entity.Block.GetInt((int)EntitySchema.AnimationSpeed));
            AssertEqual(0, entity.Block.GetInt((int)EntitySchema.IsBlocking));
            AssertEqual(0, entity.Block.GetInt((int)EntitySchema.AnimTimer));
            AssertEqual(0, entity.Block.GetInt((int)EntitySchema.CurrentFrame));
            for (int slot = (int)EntitySchema.SIZE; slot < FastSchemaBlock.MaxSize; slot++)
                AssertEqual(0, entity.Block.GetInt(slot));

            ProjectileData projectile = default;
            projectile.Damage = 41.5f;
            projectile.PiercingCount = 42;
            projectile.Speed = 43.5f;
            projectile.ActiveListIndex = 44;
            projectile.DirectionX = 45.5f;
            projectile.DirectionY = 46.5f;
            projectile.CurrentPierces = 47;
            projectile.RemainingLifetime = 48.5f;
            AssertEqual(41.5f, projectile.Block.GetFloat((int)ProjectileSchema.Damage));
            AssertEqual(42, projectile.Block.GetInt((int)ProjectileSchema.PiercingCount));
            AssertEqual(43.5f, projectile.Block.GetFloat((int)ProjectileSchema.Speed));
            AssertEqual(44, projectile.Block.GetInt((int)ProjectileSchema.ActiveListIndex));
            AssertEqual(45.5f, projectile.Block.GetFloat((int)ProjectileSchema.DirectionX));
            AssertEqual(46.5f, projectile.Block.GetFloat((int)ProjectileSchema.DirectionY));
            AssertEqual(47, projectile.Block.GetInt((int)ProjectileSchema.CurrentPierces));
            AssertEqual(48.5f, projectile.Block.GetFloat((int)ProjectileSchema.RemainingLifetime));

            PlayerData player = default;
            player.Experience = 51.5f;
            player.Level = 52;
            player.ActiveToonId = "toon-a";
            player.SessionCoins = 53;
            player.IFrameDuration = 54.5f;
            player.IFrameTimer = 55.5f;
            AssertEqual(51.5f, player.Block.GetFloat((int)PlayerSchema.Experience));
            AssertEqual(52, player.Block.GetInt((int)PlayerSchema.Level));
            AssertEqual(ReferenceFnv1A("toon-a"), player.Block.GetInt((int)PlayerSchema.ActiveToonIdHash));
            AssertEqual(53, player.Block.GetInt((int)PlayerSchema.SessionCoins));
            AssertEqual(54.5f, player.Block.GetFloat((int)PlayerSchema.IFrameDuration));
            AssertEqual(55.5f, player.Block.GetFloat((int)PlayerSchema.IFrameTimer));

            SpawnManagerData spawner = default;
            spawner.SpawnRadius = 61.5f;
            spawner.BaseSpawnRate = 62.5f;
            spawner.DifficultyMultiplier = 63.5f;
            spawner.GameTimer = 64.5f;
            spawner.SpawnAccumulator = 65.5f;
            AssertEqual(61.5f, spawner.Block.GetFloat((int)SpawnManagerSchema.SpawnRadius));
            AssertEqual(62.5f, spawner.Block.GetFloat((int)SpawnManagerSchema.BaseSpawnRate));
            AssertEqual(63.5f, spawner.Block.GetFloat((int)SpawnManagerSchema.DifficultyMultiplier));
            AssertEqual(64.5f, spawner.Block.GetFloat((int)SpawnManagerSchema.GameTimer));
            AssertEqual(65.5f, spawner.Block.GetFloat((int)SpawnManagerSchema.SpawnAccumulator));

            DropManagerData drop = default;
            drop.DynamicDropMultiplier = 66.5f;
            AssertEqual(66.5f, drop.Block.GetFloat((int)SystemSchema.DynamicDropMultiplier));

            InventoryData inventory = default;
            inventory.MaxWeapons = 67;
            AssertEqual(67, inventory.Block.GetInt((int)WeaponControllerSchema.MaxWeapons));

            MapManagerData mapManager = default;
            mapManager.IsSwitchingMap = true;
            AssertEqual(BackendConfig.Serialization.TrueValue, mapManager.Block.GetInt((int)SystemSchema.IsSwitchingMap));
            AssertEqual(0, mapManager.Block.GetInt((int)SystemSchema.IsSpecialAttackPhase));

            UpgradeManagerData upgrade = default;
            upgrade.IsSpecialAttackPhase = true;
            upgrade.SpecialAttackInvokes = 68;
            AssertEqual(BackendConfig.Serialization.TrueValue, upgrade.Block.GetInt((int)SystemSchema.IsSpecialAttackPhase));
            AssertEqual(68, upgrade.Block.GetInt((int)SystemSchema.SpecialAttackInvokes));
            AssertEqual(0, upgrade.Block.GetInt((int)SystemSchema.IsSwitchingMap));

            SystemData system = default;
            system.CurrentBlurRadius = 69;
            AssertEqual(69, system.Block.GetInt((int)SystemSchema.CurrentBlurRadius));

            EFYVProjectData project = default;
            project.TargetAssetType = "EnemyData";
            project.UnityProjectPath = "C:/unity/project";
            project.CanvasWidth = 71;
            project.CanvasHeight = 72;
            AssertEqual(ReferenceFnv1A("EnemyData"), project.Block.GetInt((int)ProjectSchema.TargetAssetTypeHash));
            AssertEqual(ReferenceFnv1A("C:/unity/project"), project.Block.GetInt((int)ProjectSchema.UnityProjectPathHash));
            AssertEqual(71, project.Block.GetInt((int)ProjectSchema.CanvasWidth));
            AssertEqual(72, project.Block.GetInt((int)ProjectSchema.CanvasHeight));
            AssertEqual("EnemyData", project.TargetAssetType);
            AssertEqual("C:/unity/project", project.UnityProjectPath);

            AnimationStateData animationState = default;
            animationState.StateName = "Walk_Procedural";
            animationState.FPS = 73;
            AssertEqual(ReferenceFnv1A("Walk_Procedural"), animationState.Block.GetInt((int)AnimationStateSchema.StateNameHash));
            AssertEqual(73, animationState.Block.GetInt((int)AnimationStateSchema.FPS));

            LayerData layer = default;
            layer.IsVisible = true;
            layer.Opacity = 0.75f;
            layer.Width = 74;
            layer.Height = 75;
            AssertEqual(BackendConfig.Serialization.TrueValue, layer.Block.GetInt((int)LayerSchema.IsVisible));
            AssertEqual(0.75f, layer.Block.GetFloat((int)LayerSchema.Opacity));
            AssertEqual(74, layer.Block.GetInt((int)LayerSchema.Width));
            AssertEqual(75, layer.Block.GetInt((int)LayerSchema.Height));
            AssertEqual(0, layer.Block.GetInt((int)LayerSchema.NameHash));

            FrameData frame = default;
            frame.FrameIndex = 76;
            AssertEqual(76, frame.Block.GetInt((int)FrameSchema.FrameIndex));

            SubElementData subElement = default;
            subElement.Name = "sub-element";
            subElement.Width = 77;
            subElement.Height = 78;
            AssertEqual(ReferenceFnv1A("sub-element"), subElement.Block.GetInt((int)SubElementSchema.NameHash));
            AssertEqual(77, subElement.Block.GetInt((int)SubElementSchema.Width));
            AssertEqual(78, subElement.Block.GetInt((int)SubElementSchema.Height));

            BrushToolData brush = default;
            brush.ActiveLayerIndex = 81;
            brush.BrushSize = 82;
            brush.TileSize = 83;
            brush.CurrentColorRgba = unchecked((int)0xFFA0B0C0u);
            brush.LastX = 84;
            brush.LastY = 85;
            AssertEqual(81, brush.Block.GetInt((int)BrushToolSchema.ActiveLayerIndex));
            AssertEqual(82, brush.Block.GetInt((int)BrushToolSchema.BrushSize));
            AssertEqual(83, brush.Block.GetInt((int)BrushToolSchema.TileSize));
            AssertEqual(unchecked((int)0xFFA0B0C0u), brush.Block.GetInt((int)BrushToolSchema.CurrentColorRgba));
            AssertEqual(84, brush.Block.GetInt((int)BrushToolSchema.LastX));
            AssertEqual(85, brush.Block.GetInt((int)BrushToolSchema.LastY));

            HitboxToolData hitboxTool = default;
            hitboxTool.ActiveHitboxKey = "Hurtbox";
            AssertEqual(ReferenceFnv1A("Hurtbox"), hitboxTool.Block.GetInt((int)HitboxToolSchema.ActiveHitboxKeyHash));

            MapToolData mapTool = default;
            mapTool.ScatterRadius = 91.5f;
            mapTool.ScatterDensity = 92;
            mapTool.MinScaleJitter = 93.5f;
            mapTool.MaxScaleJitter = 94.5f;
            mapTool.Mode = "Scatter";
            mapTool.FillProbability = 0.25f;
            mapTool.SelectedAsset = "CactusData";
            mapTool.BaseTileId = 95;
            mapTool.TargetTileId = 96;
            AssertEqual(91.5f, mapTool.Block.GetFloat((int)MapToolSchema.ScatterRadius));
            AssertEqual(92, mapTool.Block.GetInt((int)MapToolSchema.ScatterDensity));
            AssertEqual(93.5f, mapTool.Block.GetFloat((int)MapToolSchema.MinScaleJitter));
            AssertEqual(94.5f, mapTool.Block.GetFloat((int)MapToolSchema.MaxScaleJitter));
            AssertEqual(ReferenceFnv1A("Scatter"), mapTool.Block.GetInt((int)MapToolSchema.ModeHash));
            AssertEqual(0.25f, mapTool.Block.GetFloat((int)MapToolSchema.FillProbability));
            AssertEqual(ReferenceFnv1A("CactusData"), mapTool.Block.GetInt((int)MapToolSchema.SelectedAssetHash));
            AssertEqual(95, mapTool.Block.GetInt((int)MapToolSchema.BaseTileId));
            AssertEqual(96, mapTool.Block.GetInt((int)MapToolSchema.TargetTileId));

            MovingToolData moving = default;
            moving.ActiveMode = 101;
            moving.WalkSplitY = 102;
            moving.WalkBounceAmp = 103.5f;
            moving.WalkStrideAmp = 104.5f;
            moving.WalkFrameCount = 105;
            moving.JitterFrameCount = 106;
            moving.SetJitterAmplitude(0, 107.5f);
            moving.SetJitterAmplitude(BackendConfig.Deformation.OctantCount - 1, 108.5f);
            moving.SetJitterFrequency(0, 109.5f);
            moving.SetJitterFrequency(BackendConfig.Deformation.OctantCount - 1, 110.5f);
            AssertEqual(101, moving.Block.GetInt((int)MovingToolSchema.ActiveMode));
            AssertEqual(102, moving.Block.GetInt((int)MovingToolSchema.WalkSplitY));
            AssertEqual(103.5f, moving.Block.GetFloat((int)MovingToolSchema.WalkBounceAmp));
            AssertEqual(104.5f, moving.Block.GetFloat((int)MovingToolSchema.WalkStrideAmp));
            AssertEqual(105, moving.Block.GetInt((int)MovingToolSchema.WalkFrameCount));
            AssertEqual(106, moving.Block.GetInt((int)MovingToolSchema.JitterFrameCount));
            AssertEqual(107.5f, moving.Block.GetFloat((int)MovingToolSchema.JitterAmplitudesStart));
            AssertEqual(108.5f, moving.Block.GetFloat((int)MovingToolSchema.JitterFrequenciesStart - 1));
            AssertEqual(109.5f, moving.Block.GetFloat((int)MovingToolSchema.JitterFrequenciesStart));
            AssertEqual(110.5f, moving.Block.GetFloat(
                (int)MovingToolSchema.JitterFrequenciesStart + BackendConfig.Deformation.OctantCount - 1));

            // Item #10 preset gauges occupy the slots directly after the jitter
            // frequency bank.
            moving.BobAmplitude = 114.5f;
            moving.BreatheAmplitude = 115.5f;
            moving.BobFrameCount = 116;
            moving.ShakeAmplitude = 117.5f;
            moving.FlashStrength = 118.5f;
            moving.ShakeFrameCount = 119;
            AssertEqual(114.5f, moving.Block.GetFloat((int)MovingToolSchema.BobAmplitude));
            AssertEqual(115.5f, moving.Block.GetFloat((int)MovingToolSchema.BreatheAmplitude));
            AssertEqual(116, moving.Block.GetInt((int)MovingToolSchema.BobFrameCount));
            AssertEqual(117.5f, moving.Block.GetFloat((int)MovingToolSchema.ShakeAmplitude));
            AssertEqual(118.5f, moving.Block.GetFloat((int)MovingToolSchema.FlashStrength));
            AssertEqual(119, moving.Block.GetInt((int)MovingToolSchema.ShakeFrameCount));
            AssertEqual(110.5f, moving.GetJitterFrequency(BackendConfig.Deformation.OctantCount - 1));

            PurchasableData purchasable = default;
            purchasable.ItemName = "Floor Chicken";
            purchasable.Cost = 111;
            purchasable.HealAmount = 112.5f;
            purchasable.BuffId = "MoveSpeed";
            purchasable.Duration = 113.5f;
            purchasable.WeaponId = "RandomWeapon";
            AssertEqual(ReferenceFnv1A("Floor Chicken"), purchasable.Block.GetInt((int)PurchasableSchema.ItemNameHash));
            AssertEqual(111, purchasable.Block.GetInt((int)PurchasableSchema.Cost));
            AssertEqual(112.5f, purchasable.Block.GetFloat((int)PurchasableSchema.HealAmount));
            AssertEqual(ReferenceFnv1A("MoveSpeed"), purchasable.Block.GetInt((int)PurchasableSchema.BuffIdHash));
            AssertEqual(113.5f, purchasable.Block.GetFloat((int)PurchasableSchema.Duration));
            AssertEqual(ReferenceFnv1A("RandomWeapon"), purchasable.Block.GetInt((int)PurchasableSchema.WeaponIdHash));

            ViewportData viewport = default;
            viewport.ZoomLevel = 121.5f;
            viewport.OffsetX = 122;
            viewport.OffsetY = 123;
            viewport.CellSize = 124.5f;
            AssertEqual(121.5f, viewport.Block.GetFloat((int)ViewportSchema.ZoomLevel));
            AssertEqual(122, viewport.Block.GetInt((int)ViewportSchema.OffsetX));
            AssertEqual(123, viewport.Block.GetInt((int)ViewportSchema.OffsetY));
            AssertEqual(124.5f, viewport.Block.GetFloat((int)ViewportSchema.CellSize));

            PowerUpData powerUp = default;
            powerUp.PowerUpIdHash = 131;
            powerUp.Level = 132;
            powerUp.Grade = (int)PowerUpGrade.Legendary;
            powerUp.UsesRemaining = 133;
            AssertEqual(131, powerUp.Block.GetInt((int)PowerUpSchema.PowerUpIdHash));
            AssertEqual(132, powerUp.Block.GetInt((int)PowerUpSchema.Level));
            AssertEqual((int)PowerUpGrade.Legendary, powerUp.Block.GetInt((int)PowerUpSchema.Grade));
            AssertEqual(133, powerUp.Block.GetInt((int)PowerUpSchema.UsesRemaining));

            LegacyAchievementDefinitionData achievement = default;
            achievement.Id = 141;
            achievement.TitleHash = 142;
            achievement.DescriptionHash = 143;
            AssertEqual(141, achievement.Block.GetInt((int)LegacyAchievementDefinitionSchema.Id));
            AssertEqual(142, achievement.Block.GetInt((int)LegacyAchievementDefinitionSchema.TitleHash));
            AssertEqual(143, achievement.Block.GetInt((int)LegacyAchievementDefinitionSchema.DescriptionHash));

            HitboxData hitbox = default;
            hitbox.X = 151.5f;
            hitbox.Y = 152.5f;
            hitbox.Width = 153.5f;
            hitbox.Height = 154.5f;
            AssertEqual(151.5f, hitbox.Block.GetFloat((int)HitboxSchema.X));
            AssertEqual(152.5f, hitbox.Block.GetFloat((int)HitboxSchema.Y));
            AssertEqual(153.5f, hitbox.Block.GetFloat((int)HitboxSchema.Width));
            AssertEqual(154.5f, hitbox.Block.GetFloat((int)HitboxSchema.Height));
            AssertEqual(0, hitbox.Block.GetInt((int)HitboxSchema.FrameIndex));
            AssertEqual(0, hitbox.Block.GetInt((int)HitboxSchema.HitboxTypeHash));
            FastSchemaBlock* hitboxBlock = (FastSchemaBlock*)&hitbox;
            AssertEqual(153.5f, hitboxBlock->GetFloat((int)HitboxSchema.Width));
        }

        private static void TestDataModelsBoolStringAndEdgeSemantics()
        {
            // Bool decode semantics: the map/upgrade/entity getters compare == TrueValue while
            // LayerData.IsVisible compares != FalseValue. The asymmetry is current behavior.
            int[] rawBoolValues = { int.MinValue, -1, 0, 1, 2, 100 };
            for (int i = 0; i < rawBoolValues.Length; i++)
            {
                int raw = rawBoolValues[i];
                MapManagerData map = default;
                map.Block.SetInt((int)SystemSchema.IsSwitchingMap, raw);
                AssertEqual(raw == BackendConfig.Serialization.TrueValue, map.IsSwitchingMap);
                UpgradeManagerData upgrade = default;
                upgrade.Block.SetInt((int)SystemSchema.IsSpecialAttackPhase, raw);
                AssertEqual(raw == BackendConfig.Serialization.TrueValue, upgrade.IsSpecialAttackPhase);
                EntityData entity = default;
                entity.Block.SetInt((int)EntitySchema.IsEnraged, raw);
                AssertEqual(raw == BackendConfig.Serialization.TrueValue, entity.IsEnraged);
                LayerData layer = default;
                layer.Block.SetInt((int)LayerSchema.IsVisible, raw);
                AssertEqual(raw != BackendConfig.Serialization.FalseValue, layer.IsVisible);
            }
            // Setters canonicalize any prior raw value to exactly TrueValue/FalseValue.
            LayerData canonical = default;
            canonical.Block.SetInt((int)LayerSchema.IsVisible, 77);
            canonical.IsVisible = true;
            AssertEqual(BackendConfig.Serialization.TrueValue, canonical.Block.GetInt((int)LayerSchema.IsVisible));
            canonical.IsVisible = false;
            AssertEqual(BackendConfig.Serialization.FalseValue, canonical.Block.GetInt((int)LayerSchema.IsVisible));
            MapManagerData canonicalMap = default;
            canonicalMap.Block.SetInt((int)SystemSchema.IsSwitchingMap, -5);
            canonicalMap.IsSwitchingMap = true;
            AssertEqual(BackendConfig.Serialization.TrueValue, canonicalMap.Block.GetInt((int)SystemSchema.IsSwitchingMap));
            canonicalMap.IsSwitchingMap = false;
            AssertEqual(BackendConfig.Serialization.FalseValue, canonicalMap.Block.GetInt((int)SystemSchema.IsSwitchingMap));

            // String-backed properties: the string round-trips by reference, the hash slot
            // holds FNV-1a of the value, and no other slot is disturbed.
            string[] edges =
            {
                null, string.Empty, "a", "A", "toon שלום", "\0",
                new string('k', 2048)
            };
            for (int i = 0; i < edges.Length; i++)
            {
                string value = edges[i];
                PlayerData player = default;
                for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
                    player.Block.SetInt(slot, unchecked((int)0xC0DE0000) + slot);
                player.ActiveToonId = value;
                Assert(ReferenceEquals(value, player.ActiveToonId));
                AssertEqual(ReferenceFnv1A(value), player.Block.GetInt((int)PlayerSchema.ActiveToonIdHash));
                for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
                {
                    if (slot == (int)PlayerSchema.ActiveToonIdHash) continue;
                    AssertEqual(unchecked((int)0xC0DE0000) + slot, player.Block.GetInt(slot));
                }
            }
            // Null and empty hash to the configured null hash on every hashed property.
            EFYVProjectData project = default;
            project.TargetAssetType = null;
            project.UnityProjectPath = string.Empty;
            AssertEqual(BackendConfig.Serialization.NullHash, project.Block.GetInt((int)ProjectSchema.TargetAssetTypeHash));
            AssertEqual(BackendConfig.Serialization.NullHash, project.Block.GetInt((int)ProjectSchema.UnityProjectPathHash));
            AssertEqual(null, project.TargetAssetType);
            AssertEqual(string.Empty, project.UnityProjectPath);

            // Struct copies capture both the string reference and the hash at copy time.
            PlayerData original = default;
            original.ActiveToonId = "first";
            PlayerData copy = original;
            original.ActiveToonId = "second";
            AssertEqual("first", copy.ActiveToonId);
            AssertEqual(ReferenceFnv1A("first"), copy.Block.GetInt((int)PlayerSchema.ActiveToonIdHash));
            AssertEqual("second", original.ActiveToonId);
            AssertEqual(ReferenceFnv1A("second"), original.Block.GetInt((int)PlayerSchema.ActiveToonIdHash));

            // MapToolData tile ids: stored as sign-extended ints, read back by truncation.
            MapToolData tool = default;
            tool.BaseTileId = short.MinValue;
            tool.TargetTileId = short.MaxValue;
            AssertEqual((int)short.MinValue, tool.Block.GetInt((int)MapToolSchema.BaseTileId));
            AssertEqual(short.MinValue, tool.BaseTileId);
            AssertEqual(short.MaxValue, tool.TargetTileId);
            // Raw ints with set high bits are silently truncated by the getter
            // (documents current behavior).
            tool.Block.SetInt((int)MapToolSchema.BaseTileId, 0x00018000);
            AssertEqual(unchecked((short)0x8000), tool.BaseTileId);
            tool.Block.SetInt((int)MapToolSchema.TargetTileId, -1);
            AssertEqual((short)(-1), tool.TargetTileId);

            // FIXED: the MovingToolData jitter accessors validate the octant index
            // themselves. Index 8 used to alias the first frequency slot and index -1
            // used to clobber JitterFrameCount; both now throw and leave the block intact.
            MovingToolData moving = default;
            moving.JitterFrameCount = 9;
            AssertThrows<ArgumentOutOfRangeException>(
                () => moving.SetJitterAmplitude(BackendConfig.Deformation.OctantCount, 42.5f));
            AssertEqual(0f, moving.GetJitterFrequency(0));
            AssertThrows<ArgumentOutOfRangeException>(() => moving.SetJitterAmplitude(-1, 1.5f));
            AssertEqual(9, moving.JitterFrameCount);
            AssertThrows<ArgumentOutOfRangeException>(
                () => moving.GetJitterAmplitude(BackendConfig.Deformation.OctantCount));
            AssertThrows<ArgumentOutOfRangeException>(() => moving.GetJitterFrequency(-1));
            AssertThrows<ArgumentOutOfRangeException>(
                () => moving.SetJitterFrequency(BackendConfig.Deformation.OctantCount, 3f));
            AssertThrows<ArgumentOutOfRangeException>(() => moving.SetJitterFrequency(int.MinValue, 3f));
            // Every in-range octant index still round-trips both slots freely.
            for (int octant = 0; octant < BackendConfig.Deformation.OctantCount; octant++)
            {
                moving.SetJitterAmplitude(octant, octant + 0.25f);
                moving.SetJitterFrequency(octant, octant + 0.75f);
            }
            for (int octant = 0; octant < BackendConfig.Deformation.OctantCount; octant++)
            {
                AssertEqual(octant + 0.25f, moving.GetJitterAmplitude(octant));
                AssertEqual(octant + 0.75f, moving.GetJitterFrequency(octant));
            }
            AssertEqual(9, moving.JitterFrameCount);

            // HitboxData: the explicit parameterless constructor seeds defaults, but
            // default(HitboxData) and array elements bypass it (C# struct semantics).
            // The bypass is now an explicit documented contract on the struct, and
            // CreateDefault() is the non-bypassable spelling of the semantic defaults.
            HitboxData constructed = new HitboxData();
            AssertEqual(BackendConfig.Models.HitboxDefaultPosition, constructed.X);
            AssertEqual(BackendConfig.Models.HitboxDefaultPosition, constructed.Y);
            AssertEqual(BackendConfig.Models.HitboxDefaultSize, constructed.Width);
            AssertEqual(BackendConfig.Models.HitboxDefaultSize, constructed.Height);
            HitboxData created = HitboxData.CreateDefault();
            AssertEqual(BackendConfig.Models.HitboxDefaultPosition, created.X);
            AssertEqual(BackendConfig.Models.HitboxDefaultPosition, created.Y);
            AssertEqual(BackendConfig.Models.HitboxDefaultSize, created.Width);
            AssertEqual(BackendConfig.Models.HitboxDefaultSize, created.Height);
            HitboxData defaulted = default;
            AssertEqual(0f, defaulted.Width);
            AssertEqual(0f, defaulted.Height);
            HitboxData[] array = new HitboxData[2];
            AssertEqual(0f, array[1].Width);
            AssertEqual(0f, array[1].Height);
        }

        private static void TestDataModelsJsonContracts()
        {
            // Serialized field names are the exact wire contract shared with LabyMake and
            // the Unity importer.
            HitboxJson hitbox = new HitboxJson
            {
                frameIndex = -7,
                hitboxType = "Hurt☺box",
                x = -1.5f,
                y = float.MinValue,
                width = float.Epsilon,
                height = 0.1f
            };
            string hitboxJson = JsonSerializer.Serialize(hitbox);
            using (JsonDocument document = JsonDocument.Parse(hitboxJson))
            {
                JsonElement root = document.RootElement;
                List<string> names = new List<string>();
                foreach (JsonProperty property in root.EnumerateObject()) names.Add(property.Name);
                AssertSequenceEqual(
                    new[]
                    {
                        BackendConfig.Exporter.FieldFrameIndex,
                        BackendConfig.Exporter.FieldHitboxType,
                        BackendConfig.Exporter.FieldX,
                        BackendConfig.Exporter.FieldY,
                        BackendConfig.Exporter.FieldWidth,
                        BackendConfig.Exporter.FieldHeight
                    },
                    names.ToArray());
                AssertEqual(-7, root.GetProperty(BackendConfig.Exporter.FieldFrameIndex).GetInt32());
                AssertEqual("Hurt☺box", root.GetProperty(BackendConfig.Exporter.FieldHitboxType).GetString());
                AssertEqual(-1.5f, root.GetProperty(BackendConfig.Exporter.FieldX).GetSingle());
                AssertEqual(float.MinValue, root.GetProperty(BackendConfig.Exporter.FieldY).GetSingle());
                AssertEqual(float.Epsilon, root.GetProperty(BackendConfig.Exporter.FieldWidth).GetSingle());
                AssertEqual(0.1f, root.GetProperty(BackendConfig.Exporter.FieldHeight).GetSingle());
            }

            // Wrong-case names never bind: JsonPropertyName matching is case-sensitive.
            HitboxJson wrongCase = JsonSerializer.Deserialize<HitboxJson>(
                "{\"FrameIndex\":5,\"X\":1.5,\"WIDTH\":3}");
            AssertEqual(0, wrongCase.frameIndex);
            AssertEqual(0f, wrongCase.x);
            AssertEqual(0f, wrongCase.width);

            // Non-finite floats are rejected at serialization time (current behavior).
            HitboxJson notFinite = new HitboxJson { x = float.NaN };
            AssertThrows<ArgumentException>(() => JsonSerializer.Serialize(notFinite));
            notFinite.x = float.PositiveInfinity;
            AssertThrows<ArgumentException>(() => JsonSerializer.Serialize(notFinite));

            // Randomized fixed-seed round trip: ints and finite floats survive bit-exactly.
            Random random = new Random(0x150DA7A);
            for (int iteration = 0; iteration < 400; iteration++)
            {
                HitboxJson source = new HitboxJson
                {
                    frameIndex = unchecked((int)NextUInt(random)),
                    hitboxType = iteration % 5 == 0 ? null : "t" + random.Next(1000) + "א",
                    x = DataModelsNextFiniteFloat(random),
                    y = DataModelsNextFiniteFloat(random),
                    width = DataModelsNextFiniteFloat(random),
                    height = DataModelsNextFiniteFloat(random)
                };
                HitboxJson round = JsonSerializer.Deserialize<HitboxJson>(JsonSerializer.Serialize(source));
                AssertEqual(source.frameIndex, round.frameIndex);
                AssertEqual(source.hitboxType, round.hitboxType);
                AssertEqual(BitConverter.SingleToInt32Bits(source.x), BitConverter.SingleToInt32Bits(round.x));
                AssertEqual(BitConverter.SingleToInt32Bits(source.y), BitConverter.SingleToInt32Bits(round.y));
                AssertEqual(BitConverter.SingleToInt32Bits(source.width), BitConverter.SingleToInt32Bits(round.width));
                AssertEqual(BitConverter.SingleToInt32Bits(source.height), BitConverter.SingleToInt32Bits(round.height));
            }

            // Full document: round trip, exact member names, and re-serialization stability.
            // The model layer is intentionally permissive about values (FastExporter validates).
            EFYVJsonFormat format = new EFYVJsonFormat
            {
                documentVersion = 1,
                assetType = "EnemyData",
                baseAssetType = "EnemyData",
                properties = new Dictionary<string, JsonElement>
                {
                    ["entityName"] = JsonDocument.Parse("\"Sphinx \\u05d7\\u05ea\\u05d5\\u05dc\"").RootElement.Clone(),
                    ["maxHealth"] = JsonDocument.Parse("12.5").RootElement.Clone(),
                    ["nested"] = JsonDocument.Parse("{\"deep\":[1,2,{\"x\":null}]}").RootElement.Clone()
                },
                hitboxes = new List<HitboxJson>
                {
                    new HitboxJson { frameIndex = 0, hitboxType = "Hurtbox", x = 1f, y = 2f, width = 3f, height = 4f },
                    new HitboxJson { frameIndex = int.MaxValue, hitboxType = null, x = -0.5f, y = 0f, width = 0f, height = 9.75f }
                },
                atlas = new AtlasMetadataJson
                {
                    formatVersion = -3,
                    frameWidth = int.MaxValue,
                    frameHeight = 0,
                    atlasWidth = -1,
                    atlasHeight = 2,
                    animations = new List<AnimationMetadataJson>
                    {
                        new AnimationMetadataJson { name = "", fps = int.MinValue, startFrame = -5, frameCount = 0 }
                    }
                }
            };
            string serialized = JsonSerializer.Serialize(format);
            EFYVJsonFormat round2 = JsonSerializer.Deserialize<EFYVJsonFormat>(serialized);
            AssertEqual("EnemyData", round2.assetType);
            AssertEqual("EnemyData", round2.baseAssetType);
            Assert(round2.documentVersion.HasValue);
            AssertEqual(1, round2.EffectiveDocumentVersion);
            AssertEqual(3, round2.properties.Count);
            AssertEqual("Sphinx חתול", round2.properties["entityName"].GetString());
            AssertEqual(12.5f, round2.properties["maxHealth"].GetSingle());
            AssertEqual("{\"deep\":[1,2,{\"x\":null}]}", round2.properties["nested"].GetRawText());
            AssertEqual(2, round2.hitboxes.Count);
            AssertEqual("Hurtbox", round2.hitboxes[0].hitboxType);
            AssertEqual(4f, round2.hitboxes[0].height);
            AssertEqual(int.MaxValue, round2.hitboxes[1].frameIndex);
            AssertEqual(null, round2.hitboxes[1].hitboxType);
            AssertEqual(BitConverter.SingleToInt32Bits(-0.5f), BitConverter.SingleToInt32Bits(round2.hitboxes[1].x));
            Assert(round2.atlas.HasValue);
            AssertEqual(-3, round2.atlas.Value.formatVersion);
            AssertEqual(int.MaxValue, round2.atlas.Value.frameWidth);
            AssertEqual(0, round2.atlas.Value.frameHeight);
            AssertEqual(-1, round2.atlas.Value.atlasWidth);
            AssertEqual(2, round2.atlas.Value.atlasHeight);
            AssertEqual(1, round2.atlas.Value.animations.Count);
            AssertEqual("", round2.atlas.Value.animations[0].name);
            AssertEqual(int.MinValue, round2.atlas.Value.animations[0].fps);
            AssertEqual(-5, round2.atlas.Value.animations[0].startFrame);
            AssertEqual(0, round2.atlas.Value.animations[0].frameCount);
            AssertEqual(serialized, JsonSerializer.Serialize(round2));

            using (JsonDocument document = JsonDocument.Parse(serialized))
            {
                List<string> topNames = new List<string>();
                foreach (JsonProperty property in document.RootElement.EnumerateObject()) topNames.Add(property.Name);
                AssertSequenceEqual(
                    new[]
                    {
                        BackendConfig.Exporter.FieldDocumentVersion,
                        BackendConfig.Exporter.FieldAssetType,
                        BackendConfig.Exporter.FieldBaseAssetType,
                        BackendConfig.Exporter.FieldProperties,
                        BackendConfig.Exporter.FieldHitboxes,
                        BackendConfig.Exporter.FieldAtlas,
                        // Item #6 optional attachment records (reflection
                        // serialization emits the member as an explicit null;
                        // the exporter's hand-written path omits it when
                        // absent/empty).
                        BackendConfig.Exporter.FieldAttachments,
                        // Item #5 optional tileset manifest block (same rule).
                        BackendConfig.Exporter.FieldTileset
                    },
                    topNames.ToArray());
                List<string> atlasNames = new List<string>();
                foreach (JsonProperty property in document.RootElement.GetProperty(BackendConfig.Exporter.FieldAtlas).EnumerateObject())
                    atlasNames.Add(property.Name);
                AssertSequenceEqual(
                    new[]
                    {
                        BackendConfig.Exporter.FieldFormatVersion,
                        BackendConfig.Exporter.FieldFrameWidth,
                        BackendConfig.Exporter.FieldFrameHeight,
                        BackendConfig.Exporter.FieldAtlasWidth,
                        BackendConfig.Exporter.FieldAtlasHeight,
                        BackendConfig.Exporter.FieldAnimations
                    },
                    atlasNames.ToArray());
                List<string> animationNames = new List<string>();
                JsonElement animation = document.RootElement
                    .GetProperty(BackendConfig.Exporter.FieldAtlas)
                    .GetProperty(BackendConfig.Exporter.FieldAnimations)[0];
                foreach (JsonProperty property in animation.EnumerateObject()) animationNames.Add(property.Name);
                AssertSequenceEqual(
                    new[]
                    {
                        BackendConfig.Exporter.FieldName,
                        BackendConfig.Exporter.FieldFps,
                        BackendConfig.Exporter.FieldStartFrame,
                        BackendConfig.Exporter.FieldFrameCount,
                        // Item #10 optional timing/playback members (reflection
                        // serialization emits them as explicit nulls; the
                        // exporter's hand-written path omits them when default).
                        BackendConfig.Exporter.FieldFrameDurationsMs,
                        BackendConfig.Exporter.FieldLoopStart,
                        BackendConfig.Exporter.FieldLoopEnd,
                        BackendConfig.Exporter.FieldPingPong,
                        // Item #7 optional effect descriptors (same rule).
                        BackendConfig.Exporter.FieldEffects
                    },
                    animationNames.ToArray());
            }

            // The default struct keeps every member as an explicit JSON null; a
            // version-absent (legacy) document reads as LegacyDocumentVersion.
            AssertEqual(
                "{\"documentVersion\":null,\"assetType\":null,\"baseAssetType\":null,\"properties\":null,\"hitboxes\":null,\"atlas\":null,\"attachments\":null,\"tileset\":null}",
                JsonSerializer.Serialize(default(EFYVJsonFormat)));
            EFYVJsonFormat legacyDocument = JsonSerializer.Deserialize<EFYVJsonFormat>(
                "{\"assetType\":\"Legacy\",\"properties\":{},\"hitboxes\":[]}");
            Assert(!legacyDocument.documentVersion.HasValue);
            AssertEqual(BackendConfig.Exporter.LegacyDocumentVersion, legacyDocument.EffectiveDocumentVersion);
            AssertEqual(null, legacyDocument.baseAssetType);
            EFYVJsonFormat versionedDocument = JsonSerializer.Deserialize<EFYVJsonFormat>(
                "{\"documentVersion\":7,\"assetType\":\"Future\",\"baseAssetType\":\"EnemyData\"}");
            AssertEqual(7, versionedDocument.EffectiveDocumentVersion);
            AssertEqual("EnemyData", versionedDocument.baseAssetType);
        }

        private static void TestDataModelsConfigInvariants()
        {
            // Math constants agree with their double-precision definitions.
            AssertEqual((float)System.Math.PI, BackendConfig.Math.PI);
            AssertEqual((float)(System.Math.PI * 2d), BackendConfig.Math.TwoPI);
            AssertEqual((float)(System.Math.PI / 2d), BackendConfig.Math.PI_HALF);
            AssertEqual((float)(System.Math.PI / 180d), BackendConfig.Math.Deg2Rad);
            AssertEqual((float)(4d / System.Math.PI), BackendConfig.Math.TaylorSinA);
            AssertEqual((float)(4d / (System.Math.PI * System.Math.PI)), BackendConfig.Math.TaylorSinB);
            AssertEqual((float)(1d / 4294967296d), BackendConfig.Random.UIntToUnitFloat);

            // Xorshift parameters: the (13, 17, 5) full-period triple the reference model uses.
            AssertEqual(13, BackendConfig.Random.XorShiftLeftA);
            AssertEqual(17, BackendConfig.Random.XorShiftRight);
            AssertEqual(5, BackendConfig.Random.XorShiftLeftB);
            Assert(BackendConfig.Random.DefaultSeed != BackendConfig.Random.InvalidSeed);
            Assert(BackendConfig.Random.FallbackSeed != BackendConfig.Random.InvalidSeed);

            // Schema enums: member values distinct, non-negative, terminal member strictly
            // above all others, and everything fits inside the 64-slot block.
            Type[] schemaEnums =
            {
                typeof(StatSchema), typeof(ToonSchema), typeof(AssetSchema), typeof(PowerUpSchema),
                typeof(WeaponSchema), typeof(MovingToolSchema), typeof(BrushToolSchema), typeof(HitboxSchema),
                typeof(LayerSchema), typeof(EntitySchema), typeof(PlayerSchema), typeof(ProjectileSchema),
                typeof(SpawnManagerSchema), typeof(MapToolSchema), typeof(PurchasableSchema), typeof(PropSchema),
                typeof(HitboxToolSchema), typeof(ViewportSchema), typeof(SystemSchema), typeof(ProjectSchema),
                typeof(AnimationStateSchema), typeof(FrameSchema), typeof(SubElementSchema),
                typeof(WeaponControllerSchema), typeof(DoorPropSchema), typeof(LegacyAchievementDefinitionSchema)
            };
            for (int i = 0; i < schemaEnums.Length; i++)
            {
                Type enumType = schemaEnums[i];
                HashSet<int> seen = new HashSet<int>();
                int terminal = -1;
                int maxOther = -1;
                foreach (string name in Enum.GetNames(enumType))
                {
                    int value = (int)Enum.Parse(enumType, name);
                    Assert(value >= 0);
                    Assert(seen.Add(value));
                    if (name == "SIZE" || name == "MAX_STATS") terminal = value;
                    else if (value > maxOther) maxOther = value;
                }
                if (terminal >= 0)
                {
                    Assert(terminal > maxOther);
                    Assert(terminal <= FastSchemaBlock.MaxSize);
                }
            }
            AssertEqual(20, (int)ToonSchema.StatsStart + (int)StatSchema.MAX_STATS);
            Assert((int)ToonSchema.StatsStart + (int)StatSchema.MAX_STATS <= FastSchemaBlock.MaxSize);
            AssertEqual(
                (int)MovingToolSchema.JitterFrequenciesStart,
                (int)MovingToolSchema.JitterAmplitudesStart + BackendConfig.Deformation.OctantCount);
            // Item #10: the preset gauges start right after the jitter
            // frequency bank; SIZE is the terminal gauge slot + 1.
            AssertEqual(
                (int)MovingToolSchema.BobAmplitude,
                (int)MovingToolSchema.JitterFrequenciesStart + BackendConfig.Deformation.OctantCount);
            AssertEqual((int)MovingToolSchema.SIZE, (int)MovingToolSchema.ShakeFrameCount + 1);

            // Packed octant lookup decodes to a permutation of the eight octants.
            AssertEqual((1 << BackendConfig.Deformation.BitsPerOctant) - 1, BackendConfig.Deformation.OctantMask);
            Assert(BackendConfig.Deformation.OctantCount * BackendConfig.Deformation.BitsPerOctant <= 32);
            HashSet<int> octantEntries = new HashSet<int>();
            for (int i = 0; i < BackendConfig.Deformation.OctantCount; i++)
            {
                int entry = (BackendConfig.Deformation.PackedOctantLookup >> (i * BackendConfig.Deformation.BitsPerOctant))
                    & BackendConfig.Deformation.OctantMask;
                Assert(octantEntries.Add(entry));
            }
            AssertEqual(BackendConfig.Deformation.OctantCount, octantEntries.Count);

            // PNG constants match the PNG/zlib specifications.
            AssertSequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
                BackendConfig.Exporter.Png.Signature);
            AssertEqual("IHDR", Encoding.ASCII.GetString(BackendConfig.Exporter.Png.IhdrChunkType));
            AssertEqual("IDAT", Encoding.ASCII.GetString(BackendConfig.Exporter.Png.IdatChunkType));
            AssertEqual("IEND", Encoding.ASCII.GetString(BackendConfig.Exporter.Png.IendChunkType));
            AssertEqual(BackendConfig.Exporter.Png.ChunkTypeLength, BackendConfig.Exporter.Png.IhdrChunkType.Length);
            AssertEqual(65521u, BackendConfig.Exporter.Png.AdlerModulus);
            for (int divisor = 2; divisor * divisor <= 65521; divisor++)
                Assert(65521 % divisor != 0);
            AssertEqual(0xEDB88320u, BackendConfig.Exporter.Png.CrcPolynomial);
            AssertEqual((byte)8, BackendConfig.Exporter.Png.RgbaBitDepth);
            AssertEqual((byte)6, BackendConfig.Exporter.Png.RgbaColorType);
            AssertEqual(ushort.MaxValue, BackendConfig.Exporter.Png.StoredBlockMaxLength);
            AssertEqual(5552, BackendConfig.Exporter.Png.AdlerModuloBlockLength);

            // Cumulative probability tables stay ordered inside (0, 1).
            Assert(0f < GameConfig.Map.SarcophageTeleportChance);
            Assert(GameConfig.Map.SarcophageTeleportChance < GameConfig.Map.SarcophageSpawnEnemyChance);
            Assert(GameConfig.Map.SarcophageSpawnEnemyChance < GameConfig.Map.SarcophageTrapChance);
            Assert(GameConfig.Map.SarcophageTrapChance < 1f);
            Assert(0f < GameConfig.Merchant.RollThresholdChicken);
            Assert(GameConfig.Merchant.RollThresholdChicken < GameConfig.Merchant.RollThresholdPotion);
            Assert(GameConfig.Merchant.RollThresholdPotion < 1f);
            Assert(GameConfig.Drops.BaseChestChance >= 0f && GameConfig.Drops.BaseChestChance <= 1f);
            Assert(GameConfig.Drops.BaseCoinChance >= 0f && GameConfig.Drops.BaseCoinChance <= 1f);
            AssertEqual(1f, GameConfig.Drops.GuaranteedDropChance);

            // Drop grade ranges stay ordered and within the global maxima.
            Assert(GameConfig.Drops.MinCoinGrade <= GameConfig.Drops.EnemyMaxCoinGrade);
            Assert(GameConfig.Drops.EnemyMaxCoinGrade <= GameConfig.Drops.MaxCoinGrade);
            Assert(GameConfig.Drops.MiniBossMinCoinGrade <= GameConfig.Drops.MiniBossMaxCoinGrade);
            Assert(GameConfig.Drops.MiniBossMaxCoinGrade <= GameConfig.Drops.MaxCoinGrade);
            Assert(GameConfig.Drops.BossCoinGrade <= GameConfig.Drops.MaxCoinGrade);
            Assert(GameConfig.Drops.MinChestGrade <= GameConfig.Drops.EnemyChestGrade);
            Assert(GameConfig.Drops.MiniBossMinChestGrade <= GameConfig.Drops.MiniBossMaxChestGrade);
            Assert(GameConfig.Drops.MiniBossMaxChestGrade <= GameConfig.Drops.MaxChestGrade);
            Assert(GameConfig.Drops.BossMinChestGrade <= GameConfig.Drops.BossMaxChestGrade);
            Assert(GameConfig.Drops.BossMaxChestGrade <= GameConfig.Drops.MaxChestGrade);
            Assert(GameConfig.Drops.BossMinChestCount < GameConfig.Drops.BossMaxChestCountExclusive);

            // Achievement basis arrays are parallel, non-blank, and hash-distinct: the
            // LegacyAchievementDefinitionData model stores FNV hashes of titles/descriptions.
            string[] titles = GameConfig.Achievements.BasisData.Titles;
            string[] descriptions = GameConfig.Achievements.BasisData.Descriptions;
            AssertEqual(titles.Length, descriptions.Length);
            AssertEqual(30, titles.Length);
            Assert(titles.Length <= GameConfig.Achievements.MaxAchievements);
            AssertEqual(0, GameConfig.Achievements.MaxAchievements % GameConfig.Achievements.BitsPerInt);
            Assert(GameConfig.Achievements.MaxAchievements / GameConfig.Achievements.BitsPerInt <= FastSchemaBlock.MaxSize);
            HashSet<int> titleHashes = new HashSet<int>();
            HashSet<int> descriptionHashes = new HashSet<int>();
            for (int i = 0; i < titles.Length; i++)
            {
                Assert(!string.IsNullOrWhiteSpace(titles[i]));
                Assert(!string.IsNullOrWhiteSpace(descriptions[i]));
                Assert(titleHashes.Add(FastMath.FastHash(titles[i])));
                Assert(descriptionHashes.Add(FastMath.FastHash(descriptions[i])));
            }

            // Asset definitions: distinct identities, hierarchical field flags, and
            // hash-distinct type ids (models dispatch on FastHash of the type name).
            LabyMakeConfig.Schema.AssetDefinition[] definitions = LabyMakeConfig.Schema.AssetDefinitions;
            AssertEqual(4, definitions.Length);
            HashSet<string> definitionTypes = new HashSet<string>();
            HashSet<string> definitionDisplays = new HashSet<string>();
            HashSet<int> typeHashes = new HashSet<int>();
            for (int i = 0; i < definitions.Length; i++)
            {
                LabyMakeConfig.Schema.AssetDefinition definition = definitions[i];
                Assert(!string.IsNullOrWhiteSpace(definition.AssetType));
                Assert(!string.IsNullOrWhiteSpace(definition.DisplayName));
                Assert(definitionTypes.Add(definition.AssetType));
                Assert(definitionDisplays.Add(definition.DisplayName));
                Assert(typeHashes.Add(FastMath.FastHash(definition.AssetType)));
                if (definition.IncludesBossFields) Assert(definition.IncludesEnemyFields);
                if (definition.IncludesEnemyFields) Assert(definition.UsesLivingEntityFields);
                if (definition.UsesLivingEntityFields) Assert(definition.IsDirectional);
            }
            LabyMakeConfig.Schema.AssetRegistration[] registrations = LabyMakeConfig.Schema.BuiltInAssetRegistrations;
            AssertEqual(17, registrations.Length);
            HashSet<string> registrationTypes = new HashSet<string>();
            HashSet<string> registrationDisplays = new HashSet<string>();
            for (int i = 0; i < registrations.Length; i++)
            {
                LabyMakeConfig.Schema.AssetRegistration registration = registrations[i];
                Assert(!string.IsNullOrWhiteSpace(registration.AssetType));
                Assert(!string.IsNullOrWhiteSpace(registration.DisplayName));
                Assert(definitionTypes.Contains(registration.BaseAssetType));
                Assert(registrationTypes.Add(registration.AssetType));
                Assert(!definitionTypes.Contains(registration.AssetType));
                Assert(registrationDisplays.Add(registration.DisplayName));
                Assert(typeHashes.Add(FastMath.FastHash(registration.AssetType)));
            }

            // Field definition tables: unique names, valid editor metadata, defaults inside
            // declared ranges, and declared counts matching the arrays.
            AssertEqual(LabyMakeConfig.Schema.AdditionalEnemyFieldCount, LabyMakeConfig.Schema.EnemyFields.Length);
            AssertEqual(LabyMakeConfig.Schema.AdditionalBossFieldCount, LabyMakeConfig.Schema.BossFields.Length);
            List<LabyMakeConfig.Schema.FieldDefinition> allFields = new List<LabyMakeConfig.Schema.FieldDefinition>();
            allFields.AddRange(LabyMakeConfig.Schema.LivingEntityFields);
            allFields.AddRange(LabyMakeConfig.Schema.EnemyFields);
            allFields.AddRange(LabyMakeConfig.Schema.BossFields);
            allFields.AddRange(LabyMakeConfig.Schema.GameAssetFields);
            HashSet<string> fieldNames = new HashSet<string>();
            for (int i = 0; i < allFields.Count; i++)
            {
                LabyMakeConfig.Schema.FieldDefinition field = allFields[i];
                Assert(!string.IsNullOrWhiteSpace(field.Name));
                Assert(!string.IsNullOrWhiteSpace(field.FieldType));
                Assert(fieldNames.Add(field.Name));
                Assert(!string.IsNullOrWhiteSpace(field.Editor.Label));
                if (field.Editor.HasRange)
                {
                    Assert(field.Editor.Minimum <= field.Editor.Maximum);
                    Assert(field.Editor.Step > 0d);
                    double defaultValue = Convert.ToDouble(field.Editor.DefaultValue);
                    Assert(defaultValue >= field.Editor.Minimum && defaultValue <= field.Editor.Maximum);
                }
                if (field.Editor.Choices != null)
                {
                    Assert(field.Editor.Choices.Length > 0);
                    Assert(Array.IndexOf(field.Editor.Choices, (string)field.Editor.DefaultValue) >= 0);
                }
            }

            // Shared schema-field manifest (#15): unique names, unique valid slots
            // inside the AssetSchema window, and every numeric designer field on
            // the LabyMake side is mapped (single-sourced wire contract, so a new
            // designer number can never silently vanish on import again).
            SharedConfig.SchemaFieldMapping[] manifest = SharedConfig.AssetSchemaFieldManifest;
            Assert(manifest.Length >= 9);
            HashSet<string> manifestNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> manifestSlots = new HashSet<int>();
            foreach (SharedConfig.SchemaFieldMapping mapping in manifest)
            {
                Assert(!string.IsNullOrWhiteSpace(mapping.FieldName));
                Assert(manifestNames.Add(mapping.FieldName));
                Assert(manifestSlots.Add(mapping.Slot));
                Assert(mapping.Slot > (int)AssetSchema.AssetIdHash);
                Assert(mapping.Slot < (int)AssetSchema.SIZE);
                Assert(mapping.Kind == SharedConfig.SchemaFieldKind.Float ||
                    mapping.Kind == SharedConfig.SchemaFieldKind.Boolean);
            }
            AssertEqual((int)AssetSchema.MaxHealth,
                DataModelsManifestSlot(manifest, SharedConfig.MaxHealthField));
            AssertEqual((int)AssetSchema.BaseSpeed,
                DataModelsManifestSlot(manifest, SharedConfig.BaseSpeedField));
            AssertEqual((int)AssetSchema.DamageToPlayer,
                DataModelsManifestSlot(manifest, SharedConfig.DamageToPlayerField));
            AssertEqual((int)AssetSchema.ExperienceValue,
                DataModelsManifestSlot(manifest, SharedConfig.ExperienceValueField));
            AssertEqual((int)AssetSchema.Phase2HealthThreshold,
                DataModelsManifestSlot(manifest, SharedConfig.Phase2HealthThresholdField));
            AssertEqual((int)AssetSchema.BaseDamage,
                DataModelsManifestSlot(manifest, SharedConfig.BaseDamageField));
            AssertEqual((int)AssetSchema.CooldownTimer,
                DataModelsManifestSlot(manifest, SharedConfig.CooldownTimerField));
            AssertEqual((int)AssetSchema.IsWalkable,
                DataModelsManifestSlot(manifest, SharedConfig.IsWalkableField));
            AssertEqual((int)AssetSchema.TrapDamage,
                DataModelsManifestSlot(manifest, SharedConfig.TrapDamageField));
            foreach (LabyMakeConfig.Schema.FieldDefinition field in allFields)
            {
                bool isNumeric = field.FieldType == LabyMakeConfig.Types.FloatSingle ||
                    field.FieldType == LabyMakeConfig.Types.FloatLower ||
                    field.FieldType == LabyMakeConfig.Types.Int32 ||
                    field.FieldType == LabyMakeConfig.Types.IntLower;
                // Every numeric designer field must occupy a manifest slot; text
                // fields must be known identity/routing keys.
                if (isNumeric)
                    Assert(manifestNames.Contains(field.Name));
                else
                    Assert(Array.IndexOf(SharedConfig.NonSchemaPropertyFields, field.Name) >= 0 ||
                        manifestNames.Contains(field.Name));
            }

            // Save envelope constants (#19) stay self-consistent.
            AssertEqual(12, BackendConfig.Save.HeaderSizeBytes);
            AssertEqual(0, BackendConfig.Save.MagicOffset);
            AssertEqual(4, BackendConfig.Save.VersionOffset);
            AssertEqual(8, BackendConfig.Save.ChecksumOffset);
            AssertSequenceEqual(
                new byte[] { (byte)'E', (byte)'F', (byte)'Y', (byte)'V' },
                BitConverter.GetBytes(BackendConfig.Save.MagicNumber));
            AssertEqual(
                PlayerMetaSchema.ExpectedSizeBytes,
                sizeof(int) + (2 + PlayerMetaSchema.MaxToons) * BackendConfig.Schema.BlockSizeBytes);
            // Document-version range semantics (item #10): the supported floor
            // never exceeds the current version and legacy files sit inside
            // the range. Version 5 is current: 2 added the optional atlas
            // timing fields (item #10), 3 the optional effect descriptors
            // (item #7), 4 the optional attachment records (item #6), 5 the
            // optional tileset manifest block (item #5).
            AssertEqual(5, BackendConfig.Exporter.CurrentDocumentVersion);
            AssertEqual(1, BackendConfig.Exporter.MinSupportedDocumentVersion);
            Assert(BackendConfig.Exporter.MinSupportedDocumentVersion <=
                BackendConfig.Exporter.CurrentDocumentVersion);
            Assert(BackendConfig.Exporter.LegacyDocumentVersion >=
                BackendConfig.Exporter.MinSupportedDocumentVersion);
            Assert(BackendConfig.Exporter.LegacyDocumentVersion <=
                BackendConfig.Exporter.CurrentDocumentVersion);
            // Wire cap for per-frame durations: sentinel below the minimum real
            // value, cap positive.
            AssertEqual(0, BackendConfig.Exporter.InheritFrameDurationMs);
            Assert(BackendConfig.Exporter.MaxFrameDurationMs > 0);
            AssertEqual(
                BackendConfig.Exporter.MaxFrameDurationMs,
                LabyMakeConfig.Animation.MaxFrameDurationMs);
            AssertEqual(
                BackendConfig.Exporter.InheritFrameDurationMs,
                LabyMakeConfig.Animation.InheritFrameDurationMs);

            // Publish retry (#12): bounded attempts with 20-50ms backoff.
            AssertEqual(3, BackendConfig.IO.PublishRetryAttempts);
            Assert(BackendConfig.IO.PublishRetryFirstDelayMilliseconds >= 20);
            Assert(BackendConfig.IO.PublishRetryMaxDelayMilliseconds <= 50);
            Assert(BackendConfig.IO.PublishRetryFirstDelayMilliseconds <=
                BackendConfig.IO.PublishRetryMaxDelayMilliseconds);

            // Facing vocabulary: four distinct directions, file suffixes derived from them.
            AssertEqual(4, LabyMakeConfig.Schema.FacingChoices.Length);
            AssertEqual(LabyMakeConfig.Entity.DirectionalVariantCount, LabyMakeConfig.Schema.FacingChoices.Length);
            HashSet<string> facings = new HashSet<string>(LabyMakeConfig.Schema.FacingChoices);
            AssertEqual(4, facings.Count);
            AssertEqual("_" + SharedConfig.FacingUp, SharedConfig.FacingFileSuffixUp);
            AssertEqual("_" + SharedConfig.FacingDown, SharedConfig.FacingFileSuffixDown);
            AssertEqual("_" + SharedConfig.FacingLeft, SharedConfig.FacingFileSuffixLeft);
            AssertEqual("_" + SharedConfig.FacingRight, SharedConfig.FacingFileSuffixRight);

            // Map tool modes are hash-dispatched via MapToolData.Mode: distinct hashes.
            string[] modes =
            {
                LabyMakeConfig.Tool.Map.ModeScatter,
                LabyMakeConfig.Tool.Map.ModeTile,
                LabyMakeConfig.Tool.Map.ModeNoiseFill,
                LabyMakeConfig.Tool.Map.ModeAutomataSmooth
            };
            HashSet<string> modeSet = new HashSet<string>(modes);
            AssertEqual(modes.Length, modeSet.Count);
            HashSet<int> modeHashes = new HashSet<int>();
            for (int i = 0; i < modes.Length; i++) Assert(modeHashes.Add(FastMath.FastHash(modes[i])));

            // Size and range limits are mutually consistent across the config.
            AssertEqual(
                LabyMakeConfig.Persistence.MaxCanvasDimension * LabyMakeConfig.Persistence.MaxCanvasDimension,
                LabyMakeConfig.Persistence.MaxSubElementPixelCount);
            AssertEqual(LabyMakeConfig.Persistence.MaxCanvasDimension, LabyMakeConfig.Persistence.MaxSubElementDimension);
            Assert((long)LabyMakeConfig.Export.MaxAtlasPixelCount
                <= (long)LabyMakeConfig.Export.MaxAtlasDimension * LabyMakeConfig.Export.MaxAtlasDimension);
            Assert(LabyMakeConfig.Persistence.MaxCanvasDimension <= LabyMakeConfig.Export.MaxAtlasDimension);
            Assert(LabyMakeConfig.Canvas.DefaultWidth <= LabyMakeConfig.Persistence.MaxCanvasDimension);
            Assert(LabyMakeConfig.Canvas.DefaultHeight <= LabyMakeConfig.Persistence.MaxCanvasDimension);
            Assert(LabyMakeConfig.Viewport.MinZoom < LabyMakeConfig.Viewport.MaxZoom);
            Assert(LabyMakeConfig.Viewport.DefaultZoomLevel >= LabyMakeConfig.Viewport.MinZoom);
            Assert(LabyMakeConfig.Viewport.DefaultZoomLevel <= LabyMakeConfig.Viewport.MaxZoom);
            Assert(LabyMakeConfig.Viewport.ZoomStep > 0f);
            Assert(LabyMakeConfig.Tool.Pencil.MinThickBrushSize >= 1);
            Assert(LabyMakeConfig.Tool.Pencil.DefaultBrushSize >= LabyMakeConfig.Tool.Pencil.MinThickBrushSize);
            Assert(LabyMakeConfig.Tool.Pencil.DefaultBrushSize <= LabyMakeConfig.Tool.MaxBrushSize);
            Assert(LabyMakeConfig.Tool.TileMaker.MinTileSize <= LabyMakeConfig.Tool.TileMaker.DefaultTileSize);
            Assert(LabyMakeConfig.Tool.TileMaker.DefaultTileSize <= LabyMakeConfig.Tool.TileMaker.MaxTileSize);
            Assert(LabyMakeConfig.Tool.Map.MinScaleJitter <= LabyMakeConfig.Tool.Map.MaxScaleJitter);
            Assert(LabyMakeConfig.Tool.Map.DefaultFillProbability >= 0f);
            Assert(LabyMakeConfig.Tool.Map.DefaultFillProbability <= 1f);
            Assert(LabyMakeConfig.Tool.Map.DefaultScatterDensity <= LabyMakeConfig.Tool.Map.MaxScatterDensity);
            Assert(GameConfig.Weapons.Inventory.InitialLevel <= GameConfig.Weapons.Inventory.MaxLevel);
            Assert(GameConfig.Weapons.Inventory.MonsterMaxWeapons <= GameConfig.Weapons.Inventory.MiniBossMaxWeapons);
            Assert(GameConfig.Weapons.Inventory.MiniBossMaxWeapons <= GameConfig.Weapons.Inventory.BossMaxWeapons);
        }

        private static int DataModelsManifestSlot(
            SharedConfig.SchemaFieldMapping[] manifest,
            string fieldName)
        {
            foreach (SharedConfig.SchemaFieldMapping mapping in manifest)
            {
                if (mapping.FieldName == fieldName) return mapping.Slot;
            }
            throw new InvalidOperationException("Missing manifest field: " + fieldName);
        }

        private static unsafe byte[] DataModelsStructBytes(void* pointer, int byteCount)
        {
            return new ReadOnlySpan<byte>(pointer, byteCount).ToArray();
        }

        private static void DataModelsAssertOnlyInt(byte[] bytes, int offset, int expected)
        {
            AssertEqual(expected, BitConverter.ToInt32(bytes, offset));
            int strayBytes = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i >= offset && i < offset + sizeof(int)) continue;
                if (bytes[i] != 0) strayBytes++;
            }
            AssertEqual(0, strayBytes);
        }

        private static float DataModelsNextFiniteFloat(Random random)
        {
            while (true)
            {
                float candidate = BitConverter.Int32BitsToSingle(unchecked((int)NextUInt(random)));
                if (!float.IsNaN(candidate) && !float.IsInfinity(candidate)) return candidate;
            }
        }
    }
}
