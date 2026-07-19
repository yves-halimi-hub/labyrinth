using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EFYVBackend.Core.Export;
using EFYVBackend.Core.IO;
using EFYVBackend.Core.Memory;
using EFYVBackend.Core.Models;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Verification
{
    // batch3.4 agent (item #7): FastEffects filter primitives (outline, glow,
    // HSV color shift) against managed reference models, the runtime-effect
    // descriptor wire contract (validation + export/parse round trip), and
    // the single-sourced PNG CRC table (batch-2 deferred nit).
    internal static partial class Program
    {
        private const uint EffectsOpaqueRed = 0xFF0000FFu;   // R=255 A=255
        private const uint EffectsOutlineBlue = 0xFFFF0000u; // B=255 A=255

        // ------------------------------------------------------------------
        // Outline: 1px 8-neighborhood silhouette expansion.
        // ------------------------------------------------------------------
        private static unsafe void TestEffectsOutlineReferenceModel()
        {
            // Single opaque center pixel on 3x3: every other pixel is an
            // 8-neighbor and becomes the outline color; the center is kept.
            uint[] source = new uint[9];
            source[4] = EffectsOpaqueRed;
            uint[] destination = new uint[9];
            fixed (uint* sourcePointer = source)
            fixed (uint* destinationPointer = destination)
            {
                FastEffects.Outline(sourcePointer, destinationPointer, 3, 3, EffectsOutlineBlue);
            }
            for (int index = 0; index < 9; index++)
            {
                AssertEqual(index == 4 ? EffectsOpaqueRed : EffectsOutlineBlue, destination[index]);
            }

            // Diagonal-only contact still outlines (8-neighborhood): opaque at
            // (0,0) on 2x2 outlines all three remaining pixels including the
            // diagonal (1,1).
            uint[] corner = { EffectsOpaqueRed, 0u, 0u, 0u };
            uint[] cornerOut = new uint[4];
            fixed (uint* sourcePointer = corner)
            fixed (uint* destinationPointer = cornerOut)
            {
                FastEffects.Outline(sourcePointer, destinationPointer, 2, 2, EffectsOutlineBlue);
            }
            AssertEqual(EffectsOpaqueRed, cornerOut[0]);
            AssertEqual(EffectsOutlineBlue, cornerOut[1]);
            AssertEqual(EffectsOutlineBlue, cornerOut[2]);
            AssertEqual(EffectsOutlineBlue, cornerOut[3]);

            // Any alpha > 0 counts as silhouette; pixels beyond 1px stay
            // untouched, and zero-alpha pixels with non-zero RGB garbage that
            // do NOT touch the silhouette are copied bit-exact.
            uint[] wide = new uint[25]; // 5x5
            wide[12] = 0x01FFFFFFu;     // barely-visible center
            wide[0] = 0x00ABCDEFu;      // zero-alpha garbage in the far corner
            uint[] wideOut = new uint[25];
            fixed (uint* sourcePointer = wide)
            fixed (uint* destinationPointer = wideOut)
            {
                FastEffects.Outline(sourcePointer, destinationPointer, 5, 5, EffectsOutlineBlue);
            }
            AssertEqual(0x00ABCDEFu, wideOut[0]);
            AssertEqual(0x01FFFFFFu, wideOut[12]);
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    int index = y * 5 + x;
                    if (index == 0 || index == 12) continue;
                    bool touchesCenter = System.Math.Abs(x - 2) <= 1 && System.Math.Abs(y - 2) <= 1;
                    AssertEqual(touchesCenter ? EffectsOutlineBlue : 0u, wideOut[index]);
                }
            }

            // Randomized invariants against a naive reference on mixed noise:
            // silhouette pixels are never recolored, and the output equals the
            // straightforward per-pixel dilation model.
            var random = new Random(0x0E77EC7);
            for (int iteration = 0; iteration < 40; iteration++)
            {
                int width = 1 + random.Next(9);
                int height = 1 + random.Next(9);
                uint[] noise = new uint[width * height];
                for (int index = 0; index < noise.Length; index++)
                {
                    // ~half the pixels transparent (with junk RGB), half opaque-ish.
                    noise[index] = random.Next(2) == 0
                        ? (uint)random.Next() & 0x00FFFFFFu
                        : (uint)random.Next() | 0x01000000u;
                }
                uint[] actual = new uint[noise.Length];
                fixed (uint* sourcePointer = noise)
                fixed (uint* destinationPointer = actual)
                {
                    FastEffects.Outline(sourcePointer, destinationPointer, width, height, EffectsOutlineBlue);
                }
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        uint pixel = noise[y * width + x];
                        uint expected;
                        if ((byte)(pixel >> BackendConfig.Pixel.AlphaShift) != 0)
                        {
                            expected = pixel;
                        }
                        else
                        {
                            bool touches = false;
                            for (int dy = -1; dy <= 1 && !touches; dy++)
                            {
                                for (int dx = -1; dx <= 1 && !touches; dx++)
                                {
                                    if (dx == 0 && dy == 0) continue;
                                    int nx = x + dx;
                                    int ny = y + dy;
                                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                                    touches = (byte)(noise[ny * width + nx] >> BackendConfig.Pixel.AlphaShift) != 0;
                                }
                            }
                            expected = touches ? EffectsOutlineBlue : pixel;
                        }
                        AssertEqual(expected, actual[y * width + x]);
                    }
                }
            }

            // In-place (src == dest) must match the out-of-place result: the
            // primitive stages through scratch storage.
            uint[] inPlace = new uint[9];
            inPlace[4] = EffectsOpaqueRed;
            fixed (uint* pixels = inPlace)
            {
                FastEffects.Outline(pixels, pixels, 3, 3, EffectsOutlineBlue);
            }
            AssertSequenceEqual(destination, inPlace);

            // Guards.
            AssertThrows<ArgumentNullException>(() =>
            {
                fixed (uint* pixels = inPlace) FastEffects.Outline(null, pixels, 3, 3, 0u);
            });
            AssertThrows<ArgumentNullException>(() =>
            {
                fixed (uint* pixels = inPlace) FastEffects.Outline(pixels, null, 3, 3, 0u);
            });
            AssertThrows<ArgumentOutOfRangeException>(() =>
            {
                uint probe = 0;
                FastEffects.Outline(&probe, &probe, 0, 1, 0u);
            });
            AssertThrows<ArgumentOutOfRangeException>(() =>
            {
                uint probe = 0;
                FastEffects.Outline(&probe, &probe, 1, -1, 0u);
            });
        }

        // ------------------------------------------------------------------
        // Glow: silhouette+rim flood, blur, source composited on top.
        // ------------------------------------------------------------------
        private static unsafe void TestEffectsGlowReferenceModel()
        {
            // radius 0: the halo is the un-blurred silhouette+rim, so the
            // result is glow color on the rim, source pixels on the
            // silhouette (opaque source wins the blend), zero elsewhere.
            uint[] source = new uint[25]; // 5x5
            source[12] = EffectsOpaqueRed;
            uint[] destination = new uint[25];
            fixed (uint* sourcePointer = source)
            fixed (uint* destinationPointer = destination)
            {
                FastEffects.Glow(sourcePointer, destinationPointer, 5, 5, EffectsOutlineBlue, 0);
            }
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    int index = y * 5 + x;
                    uint expected;
                    if (index == 12) expected = EffectsOpaqueRed;
                    else if (System.Math.Abs(x - 2) <= 1 && System.Math.Abs(y - 2) <= 1) expected = EffectsOutlineBlue;
                    else expected = 0u;
                    AssertEqual(expected, destination[index]);
                }
            }

            // radius 1: the blur spreads halo alpha outward - pixels OUTSIDE
            // the hard rim now carry some glow, alpha decays with distance,
            // and the fully opaque source pixel still lands exactly on top.
            uint[] soft = new uint[49]; // 7x7
            soft[24] = EffectsOpaqueRed; // center (3,3)
            uint[] softOut = new uint[49];
            fixed (uint* sourcePointer = soft)
            fixed (uint* destinationPointer = softOut)
            {
                FastEffects.Glow(sourcePointer, destinationPointer, 7, 7, EffectsOutlineBlue, 1);
            }
            AssertEqual(EffectsOpaqueRed, softOut[24]);
            byte TwoOut = (byte)(softOut[3 * 7 + 1] >> BackendConfig.Pixel.AlphaShift); // (1,3): 2 from center
            byte ThreeOut = (byte)(softOut[3 * 7 + 0] >> BackendConfig.Pixel.AlphaShift); // (0,3): 3 from center
            Assert(TwoOut > 0); // blur reached one past the rim
            Assert(ThreeOut == 0); // radius-1 blur of a 1px rim cannot reach 3 away
            byte rimAlpha = (byte)(softOut[3 * 7 + 2] >> BackendConfig.Pixel.AlphaShift); // (2,3): on the rim
            Assert(rimAlpha > TwoOut); // alpha decays outward

            // Glow of a fully transparent surface is fully transparent.
            uint[] empty = new uint[16];
            uint[] emptyOut = new uint[16];
            fixed (uint* sourcePointer = empty)
            fixed (uint* destinationPointer = emptyOut)
            {
                FastEffects.Glow(sourcePointer, destinationPointer, 4, 4, EffectsOutlineBlue, 2);
            }
            for (int index = 0; index < emptyOut.Length; index++) AssertEqual(0u, emptyOut[index]);

            // Semi-transparent source blends OVER the halo instead of
            // replacing it: the result at the silhouette is the source-over
            // composite of source on glow.
            uint[] translucent = new uint[9];
            translucent[4] = 0x80_00_00_FFu; // half-alpha red
            uint[] translucentOut = new uint[9];
            fixed (uint* sourcePointer = translucent)
            fixed (uint* destinationPointer = translucentOut)
            {
                FastEffects.Glow(sourcePointer, destinationPointer, 3, 3, EffectsOutlineBlue, 0);
            }
            uint composite = EffectsOutlineBlue;
            FastMemory.BlendColor(ref composite, translucent[4]);
            AssertEqual(composite, translucentOut[4]);

            // In-place equals out-of-place.
            uint[] inPlace = new uint[25];
            inPlace[12] = EffectsOpaqueRed;
            fixed (uint* pixels = inPlace)
            {
                FastEffects.Glow(pixels, pixels, 5, 5, EffectsOutlineBlue, 0);
            }
            AssertSequenceEqual(destination, inPlace);

            // Guards.
            AssertThrows<ArgumentOutOfRangeException>(() =>
            {
                uint probe = 0;
                FastEffects.Glow(&probe, &probe, 1, 1, 0u, -1);
            });
            AssertThrows<ArgumentNullException>(() =>
            {
                uint probe = 0;
                FastEffects.Glow(null, &probe, 1, 1, 0u, 0);
            });
            AssertThrows<ArgumentNullException>(() =>
            {
                uint probe = 0;
                FastEffects.Glow(&probe, null, 1, 1, 0u, 0);
            });
            AssertThrows<ArgumentOutOfRangeException>(() =>
            {
                uint probe = 0;
                FastEffects.Glow(&probe, &probe, 0, 1, 0u, 0);
            });
        }

        // ------------------------------------------------------------------
        // HSV color shift: exact primaries, invariants, mirrored reference.
        // ------------------------------------------------------------------
        private static unsafe void TestEffectsColorShiftHsvReference()
        {
            uint Shift1(uint pixel, float hue, float saturation, float value)
            {
                uint result;
                FastEffects.ColorShift(&pixel, &result, 1, 1, hue, saturation, value);
                return result;
            }

            // Primary rotations: red -> green -> blue -> red around the wheel.
            AssertEqual(0xFF00FF00u, Shift1(0xFF0000FFu, 120f, 0f, 0f)); // red +120 = green
            AssertEqual(0xFFFF0000u, Shift1(0xFF0000FFu, 240f, 0f, 0f)); // red +240 = blue
            AssertEqual(0xFFFF0000u, Shift1(0xFF0000FFu, -120f, 0f, 0f)); // red -120 = blue (wrap)
            AssertEqual(0xFF0000FFu, Shift1(0xFF0000FFu, 720f, 0f, 0f)); // full wraps are identity
            AssertEqual(0xFF0000FFu, Shift1(0xFFFF0000u, 120f, 0f, 0f)); // blue (h=240) +120 wraps to red

            // Saturation floor makes any hue gray at its value; value floor is
            // black; raising value alone from black gives white (s stays 0),
            // while raising saturation AND value resurrects the hue-0 red.
            AssertEqual(0xFFFFFFFFu, Shift1(0xFF0000FFu, 0f, -1f, 1f)); // desaturate + max value = white
            AssertEqual(0xFF000000u, Shift1(0xFF00FF00u, 0f, 0f, -1f)); // value floor = black
            AssertEqual(0xFF808080u, Shift1(0xFF808080u, 77f, 0f, 0f)); // gray is hue-invariant
            AssertEqual(0xFFFFFFFFu, Shift1(0xFF000000u, 0f, 0f, 1f)); // black + value = white
            AssertEqual(0xFF0000FFu, Shift1(0xFF000000u, 0f, 1f, 1f)); // black + sat + value = hue-0 red

            // Alpha is preserved verbatim, including partial alpha; fully
            // transparent pixels (any RGB garbage) are bit-exact copies.
            AssertEqual(0x7F00FF00u, Shift1(0x7F0000FFu, 120f, 0f, 0f));
            AssertEqual(0x00123456u, Shift1(0x00123456u, 180f, 1f, -1f));

            // Value/saturation deltas clamp instead of wrapping.
            AssertEqual(0xFF0000FFu, Shift1(0xFF0000FFu, 0f, 5f, 5f));

            // Mirrored managed reference over seeded noise (bit-exact).
            var random = new Random(0x45FEC7);
            for (int iteration = 0; iteration < 60; iteration++)
            {
                int width = 1 + random.Next(8);
                int height = 1 + random.Next(8);
                float hueDelta = (float)(random.NextDouble() * 1440d - 720d);
                float saturationDelta = (float)(random.NextDouble() * 2d - 1d);
                float valueDelta = (float)(random.NextDouble() * 2d - 1d);
                uint[] pixels = new uint[width * height];
                for (int index = 0; index < pixels.Length; index++)
                    pixels[index] = (uint)random.Next() ^ ((uint)random.Next() << 16);

                uint[] actual = new uint[pixels.Length];
                fixed (uint* sourcePointer = pixels)
                fixed (uint* destinationPointer = actual)
                {
                    FastEffects.ColorShift(
                        sourcePointer, destinationPointer, width, height,
                        hueDelta, saturationDelta, valueDelta);
                }
                for (int index = 0; index < pixels.Length; index++)
                {
                    AssertEqual(
                        EffectsReferenceHsvShift(pixels[index], hueDelta, saturationDelta, valueDelta),
                        actual[index]);
                }
            }

            // In-place operation is safe (strictly per-pixel).
            uint[] strip = { 0xFF0000FFu, 0x00FFFFFFu, 0xFF00FF00u, 0x33221100u };
            uint[] expectedStrip = new uint[strip.Length];
            fixed (uint* sourcePointer = strip)
            fixed (uint* destinationPointer = expectedStrip)
            {
                FastEffects.ColorShift(sourcePointer, destinationPointer, 4, 1, 90f, 0.25f, -0.25f);
            }
            fixed (uint* pixels = strip)
            {
                FastEffects.ColorShift(pixels, pixels, 4, 1, 90f, 0.25f, -0.25f);
            }
            AssertSequenceEqual(expectedStrip, strip);

            // Guards: NaN/Infinity deltas and bad surfaces throw.
            AssertThrows<ArgumentOutOfRangeException>(() =>
            {
                uint probe = 0;
                FastEffects.ColorShift(&probe, &probe, 1, 1, float.NaN, 0f, 0f);
            });
            AssertThrows<ArgumentOutOfRangeException>(() =>
            {
                uint probe = 0;
                FastEffects.ColorShift(&probe, &probe, 1, 1, 0f, float.PositiveInfinity, 0f);
            });
            AssertThrows<ArgumentOutOfRangeException>(() =>
            {
                uint probe = 0;
                FastEffects.ColorShift(&probe, &probe, 1, 1, 0f, 0f, float.NegativeInfinity);
            });
            AssertThrows<ArgumentNullException>(() =>
            {
                uint probe = 0;
                FastEffects.ColorShift(null, &probe, 1, 1, 0f, 0f, 0f);
            });
            AssertThrows<ArgumentOutOfRangeException>(() =>
            {
                uint probe = 0;
                FastEffects.ColorShift(&probe, &probe, -1, 1, 0f, 0f, 0f);
            });
        }

        // Mirrors FastEffects.ShiftPixelHsv exactly (same float order of
        // operations) so the fuzz comparison is bit-exact.
        private static uint EffectsReferenceHsvShift(
            uint pixel,
            float hueDelta,
            float saturationDelta,
            float valueDelta)
        {
            byte alpha = (byte)(pixel >> BackendConfig.Pixel.AlphaShift);
            if (alpha == 0) return pixel;

            float red = (byte)pixel / 255f;
            float green = (byte)(pixel >> 8) / 255f;
            float blue = (byte)(pixel >> 16) / 255f;

            float max = System.Math.Max(red, System.Math.Max(green, blue));
            float min = System.Math.Min(red, System.Math.Min(green, blue));
            float chroma = max - min;
            float value = max;
            float saturation = max <= 0f ? 0f : chroma / max;
            float hue;
            if (chroma <= 0f)
            {
                hue = 0f;
            }
            else if (max == red)
            {
                float sector = (green - blue) / chroma;
                if (sector < 0f) sector += 6f;
                hue = sector * 60f;
            }
            else if (max == green)
            {
                hue = ((blue - red) / chroma + 2f) * 60f;
            }
            else
            {
                hue = ((red - green) / chroma + 4f) * 60f;
            }

            hue = (hue + hueDelta) % 360f;
            if (hue < 0f) hue += 360f;
            saturation = saturation + saturationDelta;
            if (saturation < 0f) saturation = 0f;
            else if (saturation > 1f) saturation = 1f;
            value = value + valueDelta;
            if (value < 0f) value = 0f;
            else if (value > 1f) value = 1f;

            float chromaOut = value * saturation;
            float sectorPosition = hue / 60f;
            float parity = sectorPosition % 2f - 1f;
            if (parity < 0f) parity = -parity;
            float intermediate = chromaOut * (1f - parity);
            float offset = value - chromaOut;
            float sectorRed;
            float sectorGreen;
            float sectorBlue;
            int sectorIndex = (int)sectorPosition;
            if (sectorIndex <= 0) { sectorRed = chromaOut; sectorGreen = intermediate; sectorBlue = 0f; }
            else if (sectorIndex == 1) { sectorRed = intermediate; sectorGreen = chromaOut; sectorBlue = 0f; }
            else if (sectorIndex == 2) { sectorRed = 0f; sectorGreen = chromaOut; sectorBlue = intermediate; }
            else if (sectorIndex == 3) { sectorRed = 0f; sectorGreen = intermediate; sectorBlue = chromaOut; }
            else if (sectorIndex == 4) { sectorRed = intermediate; sectorGreen = 0f; sectorBlue = chromaOut; }
            else { sectorRed = chromaOut; sectorGreen = 0f; sectorBlue = intermediate; }

            uint Pack(float normalized)
            {
                float scaled = normalized * 255f + 0.5f;
                if (scaled <= 0f) return 0u;
                if (scaled >= 255f) return 255u;
                return (uint)scaled;
            }

            return Pack(sectorRed + offset) |
                (Pack(sectorGreen + offset) << 8) |
                (Pack(sectorBlue + offset) << 16) |
                ((uint)alpha << 24);
        }

        // ------------------------------------------------------------------
        // Effect descriptor wire contract: validation matrix + round trip.
        // ------------------------------------------------------------------
        private static void TestEffectsDescriptorWireContract()
        {
            AtlasMetadataJson Valid()
            {
                return new AtlasMetadataJson
                {
                    formatVersion = BackendConfig.Exporter.CurrentFormatVersion,
                    frameWidth = 4,
                    frameHeight = 4,
                    atlasWidth = 8,
                    atlasHeight = 8,
                    animations = new List<AnimationMetadataJson>
                    {
                        new AnimationMetadataJson { name = "idle", fps = 8, startFrame = 0, frameCount = 2 },
                        new AnimationMetadataJson { name = "walk", fps = 12, startFrame = 2, frameCount = 2 }
                    }
                };
            }

            EffectDescriptorJson Flash()
            {
                return new EffectDescriptorJson
                {
                    name = "HurtFlash",
                    effectType = BackendConfig.Exporter.EffectTypeFlash,
                    trigger = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.EffectTriggerOnDamaged,
                    colorRgba = 0xFF0000FFu,
                    durationMs = 150,
                    strength = 0.8f
                };
            }

            // Null effects list and a fully populated valid list both pass.
            Assert(FastExporter.TryValidateAtlasMetadata(Valid(), out AtlasMetadataError baseline));
            AssertEqual(AtlasMetadataError.None, baseline);

            AtlasMetadataJson populated = Valid();
            AnimationMetadataJson walk = populated.animations[1];
            walk.effects = new List<EffectDescriptorJson>
            {
                Flash(),
                new EffectDescriptorJson
                {
                    name = "SpawnTint",
                    effectType = BackendConfig.Exporter.EffectTypeTint,
                    trigger = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.EffectTriggerOnSpawn,
                    colorRgba = 0xFF00FF00u,
                    durationMs = 0,
                    strength = 0.25f
                },
                new EffectDescriptorJson
                {
                    name = "DustPuff",
                    effectType = BackendConfig.Exporter.EffectTypeParticleHook,
                    trigger = "OnLand" // custom tags are legal
                }
            };
            populated.animations[1] = walk;
            Assert(FastExporter.TryValidateAtlasMetadata(populated, out AtlasMetadataError populatedError));
            AssertEqual(AtlasMetadataError.None, populatedError);

            // Rejection matrix - every case lands on AnimationEffects.
            void AssertEffectError(Func<EffectDescriptorJson, EffectDescriptorJson> mutate)
            {
                AtlasMetadataJson broken = Valid();
                AnimationMetadataJson animation = broken.animations[1];
                animation.effects = new List<EffectDescriptorJson> { mutate(Flash()) };
                broken.animations[1] = animation;
                Assert(!FastExporter.TryValidateAtlasMetadata(broken, out AtlasMetadataError error));
                AssertEqual(AtlasMetadataError.AnimationEffects, error);
            }

            AssertEffectError(e => { e.effectType = "sparkle"; return e; });
            AssertEffectError(e => { e.effectType = null; return e; });
            AssertEffectError(e => { e.effectType = "Flash"; return e; }); // case-sensitive
            AssertEffectError(e => { e.trigger = null; return e; });
            AssertEffectError(e => { e.trigger = "   "; return e; });
            AssertEffectError(e =>
            {
                e.effectType = BackendConfig.Exporter.EffectTypeParticleHook;
                e.name = " ";
                return e;
            });
            AssertEffectError(e => { e.durationMs = -1; return e; });
            AssertEffectError(e => { e.durationMs = BackendConfig.Exporter.MaxEffectDurationMs + 1; return e; });
            AssertEffectError(e => { e.strength = -0.01f; return e; });
            AssertEffectError(e => { e.strength = 1.01f; return e; });
            AssertEffectError(e => { e.strength = float.NaN; return e; });

            // Over the per-animation cap.
            AtlasMetadataJson overCap = Valid();
            AnimationMetadataJson capAnimation = overCap.animations[0];
            capAnimation.effects = new List<EffectDescriptorJson>();
            for (int index = 0; index <= BackendConfig.Exporter.MaxEffectsPerAnimation; index++)
                capAnimation.effects.Add(Flash());
            overCap.animations[0] = capAnimation;
            Assert(!FastExporter.TryValidateAtlasMetadata(overCap, out AtlasMetadataError capError));
            AssertEqual(AtlasMetadataError.AnimationEffects, capError);
            capAnimation.effects.RemoveAt(0); // exactly at the cap is legal
            overCap.animations[0] = capAnimation;
            Assert(FastExporter.TryValidateAtlasMetadata(overCap, out _));

            // Export/parse round trip + raw JSON shape.
            string root = Path.Combine(
                Path.GetTempPath(), "EFYVEffectsWire-" + Guid.NewGuid().ToString("N"));
            try
            {
                var properties = new Dictionary<string, object> { ["entityName"] = "EffectsProbe" };
                var pixels = new uint[8 * 8];
                FastExporter.PushToUnityLiveHook(
                    root, "EnemyData", properties, new List<HitboxJson>(), pixels, 8, 8, populated, "EnemyData");

                string jsonPath = Path.Combine(root, "EffectsProbe" + BackendConfig.Exporter.EfyvExtension);
                EFYVJsonFormat imported = FastImporter.ParseEfyvFile(jsonPath);
                AssertEqual(BackendConfig.Exporter.CurrentDocumentVersion, imported.EffectiveDocumentVersion);
                Assert(imported.atlas.HasValue);
                List<AnimationMetadataJson> animations = imported.atlas.Value.animations;
                AssertEqual(null, animations[0].effects); // absent stays absent
                AssertEqual(3, animations[1].effects.Count);
                EffectDescriptorJson flash = animations[1].effects[0];
                AssertEqual("HurtFlash", flash.name);
                AssertEqual(BackendConfig.Exporter.EffectTypeFlash, flash.effectType);
                AssertEqual(
                    EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.EffectTriggerOnDamaged,
                    flash.trigger);
                AssertEqual(0xFF0000FFu, flash.colorRgba.Value);
                AssertEqual(150, flash.durationMs.Value);
                AssertEqual(0.8f, flash.strength.Value);
                EffectDescriptorJson hook = animations[1].effects[2];
                AssertEqual(BackendConfig.Exporter.EffectTypeParticleHook, hook.effectType);
                Assert(!hook.colorRgba.HasValue); // optionals absent on the wire stay absent
                Assert(!hook.durationMs.HasValue);
                Assert(!hook.strength.HasValue);

                // Raw shape: the effect-free animation carries no "effects"
                // member at all (byte stability), the populated one carries
                // exactly the writer's field order per descriptor.
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(jsonPath)))
                {
                    JsonElement animationArray = document.RootElement
                        .GetProperty(BackendConfig.Exporter.FieldAtlas)
                        .GetProperty(BackendConfig.Exporter.FieldAnimations);
                    Assert(!animationArray[0].TryGetProperty(
                        BackendConfig.Exporter.FieldEffects, out _));
                    JsonElement effectArray = animationArray[1].GetProperty(
                        BackendConfig.Exporter.FieldEffects);
                    var flashNames = new List<string>();
                    foreach (JsonProperty property in effectArray[0].EnumerateObject())
                        flashNames.Add(property.Name);
                    AssertSequenceEqual(
                        new[]
                        {
                            BackendConfig.Exporter.FieldName,
                            BackendConfig.Exporter.FieldEffectType,
                            BackendConfig.Exporter.FieldTrigger,
                            BackendConfig.Exporter.FieldColorRgba,
                            BackendConfig.Exporter.FieldDurationMs,
                            BackendConfig.Exporter.FieldStrength
                        },
                        flashNames.ToArray());
                    var hookNames = new List<string>();
                    foreach (JsonProperty property in effectArray[2].EnumerateObject())
                        hookNames.Add(property.Name);
                    AssertSequenceEqual(
                        new[]
                        {
                            BackendConfig.Exporter.FieldName,
                            BackendConfig.Exporter.FieldEffectType,
                            BackendConfig.Exporter.FieldTrigger
                        },
                        hookNames.ToArray());
                }

                // Exporting INVALID effect metadata is rejected up front (the
                // AnimationEffects error maps to the default ArgumentException).
                AtlasMetadataJson invalid = Valid();
                AnimationMetadataJson brokenAnimation = invalid.animations[1];
                brokenAnimation.effects = new List<EffectDescriptorJson>
                {
                    new EffectDescriptorJson { effectType = "sparkle", trigger = "OnSpawn" }
                };
                invalid.animations[1] = brokenAnimation;
                AssertThrows<ArgumentException>(() => FastExporter.PushToUnityLiveHook(
                    root, "EnemyData", properties, new List<HitboxJson>(), pixels, 8, 8, invalid, null));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        // ------------------------------------------------------------------
        // Single-sourced PNG CRC-32 (batch-2 deferred nit).
        // ------------------------------------------------------------------
        private static void TestEffectsPngCrcSingleSource()
        {
            // The ISO-HDLC check vector, and the empty-input identity.
            AssertEqual(0xCBF43926u, FastCrc32.Compute(Encoding.ASCII.GetBytes("123456789")));
            AssertEqual(0u, FastCrc32.Compute(ReadOnlySpan<byte>.Empty));

            // Every chunk CRC the encoder writes validates against FastCrc32
            // directly (proving both sides share one table). FastPngEncoder is
            // internal but the verification project source-links Core.
            var pixels = new uint[6];
            for (int index = 0; index < pixels.Length; index++) pixels[index] = 0xFF336699u + (uint)index;
            byte[] png;
            using (var stream = new MemoryStream())
            {
                FastPngEncoder.Write(stream, pixels, 3, 2);
                png = stream.ToArray();
            }
            int offset = BackendConfig.Exporter.Png.Signature.Length;
            int chunkCount = 0;
            while (offset < png.Length)
            {
                int dataLength = (png[offset] << 24) | (png[offset + 1] << 16) |
                    (png[offset + 2] << 8) | png[offset + 3];
                int typeOffset = offset + 4;
                uint expectedCrc = FastCrc32.Compute(
                    new ReadOnlySpan<byte>(png, typeOffset, BackendConfig.Exporter.Png.ChunkTypeLength + dataLength));
                int crcOffset = typeOffset + BackendConfig.Exporter.Png.ChunkTypeLength + dataLength;
                uint writtenCrc = ((uint)png[crcOffset] << 24) | ((uint)png[crcOffset + 1] << 16) |
                    ((uint)png[crcOffset + 2] << 8) | png[crcOffset + 3];
                AssertEqual(expectedCrc, writtenCrc);
                chunkCount++;
                offset = crcOffset + 4;
            }
            AssertEqual(3, chunkCount); // IHDR + IDAT + IEND

            // The decoder accepts its sibling's output (shared table on the
            // read side too) and returns the exact pixels.
            uint[] decoded = FastPngDecoder.Read(png, out int width, out int height);
            AssertEqual(3, width);
            AssertEqual(2, height);
            AssertSequenceEqual(pixels, decoded);

            // Structural pin for the consolidation itself: neither codec type
            // declares a private CRC table field anymore - FastCrc32 owns the
            // ONE table in the backend.
            Type encoder = typeof(FastPngEncoder);
            Type decoder = typeof(FastPngDecoder);
            foreach (Type codec in new[] { encoder, decoder })
            {
                foreach (FieldInfo field in codec.GetFields(
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
                {
                    Assert(field.FieldType != typeof(uint[]));
                }
            }
            FieldInfo sharedTable = typeof(FastCrc32).GetField(
                "Table", BindingFlags.NonPublic | BindingFlags.Static);
            Assert(sharedTable != null && sharedTable.FieldType == typeof(uint[]));
        }
    }
}
