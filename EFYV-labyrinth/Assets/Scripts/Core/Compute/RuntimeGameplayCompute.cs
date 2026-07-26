using System;
using Efyv.RuntimeKernel;
using EFYV.Core.Entities;
using UnityEngine;

namespace EFYV.Core.Compute
{
    /// <summary>
    /// Main-thread bridge from Unity-owned objects to the official native Runtime
    /// Kernel batch API. Native code receives only blittable snapshots and stable
    /// batch-local indexes; Unity references and gameplay mutations stay in C#.
    /// </summary>
    internal static class RuntimeGameplayCompute
    {
        private const uint EnemyItemFlag = 1u;
        private const float MinimumSpatialCellSize = 1f;
        private const int NativeDirectItemCapacity = 64;

        private static RuntimeSpatialItem2d[] items = Array.Empty<RuntimeSpatialItem2d>();
        private static Enemy[] enemiesByItemIndex = Array.Empty<Enemy>();
        private static RuntimeSpatialQuery2d[] queries = Array.Empty<RuntimeSpatialQuery2d>();
        private static RuntimeSpatialHit2d[] hits = Array.Empty<RuntimeSpatialHit2d>();
        private static int[] queryHitStarts = Array.Empty<int>();
        private static int[] queryHitEnds = Array.Empty<int>();
        private static byte[] scratch = Array.Empty<byte>();
        private static int scratchItemCapacity;
        private static int hitCount;
        private static int queryCount;
        private static bool usedUniformGrid;

        internal static bool IsNativeAvailable => Kernel.IsNativeAvailable;
        internal static int HitCount => hitCount;
        internal static int QueryCount => queryCount;
        internal static bool UsedUniformGrid => usedUniformGrid;

        internal static void SinCosRadians(
            ReadOnlySpan<float> radians,
            Span<float> sines,
            Span<float> cosines)
        {
            // One pinned native call for the whole gameplay group. The Runtime
            // Kernel performs shared range reduction and geometry-grade sin/cos.
            Kernel.SinCosGeometry(radians, sines, cosines);
        }

        internal static float NormalizeRadians(float radians)
        {
            // SinCosGeometry deliberately accepts only [-pi, pi]. Gameplay
            // owns periodic angle state, so reduce it once before batching;
            // the native kernel then performs the accuracy-critical quadrant
            // reduction and paired polynomial evaluation.
            float twoPi =
                EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Math.TwoPI;
            float pi = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Math.PI;
            float normalized = radians % twoPi;
            if (normalized > pi)
            {
                normalized -= twoPi;
            }
            else if (normalized < -pi)
            {
                normalized += twoPi;
            }
            return normalized;
        }

        internal static Enemy FindNearestEnemy(Vector3 origin)
        {
            int itemCount = CaptureEnemySnapshot();
            ResetResultState(1);
            if (itemCount == 0)
            {
                return null;
            }

            EnsureQueryCapacity(1);
            queries[0] = new RuntimeSpatialQuery2d(
                0,
                origin.x,
                origin.y,
                0f,
                EnemyItemFlag,
                RuntimeSpatialQueryKind.Nearest);

            EnsureScratchCapacity(itemCount);
            EnsureHitCapacity(1);
            RuntimeSpatialQueryResult result = Kernel.SpatialQueryBatch(
                items.AsSpan(0, itemCount),
                queries.AsSpan(0, 1),
                MinimumSpatialCellSize,
                scratch,
                hits.AsSpan(0, 1));
            hitCount = checked((int)result.HitCount);
            usedUniformGrid = result.UsedUniformGrid != 0;
            return hitCount == 0 ? null : EnemyAtHit(0);
        }

        internal static int QueryEnemyRadius(Vector3 center, float radius)
        {
            int itemCount = CaptureEnemySnapshot();
            ResetResultState(1);
            if (itemCount == 0)
            {
                return 0;
            }

            EnsureQueryCapacity(1);
            queries[0] = RadiusQuery(center, radius);
            RunRadiusBatch(itemCount, 1);
            return hitCount;
        }

        internal static int QueryEnemyRadii(
            ReadOnlySpan<Vector3> centers,
            float radius)
        {
            int requestedQueryCount = centers.Length;
            ResetResultState(requestedQueryCount);
            if (requestedQueryCount == 0)
            {
                return 0;
            }

            int itemCount = CaptureEnemySnapshot();
            if (itemCount == 0)
            {
                return 0;
            }

            EnsureQueryCapacity(requestedQueryCount);
            for (int index = 0; index < requestedQueryCount; index++)
            {
                queries[index] = RadiusQuery(centers[index], radius);
            }

            RunRadiusBatch(itemCount, requestedQueryCount);
            return hitCount;
        }

        internal static int QueryHitStart(int queryIndex)
        {
            if ((uint)queryIndex >= (uint)queryCount)
            {
                throw new ArgumentOutOfRangeException(nameof(queryIndex));
            }
            return queryHitStarts[queryIndex];
        }

        internal static int QueryHitEnd(int queryIndex)
        {
            if ((uint)queryIndex >= (uint)queryCount)
            {
                throw new ArgumentOutOfRangeException(nameof(queryIndex));
            }
            return queryHitEnds[queryIndex];
        }

        internal static Enemy EnemyAtHit(int hitIndex)
        {
            if ((uint)hitIndex >= (uint)hitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(hitIndex));
            }

