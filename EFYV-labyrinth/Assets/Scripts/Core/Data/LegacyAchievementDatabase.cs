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
            // #24: Unity deserialization populates only the serialized fields, and
            // OnValidate (the editor-only sync) never runs in player builds, so a
            // freshly loaded definition has Data.Id == 0. Fall back to the
            // serialized _id in that state; the setter keeps both in lockstep, so
            // a zero Data.Id with a nonzero _id can only mean "not yet synced".
            get => Data.Id != 0 ? Data.Id : _id;
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

        // #24: reconciles every definition's serialized fields into its packed
        // Data block. OnValidate covers the editor; AchievementManager.Awake calls
        // this so player builds get the same sync at startup.
        public void SyncAllDefinitions()
        {
            for (int i = 0; i < achievements.Count; i++)
            {
                var ach = achievements[i];
                ach.SyncData();
                achievements[i] = ach;
            }
        }

        private void OnValidate()
        {
            SyncAllDefinitions();
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
