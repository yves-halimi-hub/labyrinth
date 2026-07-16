using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using EFYVBackend.Core.Collections;
using EFYVBackend.Core.Data;
using EFYVBackend.Core.Math;
using EFYVBackend.Core.Models;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Verification
{
    internal static partial class Program
    {
        private const ulong BeforeCanary = 0x0123456789ABCDEFul;
        private const ulong AfterCanary = 0xFEDCBA9876543210ul;

        private static unsafe void TestSchemaMemoryAndModels()
        {
            AssertEqual(BackendConfig.Schema.BlockSizeBytes, sizeof(FastSchemaBlock));
            AssertEqual(BackendConfig.Schema.BlockSlotCount, FastSchemaBlock.MaxSize);

            GuardedSchema guarded = new GuardedSchema
            {
                Before = BeforeCanary,
                After = AfterCanary
            };
            for (int i = 0; i < FastSchemaBlock.MaxSize; i++)
            {
                int value = unchecked((int)0xA5000000) + i;
                guarded.Block.SetInt(i, value);
                AssertEqual(value, guarded.Block.GetInt(i));
            }
            AssertEqual(BeforeCanary, guarded.Before);
            AssertEqual(AfterCanary, guarded.After);

            guarded.Block.SetFloat(0, -123.75f);
            AssertEqual(BitConverter.SingleToInt32Bits(-123.75f), guarded.Block.GetInt(0));
            guarded.Block.ApplyAdditiveFloat(0, 23.5f);
            AssertNear(-100.25f, guarded.Block.GetFloat(0), 0f);
            guarded.Block.ApplyMultiplicativeFloat(0, -2f);
            AssertNear(200.5f, guarded.Block.GetFloat(0), 0f);
            AssertEqual(BeforeCanary, guarded.Before);
            AssertEqual(AfterCanary, guarded.After);

            FastSchemaBlock defaults = FastSchemaBlock.DefaultStats();
            for (int i = 0; i < (int)StatSchema.MAX_STATS; i++)
            {
                float expected = i == (int)StatSchema.Recovery ||
                    i == (int)StatSchema.Armor ||
                    i == (int)StatSchema.Revival ||
                    i == (int)StatSchema.Amount
                    ? BackendConfig.Schema.DefaultAdditiveStat
                    : BackendConfig.Schema.DefaultStatMultiplier;
                AssertEqual(BitConverter.SingleToInt32Bits(expected), defaults.GetInt(i));
            }
            for (int i = (int)StatSchema.MAX_STATS; i < FastSchemaBlock.MaxSize; i++)
                AssertEqual(0, defaults.GetInt(i));

            GuardedProfile profileGuard = new GuardedProfile
            {
                Before = BeforeCanary,
                Profile = PlayerMetaSchema.Default(),
                After = AfterCanary
            };
            for (int toonIndex = 0; toonIndex < PlayerMetaSchema.MaxToons; toonIndex++)
            {
                FastSchemaBlock toon = default;
                for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
                    toon.SetInt(slot, unchecked((toonIndex + 1) * 1000003 + slot));
                Assert(profileGuard.Profile.TrySetToonBlock(toonIndex, in toon));
            }
            for (int toonIndex = 0; toonIndex < PlayerMetaSchema.MaxToons; toonIndex++)
            {
                Assert(profileGuard.Profile.TryGetToonBlock(toonIndex, out FastSchemaBlock toon));
                for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
                    AssertEqual(unchecked((toonIndex + 1) * 1000003 + slot), toon.GetInt(slot));
            }
            AssertEqual(BeforeCanary, profileGuard.Before);
            AssertEqual(AfterCanary, profileGuard.After);

            Assert(!profileGuard.Profile.TryGetToonBlock(int.MinValue, out FastSchemaBlock invalidLow));
            Assert(!profileGuard.Profile.TryGetToonBlock(int.MaxValue, out FastSchemaBlock invalidHigh));
            Assert(!profileGuard.Profile.TrySetToonBlock(-1, in defaults));
            Assert(!profileGuard.Profile.TrySetToonBlock(PlayerMetaSchema.MaxToons, in defaults));
            for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
            {
                AssertEqual(0, invalidLow.GetInt(slot));
                AssertEqual(0, invalidHigh.GetInt(slot));
            }

            FastSchemaBlock copiedBlock = defaults;
            copiedBlock.SetInt(0, 123456789);
            AssertEqual(BitConverter.SingleToInt32Bits(BackendConfig.Schema.DefaultStatMultiplier), defaults.GetInt(0));

            HitboxData hitboxDefaults = new HitboxData();
            AssertNear(BackendConfig.Models.HitboxDefaultPosition, hitboxDefaults.X, 0f);
            AssertNear(BackendConfig.Models.HitboxDefaultPosition, hitboxDefaults.Y, 0f);
            AssertNear(BackendConfig.Models.HitboxDefaultSize, hitboxDefaults.Width, 0f);
            AssertNear(BackendConfig.Models.HitboxDefaultSize, hitboxDefaults.Height, 0f);

            TestAllSchemaBackedModelProperties();

            MovingToolData moving = default;
            for (int i = 0; i < BackendConfig.Deformation.OctantCount; i++)
            {
                float amplitude = i + 0.25f;
                float frequency = i + 10.5f;
                moving.SetJitterAmplitude(i, amplitude);
                moving.SetJitterFrequency(i, frequency);
                AssertNear(amplitude, moving.GetJitterAmplitude(i), 0f);
                AssertNear(frequency, moving.GetJitterFrequency(i), 0f);
            }
            for (int slot = 0; slot < BackendConfig.Deformation.OctantCount; slot++)
            {
                AssertNear(slot + 0.25f, moving.Block.GetFloat((int)MovingToolSchema.JitterAmplitudesStart + slot), 0f);
                AssertNear(slot + 10.5f, moving.Block.GetFloat((int)MovingToolSchema.JitterFrequenciesStart + slot), 0f);
            }

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
            foreach (Type enumType in schemaEnums)
            {
                Array values = Enum.GetValues(enumType);
                foreach (object enumValue in values)
                {
                    int offset = (int)enumValue;
                    Assert(offset >= 0);
                    Assert(offset <= FastSchemaBlock.MaxSize);
                }
            }
        }

        private static void TestAllSchemaBackedModelProperties()
        {
            Type blockType = typeof(FastSchemaBlock);
            Type[] types = typeof(HitboxData).Assembly.GetTypes();
            int testedTypes = 0;
            int testedProperties = 0;
            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                Type type = types[typeIndex];
                if (!type.IsValueType || type.Namespace != typeof(HitboxData).Namespace) continue;
                FieldInfo blockField = type.GetField("Block", BindingFlags.Public | BindingFlags.Instance);
                if (blockField == null || blockField.FieldType != blockType) continue;
                testedTypes++;

                PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                for (int propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
                {
                    PropertyInfo property = properties[propertyIndex];
                    if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0) continue;
                    object sample = GetSchemaPropertySample(property.PropertyType, testedProperties);
                    if (sample == null && property.PropertyType != typeof(string)) continue;

                    object boxed = Activator.CreateInstance(type);
                    FastSchemaBlock baseline = default;
                    for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
                        baseline.SetInt(slot, unchecked((int)0xA5000000) + slot);
                    blockField.SetValue(boxed, baseline);

                    property.SetValue(boxed, sample);
                    object roundTrip = property.GetValue(boxed);
                    AssertEqual(sample, roundTrip);
                    FastSchemaBlock after = (FastSchemaBlock)blockField.GetValue(boxed);
                    int changedSlots = 0;
                    for (int slot = 0; slot < FastSchemaBlock.MaxSize; slot++)
                    {
                        if (baseline.GetInt(slot) != after.GetInt(slot)) changedSlots++;
                    }
                    AssertEqual(1, changedSlots);
                    testedProperties++;
                }
            }
            Assert(testedTypes >= 20);
            Assert(testedProperties >= 70);
        }

        private static object GetSchemaPropertySample(Type type, int salt)
        {
            if (type == typeof(int)) return unchecked((int)0x13570000) + salt;
            if (type == typeof(short)) return (short)(1234 + salt % 1000);
            if (type == typeof(float)) return 123.25f + salt;
            if (type == typeof(bool)) return true;
            if (type == typeof(string)) return "schema-value-" + salt;
            return null;
        }

        private static void TestRandomizedCollections()
        {
            AssertThrows<ArgumentOutOfRangeException>(() => new FastPool<object>(-1, () => new object()));
            AssertThrows<ArgumentNullException>(() => new FastPool<object>(1, null));
            FastPool<object> empty = new FastPool<object>(0, () => new object());
            AssertEqual<object>(null, empty.Rent());
            AssertThrows<ArgumentOutOfRangeException>(() => empty.Prewarm(1));

            int nextId = 0;
            FastPool<PoolItem> pool = new FastPool<PoolItem>(31, () => new PoolItem(++nextId));
            List<PoolItem> rented = new List<PoolItem>();
            HashSet<PoolItem> everSeen = new HashSet<PoolItem>();
            Random random = new Random(0x51A7E);
            for (int operation = 0; operation < 10000; operation++)
            {
                int choice = random.Next(100);
                if (choice < 55)
                {
                    PoolItem item = pool.Rent();
                    if (item == null)
                    {
                        AssertEqual(pool.Capacity, pool.CreatedCount);
                        AssertEqual(0, pool.AvailableCount);
                    }
                    else
                    {
                        Assert(!rented.Contains(item));
                        Assert(everSeen.Add(item) || item.Id <= pool.CreatedCount);
                        rented.Add(item);
                    }
                }
                else if (choice < 90 && rented.Count > 0)
                {
                    int index = random.Next(rented.Count);
                    PoolItem item = rented[index];
                    rented.RemoveAt(index);
                    pool.Return(item);
                    AssertThrows<InvalidOperationException>(() => pool.Return(item));
                }
                else
                {
                    int target = random.Next(pool.Capacity + 1);
                    pool.Prewarm(target);
                    Assert(pool.CreatedCount >= target);
                }

                Assert(pool.CreatedCount >= 0 && pool.CreatedCount <= pool.Capacity);
                AssertEqual(pool.CreatedCount - rented.Count, pool.AvailableCount);
                AssertEqual(pool.CreatedCount, nextId);
                Assert(everSeen.Count <= pool.CreatedCount);
            }
            AssertThrows<ArgumentNullException>(() => pool.Return(null));
            AssertThrows<ArgumentException>(() => pool.Return(new PoolItem(-1)));

            object duplicate = new object();
            FastPool<object> duplicateFactoryPool = new FastPool<object>(2, () => duplicate);
            Assert(ReferenceEquals(duplicate, duplicateFactoryPool.Rent()));
            AssertThrows<InvalidOperationException>(() => duplicateFactoryPool.Rent());
            AssertEqual(1, duplicateFactoryPool.CreatedCount);
            FastPool<object> nullFactoryPool = new FastPool<object>(1, () => null);
            AssertThrows<InvalidOperationException>(() => nullFactoryPool.Rent());
            AssertEqual(0, nullFactoryPool.CreatedCount);

            FastPoolRegistry<PoolItem>.Clear();
            int registryId = 0;
            FastPoolRegistry<PoolItem>.RegisterPool(7, 3, () => new PoolItem(++registryId));
            FastPoolRegistry<PoolItem>.RegisterPool(7, 100, () => new PoolItem(9999));
            Assert(FastPoolRegistry<PoolItem>.Prewarm(7, 3));
            Assert(!FastPoolRegistry<PoolItem>.Prewarm(404, 1));
            PoolItem registryA = FastPoolRegistry<PoolItem>.Rent(7);
            PoolItem registryB = FastPoolRegistry<PoolItem>.Rent(7);
            PoolItem registryC = FastPoolRegistry<PoolItem>.Rent(7);
            AssertEqual<PoolItem>(null, FastPoolRegistry<PoolItem>.Rent(7));
            Assert(registryA.Id <= 3 && registryB.Id <= 3 && registryC.Id <= 3);
            FastPoolRegistry<PoolItem>.Return(7, registryB);
            Assert(ReferenceEquals(registryB, FastPoolRegistry<PoolItem>.Rent(7)));
            FastPoolRegistry<PoolItem>.Return(999, null);
            FastPoolRegistry<PoolItem>.Clear();
            AssertEqual<PoolItem>(null, FastPoolRegistry<PoolItem>.Rent(7));

            AssertThrows<ArgumentOutOfRangeException>(() => new FastSwapList<StateTrackable>(-1));
            FastSwapList<StateTrackable> swap = new FastSwapList<StateTrackable>(0);
            List<StateTrackable> expected = new List<StateTrackable>();
            StateTrackable[] candidates = new StateTrackable[64];
            for (int i = 0; i < candidates.Length; i++) candidates[i] = new StateTrackable(i);
            random = new Random(0x5A9A);
            for (int operation = 0; operation < 12000; operation++)
            {
                StateTrackable item = candidates[random.Next(candidates.Length)];
                int expectedIndex = expected.IndexOf(item);
                if (expectedIndex < 0 && random.Next(100) < 55)
                {
                    swap.Add(item);
                    expected.Add(item);
                    AssertThrows<InvalidOperationException>(() => swap.Add(item));
                }
                else
                {
                    swap.Remove(item);
                    if (expectedIndex >= 0)
                    {
                        int last = expected.Count - 1;
                        expected[expectedIndex] = expected[last];
                        expected.RemoveAt(last);
                    }
                }

                AssertEqual(expected.Count, swap.Count);
                for (int i = 0; i < expected.Count; i++)
                {
                    Assert(ReferenceEquals(expected[i], swap[i]));
                    AssertEqual(i, swap[i].ActiveListIndex);
                }
                for (int i = 0; i < candidates.Length; i++)
                {
                    bool present = expected.Contains(candidates[i]);
                    AssertEqual(present ? expected.IndexOf(candidates[i]) : BackendConfig.Collections.UnregisteredListIndex,
                        candidates[i].ActiveListIndex);
                }
            }
            AssertThrows<ArgumentNullException>(() => swap.Add(null));
            AssertThrows<ArgumentNullException>(() => swap.Remove(null));
            swap.Clear();
            AssertEqual(0, swap.Count);
            for (int i = 0; i < candidates.Length; i++)
                AssertEqual(BackendConfig.Collections.UnregisteredListIndex, candidates[i].ActiveListIndex);
        }

        private static void TestRandomizedGridAndRingBuffer()
        {
            AssertThrows<ArgumentOutOfRangeException>(() => new FastGridMap(-1, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => new FastGridMap(1, -1));
            AssertThrows<OverflowException>(() => new FastGridMap(int.MaxValue, 2));
            FastGridMap map = new FastGridMap(37, 23);
            short[,] expected = new short[map.Height, map.Width];
            Random random = new Random(0x6D4150);
            for (int i = 0; i < 20000; i++)
            {
                int x = random.Next(-8, map.Width + 8);
                int y = random.Next(-8, map.Height + 8);
                short tile = (short)random.Next(short.MinValue, short.MaxValue + 1);
                map.SetTile(x, y, tile);
                if ((uint)x < (uint)map.Width && (uint)y < (uint)map.Height) expected[y, x] = tile;
                AssertEqual(
                    (uint)x < (uint)map.Width && (uint)y < (uint)map.Height
                        ? expected[y, x]
                        : BackendConfig.Collections.EmptyTileId,
                    map.GetTile(x, y));
            }
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                    AssertEqual(expected[y, x], map.RawData[y * map.Width + x]);
            }
            map.RawData[0] = short.MaxValue;
            AssertEqual(short.MaxValue, map.GetTile(0, 0));

            float[] cameraValues = { -1000f, -2.25f, 0f, 18.5f, 40f, 1000f };
            for (int i = 0; i < cameraValues.Length; i++)
            {
                map.GetVisibleBounds(cameraValues[i], cameraValues[cameraValues.Length - 1 - i], 8f, 6f, 1.5f, 2,
                    out int minX, out int maxX, out int minY, out int maxY);
                float cameraX = cameraValues[i];
                float cameraY = cameraValues[cameraValues.Length - 1 - i];
                float inverseCellSize = 1f / 1.5f;
                int expectedMinX = (int)((cameraX - 4f) * inverseCellSize) - 2;
                int expectedMaxX = (int)((cameraX + 4f) * inverseCellSize) + 2;
                int expectedMinY = (int)((cameraY - 3f) * inverseCellSize) - 2;
                int expectedMaxY = (int)((cameraY + 3f) * inverseCellSize) + 2;
                if (expectedMinX < 0) expectedMinX = 0;
                if (expectedMaxX >= map.Width) expectedMaxX = map.Width - 1;
                if (expectedMinY < 0) expectedMinY = 0;
                if (expectedMaxY >= map.Height) expectedMaxY = map.Height - 1;
                AssertEqual(expectedMinX, minX);
                AssertEqual(expectedMaxX, maxX);
                AssertEqual(expectedMinY, minY);
                AssertEqual(expectedMaxY, maxY);
            }
            AssertThrows<ArgumentOutOfRangeException>(() => map.GetVisibleBounds(0, 0, -1, 1, 1, 0, out _, out _, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => map.GetVisibleBounds(0, 0, 1, -1, 1, 0, out _, out _, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => map.GetVisibleBounds(0, 0, 1, 1, float.NaN, 0, out _, out _, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => map.GetVisibleBounds(0, 0, 1, 1, float.PositiveInfinity, 0, out _, out _, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(() => map.GetVisibleBounds(0, 0, 1, 1, 1, -1, out _, out _, out _, out _));

            FastGridMap.MapPropData prop = new FastGridMap.MapPropData { AssetKey = "tree", X = 3, Y = 4, Scale = 1.5f };
            map.Props.Add(prop);
            AssertEqual(0, prop.ActiveListIndex);
            AssertEqual("tree", map.Props[0].AssetKey);
            map.Props.Remove(prop);
            AssertEqual(BackendConfig.Collections.UnregisteredListIndex, prop.ActiveListIndex);

            AssertThrows<ArgumentOutOfRangeException>(() => new FastRingBufferViewport(0, 1));
            FastRingBufferViewport viewport = new FastRingBufferViewport(17, 11);
            int[] coordinates = { int.MinValue, -1000001, -18, -17, -1, 0, 1, 16, 17, 18, 1000001, int.MaxValue };
            for (int xIndex = 0; xIndex < coordinates.Length; xIndex++)
            {
                for (int yIndex = 0; yIndex < coordinates.Length; yIndex++)
                {
                    int worldX = coordinates[xIndex];
                    int worldY = coordinates[yIndex];
                    viewport.GetRingBufferIndex(worldX, worldY, out int ringX, out int ringY);
                    AssertEqual(PositiveModulo(worldX, viewport.ViewportCols), ringX);
                    AssertEqual(PositiveModulo(worldY, viewport.ViewportRows), ringY);
                }
            }
            Assert(!viewport.HasViewportShifted(int.MaxValue, int.MaxValue));
            Assert(viewport.HasViewportShifted(int.MaxValue - 1, int.MaxValue));
            viewport.UpdatePreviousBounds(int.MinValue, int.MaxValue);
            Assert(!viewport.HasViewportShifted(int.MinValue, int.MaxValue));
            Assert(viewport.HasViewportShifted(int.MinValue + 1, int.MaxValue));
        }

        private static int PositiveModulo(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuardedSchema
        {
            public ulong Before;
            public FastSchemaBlock Block;
            public ulong After;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuardedProfile
        {
            public ulong Before;
            public PlayerMetaSchema Profile;
            public ulong After;
        }

        private sealed class PoolItem
        {
            public int Id { get; }

            public PoolItem(int id)
            {
                Id = id;
            }
        }

        private sealed class StateTrackable : IFastListTrackable
        {
            public int Id { get; }
            public int ActiveListIndex { get; set; } = BackendConfig.Collections.UnregisteredListIndex;

            public StateTrackable(int id)
            {
                Id = id;
            }
        }
    }
}
