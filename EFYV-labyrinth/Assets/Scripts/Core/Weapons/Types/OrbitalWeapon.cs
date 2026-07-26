using System;
using UnityEngine;
using EFYV.Core.Compute;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Types
{
    // Projectiles that mathematically orbit the player (e.g. Spinning Axes, Beyblades)
    public abstract class OrbitalWeapon : Weapon
    {
        public Transform[] visualSprites;

        private float[] angleRadians = Array.Empty<float>();
        private float[] angleSines = Array.Empty<float>();
        private float[] angleCosines = Array.Empty<float>();
        private Vector3[] projectilePositions = Array.Empty<Vector3>();
        
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
            float frameDamage = BaseDamage * TickDeltaTime;
            EnsureProjectileCapacity(projectileCount);

            for (int p = 0; p < projectileCount; p++)
            {
                float angle = currentAngle + (p * angleStep);
                angleRadians[p] = RuntimeGameplayCompute.NormalizeRadians(
                    angle * EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend.Math.Deg2Rad);
            }

            // All orbit angles share one range-reduced native sin/cos batch.
            RuntimeGameplayCompute.SinCosRadians(
                angleRadians.AsSpan(0, projectileCount),
                angleSines.AsSpan(0, projectileCount),
                angleCosines.AsSpan(0, projectileCount));

            for (int p = 0; p < projectileCount; p++)
            {
                float sin = angleSines[p];
                float cos = angleCosines[p];
                float x = cos * orbitRadius;
                float y = sin * orbitRadius;

                Vector3 projPos = center + new Vector3(x, y, GameConfig.Weapons.DefaultZOffset);
                projectilePositions[p] = projPos;

                if (visualSprites != null && p < visualSprites.Length && visualSprites[p] != null)
                {
                    visualSprites[p].position = projPos;
                }
            }

            // One spatial batch for all projectiles; consuming query groups in
            // order preserves stacked contact damage.
            DamageTargetsInRadiusBatch(
                projectilePositions.AsSpan(0, projectileCount),
                damageRadius,
                frameDamage);
        }

        private void EnsureProjectileCapacity(int required)
        {
            if (angleRadians.Length >= required)
            {
                return;
            }

            int capacity = 4;
            while (capacity < required)
            {
                capacity = checked(capacity * 2);
            }
            Array.Resize(ref angleRadians, capacity);
            Array.Resize(ref angleSines, capacity);
            Array.Resize(ref angleCosines, capacity);
            Array.Resize(ref projectilePositions, capacity);
        }
    }
}
