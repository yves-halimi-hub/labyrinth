using System;
using EFYVLabyMake.Core.Models;
using EFYVBackend.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Tools
{
    public sealed class HitboxTool : Tool
    {
        private EFYVBackend.Core.Models.HitboxToolData Data = new EFYVBackend.Core.Models.HitboxToolData { Block = new EFYVBackend.Core.Data.FastSchemaBlock() };

        private int startX, startY;
        private bool isDrawing;
        
        // CONTROLLABILITY AUDIT: Artist can now select WHICH hitbox they are drawing (e.g. "Hurtbox" vs "AttackBox")
        public string ActiveHitboxKey 
        { 
            get => Data.ActiveHitboxKey; 
            set => Data.ActiveHitboxKey = value; 
        }

        public HitboxTool()
        {
            ActiveHitboxKey = Config.Hitbox.DefaultKeyHurtbox;
        }

        public override void OnPointerDown(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (currentFrame == null || string.IsNullOrWhiteSpace(ActiveHitboxKey)) return;

            startX = ClampCoordinate(x, currentFrame.Width);
            startY = ClampCoordinate(y, currentFrame.Height);
            isDrawing = true;
        }

        public override void OnPointerDrag(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (isDrawing) UpdateHitbox(currentFrame, x, y);
        }

        public override void OnPointerUp(EFYVProject project, Frame currentFrame, int x, int y)
        {
            if (!isDrawing) return;
            UpdateHitbox(currentFrame, x, y);
            isDrawing = false;
        }

        private void UpdateHitbox(Frame frame, int currentX, int currentY)
        {
            if (frame == null || string.IsNullOrWhiteSpace(ActiveHitboxKey)) return;

            HitboxData targetHitbox;
            if (!frame.Hitboxes.TryGetValue(ActiveHitboxKey, out targetHitbox))
                targetHitbox = new HitboxData();

            float pixelsPerUnit = Config.Hitbox.PixelsPerUnit;
            currentX = ClampCoordinate(currentX, frame.Width);
            currentY = ClampCoordinate(currentY, frame.Height);

            float minX = EFYVBackend.Core.Math.FastMath.FastMin(startX, currentX) / pixelsPerUnit;
            float minY = EFYVBackend.Core.Math.FastMath.FastMin(startY, currentY) / pixelsPerUnit;
            float maxX = EFYVBackend.Core.Math.FastMath.FastMax(startX, currentX) / pixelsPerUnit;
            float maxY = EFYVBackend.Core.Math.FastMath.FastMax(startY, currentY) / pixelsPerUnit;

            targetHitbox.X = minX;
            targetHitbox.Y = minY;
            targetHitbox.Width = maxX - minX;
            targetHitbox.Height = maxY - minY;
            frame.Hitboxes[ActiveHitboxKey] = targetHitbox;
        }

        private static int ClampCoordinate(int value, int maximum)
        {
            return EFYVBackend.Core.Math.FastMath.FastMax(
                Config.Canvas.MinCoordinate,
                EFYVBackend.Core.Math.FastMath.FastMin(value, maximum));
        }
    }
}
