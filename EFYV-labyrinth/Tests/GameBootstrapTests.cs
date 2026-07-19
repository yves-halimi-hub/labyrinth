using System;
using EFYV.Core;
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Core.Managers;
using EFYVBackend.Core.Data;
using UnityEngine;

// batch2/unity-project agent: scene bootstrap coverage for Assets/Scenes/Labyrinth.unity.
//
// GameBootstrap is the only script allowed to compose the hand-authored scene at
// runtime (placeholder sprites, tile palette, schema-backed enemy template data).
// These groups prove the composition rules: deterministic generated art, palette
// wiring before viewport Start, authored-data-wins guards, and the full
// template-to-pooled-clone stat path used by SpawnManager.
internal static partial class Program
{
    private const float BootstrapEnemyMaxHealth = 30f;
    private const float BootstrapEnemyBaseSpeed = 2.5f;
    private const float BootstrapEnemyDamage = 5f;
    private const float BootstrapEnemyExperience = 20f;

    private sealed class BootstrapFixture
    {
        public GameBootstrap Bootstrap;
        public PlayerController Player;
        public SpriteRenderer PlayerRenderer;
        public MapViewportController Viewport;
        public Monster EnemyTemplate;
        public SpriteRenderer EnemyRenderer;
    }

    // Mirrors the authored scene: active player (Awake ran), active viewport,
    // INACTIVE enemy template whose Awake has never run, and a bootstrap whose
    // serialized fields are wired exactly like Labyrinth.unity wires them.
    private static BootstrapFixture CreateBootstrapFixture(bool wireEnemyTemplate = true)
    {
        var fixture = new BootstrapFixture();

        var playerObject = new GameObject("Player");
        fixture.PlayerRenderer = playerObject.AddComponent<SpriteRenderer>();
        fixture.Player = (PlayerController)playerObject.AddComponent(typeof(PlayerController), true);

        fixture.Viewport = TestRuntime.CreateComponent<MapViewportController>("MapViewport");

        var enemyObject = new GameObject("EnemyTemplate");
        fixture.EnemyRenderer = enemyObject.AddComponent<SpriteRenderer>();
        fixture.EnemyTemplate = (Monster)enemyObject.AddComponent(typeof(Monster), false);
        enemyObject.SetActive(false);

        var bootstrapObject = new GameObject("GameBootstrap");
        fixture.Bootstrap = (GameBootstrap)bootstrapObject.AddComponent(typeof(GameBootstrap), false);
        SetField(fixture.Bootstrap, "player", fixture.Player);
        SetField(fixture.Bootstrap, "mapViewport", fixture.Viewport);
        if (wireEnemyTemplate) SetField(fixture.Bootstrap, "enemyTemplate", fixture.EnemyTemplate);
        return fixture;
    }

    private static void AwakeBootstrap(BootstrapFixture fixture)
    {
        TestRuntime.InvokeLifecycle(fixture.Bootstrap, "Awake");
    }

