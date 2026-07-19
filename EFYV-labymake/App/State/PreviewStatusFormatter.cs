using System.Globalization;
using EFYVLabyMake.Core.Logic;

namespace EFYVLabyMake.App.State
{
    // Pure, UI-framework-free formatting of a PreviewStateSnapshot for the
    // preview panel (item #4).
    public static class PreviewStatusFormatter
    {
        public static string FormatStatus(PreviewStateSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case PreviewPlaybackState.Empty:
                    return "No frames to preview";
                case PreviewPlaybackState.Stopped:
                    return "Stopped · " + FormatFrame(snapshot) + " · " + FormatRate(snapshot);
                case PreviewPlaybackState.Paused:
                    return "Paused · " + FormatFrame(snapshot) + " · " + FormatRate(snapshot);
                case PreviewPlaybackState.Playing:
                    return "Playing · " + FormatFrame(snapshot) + " · " + FormatRate(snapshot);
                default:
                    return snapshot.State.ToString();
            }
        }

        public static string FormatFrame(PreviewStateSnapshot snapshot)
        {
            return "Frame " +
                (snapshot.FrameIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" +
                snapshot.FrameCount.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatRate(PreviewStateSnapshot snapshot)
        {
            string rate = snapshot.FPS.ToString(CultureInfo.InvariantCulture) + " fps";
            if (snapshot.PingPong) rate += " · ping-pong";
            else if (snapshot.IsLooping) rate += " · loop";
            return rate;
        }
    }
}
