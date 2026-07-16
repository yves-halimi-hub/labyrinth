using UnityEngine;
using EFYV.Core.Managers;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Entities.Environment.Implementations
{
    public class DoorProp : InteractableProp
    {
        [Tooltip(GameConfig.Map.TooltipDoorTargetMapId)]
        [SerializeField] private string _targetMapId;
        public string TargetMapId
        {
            get => _targetMapId;
            set
            {
                _targetMapId = value;
                Data.Block.SetInt((int)EFYVBackend.Core.Data.DoorPropSchema.TargetMapIdHash, EFYVBackend.Core.Math.FastMath.FastHash(value));
            }
        }

        public override void Initialize()
        {
            base.Initialize();
            TargetMapId = _targetMapId;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            TargetMapId = _targetMapId;
        }

        // Triggered when the player interacts with this object
        public override void OnInteract(PlayerController player)
        {
            if (string.IsNullOrEmpty(TargetMapId))
            {
                Debug.LogWarning(GameConfig.Map.LogTargetMapIdEmpty);
                return;
            }

            Debug.LogFormat(GameConfig.Map.LogSwitchingToMap, TargetMapId);
            MapManager.Instance.SwitchMap(TargetMapId);
        }
    }
}
