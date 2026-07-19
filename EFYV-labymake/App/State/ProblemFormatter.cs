using System.Globalization;
using System.Text;
using EFYVLabyMake.Core.Logic;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.App.State
{
    // Pure, UI-framework-free formatting of a ProjectValidationResult's issues
    // for the Problems panel (item #3): a human message per issue code, a
    // severity label, a "where" descriptor from the issue's carried location,
    // and whether that location can be focused (click-to-select the frame).
    // Kept free of Avalonia so the verification suite can pin every branch.
    public static class ProblemFormatter
    {
        public static string FormatSeverity(ProjectIssueSeverity severity)
        {
            return severity == ProjectIssueSeverity.Error ? "Error" : "Warning";
        }

        // An issue is focusable when it names at least an animation - clicking
        // it selects that animation's frame (see EditorShell.FocusProblem).
        public static bool HasFocusLocation(ProjectIssue issue)
        {
            return issue.AnimationIndex >= Config.Common.FirstIndex;
        }

        // "Anim 2 · Frame 3 · Layer 1" from the (0-based) carried indices,
        // shown 1-based; empty when the issue carries no location.
        public static string FormatLocation(ProjectIssue issue)
        {
            var builder = new StringBuilder();
            AppendIndex(builder, "Anim", issue.AnimationIndex);
            AppendIndex(builder, "Frame", issue.FrameIndex);
            AppendIndex(builder, "Layer", issue.LayerIndex);
            return builder.ToString();
        }

        // A single-line "message (subject) — location" summary for a flat list.
        public static string FormatLine(ProjectIssue issue)
        {
            string message = Describe(issue);
            string location = FormatLocation(issue);
            return location.Length == 0 ? message : message + " — " + location;
        }

        public static string Describe(ProjectIssue issue)
        {
            string core = DescribeCode(issue.Code);
            return string.IsNullOrEmpty(issue.Subject) ? core : core + " (" + issue.Subject + ")";
        }

        private static void AppendIndex(StringBuilder builder, string label, int index)
        {
            if (index < Config.Common.FirstIndex) return;
            if (builder.Length > 0) builder.Append(" · ");
            builder.Append(label).Append(' ')
                .Append((index + 1).ToString(CultureInfo.InvariantCulture));
        }

        private static string DescribeCode(ProjectIssueCode code)
        {
            switch (code)
            {
                case ProjectIssueCode.MissingProject: return "No project open";
                case ProjectIssueCode.MissingTargetAssetType: return "Project has no asset type";
                case ProjectIssueCode.UnknownTargetAssetType: return "Unknown asset type";
                case ProjectIssueCode.MissingProperty: return "Required property is missing";
                case ProjectIssueCode.UnknownProperty: return "Unknown property";
                case ProjectIssueCode.InvalidPropertyType: return "Property has the wrong type";
                case ProjectIssueCode.PropertyOutOfRange: return "Property is out of range";
                case ProjectIssueCode.BossPhaseThresholdExceedsMaxHealth:
                    return "Phase-2 threshold exceeds max health";
                case ProjectIssueCode.InvalidPropertyChoice: return "Property is not an allowed choice";
                case ProjectIssueCode.EmptyIdentityName: return "Identity name is empty";
                case ProjectIssueCode.InvalidIdentityName: return "Identity name is not a safe file name";
                case ProjectIssueCode.InvalidFacing: return "Facing value is invalid";
                case ProjectIssueCode.InvalidCanvasDimensions: return "Canvas dimensions are invalid";
                case ProjectIssueCode.CanvasLimitExceeded: return "Canvas exceeds the size limit";
                case ProjectIssueCode.AtlasLimitExceeded: return "Frames exceed the atlas size budget";
                case ProjectIssueCode.AnimationLimitExceeded: return "Too many animations";
                case ProjectIssueCode.FrameLimitExceeded: return "Too many frames";
                case ProjectIssueCode.LayerLimitExceeded: return "Too many layers";
                case ProjectIssueCode.MissingAnimations: return "Project has no animations";
                case ProjectIssueCode.MissingFrames: return "Animation has no frames";
                case ProjectIssueCode.InvalidFrameRate: return "Animation FPS must be positive";
                case ProjectIssueCode.FrameDimensionMismatch: return "Frame size does not match the canvas";
                case ProjectIssueCode.MissingLayers: return "Frame has no layers";
                case ProjectIssueCode.LayerDimensionMismatch: return "Layer size does not match the frame";
                case ProjectIssueCode.LayerPixelCountMismatch: return "Layer pixel count is wrong";
                case ProjectIssueCode.InvalidLayerOpacity: return "Layer opacity is out of range";
                case ProjectIssueCode.EmptyHitboxKey: return "Hitbox has an empty key";
                case ProjectIssueCode.InvalidHitboxBounds: return "Hitbox bounds are outside the canvas";
                case ProjectIssueCode.MissingUnityProjectPath: return "Unity project path is not set";
                case ProjectIssueCode.MissingUnityAssetsDirectory: return "Unity Assets directory not found";
                case ProjectIssueCode.EffectLimitExceeded: return "Too many effects on an animation";
                case ProjectIssueCode.InvalidEffectDescriptor: return "Animation has an invalid effect";
                case ProjectIssueCode.AttachmentLimitExceeded: return "Too many attachments on a frame";
                case ProjectIssueCode.InvalidAttachment: return "Frame has an invalid attachment";
                case ProjectIssueCode.DirectionalFacingIncomplete: return "A facing has no animations";
                default: return code.ToString();
            }
        }
    }
}
