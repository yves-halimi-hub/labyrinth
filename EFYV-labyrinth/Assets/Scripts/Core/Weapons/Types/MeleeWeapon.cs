using UnityEngine;
using EFYV.Core.Compute;
using EFYV.Core.Entities;
using EFYV.Core.Utils;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Weapons.Types
{
    // Hits when an enemy gets physically close to the player (e.g. Aluminum bat, Longsword)
    public abstract class MeleeWeapon : Weapon
    {
        public float attackRange
        {
            get => Data.AttackRange;
            set => Data.AttackRange = value;
        }

        public float knockbackForce
        {
            get => Data.KnockbackForce;
            set => Data.KnockbackForce = value;
        }

        protected override void Awake()
        {
            base.Awake();
            attackRange = GameConfig.Weapons.Melee.DefaultAttackRange;
            knockbackForce = GameConfig.Weapons.Melee.DefaultKnockback;
        }

        public override void Fire()
        {
            float effectiveRange = attackRange < GameConfig.Runtime.UnitIntervalMin
                ? -attackRange
                : attackRange;
            float sqrRange = effectiveRange * effectiveRange;
            Vector3 myPos = transform.position;
            // Time-scaled knockback uses the driving tick's deltaTime, never the
            // global clock, so custom-dt drivers stay in sync with rotation/damage.
            float knockbackStep = knockbackForce * TickDeltaTime;

            // Faction-aware: an enemy-held melee weapon swings at the player only.
            if (OwnerFaction == Faction.Enemy)
            {
                PlayerController player = PlayerController.Instance;
                if (player == null || player.IsDead) return;
                if (player.entityTransform.position.FastSqrDistance(myPos) <= sqrRange)
                {
                    Vector3 playerOffset = player.entityTransform.position - myPos;
                    player.TakeDamage(BaseDamage);
                    if (!player.IsDead)
                    {
                        player.entityTransform.position += playerOffset.normalized * knockbackStep;
                    }
                }
                return;
            }

            RuntimeGameplayCompute.QueryEnemyRadius(myPos, effectiveRange);
            int start = RuntimeGameplayCompute.QueryHitStart(GameConfig.Runtime.FirstIndex);
            for (int hitIndex =
                    RuntimeGameplayCompute.QueryHitEnd(GameConfig.Runtime.FirstIndex) - 1;
                hitIndex >= start;
                hitIndex--)
            {
                Enemy enemy = RuntimeGameplayCompute.EnemyAtHit(hitIndex);
                if (!enemy.IsSpawned)
                {
                    continue;
                }

                Vector3 offset = enemy.entityTransform.position - myPos;
                enemy.TakeDamage(BaseDamage);
                if (enemy.IsSpawned)
                {
                    enemy.entityTransform.position += offset.normalized * knockbackStep;
                }
            }
        }
    }
}
