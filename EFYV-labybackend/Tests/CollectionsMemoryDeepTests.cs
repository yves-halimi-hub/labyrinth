using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using EFYVBackend.Core.Collections;
using EFYVBackend.Core.Memory;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Verification
{
    internal static partial class Program
    {
        // ------------------------------------------------------------------
        // FastPool: exact LIFO identity model + factory fault injection
        // ------------------------------------------------------------------
        private static void TestCollectionsMemoryPoolLifoModel()
        {
            // The pool is an exact LIFO stack over available items: Return and
            // Prewarm both push on top, Rent pops the top. The existing randomized
            // test only verifies counters and membership; this model verifies the
            // IDENTITY of every rented object against a reference stack.
            List<CollectionsMemoryPoolNode> created = new List<CollectionsMemoryPoolNode>();
            int remainingBeforeFault = int.MaxValue;
            Func<CollectionsMemoryPoolNode> factory = () =>
            {
                if (remainingBeforeFault <= 0) throw new CollectionsMemoryFactoryException();
                remainingBeforeFault--;
                CollectionsMemoryPoolNode node = new CollectionsMemoryPoolNode(created.Count);
                created.Add(node);
                return node;
            };

            const int capacity = 12;
            FastPool<CollectionsMemoryPoolNode> pool = new FastPool<CollectionsMemoryPoolNode>(capacity, factory);
            Stack<CollectionsMemoryPoolNode> availableModel = new Stack<CollectionsMemoryPoolNode>();
            List<CollectionsMemoryPoolNode> rentedModel = new List<CollectionsMemoryPoolNode>();
            Random random = new Random(0x0C011EC7);
            for (int operation = 0; operation < 6000; operation++)
            {
                int choice = random.Next(100);
                if (choice < 50)
                {
                    int createdBefore = created.Count;
                    CollectionsMemoryPoolNode item = pool.Rent();
                    if (availableModel.Count > 0)
                    {
                        Assert(ReferenceEquals(availableModel.Pop(), item));
                        AssertEqual(createdBefore, created.Count);
                        rentedModel.Add(item);
                    }
                    else if (createdBefore < capacity)
                    {
                        AssertEqual(createdBefore + 1, created.Count);
                        Assert(ReferenceEquals(created[createdBefore], item));
                        rentedModel.Add(item);
                    }
                    else
                    {
                        AssertEqual<CollectionsMemoryPoolNode>(null, item);
                    }
                }
                else if (choice < 85 && rentedModel.Count > 0)
                {
                    int index = random.Next(rentedModel.Count);
                    CollectionsMemoryPoolNode item = rentedModel[index];
                    rentedModel.RemoveAt(index);
                    pool.Return(item);
                    availableModel.Push(item);
                }
                else
                {
                    int target = random.Next(capacity + 1);
                    int createdBefore = created.Count;
                    pool.Prewarm(target);
                    for (int i = createdBefore; i < created.Count; i++) availableModel.Push(created[i]);
                    Assert(created.Count >= target);
                }

                AssertEqual(availableModel.Count, pool.AvailableCount);
                AssertEqual(created.Count, pool.CreatedCount);
            }

            // Deterministic drain: return everything, then rents must pop in exact
            // reverse-return order until the pool is empty.
            for (int i = 0; i < rentedModel.Count; i++)
            {
                pool.Return(rentedModel[i]);
                availableModel.Push(rentedModel[i]);
            }
            rentedModel.Clear();
            int createdBeforeDrain = created.Count;
            pool.Prewarm(capacity);
            for (int i = createdBeforeDrain; i < created.Count; i++) availableModel.Push(created[i]);
            AssertEqual(capacity, pool.CreatedCount);
            AssertEqual(capacity, pool.AvailableCount);
            while (availableModel.Count > 0)
            {
                Assert(ReferenceEquals(availableModel.Pop(), pool.Rent()));
            }
            AssertEqual<CollectionsMemoryPoolNode>(null, pool.Rent());

            // A failed foreign Return must leave counters untouched.
            int createdSnapshot = pool.CreatedCount;
            int availableSnapshot = pool.AvailableCount;
            AssertThrows<ArgumentException>(() => pool.Return(new CollectionsMemoryPoolNode(-1)));
            AssertEqual(createdSnapshot, pool.CreatedCount);
            AssertEqual(availableSnapshot, pool.AvailableCount);

            // Factory fault injection: a throwing factory must propagate out of
            // Rent and Prewarm and leave the pool in a consistent, usable state.
            created.Clear();
            FastPool<CollectionsMemoryPoolNode> faultPool = new FastPool<CollectionsMemoryPoolNode>(8, factory);
            CollectionsMemoryPoolNode firstRented = faultPool.Rent();
            CollectionsMemoryPoolNode secondRented = faultPool.Rent();
            Assert(firstRented != null && secondRented != null);
            remainingBeforeFault = 0;
            AssertThrows<CollectionsMemoryFactoryException>(() => faultPool.Rent());
            AssertEqual(2, faultPool.CreatedCount);
            AssertEqual(0, faultPool.AvailableCount);
            AssertThrows<CollectionsMemoryFactoryException>(() => faultPool.Prewarm(5));
            AssertEqual(2, faultPool.CreatedCount);
            AssertEqual(0, faultPool.AvailableCount);

            // Partial Prewarm fault: two creations succeed, the third throws.
            remainingBeforeFault = 2;
            AssertThrows<CollectionsMemoryFactoryException>(() => faultPool.Prewarm(8));
            AssertEqual(4, faultPool.CreatedCount);
            AssertEqual(2, faultPool.AvailableCount);

            // Recovery: once the factory works again the pool completes normally.
            remainingBeforeFault = int.MaxValue;
            faultPool.Prewarm(8);
            AssertEqual(8, faultPool.CreatedCount);
            AssertEqual(6, faultPool.AvailableCount);
            faultPool.Return(firstRented);
            faultPool.Return(secondRented);
            for (int i = 0; i < 8; i++) Assert(faultPool.Rent() != null);
            AssertEqual<CollectionsMemoryPoolNode>(null, faultPool.Rent());
        }

        // ------------------------------------------------------------------
        // FastPoolRegistry: exception propagation, failed-registration
        // recovery, and per-type static isolation
        // ------------------------------------------------------------------
        private static void TestCollectionsMemoryPoolRegistryContracts()
        {
            try
            {
                FastPoolRegistry<CollectionsMemoryNodeA>.Clear();
                FastPoolRegistry<CollectionsMemoryNodeB>.Clear();

                // Invalid registrations throw from the pool constructor and must
                // NOT poison the key (dictionary Add never runs).
                AssertThrows<ArgumentOutOfRangeException>(
                    () => FastPoolRegistry<CollectionsMemoryNodeA>.RegisterPool(11, -1, () => new CollectionsMemoryNodeA()));
                AssertEqual<CollectionsMemoryNodeA>(null, FastPoolRegistry<CollectionsMemoryNodeA>.Rent(11));
                Assert(!FastPoolRegistry<CollectionsMemoryNodeA>.Prewarm(11, 0));
                AssertThrows<ArgumentNullException>(
                    () => FastPoolRegistry<CollectionsMemoryNodeA>.RegisterPool(11, 1, null));
                Assert(!FastPoolRegistry<CollectionsMemoryNodeA>.Prewarm(11, 0));

                // The same key registers fine afterwards.
                FastPoolRegistry<CollectionsMemoryNodeA>.RegisterPool(11, 2, () => new CollectionsMemoryNodeA());
                Assert(FastPoolRegistry<CollectionsMemoryNodeA>.Prewarm(11, 2));
                CollectionsMemoryNodeA rentedA = FastPoolRegistry<CollectionsMemoryNodeA>.Rent(11);
                Assert(rentedA != null);

                // Pool-level guards surface through the registry for KNOWN keys
                // (the existing tests only exercise unknown-key silence).
                AssertThrows<ArgumentNullException>(() => FastPoolRegistry<CollectionsMemoryNodeA>.Return(11, null));
                AssertThrows<ArgumentException>(
                    () => FastPoolRegistry<CollectionsMemoryNodeA>.Return(11, new CollectionsMemoryNodeA()));
                AssertThrows<ArgumentOutOfRangeException>(() => FastPoolRegistry<CollectionsMemoryNodeA>.Prewarm(11, 3));

                // Capacity-zero pool rents null through the registry.
                FastPoolRegistry<CollectionsMemoryNodeA>.RegisterPool(12, 0, () => new CollectionsMemoryNodeA());
                AssertEqual<CollectionsMemoryNodeA>(null, FastPoolRegistry<CollectionsMemoryNodeA>.Rent(12));

                // Per-type isolation: the same key in a differently-typed registry
                // is a different static dictionary, and Clear on one type must not
                // affect the other.
                FastPoolRegistry<CollectionsMemoryNodeB>.RegisterPool(11, 1, () => new CollectionsMemoryNodeB());
                CollectionsMemoryNodeB rentedB = FastPoolRegistry<CollectionsMemoryNodeB>.Rent(11);
                Assert(rentedB != null);
                FastPoolRegistry<CollectionsMemoryNodeA>.Clear();
                AssertEqual<CollectionsMemoryNodeA>(null, FastPoolRegistry<CollectionsMemoryNodeA>.Rent(11));
                FastPoolRegistry<CollectionsMemoryNodeB>.Return(11, rentedB);
                Assert(ReferenceEquals(rentedB, FastPoolRegistry<CollectionsMemoryNodeB>.Rent(11)));

                // Duplicate registration keeps the FIRST pool: capacity stays 1,
                // so prewarming to 2 must throw from the original pool.
                FastPoolRegistry<CollectionsMemoryNodeB>.RegisterPool(11, 50, () => new CollectionsMemoryNodeB());
                AssertThrows<ArgumentOutOfRangeException>(() => FastPoolRegistry<CollectionsMemoryNodeB>.Prewarm(11, 2));
            }
            finally
            {
                FastPoolRegistry<CollectionsMemoryNodeA>.Clear();
                FastPoolRegistry<CollectionsMemoryNodeB>.Clear();
            }
        }

        // ------------------------------------------------------------------
        // FastSwapList: cross-list confusion and tampered-index adversarial
        // ------------------------------------------------------------------
        private static void TestCollectionsMemorySwapListAdversarial()
        {
            FastSwapList<CollectionsMemoryTrackable> listOne = new FastSwapList<CollectionsMemoryTrackable>(4);
            FastSwapList<CollectionsMemoryTrackable> listTwo = new FastSwapList<CollectionsMemoryTrackable>(4);
            CollectionsMemoryTrackable a = new CollectionsMemoryTrackable();
            CollectionsMemoryTrackable b = new CollectionsMemoryTrackable();
            CollectionsMemoryTrackable c = new CollectionsMemoryTrackable();
            CollectionsMemoryTrackable d = new CollectionsMemoryTrackable();
            CollectionsMemoryTrackable e = new CollectionsMemoryTrackable();
            listOne.Add(a);
            listOne.Add(b);
            listOne.Add(c);
            listTwo.Add(d);
            listTwo.Add(e);

            // Removing an item that belongs to ANOTHER list is a validated no-op:
            // d.ActiveListIndex is 0, but listOne[0] is a different object.
            listOne.Remove(d);
            AssertEqual(3, listOne.Count);
            AssertEqual(2, listTwo.Count);
            AssertEqual(0, d.ActiveListIndex);
            Assert(ReferenceEquals(d, listTwo[0]));
            Assert(ReferenceEquals(a, listOne[0]));

            // Adding an item registered elsewhere throws and leaves the target list
            // and the item untouched.
            AssertThrows<InvalidOperationException>(() => listTwo.Add(b));
            AssertEqual(2, listTwo.Count);
            AssertEqual(1, b.ActiveListIndex);
            Assert(ReferenceEquals(b, listOne[1]));

            // Tampered too-large index: Remove is a no-op and does not repair it.
            c.ActiveListIndex = 999;
            listOne.Remove(c);
            AssertEqual(3, listOne.Count);
            AssertEqual(999, c.ActiveListIndex);
            Assert(ReferenceEquals(c, listOne[2]));

            // Tampered index equal to Count (one past the end).
            c.ActiveListIndex = listOne.Count;
            listOne.Remove(c);
            AssertEqual(3, listOne.Count);

            // Tampered negative index.
            c.ActiveListIndex = -5;
            listOne.Remove(c);
            AssertEqual(3, listOne.Count);
            c.ActiveListIndex = 2; // repair

            // Tampered index pointing at another live item's slot: the reference
            // check must protect the innocent item.
            a.ActiveListIndex = 1;
            listOne.Remove(a);
            AssertEqual(3, listOne.Count);
            Assert(ReferenceEquals(a, listOne[0]));
            Assert(ReferenceEquals(b, listOne[1]));
            AssertEqual(1, b.ActiveListIndex);
            a.ActiveListIndex = 0; // repair

            // Indexer follows List<T> bounds semantics.
            AssertThrows<ArgumentOutOfRangeException>(() => { CollectionsMemoryTrackable unused = listOne[3]; });
            AssertThrows<ArgumentOutOfRangeException>(() => { CollectionsMemoryTrackable unused = listOne[-1]; });

            // The read-only view is cached (same instance) and live.
            Assert(ReferenceEquals(listOne.Items, listOne.Items));
            IReadOnlyList<CollectionsMemoryTrackable> view = listTwo.Items;
            AssertEqual(2, view.Count);
            listTwo.Remove(e);
            AssertEqual(1, view.Count);
            Assert(ReferenceEquals(d, view[0]));

            // Removing the last element swaps the item with itself and pops.
            listOne.Remove(c);
            AssertEqual(2, listOne.Count);
            AssertEqual(BackendConfig.Collections.UnregisteredListIndex, c.ActiveListIndex);
            Assert(ReferenceEquals(a, listOne[0]));
            Assert(ReferenceEquals(b, listOne[1]));

            // Clear resets EVERY stored item, even one whose index was tampered
            // while it sat in the list.
            b.ActiveListIndex = 12345;
            listOne.Clear();
            AssertEqual(0, listOne.Count);
            AssertEqual(BackendConfig.Collections.UnregisteredListIndex, a.ActiveListIndex);
            AssertEqual(BackendConfig.Collections.UnregisteredListIndex, b.ActiveListIndex);
            listOne.Clear(); // idempotent on empty
            AssertEqual(0, listOne.Count);
            listTwo.Clear();
            AssertEqual(BackendConfig.Collections.UnregisteredListIndex, d.ActiveListIndex);

            // Everything is reusable after Clear.
            listTwo.Add(a);
            AssertEqual(0, a.ActiveListIndex);
            listTwo.Clear();
        }

        // ------------------------------------------------------------------
        // FastGridMap: extreme coordinates, empty-range bounds convention,
        // NaN fov validation gap, prop defaults
        // ------------------------------------------------------------------
        private static void TestCollectionsMemoryGridExtremes()
        {
            FastGridMap map = new FastGridMap(5, 4);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 5; x++) map.SetTile(x, y, (short)(y * 5 + x + 1));
            }

            int[] badCoordinates = { int.MinValue, int.MinValue + 1, -1, 5, 6, 99999, int.MaxValue };
            for (int i = 0; i < badCoordinates.Length; i++)
            {
                int bad = badCoordinates[i];
                map.SetTile(bad, 1, 77);
                map.SetTile(1, bad, 77);
                map.SetTile(bad, bad, 77);
                AssertEqual(BackendConfig.Collections.EmptyTileId, map.GetTile(bad, 1));
                AssertEqual(BackendConfig.Collections.EmptyTileId, map.GetTile(1, bad));
                AssertEqual(BackendConfig.Collections.EmptyTileId, map.GetTile(bad, bad));
            }
            // Height-only out-of-range (4 is a valid x but not a valid y).
            map.SetTile(4, 4, 77);
            AssertEqual(BackendConfig.Collections.EmptyTileId, map.GetTile(4, 4));
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 5; x++) AssertEqual((short)(y * 5 + x + 1), map.GetTile(x, y));
            }

            // Fully off-map east: minX is NOT clamped down, giving minX > maxX as
            // the documented empty-range convention.
            FastGridMap tenByTen = new FastGridMap(10, 10);
            tenByTen.GetVisibleBounds(1000f, 1000f, 8f, 8f, 1f, 2, out int minX, out int maxX, out int minY, out int maxY);
            AssertEqual(994, minX);
            AssertEqual(9, maxX);
            AssertEqual(994, minY);
            AssertEqual(9, maxY);
            Assert(minX > maxX);

            // Fully off-map west: maxX is NOT clamped up, so it stays negative.
            tenByTen.GetVisibleBounds(-1000f, -1000f, 8f, 8f, 1f, 2, out minX, out maxX, out minY, out maxY);
            AssertEqual(0, minX);
            AssertEqual(-994, maxX);
            AssertEqual(0, minY);
            AssertEqual(-994, maxY);
            Assert(minX > maxX);

            // Zero-size FOV is legal: bounds collapse to the camera cell +/- padding.
            FastGridMap twenty = new FastGridMap(20, 20);
            twenty.GetVisibleBounds(7.3f, 7.3f, 0f, 0f, 1f, 1, out minX, out maxX, out minY, out maxY);
            AssertEqual(6, minX);
            AssertEqual(8, maxX);
            AssertEqual(6, minY);
            AssertEqual(8, maxY);

            // Enormous padding saturates to the full map thanks to the clamps.
            tenByTen.GetVisibleBounds(0f, 0f, 0f, 0f, 1f, int.MaxValue, out minX, out maxX, out minY, out maxY);
            AssertEqual(0, minX);
            AssertEqual(9, maxX);
            AssertEqual(0, minY);
            AssertEqual(9, maxY);

            // 1x1 map: the whole world is cell (0,0).
            FastGridMap single = new FastGridMap(1, 1);
            single.SetTile(0, 0, -7);
            AssertEqual((short)-7, single.GetTile(0, 0));
            single.GetVisibleBounds(0.5f, 0.5f, 1f, 1f, 1f, 0, out minX, out maxX, out minY, out maxY);
            AssertEqual(0, minX);
            AssertEqual(0, maxX);
            AssertEqual(0, minY);
            AssertEqual(0, maxY);

            // Negative cell size is rejected like zero.
            AssertThrows<ArgumentOutOfRangeException>(
                () => single.GetVisibleBounds(0f, 0f, 1f, 1f, -1f, 0, out _, out _, out _, out _));

            // FIXED: NaN fovWidth/fovHeight are rejected by the negated "!(fov >= 0)"
            // guards (NaN comparisons are false, so the old "< 0" form let NaN slip
            // through into the truncated casts and returned a garbage empty range).
            AssertThrows<ArgumentOutOfRangeException>(
                () => tenByTen.GetVisibleBounds(5f, 5f, float.NaN, 1f, 1f, 1, out _, out _, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(
                () => tenByTen.GetVisibleBounds(5f, 5f, 1f, float.NaN, 1f, 1, out _, out _, out _, out _));
            AssertThrows<ArgumentOutOfRangeException>(
                () => tenByTen.GetVisibleBounds(5f, 5f, float.NaN, float.NaN, 1f, 1, out _, out _, out _, out _));
            // Zero fov is still a valid (single-cell) query, so the guard flip did not
            // tighten the accepted domain.
            tenByTen.GetVisibleBounds(5f, 5f, 0f, 0f, 1f, 0, out minX, out maxX, out minY, out maxY);
            AssertEqual(5, minX);
            AssertEqual(5, maxX);
            AssertEqual(5, minY);
            AssertEqual(5, maxY);

            // MapPropData defaults.
            FastGridMap.MapPropData prop = new FastGridMap.MapPropData();
            AssertEqual(BackendConfig.Collections.UnregisteredListIndex, prop.ActiveListIndex);
            AssertEqual<string>(null, prop.AssetKey);
            AssertEqual(0, prop.X);
            AssertEqual(0, prop.Y);
            AssertEqual(0f, prop.Scale);

            // Props participates in swap-list semantics with tile storage untouched.
            map.Props.Add(prop);
            FastGridMap.MapPropData second = new FastGridMap.MapPropData { AssetKey = "rock" };
            map.Props.Add(second);
            map.Props.Remove(prop);
            AssertEqual(1, map.Props.Count);
            Assert(ReferenceEquals(second, map.Props[0]));
            AssertEqual(0, second.ActiveListIndex);
            AssertEqual(BackendConfig.Collections.UnregisteredListIndex, prop.ActiveListIndex);
            AssertEqual((short)1, map.GetTile(0, 0));
            map.Props.Clear();
        }

        // ------------------------------------------------------------------
        // FastRingBufferViewport: degenerate sizes, periodicity, axis shifts
        // ------------------------------------------------------------------
        private static void TestCollectionsMemoryRingBufferDegenerate()
        {
            // 1x1 viewport maps every coordinate to (0,0).
            FastRingBufferViewport unit = new FastRingBufferViewport(1, 1);
            int[] extremes = { int.MinValue, -12345, -1, 0, 1, 12345, int.MaxValue };
            for (int i = 0; i < extremes.Length; i++)
            {
                unit.GetRingBufferIndex(extremes[i], extremes[extremes.Length - 1 - i], out int ringX, out int ringY);
                AssertEqual(0, ringX);
                AssertEqual(0, ringY);
            }

            // int.MaxValue-sized viewport: modulo identity and negative folding.
            FastRingBufferViewport huge = new FastRingBufferViewport(int.MaxValue, int.MaxValue);
            huge.GetRingBufferIndex(0, int.MaxValue, out int hx, out int hy);
            AssertEqual(0, hx);
            AssertEqual(0, hy);
            huge.GetRingBufferIndex(-1, int.MinValue, out hx, out hy);
            AssertEqual(int.MaxValue - 1, hx);
            AssertEqual(int.MaxValue - 1, hy);
            huge.GetRingBufferIndex(int.MaxValue - 1, 12345, out hx, out hy);
            AssertEqual(int.MaxValue - 1, hx);
            AssertEqual(12345, hy);

            // Periodicity: shifting the world coordinate by whole viewport spans
            // never changes the ring index.
            FastRingBufferViewport viewport = new FastRingBufferViewport(13, 7);
            Random random = new Random(0x000D16B0);
            for (int i = 0; i < 400; i++)
            {
                int worldX = random.Next(-100000, 100001);
                int worldY = random.Next(-100000, 100001);
                viewport.GetRingBufferIndex(worldX, worldY, out int baseX, out int baseY);
                Assert(baseX >= 0 && baseX < 13);
                Assert(baseY >= 0 && baseY < 7);
                for (int k = -3; k <= 3; k++)
                {
                    viewport.GetRingBufferIndex(worldX + k * 13, worldY + k * 7, out int shiftedX, out int shiftedY);
                    AssertEqual(baseX, shiftedX);
                    AssertEqual(baseY, shiftedY);
                }
            }

            // Adjacent world coordinates land on adjacent (wrapped) ring slots.
            viewport.GetRingBufferIndex(25, 13, out int currentX, out int currentY);
            viewport.GetRingBufferIndex(26, 14, out int nextX, out int nextY);
            AssertEqual((currentX + 1) % 13, nextX);
            AssertEqual((currentY + 1) % 7, nextY);

            // Axis-independent shift detection (Y-only shifts were untested).
            FastRingBufferViewport shifting = new FastRingBufferViewport(4, 4);
            shifting.UpdatePreviousBounds(3, 4);
            Assert(!shifting.HasViewportShifted(3, 4));
            Assert(shifting.HasViewportShifted(3, 5));
            Assert(shifting.HasViewportShifted(4, 4));
            Assert(shifting.HasViewportShifted(4, 5));
            shifting.UpdatePreviousBounds(3, 5);
            Assert(!shifting.HasViewportShifted(3, 5));
            Assert(shifting.HasViewportShifted(3, 4));
        }

        // ------------------------------------------------------------------
        // FastMemory blending: bit-exact reference models for every branch of
        // BlendColor (2-arg and 3-arg) and the BlendLayer threshold/opacity
        // pipeline; independent alpha-monotonicity property
        // ------------------------------------------------------------------
        private static unsafe void TestCollectionsMemoryBlendExactModel()
        {
            Random random = new Random(0x00B1E4D0);

            // Branch-directed random sweep, exact equality (the existing random
            // test only checks within tolerance 1 against a double model).
            int[] branchHits = new int[5];
            for (int i = 0; i < 30000; i++)
            {
                uint src = NextUInt(random);
                uint dest = NextUInt(random);
                int shape = random.Next(8);
                if (shape == 0) src &= 0x00FFFFFFu;        // srcA == 0
                else if (shape == 1) src |= 0xFF000000u;   // srcA == 255
                else if (shape == 2) dest &= 0x00FFFFFFu;  // destA == 0
                else if (shape == 3) dest |= 0xFF000000u;  // destA == 255
                uint expected = dest;
                int branch = CollectionsMemoryReferenceBlendExact(ref expected, src);
                branchHits[branch]++;
                uint actual = dest;
                FastMemory.BlendColor(ref actual, src);
                AssertEqual(expected, actual);

                // Independent Porter-Duff property: output alpha never drops below
                // either input alpha.
                int srcAlpha = (byte)(src >> 24);
                int destAlpha = (byte)(dest >> 24);
                int outAlpha = (byte)(actual >> 24);
                Assert(outAlpha >= System.Math.Max(srcAlpha, destAlpha));
            }
            for (int branch = 0; branch < branchHits.Length; branch++) Assert(branchHits[branch] > 1000);

            // 3-arg opacity overload: exact model including the alpha pre-scale.
            for (int i = 0; i < 20000; i++)
            {
                uint src = NextUInt(random);
                uint dest = NextUInt(random);
                byte opacity = i % 5 == 0 ? (byte)0 : i % 5 == 1 ? (byte)255 : (byte)random.Next(256);
                uint expected = dest;
                CollectionsMemoryReferenceBlendOpacityExact(ref expected, src, opacity);
                uint actual = dest;
                FastMemory.BlendColor(ref actual, src, opacity);
                AssertEqual(expected, actual);
            }

            // Semantic pins for the special branches.
            uint transparentDest = 0x00ABCDEFu;
            FastMemory.BlendColor(ref transparentDest, 0x7F112233u);
            AssertEqual(0x7F112233u, transparentDest); // copied verbatim, no premultiply
            uint keep = 0x40556677u;
            FastMemory.BlendColor(ref keep, 0x00FFEEDDu);
            AssertEqual(0x40556677u, keep); // srcA == 0 keeps dest even with RGB garbage
            uint replaced = 0x33445566u;
            FastMemory.BlendColor(ref replaced, 0xFF010203u);
            AssertEqual(0xFF010203u, replaced);

            // Idempotence: blending a color onto an opaque destination of the same
            // color is exact for every channel value and any source alpha.
            for (int channel = 0; channel <= 255; channel += 3)
            {
                uint rgb = (uint)channel | ((uint)channel << 8) | ((uint)channel << 16);
                int[] alphas = { 1, 64, 128, 200, 254 };
                for (int alphaIndex = 0; alphaIndex < alphas.Length; alphaIndex++)
                {
                    uint pixel = rgb | 0xFF000000u;
                    FastMemory.BlendColor(ref pixel, rgb | ((uint)alphas[alphaIndex] << 24));
                    AssertEqual(rgb | 0xFF000000u, pixel);
                }
            }

            // BlendLayer threshold/opacity pipeline against the exact model, with
            // guard regions, over randomized thresholds and out-of-range opacities.
            for (int iteration = 0; iteration < 120; iteration++)
            {
                int count = random.Next(1, 65);
                int threshold = random.Next(256);
                float opacity = (float)(random.NextDouble() * 1.6d - 0.3d);
                uint[] destination = CreateGuardedPixels(count, GuardPixelA, GuardPixelB);
                uint[] source = CreateGuardedPixels(count, GuardPixelB, GuardPixelA);
                uint[] expected = new uint[count];
                for (int i = 0; i < count; i++)
                {
                    destination[i + 1] = NextUInt(random);
                    source[i + 1] = NextUInt(random);
                    expected[i] = destination[i + 1];
                }
                float clamped = opacity < 0f ? 0f : opacity > 1f ? 1f : opacity;
                byte byteOpacity = (byte)(clamped * 255f + 0.5f);
                if (byteOpacity != 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        byte sourceAlpha = (byte)(source[i + 1] >> 24);
                        if (sourceAlpha > threshold)
                            CollectionsMemoryReferenceBlendOpacityExact(ref expected[i], source[i + 1], byteOpacity);
                    }
                }
                fixed (uint* destinationBase = destination)
                fixed (uint* sourceBase = source)
                {
                    FastMemory.BlendLayer(destinationBase + 1, sourceBase + 1, count, threshold, opacity);
                }
                AssertPixelGuards(destination, GuardPixelA, GuardPixelB);
                AssertPixelGuards(source, GuardPixelB, GuardPixelA);
                for (int i = 0; i < count; i++) AssertEqual(expected[i], destination[i + 1]);
            }

            // Threshold is strict: srcA == threshold is skipped, srcA == threshold + 1 blends.
            uint[] thresholdDest = { 0xFF101010u };
            uint[] thresholdSrc = { 0x80FFFFFFu }; // alpha 128
            fixed (uint* destinationBase = thresholdDest)
            fixed (uint* sourceBase = thresholdSrc)
            {
                FastMemory.BlendLayer(destinationBase, sourceBase, 1, 128);
                AssertEqual(0xFF101010u, thresholdDest[0]);
                FastMemory.BlendLayer(destinationBase, sourceBase, 1, 127);
                Assert(thresholdDest[0] != 0xFF101010u);
            }

            // Opacity rounding to byte zero silences the whole call; clamping maps
            // out-of-range opacities onto the [0,1] results.
            uint[] opacityDest = { 0xFF404040u };
            uint[] opacitySrc = { 0xFF808080u };
            fixed (uint* destinationBase = opacityDest)
            fixed (uint* sourceBase = opacitySrc)
            {
                FastMemory.BlendLayer(destinationBase, sourceBase, 1, 0, 0.001f);
                AssertEqual(0xFF404040u, opacityDest[0]);
                FastMemory.BlendLayer(destinationBase, sourceBase, 1, 0, -5f);
                AssertEqual(0xFF404040u, opacityDest[0]);
                FastMemory.BlendLayer(destinationBase, sourceBase, 1, 0, 7f);
                AssertEqual(0xFF808080u, opacityDest[0]); // clamped to 1 => full copy of opaque src
            }

            // The 4-arg overload is exactly the 5-arg overload at opacity 1.
            uint[] fourArg = { 0x66112233u, 0x99445566u, 0x00778899u };
            uint[] fiveArg = (uint[])fourArg.Clone();
            uint[] layerSource = { 0x40AABBCCu, 0xF0DDEEFFu, 0x20010203u };
            fixed (uint* fourBase = fourArg)
            fixed (uint* fiveBase = fiveArg)
            fixed (uint* sourceBase = layerSource)
            {
                FastMemory.BlendLayer(fourBase, sourceBase, 3, 16);
                FastMemory.BlendLayer(fiveBase, sourceBase, 3, 16, 1f);
            }
            AssertSequenceEqual(fiveArg, fourArg);
        }

        // ------------------------------------------------------------------
        // FastEffects/FastMemory: blur overload equivalence, checked-area
        // overflow, blit extreme offsets, struct copy, linear 2D addressing
        // ------------------------------------------------------------------
        private static unsafe void TestCollectionsMemoryEffectsAndBlitExtremes()
        {
            Random random = new Random(0x00EF0E00);

            // The ArrayPool-backed overload, the caller-scratch overload, and the
            // in-place (src == dest) caller-scratch call must agree bitwise.
            for (int iteration = 0; iteration < 60; iteration++)
            {
                int width = random.Next(1, 15);
                int height = random.Next(1, 15);
                int radius = random.Next(0, 7);
                int count = width * height;
                uint[] source = new uint[count];
                for (int i = 0; i < count; i++) source[i] = NextUInt(random);
                uint[] pooledDestination = new uint[count];
                uint[] scratchDestination = new uint[count];
                uint[] scratch = CreateGuardedPixels(count, GuardPixelA, GuardPixelB);
                uint[] inPlace = (uint[])source.Clone();
                fixed (uint* sourceBase = source)
                fixed (uint* pooledBase = pooledDestination)
                fixed (uint* scratchDestinationBase = scratchDestination)
                fixed (uint* scratchBase = scratch)
                fixed (uint* inPlaceBase = inPlace)
                {
                    FastEffects.BoxBlur(sourceBase, pooledBase, width, height, radius);
                    FastEffects.BoxBlur(sourceBase, scratchDestinationBase, scratchBase + 1, width, height, radius);
                    FastEffects.BoxBlur(inPlaceBase, inPlaceBase, scratchBase + 1, width, height, radius);
                }
                AssertPixelGuards(scratch, GuardPixelA, GuardPixelB);
                for (int i = 0; i < count; i++) AssertEqual(scratchDestination[i], pooledDestination[i]);
                for (int i = 0; i < count; i++) AssertEqual(scratchDestination[i], inPlace[i]);
            }

            // Documents current behavior: when radius == 0 the scratch argument is
            // never validated, so null and aliased scratch pointers are tolerated
            // and the call degrades to a straight copy.
            uint[] radiusZeroSource = { 0x11111111u, 0x22222222u, 0x33333333u, 0x44444444u };
            uint[] radiusZeroDestination = new uint[4];
            fixed (uint* sourceBase = radiusZeroSource)
            fixed (uint* destinationBase = radiusZeroDestination)
            {
                FastEffects.BoxBlur(sourceBase, destinationBase, null, 2, 2, 0);
                for (int i = 0; i < 4; i++) AssertEqual(radiusZeroSource[i], radiusZeroDestination[i]);
                for (int i = 0; i < 4; i++) radiusZeroDestination[i] = 0u;
                FastEffects.BoxBlur(sourceBase, destinationBase, sourceBase, 2, 2, 0);
                for (int i = 0; i < 4; i++) AssertEqual(radiusZeroSource[i], radiusZeroDestination[i]);
            }

            // width * height is computed checked and throws BEFORE any pixel is
            // touched (only the radius-overflow path was previously covered).
            AssertThrows<OverflowException>(() => CollectionsMemoryBlurArea(65536, 65536, 1));
            AssertThrows<OverflowException>(() => CollectionsMemoryBlurArea(46341, 46341, 0));

            // StampBlit at extreme offsets: every dx/dy computation either lands
            // out of range or wraps in unchecked int arithmetic; the destination
            // must remain untouched and guards intact.
            int[] extremeOffsets = { int.MinValue, int.MinValue + 1, -1000000, 1000000, int.MaxValue - 1, int.MaxValue };
            uint[] stampSource = new uint[9];
            for (int i = 0; i < 9; i++) stampSource[i] = 0xFF000000u | (uint)(i + 1); // opaque: would write if in range
            uint[] stampDestination = CreateGuardedPixels(25, GuardPixelA, GuardPixelB);
            uint[] stampBaseline = new uint[25];
            for (int i = 0; i < 25; i++)
            {
                stampDestination[i + 1] = NextUInt(random);
                stampBaseline[i] = stampDestination[i + 1];
            }
            fixed (uint* sourceBase = stampSource)
            fixed (uint* destinationBase = stampDestination)
            {
                for (int i = 0; i < extremeOffsets.Length; i++)
                {
                    FastMemory.StampBlit(sourceBase, 3, 3, destinationBase + 1, 5, 5, extremeOffsets[i], 0);
                    FastMemory.StampBlit(sourceBase, 3, 3, destinationBase + 1, 5, 5, 0, extremeOffsets[i]);
                    FastMemory.StampBlit(sourceBase, 3, 3, destinationBase + 1, 5, 5, extremeOffsets[i], extremeOffsets[i]);
                }
            }
            AssertPixelGuards(stampDestination, GuardPixelA, GuardPixelB);
            for (int i = 0; i < 25; i++) AssertEqual(stampBaseline[i], stampDestination[i + 1]);

            // ScaleBlit extremes. float.Epsilon makes invScale infinite: every
            // sample becomes NaN/Infinity and must resolve to transparent, never a
            // wild read. A huge scale collapses every destination pixel onto the
            // source origin. Extreme offsets wrap in int arithmetic and must also
            // resolve to transparent.
            uint[] scaleSource = { 0xFF0000AAu, 0xFF0000BBu, 0xFF0000CCu, 0xFF0000DDu };
            uint[] scaleDestination = CreateGuardedPixels(16, GuardPixelB, GuardPixelA);
            fixed (uint* sourceBase = scaleSource)
            fixed (uint* destinationBase = scaleDestination)
            {
                for (int i = 0; i < 16; i++) scaleDestination[i + 1] = 0x12345678u;
                FastMemory.ScaleBlitNearestNeighbor(sourceBase, 2, 2, destinationBase + 1, 4, 4, float.Epsilon, 0, 0);
                for (int i = 0; i < 16; i++) AssertEqual(0u, scaleDestination[i + 1]);

                for (int i = 0; i < 16; i++) scaleDestination[i + 1] = 0x12345678u;
                FastMemory.ScaleBlitNearestNeighbor(sourceBase, 2, 2, destinationBase + 1, 4, 4, 1e9f, 0, 0);
                for (int i = 0; i < 16; i++) AssertEqual(scaleSource[0], scaleDestination[i + 1]);

                for (int i = 0; i < 16; i++) scaleDestination[i + 1] = 0x12345678u;
                FastMemory.ScaleBlitNearestNeighbor(sourceBase, 2, 2, destinationBase + 1, 4, 4, 1f, int.MinValue, 0);
                for (int i = 0; i < 16; i++) AssertEqual(0u, scaleDestination[i + 1]);

                for (int i = 0; i < 16; i++) scaleDestination[i + 1] = 0x12345678u;
                FastMemory.ScaleBlitNearestNeighbor(sourceBase, 2, 2, destinationBase + 1, 4, 4, 1f, 0, int.MaxValue);
                for (int i = 0; i < 16; i++) AssertEqual(0u, scaleDestination[i + 1]);
            }
            AssertPixelGuards(scaleDestination, GuardPixelB, GuardPixelA);

            // Copy with a non-power-of-two packed struct (7-byte stride): fields
            // survive byte-for-byte and the destination tail is preserved.
            CollectionsMemoryPackedTriple[] tripleSource = new CollectionsMemoryPackedTriple[3];
            for (int i = 0; i < 3; i++)
            {
                tripleSource[i] = new CollectionsMemoryPackedTriple
                {
                    A = (byte)(i + 1),
                    B = (ushort)(1000 + i),
                    C = 0xDEAD0000u + (uint)i
                };
            }
            CollectionsMemoryPackedTriple[] tripleDestination = new CollectionsMemoryPackedTriple[5];
            tripleDestination[3] = new CollectionsMemoryPackedTriple { A = 0x77, B = 0x8888, C = 0x99999999u };
            tripleDestination[4] = new CollectionsMemoryPackedTriple { A = 0x11, B = 0x2222, C = 0x33333333u };
            AssertEqual(7, sizeof(CollectionsMemoryPackedTriple));
            FastMemory.Copy(tripleSource, tripleDestination);
            for (int i = 0; i < 3; i++)
            {
                AssertEqual(tripleSource[i].A, tripleDestination[i].A);
                AssertEqual(tripleSource[i].B, tripleDestination[i].B);
                AssertEqual(tripleSource[i].C, tripleDestination[i].C);
            }
            AssertEqual((byte)0x77, tripleDestination[3].A);
            AssertEqual((ushort)0x8888, tripleDestination[3].B);
            AssertEqual(0x99999999u, tripleDestination[3].C);
            AssertEqual(0x33333333u, tripleDestination[4].C);

            // Read/Write2DArrayUnsafe use raw linear addressing with no bounds
            // checks by design: a negative x folds into the previous row. Pin the
            // exact linear index arithmetic (kept inside the array, so safe).
            int[] linear = new int[35];
            for (int i = 0; i < linear.Length; i++) linear[i] = i * 11;
            AssertEqual(11 * 11, FastMemory.Read2DArrayUnsafe(ref linear[0], 7, -3, 2)); // (2*7)-3 = 11
            AssertEqual(15 * 11, FastMemory.Read2DArrayUnsafe(ref linear[0], 7, 8, 1));  // (1*7)+8 = 15
            FastMemory.Write2DArrayUnsafe(ref linear[0], 7, -3, 2, -321);
            AssertEqual(-321, linear[11]);
            AssertEqual(-321, FastMemory.Read2DArrayUnsafe(ref linear[0], 7, 4, 1));     // canonical (x=4,y=1)
        }

        // ------------------------------------------------------------------
        // CollectionsMemory-private helpers
        // ------------------------------------------------------------------

        // Bit-exact replica of FastMemory.BlendColor(ref uint, uint) used as a
        // regression pin. Returns which branch was taken:
        // 0 = transparent source skip, 1 = opaque source replace,
        // 2 = transparent destination replace, 3 = opaque destination fast path,
        // 4 = general alpha compositing.
        private static int CollectionsMemoryReferenceBlendExact(ref uint destRgba, uint srcRgba)
        {
            byte srcA = (byte)(srcRgba >> 24);
            if (srcA == 0) return 0;
            if (srcA == 255)
            {
                destRgba = srcRgba;
                return 1;
            }
            byte destA = (byte)(destRgba >> 24);
            if (destA == 0)
            {
                destRgba = srcRgba;
                return 2;
            }

            byte srcR = (byte)srcRgba;
            byte srcG = (byte)(srcRgba >> 8);
            byte srcB = (byte)(srcRgba >> 16);
            byte destR = (byte)destRgba;
            byte destG = (byte)(destRgba >> 8);
            byte destB = (byte)(destRgba >> 16);
            const uint fullAlpha = 255u;
            const uint halfAlpha = 127u;
            uint invAlpha = fullAlpha - srcA;

            if (destA == 255)
            {
                uint outR = ((uint)(srcR * srcA) + destR * invAlpha + halfAlpha) / fullAlpha;
                uint outG = ((uint)(srcG * srcA) + destG * invAlpha + halfAlpha) / fullAlpha;
                uint outB = ((uint)(srcB * srcA) + destB * invAlpha + halfAlpha) / fullAlpha;
                destRgba = outR | (outG << 8) | (outB << 16) | (fullAlpha << 24);
                return 3;
            }

            uint alphaNumerator = srcA * fullAlpha + destA * invAlpha;
            uint outAlpha = (alphaNumerator + halfAlpha) / fullAlpha;
            uint channelR = ((uint)(srcR * srcA) * fullAlpha + (uint)(destR * destA) * invAlpha + (alphaNumerator >> 1)) / alphaNumerator;
            uint channelG = ((uint)(srcG * srcA) * fullAlpha + (uint)(destG * destA) * invAlpha + (alphaNumerator >> 1)) / alphaNumerator;
            uint channelB = ((uint)(srcB * srcA) * fullAlpha + (uint)(destB * destA) * invAlpha + (alphaNumerator >> 1)) / alphaNumerator;
            destRgba = channelR | (channelG << 8) | (channelB << 16) | (outAlpha << 24);
            return 4;
        }

        // Bit-exact replica of FastMemory.BlendColor(ref uint, uint, byte).
        private static void CollectionsMemoryReferenceBlendOpacityExact(ref uint destRgba, uint srcRgba, byte opacity)
        {
            if (opacity == 0) return;
            if (opacity != 255)
            {
                uint srcAlpha = srcRgba >> 24;
                uint adjustedAlpha = (srcAlpha * opacity + 127u) / 255u;
                srcRgba = (srcRgba & 0x00FFFFFFu) | (adjustedAlpha << 24);
            }
            CollectionsMemoryReferenceBlendExact(ref destRgba, srcRgba);
        }

        private static unsafe void CollectionsMemoryBlurArea(int width, int height, int radius)
        {
            uint source = 0;
            uint destination = 0;
            FastEffects.BoxBlur(&source, &destination, width, height, radius);
        }

        private sealed class CollectionsMemoryFactoryException : Exception
        {
        }

        private sealed class CollectionsMemoryPoolNode
        {
            public int Id { get; }

            public CollectionsMemoryPoolNode(int id)
            {
                Id = id;
            }
        }

        // ------------------------------------------------------------------
        // FastGridMap bulk editing primitives (batch1/backend-core)
        // ------------------------------------------------------------------
        private static void TestCollectionsMemoryGridBulkOps()
        {
            // TrySetTile reports exactly what SetTile silently drops; SetTile keeps
            // its silently-clamping compatibility contract by routing through it.
            FastGridMap map = new FastGridMap(6, 4);
            Assert(map.TrySetTile(0, 0, 5));
            Assert(map.TrySetTile(5, 3, 6));
            Assert(!map.TrySetTile(-1, 0, 7));
            Assert(!map.TrySetTile(6, 0, 7));
            Assert(!map.TrySetTile(0, 4, 7));
            Assert(!map.TrySetTile(int.MinValue, int.MaxValue, 7));
            AssertEqual((short)5, map.GetTile(0, 0));
            AssertEqual((short)6, map.GetTile(5, 3));
            map.SetTile(-1, 0, 9);
            map.SetTile(0, 0, 9);
            AssertEqual(BackendConfig.Collections.EmptyTileId, map.GetTile(-1, 0));
            AssertEqual((short)9, map.GetTile(0, 0));

            // FillRect against a reference double loop, covering clamped, degenerate,
            // fully-outside, and coordinate-overflow rectangles.
            (int X, int Y, int W, int H, short Id)[] fillCases =
            {
                (-2, -1, 5, 3, (short)11),
                (0, 0, 6, 4, (short)12),
                (5, 3, 10, 10, (short)13),
                (6, 4, 3, 3, (short)14),
                (2, 1, 0, 5, (short)15),
                (int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, (short)16),
                (-5, -5, int.MaxValue, int.MaxValue, (short)17),
                (int.MinValue, 0, 3, 2, (short)18)
            };
            FastGridMap fillMap = new FastGridMap(6, 4);
            short[] fillModel = new short[6 * 4];
            for (int caseIndex = 0; caseIndex < fillCases.Length; caseIndex++)
            {
                (int x, int y, int w, int h, short id) = fillCases[caseIndex];
                int expectedCount = 0;
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 6; column++)
                    {
                        bool inside = (long)column >= x && (long)column < (long)x + w &&
                            (long)row >= y && (long)row < (long)y + h;
                        if (!inside) continue;
                        fillModel[row * 6 + column] = id;
                        expectedCount++;
                    }
                }
                AssertEqual(expectedCount, fillMap.FillRect(x, y, w, h, id));
                for (int cell = 0; cell < fillModel.Length; cell++) AssertEqual(fillModel[cell], fillMap.RawData[cell]);
            }
            AssertThrows<ArgumentOutOfRangeException>(() => fillMap.FillRect(0, 0, -1, 2, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => fillMap.FillRect(0, 0, 2, -1, 1));

            // CopyRegion has memmove semantics: overlapping copies behave as if the
            // source rectangle had been snapshotted first. Cell pairs where either
            // endpoint is off-map are skipped. Verified against a snapshot reference
            // for every shift direction, including overlap and partial clipping.
            (int SrcX, int SrcY, int DstX, int DstY, int W, int H)[] copyCases =
            {
                (0, 0, 3, 2, 3, 2),   // disjoint
                (1, 1, 2, 1, 4, 3),   // overlapping shift right
                (2, 1, 1, 1, 4, 3),   // overlapping shift left
                (1, 0, 1, 1, 4, 3),   // overlapping shift down
                (1, 1, 1, 0, 4, 3),   // overlapping shift up
                (2, 2, 3, 3, 2, 2),   // overlapping diagonal
                (-2, -1, 0, 0, 4, 3), // source partially off-map (skipped pairs)
                (0, 0, 4, 2, 4, 3),   // destination partially off-map (skipped pairs)
                (0, 0, 0, 0, 3, 3),   // same position: no-op
                (0, 0, 1, 1, 0, 4)    // zero width: no-op
            };
            Random gridRandom = new Random(0x6B1D);
            for (int caseIndex = 0; caseIndex < copyCases.Length; caseIndex++)
            {
                (int srcX, int srcY, int dstX, int dstY, int w, int h) = copyCases[caseIndex];
                FastGridMap copyMap = new FastGridMap(7, 5);
                short[] copyModel = new short[7 * 5];
                for (int cell = 0; cell < copyModel.Length; cell++)
                {
                    copyModel[cell] = (short)gridRandom.Next(-99, 100);
                    copyMap.RawData[cell] = copyModel[cell];
                }
                short[] snapshot = (short[])copyModel.Clone();
                int expectedCopied = 0;
                bool samePosition = srcX == dstX && srcY == dstY;
                if (!samePosition)
                {
                    for (int row = 0; row < h; row++)
                    {
                        for (int column = 0; column < w; column++)
                        {
                            int fromX = srcX + column;
                            int fromY = srcY + row;
                            int toX = dstX + column;
                            int toY = dstY + row;
                            if ((uint)fromX >= 7u || (uint)fromY >= 5u) continue;
                            if ((uint)toX >= 7u || (uint)toY >= 5u) continue;
                            copyModel[toY * 7 + toX] = snapshot[fromY * 7 + fromX];
                            expectedCopied++;
                        }
                    }
                }
                AssertEqual(expectedCopied, copyMap.CopyRegion(srcX, srcY, dstX, dstY, w, h));
                for (int cell = 0; cell < copyModel.Length; cell++) AssertEqual(copyModel[cell], copyMap.RawData[cell]);
            }
            FastGridMap copyGuards = new FastGridMap(3, 3);
            AssertThrows<ArgumentOutOfRangeException>(() => copyGuards.CopyRegion(0, 0, 1, 1, -1, 2));
            AssertThrows<ArgumentOutOfRangeException>(() => copyGuards.CopyRegion(0, 0, 1, 1, 2, -1));

            // FloodFillTiles against a reference BFS on randomized small maps, plus the
            // documented no-op cases and a serpentine corridor forcing stack growth.
            FastGridMap floodTrivial = new FastGridMap(4, 4);
            AssertEqual(16, floodTrivial.FloodFillTiles(1, 1, 3));
            for (int cell = 0; cell < floodTrivial.RawData.Length; cell++) AssertEqual((short)3, floodTrivial.RawData[cell]);
            AssertEqual(0, floodTrivial.FloodFillTiles(-1, 0, 4));
            AssertEqual(0, floodTrivial.FloodFillTiles(0, 4, 4));
            AssertEqual(0, floodTrivial.FloodFillTiles(1, 1, 3)); // start already replacement id
            for (int iteration = 0; iteration < 20; iteration++)
            {
                int width = 2 + gridRandom.Next(11);
                int height = 2 + gridRandom.Next(9);
                FastGridMap floodMap = new FastGridMap(width, height);
                for (int cell = 0; cell < floodMap.RawData.Length; cell++)
                    floodMap.RawData[cell] = (short)gridRandom.Next(3);
                short[] floodModel = (short[])floodMap.RawData.Clone();
                int startX = gridRandom.Next(width);
                int startY = gridRandom.Next(height);
                short replacement = (short)(10 + gridRandom.Next(3));
                int expectedChanged = CollectionsMemoryReferenceFloodFill(floodModel, width, height, startX, startY, replacement);
                AssertEqual(expectedChanged, floodMap.FloodFillTiles(startX, startY, replacement));
                for (int cell = 0; cell < floodModel.Length; cell++) AssertEqual(floodModel[cell], floodMap.RawData[cell]);
            }
            FastGridMap serpentine = new FastGridMap(64, 64);
            for (int cell = 0; cell < serpentine.RawData.Length; cell++) serpentine.RawData[cell] = 1;
            for (int wallRow = 1; wallRow < 64; wallRow += 2)
            {
                int gapColumn = (wallRow / 2) % 2 == 0 ? 63 : 0;
                for (int column = 0; column < 64; column++)
                {
                    if (column != gapColumn) serpentine.RawData[wallRow * 64 + column] = 2;
                }
            }
            short[] serpentineModel = (short[])serpentine.RawData.Clone();
            int serpentineExpected = CollectionsMemoryReferenceFloodFill(serpentineModel, 64, 64, 0, 0, 5);
            AssertEqual(serpentineExpected, serpentine.FloodFillTiles(0, 0, 5));
            for (int cell = 0; cell < serpentineModel.Length; cell++) AssertEqual(serpentineModel[cell], serpentine.RawData[cell]);

            // Resize preserves the overlap (shrink = crop, grow = pad with the empty
            // tile) and duplicates props as fresh instances tracked by the NEW map.
            FastGridMap resizeSource = new FastGridMap(4, 3);
            for (int cell = 0; cell < resizeSource.RawData.Length; cell++) resizeSource.RawData[cell] = (short)(cell + 1);
            FastGridMap.MapPropData resizeProp = new FastGridMap.MapPropData { AssetKey = "tree", X = 3, Y = 2, Scale = 1.5f };
            resizeSource.Props.Add(resizeProp);
            FastGridMap grown = resizeSource.Resize(6, 5);
            AssertEqual(6, grown.Width);
            AssertEqual(5, grown.Height);
            for (int row = 0; row < 5; row++)
            {
                for (int column = 0; column < 6; column++)
                {
                    short expected = row < 3 && column < 4 ? (short)(row * 4 + column + 1) : BackendConfig.Collections.EmptyTileId;
                    AssertEqual(expected, grown.GetTile(column, row));
                }
            }
            AssertEqual(1, grown.Props.Count);
            Assert(!ReferenceEquals(resizeProp, grown.Props[0]));
            AssertEqual("tree", grown.Props[0].AssetKey);
            AssertEqual(3, grown.Props[0].X);
            AssertEqual(2, grown.Props[0].Y);
            AssertEqual(1.5f, grown.Props[0].Scale);
            AssertEqual(0, grown.Props[0].ActiveListIndex);
            AssertEqual(0, resizeProp.ActiveListIndex); // original list untouched
            AssertEqual(1, resizeSource.Props.Count);
            FastGridMap shrunk = resizeSource.Resize(2, 2);
            AssertEqual((short)1, shrunk.GetTile(0, 0));
            AssertEqual((short)2, shrunk.GetTile(1, 0));
            AssertEqual((short)5, shrunk.GetTile(0, 1));
            AssertEqual((short)6, shrunk.GetTile(1, 1));
            AssertThrows<ArgumentOutOfRangeException>(() => resizeSource.Resize(0, 5));
            AssertThrows<ArgumentOutOfRangeException>(() => resizeSource.Resize(5, -1));
        }

        private static int CollectionsMemoryReferenceFloodFill(
            short[] model, int width, int height, int startX, int startY, short replacement)
        {
            if ((uint)startX >= (uint)width || (uint)startY >= (uint)height) return 0;
            short target = model[startY * width + startX];
            if (target == replacement) return 0;
            int[] neighborDeltaX = { -1, 1, 0, 0 };
            int[] neighborDeltaY = { 0, 0, -1, 1 };
            Queue<(int X, int Y)> frontier = new Queue<(int X, int Y)>();
            frontier.Enqueue((startX, startY));
            model[startY * width + startX] = replacement;
            int changed = 1;
            while (frontier.Count > 0)
            {
                (int x, int y) = frontier.Dequeue();
                for (int neighbor = 0; neighbor < neighborDeltaX.Length; neighbor++)
                {
                    int nx = x + neighborDeltaX[neighbor];
                    int ny = y + neighborDeltaY[neighbor];
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                    if (model[ny * width + nx] != target) continue;
                    model[ny * width + nx] = replacement;
                    changed++;
                    frontier.Enqueue((nx, ny));
                }
            }
            return changed;
        }

        // ------------------------------------------------------------------
        // FastPoolRegistry locking and first-registration-wins (batch1/backend-core)
        // ------------------------------------------------------------------
        private static void TestCollectionsMemoryPoolRegistryLocking()
        {
            // First registration wins; a duplicate key with a different capacity is
            // ignored (observable through exhaustion of the ORIGINAL capacity).
            FastPoolRegistry<CollectionsMemoryNodeA>.Clear();
            FastPoolRegistry<CollectionsMemoryNodeA>.RegisterPool(1, 1, () => new CollectionsMemoryNodeA());
            FastPoolRegistry<CollectionsMemoryNodeA>.RegisterPool(1, 2, () => new CollectionsMemoryNodeA());
            CollectionsMemoryNodeA onlyItem = FastPoolRegistry<CollectionsMemoryNodeA>.Rent(1);
            Assert(onlyItem != null);
            AssertEqual<CollectionsMemoryNodeA>(null, FastPoolRegistry<CollectionsMemoryNodeA>.Rent(1));
            FastPoolRegistry<CollectionsMemoryNodeA>.Return(1, onlyItem);

            // The registry map is guarded by a lock: concurrent registration of the
            // same key set from many threads must neither throw nor lose a key. The
            // per-thread schedule is nondeterministic but the asserted outcome is not.
            FastPoolRegistry<CollectionsMemoryNodeA>.Clear();
            const int threadCount = 8;
            const int keyCount = 200;
            Thread[] threads = new Thread[threadCount];
            Exception[] threadFailures = new Exception[threadCount];
            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
            {
                int capturedIndex = threadIndex;
                threads[threadIndex] = new Thread(() =>
                {
                    try
                    {
                        for (int key = 0; key < keyCount; key++)
                        {
                            FastPoolRegistry<CollectionsMemoryNodeA>.RegisterPool(key, 1, () => new CollectionsMemoryNodeA());
                        }
                    }
                    catch (Exception exception)
                    {
                        threadFailures[capturedIndex] = exception;
                    }
                });
            }
            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++) threads[threadIndex].Start();
            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++) threads[threadIndex].Join();
            for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
                AssertEqual<Exception>(null, threadFailures[threadIndex]);
            for (int key = 0; key < keyCount; key++)
                Assert(FastPoolRegistry<CollectionsMemoryNodeA>.Prewarm(key, 1));
            Assert(!FastPoolRegistry<CollectionsMemoryNodeA>.Prewarm(keyCount, 1));
            FastPoolRegistry<CollectionsMemoryNodeA>.Clear();
            Assert(!FastPoolRegistry<CollectionsMemoryNodeA>.Prewarm(0, 1));
        }

        private sealed class CollectionsMemoryNodeA
        {
        }

        private sealed class CollectionsMemoryNodeB
        {
        }

        private sealed class CollectionsMemoryTrackable : IFastListTrackable
        {
            public int ActiveListIndex { get; set; } = BackendConfig.Collections.UnregisteredListIndex;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CollectionsMemoryPackedTriple
        {
            public byte A;
            public ushort B;
            public uint C;
        }
    }
}
