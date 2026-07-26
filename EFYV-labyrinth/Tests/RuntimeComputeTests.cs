using System;
using System.Collections.Generic;
using EFYV.Core.Compute;
using EFYV.Core.Entities;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;

internal static partial class Program
{
    private static void TestRuntimeKernelGameplayBatches()
    {
        Check(RuntimeGameplayCompute.IsNativeAvailable,
            "Gameplay geometry/spatial work must not silently fall back to managed code.");

        // Geometry-grade paired trigonometry: periodic state is normalized by
        // the gameplay bridge, then one native call evaluates the whole span.
        float[] sourceAngles =
        {
            0f,
            Config.Backend.Math.PI * 0.5f,
            Config.Backend.Math.PI,
            -Config.Backend.Math.PI,
            19f * Config.Backend.Math.PI,
            -12345.625f
        };
        var normalized = new float[sourceAngles.Length];
        var sines = new float[sourceAngles.Length];
        var cosines = new float[sourceAngles.Length];
        for (int index = 0; index < sourceAngles.Length; index++)
        {
            normalized[index] =
                RuntimeGameplayCompute.NormalizeRadians(sourceAngles[index]);
        }
        RuntimeGameplayCompute.SinCosRadians(normalized, sines, cosines);
        for (int index = 0; index < normalized.Length; index++)
        {
            Near(MathF.Sin(normalized[index]), sines[index], 1e-6f);
            Near(MathF.Cos(normalized[index]), cosines[index], 1e-6f);
        }

        // Equal-distance nearest ties retain packed registration order through
        // one-based stable snapshot ids.
        ProbeEnemy first = SpawnEnemy(1f, 0f, 100f);
        ProbeEnemy second = SpawnEnemy(-1f, 0f, 100f);
        Same(first, RuntimeGameplayCompute.FindNearestEnemy(Vector3.zero));

        // More than 64 enemies forces the Runtime Kernel's uniform-grid radius
        // path. Boundary containment and per-query item ordering are compared
        // to a simple point-center oracle.
        for (int index = 0; index < 78; index++)
        {
            float x = 10f + (index % 20);
            float y = -12f + (index / 20);
            SpawnEnemy(x, y, 100f);
        }
        ProbeEnemy boundary = SpawnEnemy(2.5f, 0f, 100f);
        ProbeEnemy outside = SpawnEnemy(2.5001f, 0f, 100f);

        // Collider/hurtbox dimensions are intentionally not an implicit
        // weapon-radius term: gameplay range remains transform-center based.
        ProbeEnemy largeColliderOutside = SpawnEnemy(4f, 0f, 100f);
        var largeCollider = largeColliderOutside.gameObject.AddComponent<BoxCollider2D>();
        largeCollider.size = new Vector2(100f, 100f);

        Vector3[] centers =
        {
            Vector3.zero,
            new Vector3(20f, -10f, 99f),
            new Vector3(27f, -9f, -99f)
        };
        const float radius = 2.5f;
        RuntimeGameplayCompute.QueryEnemyRadii(centers, radius);
        Check(RuntimeGameplayCompute.UsedUniformGrid,
            "The >64-item radius golden must exercise the indexed native path.");

        float squaredRadius = radius * radius;
        for (int queryIndex = 0; queryIndex < centers.Length; queryIndex++)
        {
            var expected = new List<Enemy>();
            for (int itemIndex = 0; itemIndex < Enemy.ActiveEnemies.Count; itemIndex++)
            {
                Enemy enemy = Enemy.ActiveEnemies[itemIndex];
                Vector3 position = enemy.entityTransform.position;
                float dx = position.x - centers[queryIndex].x;
                float dy = position.y - centers[queryIndex].y;
                if ((dx * dx) + (dy * dy) <= squaredRadius)
                {
                    expected.Add(enemy);
                }
            }

            int start = RuntimeGameplayCompute.QueryHitStart(queryIndex);
            int end = RuntimeGameplayCompute.QueryHitEnd(queryIndex);
            Equal(expected.Count, end - start);
            for (int index = 0; index < expected.Count; index++)
            {
                Same(expected[index], RuntimeGameplayCompute.EnemyAtHit(start + index));
            }
        }

        int firstStart = RuntimeGameplayCompute.QueryHitStart(0);
        int firstEnd = RuntimeGameplayCompute.QueryHitEnd(0);
        bool sawBoundary = false;
        bool sawOutside = false;
        bool sawLargeCollider = false;
        for (int hitIndex = firstStart; hitIndex < firstEnd; hitIndex++)
        {
            Enemy enemy = RuntimeGameplayCompute.EnemyAtHit(hitIndex);
            sawBoundary |= ReferenceEquals(enemy, boundary);
            sawOutside |= ReferenceEquals(enemy, outside);
            sawLargeCollider |= ReferenceEquals(enemy, largeColliderOutside);
        }
        Check(sawBoundary, "Radius contact is inclusive.");
        Check(!sawOutside, "A center just outside radius must be excluded.");
        Check(!sawLargeCollider,
            "A large authored/runtime collider must not silently enlarge weapon reach.");
    }
}
