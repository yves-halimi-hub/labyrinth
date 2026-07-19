using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Managers;
using EFYV.Core.Spawning;
using SpawnConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game.SpawnPalette;
using RuntimeConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Game.Runtime;

namespace EFYV.Editor
{
    // Item #4: the Play-Mode debug spawn window. It lists the imported assets
    // discovered under Assets/RawArt, auto-offers the most recently
    // imported/refreshed one (the EFYVLiveDebugBridge seam), and one-clicks the
    // selection into the running game through DataToPrefabFactory - which picks
    // the matching archetype template prefab, spawns it through PoolManager, and
    // binds the asset (flipbook + hurtbox active).
    //
    // Placement is player-relative (the simpler of the two options - no
    // scene-view raycast): the asset spawns at the player's position plus the
    // configurable offset, or at world origin when no player exists yet.
    //
    // All list/selection state lives in the plain SpawnPaletteModel (unit-tested
    // headlessly); this window is a thin IMGUI shell around it plus the
    // editor-only discovery/loading the harness cannot exercise.
    internal sealed class EFYVSpawnPaletteWindow : EditorWindow
    {
        private readonly SpawnPaletteModel model = new SpawnPaletteModel();
        private readonly EditorSpawnTemplateProvider templates = new EditorSpawnTemplateProvider();
        private Vector2 spawnOffset = new Vector2(
            SpawnConfig.DefaultSpawnOffsetX, SpawnConfig.DefaultSpawnOffsetY);
        private int lastSeenRefreshVersion;

        [MenuItem(SpawnConfig.MenuPath)]
        private static void Open()
        {
            EFYVSpawnPaletteWindow window = GetWindow<EFYVSpawnPaletteWindow>(SpawnConfig.WindowTitle);
            window.titleContent = new GUIContent(SpawnConfig.WindowTitle);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(SpawnConfig.WindowTitle);
            RediscoverAssets();
        }

        // EditorWindow.Update ticks ~10x/sec even without focus: cheaply poll the
        // bridge's refresh counter so a live import auto-offers itself.
        private void Update()
        {
            if (EFYVLiveDebugBridge.RefreshVersion == lastSeenRefreshVersion) return;
            lastSeenRefreshVersion = EFYVLiveDebugBridge.RefreshVersion;
            model.NotifyRefreshed(EFYVLiveDebugBridge.LastRefreshedAsset);
            RediscoverAssets();
            Repaint();
        }

        private void OnGUI()
        {
            GUILayout.Label(SpawnConfig.HeaderImportedAssets);

            if (model.Count == RuntimeConfig.EmptyCollectionCount)
            {
                EditorGUILayout.HelpBox(SpawnConfig.HelpNoAssets, MessageType.Info);
            }
            else
            {
                DrawAssetList();
            }

            if (GUILayout.Button(SpawnConfig.RefreshButtonLabel))
            {
                RediscoverAssets();
            }

            EditorGUILayout.Space();
            spawnOffset = EditorGUILayout.Vector2Field(SpawnConfig.OffsetFieldLabel, spawnOffset);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(SpawnConfig.HelpEnterPlayMode, MessageType.Info);
                return;
            }

            PoolManager pool = UnityEngine.Object.FindAnyObjectByType<PoolManager>();
            if (pool == null)
            {
                EditorGUILayout.HelpBox(SpawnConfig.HelpNoPool, MessageType.Warning);
                return;
            }

            if (model.TryGetSelectedEntry(out SpawnPaletteModel.Entry selected) &&
                selected.CanSpawn &&
                GUILayout.Button(SpawnConfig.SpawnButtonLabel))
            {
                SpawnSelected(selected.Asset, pool);
            }
        }

        private void DrawAssetList()
        {
            IReadOnlyList<SpawnPaletteModel.Entry> entries = model.Entries;
            for (int i = RuntimeConfig.FirstIndex; i < entries.Count; i++)
            {
                SpawnPaletteModel.Entry entry = entries[i];
                string suffix = entry.CanSpawn
                    ? string.Format(SpawnConfig.ArchetypeSuffixFormat,
                        DataToPrefabFactory.ArchetypeName(entry.Archetype))
                    : SpawnConfig.UnspawnableSuffix;
                string prefix = i == model.SelectedIndex ? SpawnConfig.SelectedPrefix : string.Empty;
                if (GUILayout.Button(prefix + entry.DisplayName + suffix))
                {
                    model.Select(i);
                }
            }
        }

        private void SpawnSelected(SchemaBackedAssetData asset, PoolManager pool)
        {
            Vector3 origin = Vector3.zero;
            PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player != null) origin = player.entityTransform.position;
            Vector3 position = origin + new Vector3(spawnOffset.x, spawnOffset.y, RuntimeConfig.UnitIntervalMin);
            DataToPrefabFactory.Spawn(asset, templates, pool, position, Quaternion.identity);
        }

        // Scans the RawArt export root for imported schema-backed assets. Editor-
        // only (AssetDatabase); the model it feeds is what the harness tests.
        private void RediscoverAssets()
        {
            var discovered = new List<SchemaBackedAssetData>();
            string[] guids = AssetDatabase.FindAssets(SpawnConfig.AssetSearchFilter);
            for (int i = RuntimeConfig.FirstIndex; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) ||
                    !path.StartsWith(SpawnConfig.DiscoveryRoot, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                SchemaBackedAssetData asset = AssetDatabase.LoadAssetAtPath<SchemaBackedAssetData>(path);
                if (asset != null) discovered.Add(asset);
            }
            model.SetAssets(discovered);
        }
    }

    // Item #4: loads and caches the three archetype template prefabs by their
    // configured asset paths. Editor-only (AssetDatabase); the runtime factory
    // sees it only through ISpawnTemplateProvider.
    internal sealed class EditorSpawnTemplateProvider : ISpawnTemplateProvider
    {
        private readonly Dictionary<SpawnArchetype, GameEntity> cache =
            new Dictionary<SpawnArchetype, GameEntity>();

        public GameEntity GetTemplate(SpawnArchetype archetype)
        {
            if (cache.TryGetValue(archetype, out GameEntity cached) && cached != null) return cached;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath(archetype));
            GameEntity template = prefab != null ? prefab.GetComponent<GameEntity>() : null;
            cache[archetype] = template;
            return template;
        }

        private static string TemplatePath(SpawnArchetype archetype)
        {
            switch (archetype)
            {
                case SpawnArchetype.Enemy: return SpawnConfig.TemplatePathEnemy;
                case SpawnArchetype.Boss: return SpawnConfig.TemplatePathBoss;
                default: return SpawnConfig.TemplatePathProp;
            }
        }
    }
}
