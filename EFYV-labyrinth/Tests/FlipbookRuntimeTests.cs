// Items #13 + #14: the runtime flipbook and designer hitboxes to gameplay.
//
// #13 - LivingEntity plays the CURRENT facing's imported animation over
// EntityFacingImportData.Frames, advanced by the central update loops
// (Enemy.Tick / PlayerController.Update via TickFlipbook), honoring per-frame
// durations (0 = inherit fps), the loop range, and ping-pong. Facing changes
// preserve progress when the same clip keeps playing and restart it otherwise;
// movement selects walk/idle when the atlas names those clips.
//
// #14 - the current facing + flipbook frame's Hurtbox record drives a
// BoxCollider2D (frame-specific; frames without a Hurtbox inherit the last),
// and EFYVHitboxGizmo becomes an always-on overlay that in Play Mode follows
// the live facing and flipbook frame instead of the designer preview frame.
using EFYV.Core.Data;
using EFYV.Core.Entities;
using EFYV.Editor;
using EFYVBackend.Core.Data;
using UnityEditor;
using UnityEngine;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig;
using FacingDirection = EFYVBackend.Core.Math.FastMath.FacingDirection;

internal static partial class Program
{
    private static Sprite[] NamedFrames(params string[] names)
    {
        var frames = new Sprite[names.Length];
        for (int i = 0; i < names.Length; i++) frames[i] = new Sprite { name = names[i] };
        return frames;
    }

    private static EntityAnimationMetadata FlipbookAnim(
        string name, int fps, int startFrame, int frameCount, int loopStart, int loopEnd,
        bool pingPong = false, int[] durationsMs = null)
    {
        return new EntityAnimationMetadata
        {
            Name = name,
            FramesPerSecond = fps,
            StartFrame = startFrame,
            FrameCount = frameCount,
            LoopStartFrame = loopStart,
            LoopEndFrame = loopEnd,
            PingPong = pingPong,
            FrameDurationsMs = durationsMs
        };
    }

    private static LivingEntityData FlipbookData(
        FacingDirection facing,
        Sprite[] frames,
        EntityAnimationMetadata[] animations,
        EntityHitboxRecord[] hitboxes = null,
        int frameWidth = 16,
        int frameHeight = 16)
    {
        var data = ScriptableObject.CreateInstance<LivingEntityData>();
        FastSchemaBlock block = default;
        block.SetFloat((int)AssetSchema.MaxHealth, 100f);
        block.SetFloat((int)AssetSchema.BaseSpeed, 2f);
        data.SetSchemaBlock(block);
        var atlas = new EntityAtlasMetadata
        {
            FormatVersion = Config.Backend.Exporter.CurrentFormatVersion,
            FrameWidth = frameWidth,
            FrameHeight = frameHeight,
            AtlasWidth = frameWidth * (frames.Length == 0 ? 1 : frames.Length),
            AtlasHeight = frameHeight,
            Animations = animations
        };
        data.SetImportedFacing(facing, atlas, frames, hitboxes);
        return data;
    }

