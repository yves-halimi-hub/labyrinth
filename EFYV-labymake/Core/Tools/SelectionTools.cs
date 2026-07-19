using System.Collections.Generic;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    // Selection tools define a SelectionRegion through the normal gesture
    // lifecycle without mutating any pixels (their gesture diff is therefore
    // empty and records no history). After pointer-up the DesignerSession
    // collects the completed region via TakeCompletedRegion and stores it as
    // the session selection; ActiveLayerIndex names the layer that lift/copy
    // operations read from. They deliberately do NOT implement ILayerTool so
    // a selection gesture never pays for a full layer capture.
    public interface ISelectionTool
    {
        int ActiveLayerIndex { get; set; }

        // Returns the region completed by the last gesture exactly once
        // (subsequent calls return null until another gesture completes).
        // A gesture that selected nothing completes with null, which clears
        // the session selection.
        SelectionRegion TakeCompletedRegion();
    }

    public sealed class RectSelectTool : Tool, ISelectionTool
    {
        private EFYVBackend.Core.Models.BrushToolData Data =
            new EFYVBackend.Core.Models.BrushToolData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        private bool active;
        private SelectionRegion completed;

        public int ActiveLayerIndex
        {
            get => Data.ActiveLayerIndex;
            set => Data.ActiveLayerIndex = value;
        }

        private int anchorX
        {
            get => Data.LastX;
            set => Data.LastX = value;
        }
        private int anchorY
        {
            get => Data.LastY;
            set => Data.LastY = value;
        }

        public RectSelectTool()
        {
            ActiveLayerIndex = Config.Tool.DefaultLayerIndex;
        }

        public SelectionRegion TakeCompletedRegion()
        {
            SelectionRegion region = completed;
            completed = null;
            return region;
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (currentFrame == null) return;
            anchorX = x;
            anchorY = y;
            active = true;
            completed = null;
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (!active) return;
            active = false;
            if (currentFrame == null) return;
            completed = SelectionRegion.FromRectangle(
                currentFrame.Width,
                currentFrame.Height,
                anchorX,
                anchorY,
                x,
                y);
        }
    }

    public sealed class LassoSelectTool : Tool, ISelectionTool
    {
        private EFYVBackend.Core.Models.BrushToolData Data =
            new EFYVBackend.Core.Models.BrushToolData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        private readonly List<int> pointsX = new List<int>();
        private readonly List<int> pointsY = new List<int>();
        private bool active;
        private SelectionRegion completed;

        public int ActiveLayerIndex
        {
            get => Data.ActiveLayerIndex;
            set => Data.ActiveLayerIndex = value;
        }

        public LassoSelectTool()
        {
            ActiveLayerIndex = Config.Tool.DefaultLayerIndex;
        }

        public SelectionRegion TakeCompletedRegion()
        {
            SelectionRegion region = completed;
            completed = null;
            return region;
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (currentFrame == null) return;
            pointsX.Clear();
            pointsY.Clear();
            pointsX.Add(x);
            pointsY.Add(y);
            active = true;
            completed = null;
        }

        public override void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (!active) return;
            AddPoint(x, y);
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (!active) return;
            active = false;
            AddPoint(x, y);
            if (currentFrame != null)
            {
                // The polygon closes implicitly from the last point back to the
                // first; a stroke that encloses no pixel center yields null.
                completed = SelectionRegion.FromPolygon(
                    currentFrame.Width,
                    currentFrame.Height,
                    pointsX,
                    pointsY);
            }
            pointsX.Clear();
            pointsY.Clear();
        }

        private void AddPoint(int x, int y)
        {
            if (pointsX.Count >= Config.Tool.Selection.MaxLassoPoints) return;
            int lastIndex = pointsX.Count - Config.Common.UnitCount;
            if (lastIndex >= Config.Common.FirstIndex &&
                pointsX[lastIndex] == x && pointsY[lastIndex] == y) return;
            pointsX.Add(x);
            pointsY.Add(y);
        }
    }
}
