using UnityEngine;
using System.Collections.Generic;
using GameConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game;

namespace EFYV.Core.Data
{
    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = GameConfig.Runtime.SequentialStructPack)]
    public struct LegacyAchievementDefinition
    {
        public EFYVBackend.Core.Models.LegacyAchievementDefinitionData Data;

        [Tooltip(GameConfig.Achievements.TooltipAchievementId)]
        [SerializeField] private int _id;
        public int id
        {
            get => Data.Id;
            set
            {
                _id = value;
                Data.Id = value;
            }
        }
        
        [SerializeField] private string _title;
        public string title
        {
            get => _title;
            set
            {
                _title = value;
                Data.TitleHash = EFYVBackend.Core.Math.FastMath.FastHash(value);
            }
        }

        [TextArea]
        [SerializeField] private string _description;
        public string description
        {
            get => _description;
            set
            {
                _description = value;
                Data.DescriptionHash = EFYVBackend.Core.Math.FastMath.FastHash(value);
            }
        }
        
        public Sprite icon;

        public void SyncData()
        {
            id = _id;
            title = _title;
            description = _description;
        }
    }

    [CreateAssetMenu(fileName = GameConfig.DataConfig.FileNameLegacyAchievementDb, menuName = GameConfig.DataConfig.MenuLegacyAchievementDb)]
    public class LegacyAchievementDatabase : ScriptableObject
    {
        public List<LegacyAchievementDefinition> achievements = new List<LegacyAchievementDefinition>();

        private void OnValidate()
        {
            for (int i = 0; i < achievements.Count; i++)
            {
                var ach = achievements[i];
                ach.SyncData();
                achievements[i] = ach;
            }
        }

        // Pre-fills the database with 30 basic achievements as a basis
        [ContextMenu(GameConfig.Achievements.ContextMenuPopulateBasis)]
        private void PopulateBasis()
        {
            achievements.Clear();
            
            string[] basisTitles = GameConfig.Achievements.BasisData.Titles;
            string[] basisDescs = GameConfig.Achievements.BasisData.Descriptions;

            for (int i = 0; i < basisTitles.Length; i++)
            {
                achievements.Add(new LegacyAchievementDefinition
                {
                    id = i,
                    title = basisTitles[i],
                    description = basisDescs[i]
                });
            }
        }
    }
}