    private static void TestLivingEntityFlipbookPlayback()
    {
        // ---- fps timing: one loop over the imported frames, exact indices ----
        var idle = new[] { FlipbookAnim("idle", 10, 0, 3, 0, 2) }; // 0.1s per frame
        ProbeLiving live = CreateComponent<ProbeLiving>(addRenderer: true);
        live.Initialize();
        live.LoadData(FlipbookData(FacingDirection.Down, NamedFrames("f0", "f1", "f2"), idle));

        // LoadData resolves the facing and shows the loop-start frame.
        Check(live.HasRuntimeFlipbook, "Imported frames must activate the flipbook.");
        Equal("idle", live.CurrentAnimationName);
        Equal(0, live.CurrentAnimationLocalFrame);
        Equal(0, live.CurrentFlipbookGlobalFrame);
        Equal("f0", live.spriteRenderer.sprite.name);

        live.TickFlipbook(0.05f); // below one frame -> no advance
        Equal(0, live.CurrentAnimationLocalFrame);
        Equal("f0", live.spriteRenderer.sprite.name);
        live.TickFlipbook(0.05f); // reaches 0.10 -> frame 1
        Equal(1, live.CurrentAnimationLocalFrame);
        Equal("f1", live.spriteRenderer.sprite.name);
        live.TickFlipbook(0.1f); // frame 2
        Equal(2, live.CurrentAnimationLocalFrame);
        Equal("f2", live.spriteRenderer.sprite.name);
        live.TickFlipbook(0.1f); // wraps to loop start 0
        Equal(0, live.CurrentAnimationLocalFrame);
        Equal("f0", live.spriteRenderer.sprite.name);

        // A single tick spanning several frames advances all of them.
        live.TickFlipbook(0.25f); // 0.25 / 0.1 = 2 advances (rem 0.05) -> frame 2
        Equal(2, live.CurrentAnimationLocalFrame);
        Equal("f2", live.spriteRenderer.sprite.name);

        // Non-positive / NaN dt never advances.
        live.TickFlipbook(0f);
        live.TickFlipbook(-1f);
        live.TickFlipbook(float.NaN);
        Equal(2, live.CurrentAnimationLocalFrame);

        // ---- per-frame durations override fps (0 = inherit the fps) ----
        var timed = new[] { FlipbookAnim("idle", 5, 0, 3, 0, 2, false, new[] { 100, 0, 300 }) };
        // frame0 = 100ms, frame1 = inherit fps (200ms), frame2 = 300ms.
        ProbeLiving paced = CreateComponent<ProbeLiving>(addRenderer: true);
        paced.Initialize();
        paced.LoadData(FlipbookData(FacingDirection.Down, NamedFrames("p0", "p1", "p2"), timed));
        Equal("p0", paced.spriteRenderer.sprite.name);
        paced.TickFlipbook(0.1f); // frame0 override 0.1s met -> frame 1
        Equal("p1", paced.spriteRenderer.sprite.name);
        paced.TickFlipbook(0.15f); // frame1 inherits 0.2s -> not yet
        Equal("p1", paced.spriteRenderer.sprite.name);
        paced.TickFlipbook(0.05f); // reaches 0.2s -> frame 2
        Equal("p2", paced.spriteRenderer.sprite.name);
        paced.TickFlipbook(0.25f); // frame2 needs 0.3s -> not yet
        Equal("p2", paced.spriteRenderer.sprite.name);
        paced.TickFlipbook(0.05f); // reaches 0.3s -> wraps to 0
        Equal("p0", paced.spriteRenderer.sprite.name);

        // ---- ping-pong bounces between loop start and end without repeats ----
        var pong = new[] { FlipbookAnim("idle", 10, 0, 3, 0, 2, pingPong: true) };
        ProbeLiving bouncer = CreateComponent<ProbeLiving>(addRenderer: true);
        bouncer.Initialize();
        bouncer.LoadData(FlipbookData(FacingDirection.Down, NamedFrames("b0", "b1", "b2"), pong));
        int[] expected = { 1, 2, 1, 0, 1, 2, 1, 0 };
        for (int i = 0; i < expected.Length; i++)
        {
            bouncer.TickFlipbook(0.1f);
            Equal(expected[i], bouncer.CurrentAnimationLocalFrame, "Ping-pong step " + i);
        }

        // ---- loop sub-range: only [loopStart, loopEnd] plays; ends are skipped ----
        var subRange = new[] { FlipbookAnim("idle", 10, 0, 5, 1, 3) };
        ProbeLiving ranged = CreateComponent<ProbeLiving>(addRenderer: true);
        ranged.Initialize();
        ranged.LoadData(FlipbookData(
            FacingDirection.Down, NamedFrames("r0", "r1", "r2", "r3", "r4"), subRange));
        Equal(1, ranged.CurrentAnimationLocalFrame); // starts at loop start
        Equal("r1", ranged.spriteRenderer.sprite.name);
        ranged.TickFlipbook(0.1f);
        Equal("r2", ranged.spriteRenderer.sprite.name);
        ranged.TickFlipbook(0.1f);
        Equal("r3", ranged.spriteRenderer.sprite.name);
        ranged.TickFlipbook(0.1f); // wraps back to loop start (1), never 4
        Equal(1, ranged.CurrentAnimationLocalFrame);
        Equal("r1", ranged.spriteRenderer.sprite.name);

        // A malformed loop end beyond the frame count clamps to the last frame.
        var clamped = new[] { FlipbookAnim("idle", 10, 0, 2, 0, 99) };
        ProbeLiving clampEntity = CreateComponent<ProbeLiving>(addRenderer: true);
        clampEntity.Initialize();
        clampEntity.LoadData(FlipbookData(FacingDirection.Down, NamedFrames("c0", "c1"), clamped));
        clampEntity.TickFlipbook(0.1f);
        Equal("c1", clampEntity.spriteRenderer.sprite.name);
        clampEntity.TickFlipbook(0.1f); // wraps at clamped end 1 -> 0
        Equal("c0", clampEntity.spriteRenderer.sprite.name);

        // A single-frame animation is static (nothing to advance).
        var still = new[] { FlipbookAnim("idle", 10, 0, 1, 0, 0) };
        ProbeLiving statue = CreateComponent<ProbeLiving>(addRenderer: true);
        statue.Initialize();
        statue.LoadData(FlipbookData(FacingDirection.Down, NamedFrames("s0"), still));
        Equal("s0", statue.spriteRenderer.sprite.name);
        statue.TickFlipbook(10f);
        Equal(0, statue.CurrentAnimationLocalFrame);
        Equal("s0", statue.spriteRenderer.sprite.name);

        // ---- facing change: same clip keeps progress, a state change restarts ----
        var walkDown = new[] { FlipbookAnim("walk", 10, 0, 3, 0, 2) };
        var walkRight = new[] { FlipbookAnim("walk", 10, 0, 3, 0, 2) };
        var turner = ScriptableObject.CreateInstance<LivingEntityData>();
        FastSchemaBlock turnBlock = default;
        turnBlock.SetFloat((int)AssetSchema.MaxHealth, 100f);
        turnBlock.SetFloat((int)AssetSchema.BaseSpeed, 2f);
        turner.SetSchemaBlock(turnBlock);
        var downAtlas = new EntityAtlasMetadata
        { FrameWidth = 16, FrameHeight = 16, AtlasWidth = 48, AtlasHeight = 16, Animations = walkDown };
        var rightAtlas = new EntityAtlasMetadata
        { FrameWidth = 16, FrameHeight = 16, AtlasWidth = 48, AtlasHeight = 16, Animations = walkRight };
        turner.SetImportedFacing(FacingDirection.Down, downAtlas, NamedFrames("d0", "d1", "d2"), null);
        turner.SetImportedFacing(FacingDirection.Right, rightAtlas, NamedFrames("x0", "x1", "x2"), null);

        ProbeLiving mover = CreateComponent<ProbeLiving>(addRenderer: true);
        mover.Initialize();
        mover.LoadData(turner); // default "idle" absent -> first clip "walk" plays
        Equal("walk", mover.CurrentAnimationName);
        mover.TickFlipbook(0.1f); // Down walk -> local frame 1 (d1)
        Equal(1, mover.CurrentAnimationLocalFrame);
        Equal("d1", mover.spriteRenderer.sprite.name);
        mover.UpdateDirectionalSprite(1f, 0f); // turn Right, still "walk"
        Equal(FacingDirection.Right, mover.CurrentFacing);
        Equal(1, mover.CurrentAnimationLocalFrame); // progress preserved
        Equal("x1", mover.spriteRenderer.sprite.name); // now the Right frame set

        // ---- Enemy central loop drives the flipbook + walk/idle selection ----
        var idleWalk = new[]
        {
            FlipbookAnim("idle", 10, 0, 2, 0, 1),
            FlipbookAnim("walk", 10, 2, 2, 0, 1)
        };
        ProbeEnemy enemy = CreateComponent<ProbeEnemy>(addRenderer: true);
        enemy.Initialize();
        enemy.LoadData(FlipbookData(
            FacingDirection.Down, NamedFrames("e0", "e1", "e2", "e3"), idleWalk));
        Equal("idle", enemy.CurrentAnimationName); // default state
        Equal("e0", enemy.spriteRenderer.sprite.name);

        var target = new GameObject("flipbook-target").transform;
        enemy.SetTarget(target);
        enemy.Tick(0.1f); // chasing -> walk restarts at its start, then advances one frame
        Equal("walk", enemy.CurrentAnimationName);
        Equal(1, enemy.CurrentAnimationLocalFrame);
        Equal(3, enemy.CurrentFlipbookGlobalFrame); // walk startFrame 2 + local 1
        Equal("e3", enemy.spriteRenderer.sprite.name);
        enemy.Tick(0.1f); // walk wraps local 1 -> 0
        Equal(0, enemy.CurrentAnimationLocalFrame);
        Equal("e2", enemy.spriteRenderer.sprite.name);

        enemy.SetTarget(null);
        enemy.Tick(0.1f); // not chasing -> idle restarts (0), then advances one frame
        Equal("idle", enemy.CurrentAnimationName);
        Equal(1, enemy.CurrentAnimationLocalFrame);
        Equal("e1", enemy.spriteRenderer.sprite.name);

        // ---- no imported frames -> static fallback, flipbook inactive ----
        var bareData = FlipbookData(FacingDirection.Down, System.Array.Empty<Sprite>(),
            new[] { FlipbookAnim("idle", 10, 0, 1, 0, 0) });
        bareData.spriteSheetDown = new Sprite { name = "static-down" };
        ProbeLiving bare = CreateComponent<ProbeLiving>(addRenderer: true);
        bare.Initialize();
        bare.LoadData(bareData);
        Check(!bare.HasRuntimeFlipbook, "No imported frames must leave the flipbook inactive.");
        Equal("static-down", bare.spriteRenderer.sprite.name);
        bare.TickFlipbook(1f); // inert
        Equal("static-down", bare.spriteRenderer.sprite.name);
    }

