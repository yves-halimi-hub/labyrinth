using UnityEngine;
using EFYV.Core.Entities;
using EFYVBackend.Core.Math;
using EFYV.Core.Utils;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Types
{
    // Projectiles that mathematically orbit the player (e.g. Spinning Axes, Beyblades)
    public abstract class OrbitalWeapon : Weapon
    {
        public Transform[] visualSprites;
        
        public float orbitRadius 
        { 
            get => Data.OrbitRadius; 
            set => Data.OrbitRadius = value; 
        }
        public float rotationSpeed 
        { 
            get => Data.RotationSpeed; 
            set => Data.RotationSpeed = value; 
        } // degrees per second
        public int projectileCount 
        { 
            get => Data.ProjectileCount; 
            set => Data.ProjectileCount = value; 
        }
        public float damageRadius 
        { 
            get => Data.DamageRadius; 
            set => Data.DamageRadius = value; 
        }

        protected override void Awake()
        {
            base.Awake();
            // Set defaults in the schema
            orbitRadius = GameConfig.Weapons.Orbital.DefaultOrbitRadius;
            rotationSpeed = GameConfig.Weapons.Orbital.DefaultRotationSpeed;
            projectileCount = GameConfig.Weapons.Orbital.DefaultProjectileCount;
            damageRadius = GameConfig.Weapons.Orbital.DefaultDamageRadius;
            currentAngle = GameConfig.Weapons.Orbital.InitialAngle;
        }

        private float currentAngle 
        { 
            get => Data.CurrentAngle; 
            set => Data.CurrentAngle = value; 
        }

        public override void Tick(float deltaTime)
        {
            // Orbital weapons usually don't have a "cooldown", they just spin constantly and damage things they touch.
            // Record the tick's deltaTime so Fire scales contact damage by the same
            // clock that drives rotation (never the global Time.deltaTime).
            TickDeltaTime = deltaTime;
            currentAngle += rotationSpeed * deltaTime;
            if (currentAngle >= GameConfig.Weapons.Orbital.FullCircleDegrees) currentAngle -= GameConfig.Weapons.Orbital.FullCircleDegrees;

            Fire();
        }

        public override void Fire()
        {
            if (projectileCount <= GameConfig.Runtime.EmptyCollectionCount) return;

            Vector3 center = transform.position;
            float angleStep = GameConfig.Weapons.Orbital.FullCircleDegrees / projectileCount;

            float sqrDamageRadius = damageRadius * damageRadius;
            float frameDamage = BaseDamage * TickDeltaTime;

            for (int p = 0; p < projectileCount; p++)
            {
                float angle = currentAngle + (p * angleStep);

                // PERFORMANCE: C-Optimized Taylor Series FastCos/FastSin from the backend
                FastMath.FastSinCosTaylor(
                    angle * EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Math.Deg2Rad,
                    out float sin,
                    out float cos);
                float x = cos * orbitRadius;
                float y = sin * orbitRadius;

                Vector3 projPos = center + new Vector3(x, y, GameConfig.Weapons.DefaultZOffset);

                if (visualSprites != null && p < visualSprites.Length && visualSprites[p] != null)
                {
                    visualSprites[p].position = projPos;
                }

                // Faction-aware contact damage around this specific orbital projectile
                DamageTargetsInRadius(projPos, sqrDamageRadius, frameDamage);
            }
        }
    }
}
