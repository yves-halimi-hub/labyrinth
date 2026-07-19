using System;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVLabyMake.Core.Tools
{
    public enum MapOperationStatus
    {
        Succeeded,
        MissingTargetMap,
        MissingSelectedAsset,
        UnsupportedMode
    }

    public readonly struct MapOperationResult
    {
        public MapOperationStatus Status { get; }
        public string Mode { get; }
        public uint OperationSeed { get; }
        public int AffectedCount { get; }

        internal MapOperationResult(
            MapOperationStatus status,
            string mode,
            uint operationSeed,
            int affectedCount)
        {
            Status = status;
            Mode = mode;
            OperationSeed = operationSeed;
            AffectedCount = affectedCount;
        }
    }

    public sealed class MapTool : Tool
    {
        private EFYVBackend.Core.Models.MapToolData Data =
            new EFYVBackend.Core.Models.MapToolData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };
        private short[] automataBuffer;
        private uint seed;
        // Instance PRNG from the backend (same xorshift stream the tool used to duplicate
        // line-for-line); reseeded per operation so every gesture is independently replayable.
        private EFYVBackend.Core.Math.FastRandomState randomState;
        private int operationIndex;

        public event Action<MapOperationResult> OperationCompleted;

        public uint Seed
        {
            get => seed;
            set
            {
                if (seed == value) return;
                seed = value;
                operationIndex = Config.Common.FirstIndex;
            }
        }
        public int OperationIndex => operationIndex;
        public float ScatterRadius
        {
            get => Data.ScatterRadius;
            set
            {
                RequireFinite(value, nameof(value));
                Data.ScatterRadius = value < Config.Common.ZeroFloat ? Config.Common.ZeroFloat : value;
            }
        }
        public int ScatterDensity
        {
            get => Data.ScatterDensity;
            set
            {
                if (value < Config.Common.EmptyCount)
                    Data.ScatterDensity = Config.Common.EmptyCount;
                else if (value > Config.Tool.Map.MaxScatterDensity)
                    Data.ScatterDensity = Config.Tool.Map.MaxScatterDensity;
                else
                    Data.ScatterDensity = value;
            }
        }
        public float MinScaleJitter
        {
            get => Data.MinScaleJitter;
            set
            {
                RequireFinite(value, nameof(value));
                float normalized = value < Config.Common.ZeroFloat ? Config.Common.ZeroFloat : value;
                Data.MinScaleJitter = normalized;
                if (Data.MaxScaleJitter < normalized) Data.MaxScaleJitter = normalized;
            }
        }
        public float MaxScaleJitter
        {
            get => Data.MaxScaleJitter;
            set
            {
                RequireFinite(value, nameof(value));
                float normalized = value < Config.Common.ZeroFloat ? Config.Common.ZeroFloat : value;
                Data.MaxScaleJitter = normalized;
                if (Data.MinScaleJitter > normalized) Data.MinScaleJitter = normalized;
            }
        }
        public string Mode
        {
            get => Data.Mode;
            set => Data.Mode = value;
        }
        public float FillProbability
        {
            get => Data.FillProbability;
            set
            {
                RequireFinite(value, nameof(value));
                if (value <= Config.Common.ZeroFloat) Data.FillProbability = Config.Common.ZeroFloat;
                else if (value >= Config.Common.UnitScale) Data.FillProbability = Config.Common.UnitScale;
                else Data.FillProbability = value;
            }
        }
        public short BaseTileId
        {
            get => Data.BaseTileId;
            set => Data.BaseTileId = value;
        }
        public short TargetTileId
        {
            get => Data.TargetTileId;
            set => Data.TargetTileId = value;
        }
        public string SelectedAsset
        {
            get => Data.SelectedAsset;
            set => Data.SelectedAsset = value;
        }
        public EFYVBackend.Core.Collections.FastGridMap TargetMap { get; set; }

        public MapTool()
        {
            ScatterRadius = Config.Tool.Map.DefaultScatterRadius;
            ScatterDensity = Config.Tool.Map.DefaultScatterDensity;
            MinScaleJitter = Config.Tool.Map.MinScaleJitter;
            MaxScaleJitter = Config.Tool.Map.MaxScaleJitter;
            Mode = Config.Tool.Map.ModeScatter;
            FillProbability = Config.Tool.Map.DefaultFillProbability;
            BaseTileId = Config.Tool.Map.DefaultBaseTileId;
            TargetTileId = Config.Tool.Map.DefaultTargetTileId;
            seed = Config.Tool.Map.DefaultSeed;
        }

        public void ResetSequence()
        {
            operationIndex = Config.Common.FirstIndex;
        }

        public MapOperationResult Execute(Frame frame, int x, int y)
        {
            uint operationSeed = BeginOperation();
            MapOperationResult result;
            if (TargetMap == null)
            {
                result = Result(MapOperationStatus.MissingTargetMap, operationSeed);
            }
            else if (Mode == Config.Tool.Map.ModeScatter)
            {
                result = string.IsNullOrEmpty(SelectedAsset)
                    ? Result(MapOperationStatus.MissingSelectedAsset, operationSeed)
                    : Result(MapOperationStatus.Succeeded, operationSeed, ScatterProps(x, y));
            }
            else if (Mode == Config.Tool.Map.ModeTile)
            {
                result = string.IsNullOrEmpty(SelectedAsset)
                    ? Result(MapOperationStatus.MissingSelectedAsset, operationSeed)
                    : Result(MapOperationStatus.Succeeded, operationSeed, PaintPropTile(x, y));
            }
            else if (Mode == Config.Tool.Map.ModeNoiseFill)
            {
                result = Result(MapOperationStatus.Succeeded, operationSeed, ApplyNoiseFill());
            }
            else if (Mode == Config.Tool.Map.ModeAutomataSmooth)
            {
                result = Result(MapOperationStatus.Succeeded, operationSeed, ApplyAutomataSmooth());
            }
            else
            {
                result = Result(MapOperationStatus.UnsupportedMode, operationSeed);
            }

            OperationCompleted?.Invoke(result);
            return result;
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (project != null && Seed != project.DesignerSeed) Seed = project.DesignerSeed;
            Execute(currentFrame, x, y);
        }

        private int ScatterProps(int centerX, int centerY)
        {
            for (int index = Config.Common.FirstIndex; index < ScatterDensity; index++)
            {
                float offsetX;
                float offsetY;
                GetRandomOffset2D(ScatterRadius, out offsetX, out offsetY);
                float scale = NextRange(MinScaleJitter, MaxScaleJitter);
                PlaceProp(SelectedAsset, centerX + (int)offsetX, centerY + (int)offsetY, scale);
            }
            return ScatterDensity;
        }

        private int PaintPropTile(int x, int y)
        {
            int tileSize = Config.Tool.TileMaker.DefaultTileSize;
            // FLOOR snap: integer division truncates toward zero, which used to misalign
            // negative pointer coordinates (-63 snapped to -32 instead of -64).
            int gridX = FloorSnapToTile(x, tileSize);
            int gridY = FloorSnapToTile(y, tileSize);
            // Clamp onto the target map: valid origins are the tile-size-aligned canvas
            // coordinates of tiles that exist on the map (Width/Height are tile counts).
            long maxOriginX = ((long)TargetMap.Width - EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep) * tileSize;
            long maxOriginY = ((long)TargetMap.Height - EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep) * tileSize;
            gridX = ClampToTileOrigin(gridX, maxOriginX);
            gridY = ClampToTileOrigin(gridY, maxOriginY);
            PlaceProp(SelectedAsset, gridX, gridY, Config.Tool.Map.DefaultObjectScale);
            return EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep;
        }

        private static int FloorSnapToTile(int coordinate, int tileSize)
        {
            int quotient = coordinate / tileSize;
            if (coordinate < Config.Canvas.MinCoordinate && coordinate % tileSize != Config.Common.EmptyCount) quotient--;
            return quotient * tileSize;
        }

        private static int ClampToTileOrigin(int snapped, long maxOrigin)
        {
            if (snapped < Config.Canvas.MinCoordinate) return Config.Canvas.MinCoordinate;
            if (snapped > maxOrigin) return (int)maxOrigin;
            return snapped;
        }

        private void PlaceProp(string assetKey, int x, int y, float scale)
        {
            TargetMap.Props.Add(new EFYVBackend.Core.Collections.FastGridMap.MapPropData
            {
                AssetKey = assetKey,
                X = x,
                Y = y,
                Scale = scale
            });
        }

        private int ApplyNoiseFill()
        {
            int cellCount = checked(TargetMap.Width * TargetMap.Height);
            int changed = Config.Common.EmptyCount;
            for (int index = Config.Common.FirstIndex; index < cellCount; index++)
            {
                if (NextUnitFloat() >= FillProbability) continue;
                if (TargetMap.RawData[index] != TargetTileId) changed++;
                TargetMap.RawData[index] = TargetTileId;
            }
            return changed;
        }

        private int ApplyAutomataSmooth()
        {
            int cellCount = checked(TargetMap.Width * TargetMap.Height);
            if (automataBuffer == null || automataBuffer.Length != cellCount)
                automataBuffer = new short[cellCount];
            EFYVBackend.Core.Math.FastProceduralGen.SmoothCellularAutomata(
                TargetMap.RawData,
                automataBuffer,
                TargetMap.Width,
                TargetMap.Height,
                TargetTileId,
                BaseTileId);
            return cellCount;
        }

        private uint BeginOperation()
        {
            uint operationSeed = seed ^
                ((uint)(operationIndex + EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep) *
                    BackendConfig.Math.FnvPrime);
            // FastRandomState applies the identical invalid-seed fallback; the mapped value is
            // recomputed here only because the operation result reports the effective seed.
            uint effectiveSeed = operationSeed == BackendConfig.Random.InvalidSeed
                ? BackendConfig.Random.FallbackSeed
                : operationSeed;
            randomState = new EFYVBackend.Core.Math.FastRandomState(effectiveSeed);
            operationIndex++;
            return effectiveSeed;
        }

        private float NextUnitFloat()
        {
            return randomState.NextUnitFloat();
        }

        private float NextRange(float minimum, float maximum)
        {
            // Unlike FastRandomState.Range, an empty or inverted range consumes NO draw -
            // this preserves the tool's historical (test-pinned) draw sequences.
            if (maximum <= minimum) return minimum;
            return randomState.Range(minimum, maximum);
        }

        private void GetRandomOffset2D(float radius, out float x, out float y)
        {
            // Bit-identical to the deleted hand-rolled rejection loop: InsideUnitCircle draws
            // the same (-1, 1) pairs in the same order and applies the same accept condition.
            randomState.InsideUnitCircle(out x, out y);
            x *= radius;
            y *= radius;
        }

        private MapOperationResult Result(
            MapOperationStatus status,
            uint operationSeed,
            int affectedCount = Config.Common.EmptyCount)
        {
            return new MapOperationResult(status, Mode, operationSeed, affectedCount);
        }

        private static void RequireFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
