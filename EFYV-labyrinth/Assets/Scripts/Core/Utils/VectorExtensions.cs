using UnityEngine;
using EFYVBackend.Core.Math;
using System.Runtime.CompilerServices;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Utils
{
    public static class VectorExtensions
    {
        // Extension method that wraps the fast backend normalization and prevents repeated code
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 FastNormalized(this Vector2 vector)
        {
            float dx = vector.x;
            float dy = vector.y;
            FastMath.FastNormalize(ref dx, ref dy);
            return new Vector2(dx, dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 FastNormalized(this Vector3 vector)
        {
            float dx = vector.x;
            float dy = vector.y;
            FastMath.FastNormalize(ref dx, ref dy);
            return new Vector3(dx, dy, vector.z); // Keeps Z untouched, perfect for 2D
        }

        // Compares planar distance without a square root.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastSqrDistance(this Vector3 a, Vector3 b)
        {
            return FastMath.DistanceSqr(a.x, a.y, b.x, b.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastSqrDistance(this Vector2 a, Vector2 b)
        {
            return FastMath.DistanceSqr(a.x, a.y, b.x, b.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 GetRandomOffset(float radius, float zOffset = GameConfig.EnvironmentData.PlanarZOffset)
        {
            FastMath.GetRandomOffset2D(radius, out float randX, out float randY);
            return new Vector3(randX, randY, zOffset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 GetRandomOffset2D(float radius)
        {
            FastMath.GetRandomOffset2D(radius, out float randX, out float randY);
            return new Vector2(randX, randY);
        }
    }
}