            RuntimeSpatialHit2d hit = hits[hitIndex];
            int itemIndex = checked((int)hit.ItemIndex);
            if ((uint)itemIndex >= (uint)enemiesByItemIndex.Length ||
                hit.StableId != StableIdForItem(itemIndex))
            {
                throw new InvalidOperationException(
                    "Runtime Kernel returned an invalid enemy snapshot index.");
            }
            return enemiesByItemIndex[itemIndex];
        }

        private static RuntimeSpatialQuery2d RadiusQuery(Vector3 center, float radius)
        {
            return new RuntimeSpatialQuery2d(
                0,
                center.x,
                center.y,
                radius,
                EnemyItemFlag,
                RuntimeSpatialQueryKind.Radius);
        }

        private static int CaptureEnemySnapshot()
        {
            int count = Enemy.ActiveEnemies.Count;
            EnsureItemCapacity(count);
            for (int index = 0; index < count; index++)
            {
                Enemy enemy = Enemy.ActiveEnemies[index];
                Vector3 position = enemy.entityTransform.position;
                enemiesByItemIndex[index] = enemy;
                items[index] = new RuntimeSpatialItem2d(
                    StableIdForItem(index),
                    position.x,
                    position.y,
                    0f,
                    EnemyItemFlag);
            }
            return count;
        }

        private static ulong StableIdForItem(int itemIndex)
        {
            // Zero means "exclude nobody" in gameplay queries. A one-based packed
            // index preserves the old first-registration tie break and remains
            // stable for the duration of the native call even if later damage
            // swap-removes an enemy from the live list.
            return checked((ulong)itemIndex + 1UL);
        }

        private static void RunRadiusBatch(int itemCount, int requestedQueryCount)
        {
            EnsureScratchCapacity(itemCount);
            int requiredHitCapacity = checked(itemCount * requestedQueryCount);
            EnsureHitCapacity(requiredHitCapacity);
            RuntimeSpatialQueryResult result = Kernel.SpatialQueryBatch(
                items.AsSpan(0, itemCount),
                queries.AsSpan(0, requestedQueryCount),
                SpatialCellSizeForRadius(queries[0].Radius),
                scratch,
                hits.AsSpan(0, requiredHitCapacity));
            hitCount = checked((int)result.HitCount);
            usedUniformGrid = result.UsedUniformGrid != 0;
            IndexHitsByQuery(requestedQueryCount);
        }

        private static float SpatialCellSizeForRadius(float radius)
        {
            // Matching the cell edge to a common query diameter is unnecessary:
            // radius-sized cells cap ordinary queries at roughly a 3x3 visit.
            // Zero-radius point queries retain a finite one-unit grid.
            return radius > MinimumSpatialCellSize ? radius : MinimumSpatialCellSize;
        }

        private static void IndexHitsByQuery(int requestedQueryCount)
        {
            EnsureQueryRangeCapacity(requestedQueryCount);
            int cursor = 0;
            for (int queryIndex = 0; queryIndex < requestedQueryCount; queryIndex++)
            {
                queryHitStarts[queryIndex] = cursor;
                while (cursor < hitCount &&
                    checked((int)hits[cursor].QueryIndex) == queryIndex)
                {
                    cursor++;
                }
                queryHitEnds[queryIndex] = cursor;
            }

            if (cursor != hitCount)
            {
                throw new InvalidOperationException(
                    "Runtime Kernel returned non-grouped spatial hits.");
            }
        }

        private static void ResetResultState(int requestedQueryCount)
        {
            hitCount = 0;
            queryCount = requestedQueryCount;
            usedUniformGrid = false;
            EnsureQueryRangeCapacity(requestedQueryCount);
            for (int index = 0; index < requestedQueryCount; index++)
            {
                queryHitStarts[index] = 0;
                queryHitEnds[index] = 0;
            }
        }

        private static void EnsureScratchCapacity(int requiredItemCount)
        {
            if (requiredItemCount <= scratchItemCapacity)
            {
                return;
            }

            int requestedCapacity = requiredItemCount <= NativeDirectItemCapacity
                ? NativeDirectItemCapacity
                : NextCapacity(requiredItemCount);
            nuint requiredBytes = Kernel.SpatialQueryScratchSize((nuint)requestedCapacity);
            if (requiredBytes > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "Runtime Kernel spatial scratch exceeds managed array limits.");
            }

            int byteCount = (int)requiredBytes;
            if (scratch.Length < byteCount)
            {
                scratch = new byte[byteCount];
            }
            scratchItemCapacity = requestedCapacity;
        }

        private static void EnsureItemCapacity(int required)
        {
            if (items.Length >= required)
            {
                return;
            }
            int capacity = NextCapacity(required);
            Array.Resize(ref items, capacity);
            Array.Resize(ref enemiesByItemIndex, capacity);
        }

        private static void EnsureQueryCapacity(int required)
        {
            if (queries.Length < required)
            {
                Array.Resize(ref queries, NextCapacity(required));
            }
        }

        private static void EnsureHitCapacity(int required)
        {
            if (hits.Length < required)
            {
                Array.Resize(ref hits, NextCapacity(required));
            }
        }

        private static void EnsureQueryRangeCapacity(int required)
        {
            if (queryHitStarts.Length >= required)
            {
                return;
            }
            int capacity = NextCapacity(required);
            Array.Resize(ref queryHitStarts, capacity);
            Array.Resize(ref queryHitEnds, capacity);
        }

        private static int NextCapacity(int required)
        {
            if (required <= 0)
            {
                return 0;
            }

            int capacity = 4;
            while (capacity < required)
            {
                capacity = checked(capacity * 2);
            }
            return capacity;
        }
    }
}