    private static void TestBootstrapPlaceholderArtAndPaletteWiring()
    {
        // 1. Full wiring: every visual hole is filled exactly once.
        BootstrapFixture fixture = CreateBootstrapFixture();
        fixture.Viewport.targetToFollow = null;
        AwakeBootstrap(fixture);

        Check(fixture.PlayerRenderer.sprite != null, "player got a placeholder sprite");
        Check(fixture.EnemyRenderer.sprite != null, "enemy template got a placeholder sprite");
        NotSame(fixture.PlayerRenderer.sprite, fixture.EnemyRenderer.sprite);
        Same(fixture.Player.transform, fixture.Viewport.targetToFollow);

        Sprite[] palette = fixture.Viewport.tilePalette;
        Check(palette != null, "tile palette assigned");
        Equal(4, palette.Length, "bootstrap palette has four tiles");
        for (int i = 0; i < palette.Length; i++)
        {
            Check(palette[i] != null, "palette entry " + i + " non-null");
            Check(palette[i].texture != null, "palette texture " + i + " non-null");
            for (int j = i + 1; j < palette.Length; j++)
            {
                NotSame(palette[i], palette[j]);
                Check(!PixelsEqual(palette[i].texture, palette[j].texture),
                    "palette tiles " + i + " and " + j + " differ visually");
            }
        }

        // 2. Sprite geometry contract: 16x16 texture, centered pivot, 16 PPU so a
        // tile is exactly one world unit (matches serializedCellSize: 1 in scene).
        Sprite playerSprite = fixture.PlayerRenderer.sprite;
        Equal(16, playerSprite.texture.width);
        Equal(16, playerSprite.texture.height);
        Near(16f, playerSprite.pixelsPerUnit);
        Near(0.5f, playerSprite.pivot.x);
        Near(0.5f, playerSprite.pivot.y);

        // 3. Border shading: edge pixel is strictly darker than the interior, and
        // both are fully opaque (SpriteRenderer default color multiplies alpha).
        Color32[] pixels = playerSprite.texture.GetPixels32();
        Equal(16 * 16, pixels.Length);
        Color32 border = pixels[0];
        Color32 fill = pixels[(16 * 8) + 8];
        Check(border.r < fill.r && border.g < fill.g && border.b < fill.b, "border darker than fill");
        Equal((byte)255, border.a);
        Equal((byte)255, fill.a);

        // 4. Determinism: same inputs produce byte-identical pixels.
        Sprite first = GameBootstrap.CreateSolidSprite(92, 148, 92);
        Sprite second = GameBootstrap.CreateSolidSprite(92, 148, 92);
        NotSame(first, second);
        Check(PixelsEqual(first.texture, second.texture), "generation is deterministic");

        // 5. Authored art wins: pre-assigned sprites and palettes are never replaced.
        ResetState();
        fixture = CreateBootstrapFixture();
        var authoredSprite = new Sprite();
        fixture.PlayerRenderer.sprite = authoredSprite;
        var authoredEnemySprite = new Sprite();
        fixture.EnemyRenderer.sprite = authoredEnemySprite;
        var authoredPalette = new Sprite[] { new Sprite(), new Sprite() };
        fixture.Viewport.tilePalette = authoredPalette;
        var authoredTarget = new GameObject("CustomTarget").transform;
        fixture.Viewport.targetToFollow = authoredTarget;
        AwakeBootstrap(fixture);
        Same(authoredSprite, fixture.PlayerRenderer.sprite);
        Same(authoredEnemySprite, fixture.EnemyRenderer.sprite);
        Same(authoredPalette, fixture.Viewport.tilePalette);
        Equal(2, fixture.Viewport.tilePalette.Length);
        Same(authoredTarget, fixture.Viewport.targetToFollow);

        // 6. Partially-authored scenes never throw: no refs wired, nothing found.
        ResetState();
        var lonely = TestRuntime.CreateComponent<GameBootstrap>("LonelyBootstrap", invokeAwake: false);
        TestRuntime.InvokeLifecycle(lonely, "Awake");
        Check(lonely.Player == null, "no player resolved in an empty scene");
        Check(lonely.MapViewport == null, "no viewport resolved in an empty scene");
        Check(lonely.EnemyTemplate == null, "inactive template is never auto-resolved");

        // 7. Inspector holes are filled by scene lookup (active objects only).
        ResetState();
        fixture = CreateBootstrapFixture();
        SetField(fixture.Bootstrap, "player", null);
        SetField(fixture.Bootstrap, "mapViewport", null);
        AwakeBootstrap(fixture);
        Same(fixture.Player, fixture.Bootstrap.Player);
        Same(fixture.Viewport, fixture.Bootstrap.MapViewport);
        Check(fixture.PlayerRenderer.sprite != null, "resolved player still gets art");
    }

