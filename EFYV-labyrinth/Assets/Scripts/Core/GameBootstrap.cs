using UnityEngine;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Managers;

namespace EFYV.Core
{
    // Scene-composition bootstrap for Assets/Scenes/Labyrinth.unity.
    //
    // The repository intentionally contains no binary art or ScriptableObject
    // assets, so the hand-authored scene alone would render nothing and the
    // enemy template would carry no authored stats. This behaviour fills those
    // gaps at scene load with deterministic, generated placeholders:
    //   1. a solid sprite for the player's SpriteRenderer,
    //   2. a solid sprite plus a schema-backed EnemyData for the (inactive)
    //      enemy template that SpawnManager clones through PoolManager,
    //   3. a small tile palette for MapViewportController before its Start()
    //      populates the fallback map.
    // Everything runs in Awake so it is guaranteed to finish before any
    // Start() in the scene. All wiring is guarded: fields already assigned in
    // the Inspector (or by tests) are never overwritten, and every reference
    // is optional so a partially-authored scene still loads without
    // exceptions.
    public class GameBootstrap : MonoBehaviour
    {
        // Placeholder-art values are deliberately local constants: they are
        // scene-composition details of this bootstrap, not a cross-component
        // contract (EFYV-LabyrinthConfig.cs is owned by the pipeline-contract
        // work stream in this batch).
        private const int PlaceholderTextureSize = 16;
        private const float PlaceholderPixelsPerUnit = 16f;
        private const int TilePaletteSize = 4;
        private const byte OpaqueAlpha = 255;
        private const float BorderShade = 0.75f;
        private const float EnemyMaxHealth = 30f;
        private const float EnemyBaseSpeed = 2.5f;
        private const float EnemyDamageToPlayer = 5f;
        private const float EnemyExperienceValue = 20f;
        private const string EnemyTemplateAssetName = "BootstrapEnemy";

        // Fixed, deterministic placeholder colors (r, g, b).
        private static readonly byte[,] TileColors =
        {
            { 92, 148, 92 },   // grass
            { 132, 108, 76 },  // dirt
            { 108, 112, 124 }, // stone
            { 72, 104, 140 },  // water
        };

        [SerializeField] private PlayerController player;
        [SerializeField] private MapViewportController mapViewport;
        // Wired in the Inspector only (inactive objects cannot be found at
        // runtime); the explicit initializer keeps CS0649 quiet under the
        // warnings-as-errors verification build.
        [SerializeField] private LivingEntity enemyTemplate = null;

        public PlayerController Player => player;
        public MapViewportController MapViewport => mapViewport;
        public LivingEntity EnemyTemplate => enemyTemplate;

        private void Awake()
        {
            ResolveSceneReferences();
            ApplyPlayerPlaceholderArt();
            ApplyEnemyTemplateData();
            ApplyTilePalette();
        }

        // Inspector wiring wins; lookups only fill holes. The enemy template
        // is an inactive scene object, which FindObjectOfType skips, so it
        // must be wired in the Inspector to participate.
        internal void ResolveSceneReferences()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (mapViewport == null) mapViewport = FindAnyObjectByType<MapViewportController>();
        }

        internal void ApplyPlayerPlaceholderArt()
        {
            if (player == null) return;
            SpriteRenderer renderer = player.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite == null)
            {
                renderer.sprite = CreateSolidSprite(228, 220, 148);
            }
        }

        // Gives the pooled enemy template visible art and authored stats. The
        // template GameObject is inactive, so its Awake/Initialize has not run
        // yet; LivingEntity.LoadData tolerates that (its sprite refresh is
        // spriteRenderer-guarded) and clones re-apply the shared EnemyData when
        // activation runs their Initialize.
        internal void ApplyEnemyTemplateData()
        {
            if (enemyTemplate == null) return;

            SpriteRenderer renderer = enemyTemplate.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite == null)
            {
                renderer.sprite = CreateSolidSprite(196, 72, 72);
            }

            if (enemyTemplate.SourceData != null) return; // authored data wins

            var data = ScriptableObject.CreateInstance<EnemyData>();
            data.name = EnemyTemplateAssetName;
            data.entityName = EnemyTemplateAssetName; // also stamps AssetIdHash
            EFYVBackend.Core.Data.FastSchemaBlock block = data.GetSchemaBlock();
            block.SetFloat((int)EFYVBackend.Core.Data.AssetSchema.MaxHealth, EnemyMaxHealth);
            block.SetFloat((int)EFYVBackend.Core.Data.AssetSchema.BaseSpeed, EnemyBaseSpeed);
            block.SetFloat((int)EFYVBackend.Core.Data.AssetSchema.DamageToPlayer, EnemyDamageToPlayer);
            block.SetFloat((int)EFYVBackend.Core.Data.AssetSchema.ExperienceValue, EnemyExperienceValue);
            data.SetSchemaBlock(block);
            enemyTemplate.LoadData(data);
        }

        // Must complete before MapViewportController.Start reads tilePalette.
        // Awake-before-Start ordering guarantees that without a custom script
        // execution order entry.
        internal void ApplyTilePalette()
        {
            if (mapViewport == null) return;

            if (mapViewport.tilePalette == null || mapViewport.tilePalette.Length == 0)
            {
                var palette = new Sprite[TilePaletteSize];
                for (int i = 0; i < palette.Length; i++)
                {
                    palette[i] = CreateSolidSprite(
                        TileColors[i, 0],
                        TileColors[i, 1],
                        TileColors[i, 2]);
                }
                mapViewport.tilePalette = palette;
            }

            if (mapViewport.targetToFollow == null && player != null)
            {
                mapViewport.targetToFollow = player.transform;
            }
        }

        // Solid square with a slightly darker one-pixel border so tiles read
        // as a grid and entities have a visible silhouette.
        internal static Sprite CreateSolidSprite(byte r, byte g, byte b)
        {
            var texture = new Texture2D(
                PlaceholderTextureSize,
                PlaceholderTextureSize,
                TextureFormat.RGBA32,
                false);
            texture.filterMode = FilterMode.Point;

            var borderColor = new Color32(
                (byte)(r * BorderShade),
                (byte)(g * BorderShade),
                (byte)(b * BorderShade),
                OpaqueAlpha);
            var fillColor = new Color32(r, g, b, OpaqueAlpha);
            var pixels = new Color32[PlaceholderTextureSize * PlaceholderTextureSize];
            for (int y = 0; y < PlaceholderTextureSize; y++)
            {
                bool edgeRow = y == 0 || y == PlaceholderTextureSize - 1;
                for (int x = 0; x < PlaceholderTextureSize; x++)
                {
                    bool edge = edgeRow || x == 0 || x == PlaceholderTextureSize - 1;
                    pixels[(y * PlaceholderTextureSize) + x] = edge ? borderColor : fillColor;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, PlaceholderTextureSize, PlaceholderTextureSize),
                new Vector2(0.5f, 0.5f),
                PlaceholderPixelsPerUnit);
        }
    }
}
