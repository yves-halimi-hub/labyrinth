namespace EFYVLabyMake.Core.Models
{
    // Nine-position content anchor for DesignerSession.ResizeCanvas: names the
    // corner/edge/center of the NEW canvas that the existing content sticks to.
    // TopLeft keeps pixel (0,0) fixed (grow/crop happens on the right/bottom);
    // MiddleCenter grows or crops evenly on both sides (odd differences are
    // split with integer division, biasing toward the top-left).
    public enum CanvasAnchor
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }
}
