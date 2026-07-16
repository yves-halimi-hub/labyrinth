using System.Runtime.CompilerServices;

namespace EFYVBackend.Core.Physics
{
    public static class FastPhysics
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CalculateTranslation(float dirX, float dirY, float speed, float deltaTime, ref float posX, ref float posY)
        {
            float scalar = speed * deltaTime;
            posX += dirX * scalar;
            posY += dirY * scalar;
        }
    }
}
