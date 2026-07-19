using System;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    // Item #10 onion skinning: host-agnostic settings for compositing ghost
    // frames around the current frame (ViewportController.ComposeOnionSkin).
    //
    // Ghost alpha model: the nearest previous/next neighbor renders at
    // PreviousAlpha/NextAlpha; each additional step away multiplies by
    // AlphaFalloff (so ghost k steps away uses baseAlpha * falloff^(k-1)).
    // Neighbors are clamped at the animation ends — there is no wrap-around,
    // matching how artists compare adjacent frames.
    public sealed class OnionSkinSettings
    {
        private int previousFrameCount = Config.OnionSkin.DefaultPreviousFrames;
        private int nextFrameCount = Config.OnionSkin.DefaultNextFrames;
        private float previousAlpha = Config.OnionSkin.DefaultPreviousAlpha;
        private float nextAlpha = Config.OnionSkin.DefaultNextAlpha;
        private float alphaFalloff = Config.OnionSkin.DefaultAlphaFalloff;

        public int PreviousFrameCount
        {
            get => previousFrameCount;
            set
            {
                if (value < Config.Common.FirstIndex || value > Config.OnionSkin.MaxNeighborFrames)
                    throw new ArgumentOutOfRangeException(nameof(value));
                previousFrameCount = value;
            }
        }

        public int NextFrameCount
        {
            get => nextFrameCount;
            set
            {
                if (value < Config.Common.FirstIndex || value > Config.OnionSkin.MaxNeighborFrames)
                    throw new ArgumentOutOfRangeException(nameof(value));
                nextFrameCount = value;
            }
        }

        public float PreviousAlpha
        {
            get => previousAlpha;
            set
            {
                ValidateUnitInterval(value);
                previousAlpha = value;
            }
        }

        public float NextAlpha
        {
            get => nextAlpha;
            set
            {
                ValidateUnitInterval(value);
                nextAlpha = value;
            }
        }

        public float AlphaFalloff
        {
            get => alphaFalloff;
            set
            {
                ValidateUnitInterval(value);
                alphaFalloff = value;
            }
        }

        private static void ValidateUnitInterval(float value)
        {
            if (float.IsNaN(value) || value < Config.Common.ZeroFloat || value > Config.Common.UnitScale)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