    private static void TestDesignerHitboxColliderSyncAndOverlay()
    {
        var idle = new[] { FlipbookAnim("idle", 10, 0, 3, 0, 2) };
        var hurt0 = new Rect(4f, 4f, 8f, 6f);
        var hurt1 = new Rect(2f, 3f, 10f, 5f);
        EntityHitboxRecord[] Hitboxes() => new[]
        {
            // A non-Hurtbox record on frame 0 must never drive the collider.
            new EntityHitboxRecord { FrameIndex = 0, HitboxType = "Hitbox", Bounds = new Rect(1f, 1f, 2f, 2f) },
            new EntityHitboxRecord { FrameIndex = 0, HitboxType = Config.Game.Hitbox.HurtboxType, Bounds = hurt0 },
            new EntityHitboxRecord { FrameIndex = 1, HitboxType = Config.Game.Hitbox.HurtboxType, Bounds = hurt1 },
            // Frame 2 authors only a non-Hurtbox: the collider inherits frame 1.
            new EntityHitboxRecord { FrameIndex = 2, HitboxType = "Hitbox", Bounds = new Rect(0f, 0f, 3f, 3f) }
        };

        LivingEntityData data = FlipbookData(
            FacingDirection.Down, NamedFrames("h0", "h1", "h2"), idle, Hitboxes());
        data.TryGetImportedFacing(FacingDirection.Down, out EntityFacingImportData facing);
        EntityAtlasMetadata atlas = facing.AtlasMetadata;

        ProbeLiving live = CreateComponent<ProbeLiving>(addRenderer: true);
        var collider = live.gameObject.AddComponent<BoxCollider2D>();
        live.Initialize();
        live.LoadData(data);

        // On LoadData the frame-0 Hurtbox (not the Hitbox) drives the collider,
        // using the SAME pixel-to-local-units math the gizmo draws.
        Check(EntityHitboxGeometry.TryGetLocalBounds(atlas, hurt0, out Vector3 c0, out Vector3 s0));
        Near(c0.x, collider.offset.x, 0f);
        Near(c0.y, collider.offset.y, 0f);
        Near(s0.x, collider.size.x, 0f);
        Near(s0.y, collider.size.y, 0f);

        // Advancing to frame 1 re-syncs to that frame's Hurtbox (per-frame).
        live.TickFlipbook(0.1f);
        Equal(1, live.CurrentAnimationLocalFrame);
        Check(EntityHitboxGeometry.TryGetLocalBounds(atlas, hurt1, out Vector3 c1, out Vector3 s1));
        Near(c1.x, collider.offset.x, 0f);
        Near(c1.y, collider.offset.y, 0f);
        Near(s1.x, collider.size.x, 0f);
        Near(s1.y, collider.size.y, 0f);

        // Frame 2 has no Hurtbox: the collider inherits the last synced bounds.
        live.TickFlipbook(0.1f);
        Equal(2, live.CurrentAnimationLocalFrame);
        Near(c1.x, collider.offset.x, 0f);
        Near(c1.y, collider.offset.y, 0f);
        Near(s1.x, collider.size.x, 0f);
        Near(s1.y, collider.size.y, 0f);

        // An entity without a BoxCollider2D is a safe no-op on every seam.
        ProbeLiving noCollider = CreateComponent<ProbeLiving>(addRenderer: true);
        noCollider.Initialize();
        noCollider.LoadData(FlipbookData(
            FacingDirection.Down, NamedFrames("n0", "n1", "n2"), idle, Hitboxes()));
        noCollider.TickFlipbook(0.1f);
        Check(noCollider.HasRuntimeFlipbook);

        // A facing without imported hitboxes leaves a hand-placed collider alone.
        ProbeLiving handSet = CreateComponent<ProbeLiving>(addRenderer: true);
        var handCollider = handSet.gameObject.AddComponent<BoxCollider2D>();
        handCollider.offset = new Vector2(1.25f, -2.5f);
        handCollider.size = new Vector2(3f, 4f);
        handSet.Initialize();
        handSet.LoadData(FlipbookData(
            FacingDirection.Down, NamedFrames("q0", "q1"),
            new[] { FlipbookAnim("idle", 10, 0, 2, 0, 1) }, hitboxes: null));
        handSet.TickFlipbook(0.1f);
        Near(1.25f, handCollider.offset.x, 0f);
        Near(-2.5f, handCollider.offset.y, 0f);
        Near(3f, handCollider.size.x, 0f);
        Near(4f, handCollider.size.y, 0f);

        // ---- play-mode overlay follows the live flipbook frame ----
        var overlayHitboxes = new[]
        {
            new EntityHitboxRecord { FrameIndex = 0, HitboxType = "hurt", Bounds = new Rect(3f, 3f, 6f, 6f) },
            new EntityHitboxRecord { FrameIndex = 1, HitboxType = "hurt", Bounds = new Rect(1f, 2f, 8f, 4f) }
        };
        LivingEntityData overlayData = FlipbookData(
            FacingDirection.Down, NamedFrames("o0", "o1"),
            new[] { FlipbookAnim("idle", 10, 0, 2, 0, 1) }, overlayHitboxes);
        overlayData.TryGetImportedFacing(FacingDirection.Down, out EntityFacingImportData overlayFacing);
        EntityAtlasMetadata overlayAtlas = overlayFacing.AtlasMetadata;

        ProbeLiving overlay = CreateComponent<ProbeLiving>(addRenderer: true);
        overlay.Initialize();
        overlay.LoadData(overlayData);
        overlay.transform.position = new Vector3(1f, 2f, 0f);
        System.Type gizmoType = typeof(EFYVHitboxGizmo);

        // Edit mode: the overlay still previews the designer frame (default 0).
        EditorApplication.isPlaying = false;
        Gizmos.Cubes.Clear();
        InvokeStatic(gizmoType, "DrawImportedHitboxes", overlay, GizmoType.NonSelected);
        Equal(1, Gizmos.Cubes.Count, "Edit mode previews the designer frame.");
        Check(EntityHitboxGeometry.TryGetLocalBounds(
            overlayAtlas, overlayHitboxes[0].Bounds, out Vector3 editCenter, out Vector3 editSize));
        Near(editCenter.x, Gizmos.Cubes[0].Center.x, 0f);
        Near(editSize.x, Gizmos.Cubes[0].Size.x, 0f);

        // Play mode: the overlay follows the live facing and flipbook frame.
        EditorApplication.isPlaying = true;
        overlay.TickFlipbook(0.1f);
        Equal(1, overlay.CurrentFlipbookGlobalFrame);
        Gizmos.Cubes.Clear();
        InvokeStatic(gizmoType, "DrawImportedHitboxes", overlay, GizmoType.NonSelected);
        Equal(1, Gizmos.Cubes.Count, "Play mode follows the flipbook frame.");
        Check(EntityHitboxGeometry.TryGetLocalBounds(
            overlayAtlas, overlayHitboxes[1].Bounds, out Vector3 playCenter, out Vector3 playSize));
        Near(playCenter.x, Gizmos.Cubes[0].Center.x, 0f);
        Near(playSize.x, Gizmos.Cubes[0].Size.x, 0f);
        EditorApplication.isPlaying = false;
    }
}
