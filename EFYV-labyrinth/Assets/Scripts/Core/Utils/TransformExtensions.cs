using UnityEngine;
using EFYVBackend.Core.Physics;
using System.Runtime.CompilerServices;

namespace EFYV.Core.Utils
{
    public static class TransformExtensions
    {
        // Centralizes planar translation while preserving the transform's Z coordinate.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ApplyFastTranslation(this Transform transform, float dx, float dy, float speed, float deltaTime)
        {
            Vector3 pos = transform.position;
            float px = pos.x;
            float py = pos.y;
            
            FastPhysics.CalculateTranslation(dx, dy, speed, deltaTime, ref px, ref py);
            
            transform.position = new Vector3(px, py, pos.z);
        }
    }
}
