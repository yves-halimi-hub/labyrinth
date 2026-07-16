using EFYV.Core.Data;
using EFYV.Core.Interfaces;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities
{
    public abstract class BossEnemy : Enemy
    {
        private float authoredPhase2HealthThreshold;

        public float Phase2HealthThreshold 
        { 
            get => Data.Phase2HealthThreshold; 
            set => Data.Phase2HealthThreshold = value; 
        }
        protected int currentPhase 
        { 
            get => Data.CurrentPhase; 
            set => Data.CurrentPhase = value; 
        }
        protected bool isEnraged 
        { 
            get => Data.IsEnraged; 
            set => Data.IsEnraged = value; 
        }
        public int CurrentPhase => currentPhase;
        public bool IsEnraged => isEnraged;
        public event System.Action PhaseTwoStarted;

        public override void Initialize()
        {
            base.Initialize();
            currentPhase = GameConfig.Boss.PhaseOne;
            isEnraged = GameConfig.Boss.InitialEnraged;
        }

        protected override void ApplyAdditionalSchemaData(EFYVBackend.Core.Data.FastSchemaBlock block)
        {
            base.ApplyAdditionalSchemaData(block);
            authoredPhase2HealthThreshold = block.GetFloat((int)EFYVBackend.Core.Data.AssetSchema.Phase2HealthThreshold);
            Phase2HealthThreshold = ScalePhaseThreshold(authoredPhase2HealthThreshold, SpawnHealthMultiplier);
        }

        public override void TakeDamage(float amount)
        {
            base.TakeDamage(amount);
            if (CurrentHealth > GameConfig.Entity.DeathHealthThreshold && gameObject.activeInHierarchy)
            {
                CheckPhaseTransition();
            }
        }

        public override void OnSpawn()
        {
            base.OnSpawn();
            currentPhase = GameConfig.Boss.PhaseOne;
            isEnraged = GameConfig.Boss.InitialEnraged;
            Phase2HealthThreshold = ScalePhaseThreshold(authoredPhase2HealthThreshold, SpawnHealthMultiplier);
        }

        internal static float ScalePhaseThreshold(float authoredThreshold, float healthMultiplier)
        {
            return authoredThreshold * healthMultiplier;
        }

        protected virtual void CheckPhaseTransition()
        {
            if (currentPhase == GameConfig.Boss.PhaseOne && CurrentHealth <= Phase2HealthThreshold)
            {
                currentPhase = GameConfig.Boss.PhaseTwo;
                isEnraged = GameConfig.Boss.Enraged;
                OnPhaseTwoStart();
            }
        }

        protected virtual void OnPhaseTwoStart()
        {
            PhaseTwoStarted?.Invoke();
        }
    }
}