    private static void TestBootstrapEnemyDataAndPooledSpawnStats()
    {
        // 1. The inactive template receives a schema-backed EnemyData with the
        // bootstrap's authored stats and a stamped asset-name hash.
        BootstrapFixture fixture = CreateBootstrapFixture();
        var poolManager = TestRuntime.CreateComponent<PoolManager>("PoolManager");
        AwakeBootstrap(fixture);

        LivingEntityData sourceData = fixture.EnemyTemplate.SourceData;
        Check(sourceData != null, "enemy template received bootstrap data");
        Check(sourceData is EnemyData, "bootstrap data is an EnemyData");
        Equal("BootstrapEnemy", sourceData.entityName);
        FastSchemaBlock block = sourceData.GetSchemaBlock();
        Near(BootstrapEnemyMaxHealth, block.GetFloat((int)AssetSchema.MaxHealth));
        Near(BootstrapEnemyBaseSpeed, block.GetFloat((int)AssetSchema.BaseSpeed));
        Near(BootstrapEnemyDamage, block.GetFloat((int)AssetSchema.DamageToPlayer));
        Near(BootstrapEnemyExperience, block.GetFloat((int)AssetSchema.ExperienceValue));
        Equal(
            EFYVBackend.Core.Math.FastMath.FastHash("BootstrapEnemy"),
            block.GetInt((int)AssetSchema.AssetIdHash),
            "entityName setter stamped the asset id hash");

        // 2. The exact scene path: SpawnManager-style pooled spawn of the template
        // produces a live clone carrying the authored stats (no AIDirector, so
        // multipliers stay 1).
        GameEntity spawned = poolManager.Spawn(fixture.EnemyTemplate, new Vector3(3f, 4f, 0f), Quaternion.identity);
        Check(spawned != null, "pool produced a clone");
        NotSame(fixture.EnemyTemplate, spawned);
        var enemy = (Monster)spawned;
        Near(BootstrapEnemyMaxHealth, enemy.MaxHealth);
        Near(BootstrapEnemyMaxHealth, enemy.CurrentHealth);
        Near(BootstrapEnemyBaseSpeed, enemy.BaseSpeed);
        Near(BootstrapEnemyDamage, enemy.DamageToPlayer);
        Near(BootstrapEnemyExperience, enemy.ExperienceValue);
        Same(sourceData, enemy.SourceData);
        Check(enemy.IsSpawned, "clone is spawned");
        Equal(1, Enemy.ActiveEnemies.Count, "clone registered in the packed enemy list");
        Same(enemy, Enemy.ActiveEnemies[0]);
        Check(enemy.gameObject.activeSelf, "clone activated by OnSpawn");
        Check(!fixture.EnemyTemplate.gameObject.activeSelf, "template itself stays dormant");

        // 3. Chase wiring: the clone targets the fixture player and closes distance
        // at the authored speed.
        Same(fixture.Player.entityTransform, GetField<Transform>(enemy, "<TargetPlayer>k__BackingField"));
        Vector3 before = enemy.transform.position;
        enemy.Tick(0.1f);
        Vector3 after = enemy.transform.position;
        float beforeDistance = (fixture.Player.transform.position - before).magnitude;
        float afterDistance = (fixture.Player.transform.position - after).magnitude;
        Check(afterDistance < beforeDistance, "clone chases the player");
        Near(BootstrapEnemyBaseSpeed * 0.1f, beforeDistance - afterDistance, 0.01f);

        // 4. Damage pipeline: authored damage reaches the player through Attack.
        float healthBefore = fixture.Player.CurrentHealth;
        enemy.Attack(fixture.Player);
        Near(healthBefore - BootstrapEnemyDamage, fixture.Player.CurrentHealth);

        // 5. Authored data wins: a template that already carries designer data is
        // left untouched (both the reference and its stats).
        ResetState();
        fixture = CreateBootstrapFixture();
        var authored = ScriptableObject.CreateInstance<EnemyData>();
        authored.entityName = "DesignerEnemy";
        FastSchemaBlock authoredBlock = authored.GetSchemaBlock();
        authoredBlock.SetFloat((int)AssetSchema.MaxHealth, 123f);
        authoredBlock.SetFloat((int)AssetSchema.BaseSpeed, 7f);
        authored.SetSchemaBlock(authoredBlock);
        fixture.EnemyTemplate.LoadData(authored);
        AwakeBootstrap(fixture);
        Same(authored, fixture.EnemyTemplate.SourceData);
        Near(123f, fixture.EnemyTemplate.SourceData.GetSchemaBlock().GetFloat((int)AssetSchema.MaxHealth));

        // 6. Re-running Awake is idempotent: the first bootstrap data instance
        // survives and no second instance replaces it.
        ResetState();
        fixture = CreateBootstrapFixture();
        AwakeBootstrap(fixture);
        LivingEntityData firstData = fixture.EnemyTemplate.SourceData;
        Sprite firstSprite = fixture.EnemyRenderer.sprite;
        AwakeBootstrap(fixture);
        Same(firstData, fixture.EnemyTemplate.SourceData);
        Same(firstSprite, fixture.EnemyRenderer.sprite);

        // 7. Missing template: bootstrap composes everything else and never throws.
        ResetState();
        fixture = CreateBootstrapFixture(wireEnemyTemplate: false);
        AwakeBootstrap(fixture);
        Check(fixture.EnemyTemplate.SourceData == null, "unwired template untouched");
        Check(fixture.PlayerRenderer.sprite != null, "player art still applied");
        Equal(4, fixture.Viewport.tilePalette.Length);
    }

    private static bool PixelsEqual(Texture2D left, Texture2D right)
    {
        assertions++;
        Color32[] leftPixels = left.GetPixels32();
        Color32[] rightPixels = right.GetPixels32();
        if (leftPixels == null || rightPixels == null || leftPixels.Length != rightPixels.Length)
        {
            return false;
        }

        for (int i = 0; i < leftPixels.Length; i++)
        {
            if (leftPixels[i].r != rightPixels[i].r ||
                leftPixels[i].g != rightPixels[i].g ||
                leftPixels[i].b != rightPixels[i].b ||
                leftPixels[i].a != rightPixels[i].a)
            {
                return false;
            }
        }

        return true;
    }
}
